using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public static class GnnJobRegistryBuilder
    {
        public static GnnJobRegistry Build(JObject p4r, string vcRawPath)
        {
            if (p4r == null)
                throw new ArgumentNullException(nameof(p4r));
            if (string.IsNullOrWhiteSpace(vcRawPath) || !File.Exists(vcRawPath))
                throw new FileNotFoundException("GNN VCRAW was not found.", vcRawPath);
            JObject raw = JObject.Parse(File.ReadAllText(vcRawPath));
            JArray rawJobs = raw["jobs"] as JArray;
            if (rawJobs == null)
                throw new InvalidDataException("VCRAW jobs[] is missing.");
            var entries = new List<GnnJobRegistryEntry>();
            var materialByOperation = BuildMaterialByOperation(p4r);
            var materialByProduct = BuildMaterialByProduct(p4r);
            var locationById = BuildLocationById(p4r);
            var mheResourceByType = BuildMheResourceByType(p4r);
            AddMachineJobs(p4r, entries, materialByOperation);
            AddBufferJobs(p4r, entries);
            AddMheJobs(p4r, entries, mheResourceByType, locationById);
            ApplyPendingStoragePlacement(p4r, entries);
            AddNewOrderJobs(p4r, entries, materialByProduct, locationById);
            if (entries.Count != rawJobs.Count)
            {
                throw new InvalidDataException("P4R/VCRAW job count mismatch. P4R identity jobs=" + entries.Count + ", VCRAW jobs=" + rawJobs.Count + ". The P4R export rules and VC registry builder must be updated together.");
            }

            for (int i = 0; i < entries.Count; i++)
            {
                JObject rawJob = (JObject)rawJobs[i];
                int rawJobId = (int? )rawJob["job"] ?? -1;
                if (rawJobId != i)
                    throw new InvalidDataException("VCRAW job order is not contiguous at index " + i + ".");
                entries[i].Job = rawJobId;
                entries[i].Due = (double? )rawJob["due"] ?? 0.0;
            }

            string duplicate = entries.GroupBy(x => x.SourceId, StringComparer.Ordinal).Where(x => x.Count() > 1).Select(x => x.Key).FirstOrDefault();
            if (duplicate != null)
                throw new InvalidDataException("Duplicate P4R source ID: " + duplicate);
            return new GnnJobRegistry
            {
                Version = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"),
                Jobs = entries
            };
        }

        private static void ApplyPendingStoragePlacement(JObject p4r, IList<GnnJobRegistryEntry> entries)
        {
            JObject initialization = p4r["_DT_RULE_INITIALIZATION"] as JObject;
            JObject pending = initialization == null ? null : initialization["pendingStorage"] as JObject;
            if (pending == null || !((bool? )pending["preplacedInShelf"] ?? false))
                return;
            string sourceId = Text(pending, "productId");
            GnnJobRegistryEntry entry = entries.FirstOrDefault(item => string.Equals(item.SourceId, sourceId, StringComparison.Ordinal) && string.Equals(item.SourceType, "ASRS_STORAGE", StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                throw new InvalidDataException("Pending Storage WIP is not present in the GNN job registry: " + sourceId);
            entry.Row = Integer(pending, "targetRow");
            entry.Column = Integer(pending, "targetColumn");
            entry.Tier = Integer(pending, "targetTier");
            entry.Component = "ASRS";
        }

        private static Dictionary<string, string> BuildMaterialByProduct(JObject p4r)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JObject product in Objects(p4r["PRODUCT_INFO"]))
            {
                string productId = Text(product, "PRODUCT_ID");
                JObject material = Objects(product["MATERIAL_INFO"]).FirstOrDefault();
                if (productId.Length > 0 && material != null)
                    result[productId] = Text(material, "MATERIAL_ID");
            }

            return result;
        }

        private static Dictionary<string, string> BuildMaterialByOperation(JObject p4r)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JObject process in Objects(p4r["PROCESS_INFO"]))
            {
                JObject consumed = Objects(process["CONSUMED_MATERIAL_INFO"]).FirstOrDefault();
                string material = consumed == null ? "" : Text(consumed, "MATERIAL_ID");
                foreach (JObject operation in Objects(process["OPERATION_INFO"]))
                {
                    string operationId = Text(operation, "OPERATION_INFO_ID");
                    if (operationId.Length > 0)
                        result[operationId] = material;
                }
            }

            return result;
        }

        private static Dictionary<string, string> BuildMheResourceByType(JObject p4r)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JObject type in Objects(p4r["MHE_TYPE"]))
            {
                string id = Text(type, "MHE_TYPE_ID");
                if (id.Length > 0)
                    result[id] = Text(type, "RESOURCE_TYPE");
            }

            return result;
        }

        private static Dictionary<string, JObject> BuildLocationById(JObject p4r)
        {
            var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (JObject asrs in Objects(p4r["ASRS_INSTANCE"]))
                foreach (JObject inventory in Objects(asrs["INVENTORY_STATUS"]))
                    foreach (JObject location in Objects(inventory["LOCATION_INFO"]))
                    {
                        string id = Text(location, "LOCATION_INFO_ID");
                        if (id.Length > 0)
                            result[id] = location;
                    }

            return result;
        }

        private static void AddMachineJobs(JObject p4r, List<GnnJobRegistryEntry> entries, IDictionary<string, string> materialByOperation)
        {
            foreach (JObject machine in Objects(p4r["MACHINE_INSTANCE"]))
                foreach (JObject wip in Objects(machine["WORK_IN_PROCESS_STATUS"]))
                {
                    string operation = Text(wip, "OPERATION_INFO_ID");
                    string material;
                    materialByOperation.TryGetValue(operation, out material);
                    entries.Add(NewEntry(Text(wip, "WORK_IN_PROCESS_STATUS_ID"), "MACHINE_WIP", material, Text(machine, "MACHINE_INSTANCE_ID")));
                }
        }

        private static void AddBufferJobs(JObject p4r, List<GnnJobRegistryEntry> entries)
        {
            foreach (JObject buffer in Objects(p4r["BUFFER_INSTANCE"]))
                foreach (JObject status in Objects(buffer["BUFFER_STATUS"]))
                    foreach (JObject slot in Objects(status["SLOT_INFO"]))
                    {
                        GnnJobRegistryEntry entry = NewEntry(Text(slot, "SLOT_INFO_ID"), "BUFFER_WIP", Text(status, "MATERIAL_ID"), Text(buffer, "BUFFER_INSTANCE_ID"));
                        entry.Slot = Integer(slot, "SLOT_ID");
                        entries.Add(entry);
                    }
        }

        private static void AddMheJobs(JObject p4r, List<GnnJobRegistryEntry> entries, IDictionary<string, string> resourceByType, IDictionary<string, JObject> locationById)
        {
            foreach (JObject mhe in Objects(p4r["MHE_INSTANCE"]))
            {
                string mheId = Text(mhe, "MHE_INSTANCE_ID");
                string typeId = Text(mhe, "MHE_TYPE_ID");
                string resource;
                resourceByType.TryGetValue(typeId, out resource);
                bool isAmr = (resource ?? typeId).StartsWith("AMR", StringComparison.OrdinalIgnoreCase);
                foreach (JObject status in Objects(mhe["MATERIAL_HANDLING_STATUS"]))
                {
                    AddMheItems(status, "HANDLING_MATERIAL_INFO", false, mheId, isAmr, entries, locationById);
                    AddMheItems(status, "RETRIEVING_MATERIAL_INFO", true, mheId, isAmr, entries, locationById);
                }
            }
        }

        private static void AddMheItems(JObject status, string key, bool retrieving, string mheId, bool isAmr, List<GnnJobRegistryEntry> entries, IDictionary<string, JObject> locationById)
        {
            foreach (JObject item in Objects(status[key]))
            {
                string sourceId = Text(item, "HANDLING_MATERIAL_INFO_ID");
                if (sourceId.Length == 0)
                    sourceId = Text(item, "RETRIEVING_MATERIAL_INFO_ID");
                string sourceType = isAmr ? "AMR_WIP" : (retrieving ? "ASRS_RETRIEVAL" : "ASRS_STORAGE");
                string component = isAmr ? mheId : (retrieving ? "ASRS Out" : "ASRS In");
                GnnJobRegistryEntry entry = NewEntry(sourceId, sourceType, Text(item, "MATERIAL_ID"), component);
                ApplyLocation(entry, Text(item, "RETRIEVAL_LOCATION_INFO_ID"), locationById);
                entries.Add(entry);
            }
        }

        private static void AddNewOrderJobs(JObject p4r, List<GnnJobRegistryEntry> entries, IDictionary<string, string> materialByProduct, IDictionary<string, JObject> locationById)
        {
            foreach (JObject plan in Objects(p4r["PRODUCTION_PLAN"]))
            {
                string planId = Text(plan, "PRODUCTION_PLAN_ID");
                string material = Text(plan, "MATERIAL_ID");
                if (material.Length == 0)
                    materialByProduct.TryGetValue(Text(plan, "PRODUCT_ID"), out material);
                List<JObject> dues = Objects(plan["DUE_INFO"]).ToList();
                int count = dues.Count;
                if (count == 0)
                {
                    count = Math.Max(0, Integer(plan, "PRODUCTION_VOLUME") - Integer(plan, "CURRENT_VOLUME"));
                }

                for (int i = 0; i < count; i++)
                {
                    GnnJobRegistryEntry entry = NewEntry(planId + "#" + (i + 1), "NEW_ORDER", material, "ASRS");
                    if (i < dues.Count)
                        ApplyLocation(entry, Text(dues[i], "RETRIEVAL_LOCATION_INFO_ID"), locationById);
                    entries.Add(entry);
                }
            }
        }

        private static void ApplyLocation(GnnJobRegistryEntry entry, string id, IDictionary<string, JObject> locationById)
        {
            JObject location;
            if (id.Length == 0 || !locationById.TryGetValue(id, out location))
                return;
            entry.Row = Integer(location, "ROW_ID");
            entry.Column = Integer(location, "COLUMN_ID");
            entry.Tier = Integer(location, "TIER_ID");
        }

        private static GnnJobRegistryEntry NewEntry(string sourceId, string sourceType, string material, string component)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new InvalidDataException("A P4R state item has no stable source ID.");
            return new GnnJobRegistryEntry
            {
                SourceId = sourceId,
                SourceType = sourceType,
                Material = material ?? "",
                Component = component ?? ""
            };
        }

        private static IEnumerable<JObject> Objects(JToken token)
        {
            JArray array = token as JArray;
            return array == null ? Enumerable.Empty<JObject>() : array.OfType<JObject>();
        }

        private static string Text(JObject value, string name)
        {
            return (string)value[name] ?? "";
        }

        private static int Integer(JObject value, string name)
        {
            int result;
            return int.TryParse(Convert.ToString(value[name]), out result) ? result : 0;
        }
    }
}

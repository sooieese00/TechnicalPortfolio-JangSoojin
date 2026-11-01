using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public sealed class RuleShelfSnapshot
    {
        public readonly int TierIndex;
        public readonly string[] Inventory;
        public readonly JToken RetrievalSequence;
        public RuleShelfSnapshot(int tierIndex, string[] inventory, JToken retrievalSequence)
        {
            TierIndex = tierIndex;
            Inventory = inventory;
            RetrievalSequence = retrievalSequence;
        }
    }

    public sealed class RuleCncSnapshot
    {
        public readonly string MachineId;
        public readonly string MachineType;
        public readonly string[] FlowMaterials;
        public readonly double[] ProcessingTimes;
        public readonly double Mttr;
        public readonly double Mtbf;
        public readonly string WorkInProcessMaterial;
        public readonly double RemainingProcessingTime;
        public readonly double RemainingRepairTime;
        public RuleCncSnapshot(string machineId, string machineType, string[] flowMaterials, double[] processingTimes, double mttr, double mtbf, string workInProcessMaterial, double remainingProcessingTime, double remainingRepairTime)
        {
            MachineId = machineId;
            MachineType = machineType;
            FlowMaterials = flowMaterials;
            ProcessingTimes = processingTimes;
            Mttr = mttr;
            Mtbf = mtbf;
            WorkInProcessMaterial = workInProcessMaterial;
            RemainingProcessingTime = remainingProcessingTime;
            RemainingRepairTime = remainingRepairTime;
        }
    }

    public sealed class RuleAmrSnapshot
    {
        public readonly string ComponentId;
        public readonly string MheType;
        public readonly double MoveSpeed;
        public readonly double LoadedMoveSpeed;
        public readonly double Acceleration;
        public readonly double Deceleration;
        public readonly int TransportCapacity;
        public readonly double X;
        public readonly double Y;
        public RuleAmrSnapshot(string componentId, string mheType, double moveSpeed, double loadedMoveSpeed, double acceleration, double deceleration, int transportCapacity, double x, double y)
        {
            ComponentId = componentId;
            MheType = mheType;
            MoveSpeed = moveSpeed;
            LoadedMoveSpeed = loadedMoveSpeed;
            Acceleration = acceleration;
            Deceleration = deceleration;
            TransportCapacity = transportCapacity;
            X = x;
            Y = y;
        }
    }

    public sealed class RuleBufferSnapshot
    {
        public readonly string BufferId;
        public readonly string BufferType;
        public readonly bool IsInput;
        public readonly IList<string> Products;
        public RuleBufferSnapshot(string bufferId, string bufferType, bool isInput, IList<string> products)
        {
            BufferId = bufferId;
            BufferType = bufferType;
            IsInput = isInput;
            Products = products;
        }
    }

    public sealed class RuleAmrWipSnapshot
    {
        public readonly string AmrId;
        public readonly double X;
        public readonly double Y;
        public readonly IList<string> ApProducts;
        public readonly IList<string> BpProducts;
        public RuleAmrWipSnapshot(string amrId, double x, double y, IList<string> apProducts, IList<string> bpProducts)
        {
            AmrId = amrId;
            X = x;
            Y = y;
            ApProducts = apProducts;
            BpProducts = bpProducts;
        }
    }

    public sealed class RuleMheSnapshot
    {
        public readonly string ComponentId;
        public readonly string MheType;
        public readonly double[] Properties;
        public readonly string[] WipProperties;
        public readonly string Sequence;
        public readonly double[] LifterAProperties;
        public readonly double[] LifterBProperties;
        public RuleMheSnapshot(string componentId, string mheType, double[] properties, string[] wipProperties, string sequence, double[] lifterAProperties, double[] lifterBProperties)
        {
            ComponentId = componentId;
            MheType = mheType;
            Properties = properties;
            WipProperties = wipProperties;
            Sequence = sequence;
            LifterAProperties = lifterAProperties;
            LifterBProperties = lifterBProperties;
        }
    }

    public sealed class RuleJsonPreprocessing
    {
        public const int RowCount = 10;
        public const int ColumnCount = 4;
        public const int TierCount = 4;
        private readonly JsonPreprocessing _common = new JsonPreprocessing();
        public IList<RuleAmrSnapshot> GetAmrSnapshots(JObject data)
        {
            string[] mheTypes =
            {
                "AMR_A",
                "AMR_B",
                "SHUTTLE_A",
                "LIFTER_A",
                "LIFTER_B"
            };
            string[] mheList = _common.GetMHEList(data, mheTypes) ?? new string[0];
            var result = new List<RuleAmrSnapshot>();
            foreach (string mheId in mheList)
            {
                string mheType = _common.GetMHEType(data, mheId);
                if (!string.Equals(mheType, "AMR_A", StringComparison.OrdinalIgnoreCase) && !string.Equals(mheType, "AMR_B", StringComparison.OrdinalIgnoreCase))
                    continue;
                double[] properties = _common.GetPropertiesOfMHE(data, mheId);
                var position = _common.GetMHEPosition(data, mheId);
                result.Add(new RuleAmrSnapshot(mheId, mheType, properties[0], properties[1], properties[2], properties[3], Convert.ToInt32(properties[4]), Convert.ToDouble(position.Item1), Convert.ToDouble(position.Item2)));
            }

            return result;
        }

        public IList<RuleBufferSnapshot> GetBufferSnapshots(JObject data)
        {
            string[] bufferList = _common.GetBufferInstanceIdList(data) ?? new string[0];
            var result = new List<RuleBufferSnapshot>();
            foreach (string bufferId in bufferList)
            {
                string[] parts = bufferId.Split('_');
                if (parts.Length < 4)
                    continue;
                bool isInput = string.Equals(parts[2], "IN", StringComparison.OrdinalIgnoreCase);
                bool isOutput = string.Equals(parts[2], "OUT", StringComparison.OrdinalIgnoreCase);
                if (!isInput && !isOutput)
                    continue;
                string[][] wip = _common.GetWorkInProcessBuffer(data, bufferId);
                if (wip == null || wip.Length == 0)
                    continue;
                var products = new List<string>();
                foreach (string[] material in wip)
                {
                    int quantity;
                    if (material == null || material.Length < 2 || string.IsNullOrWhiteSpace(material[0]) || !int.TryParse(material[1], out quantity) || quantity <= 0)
                        continue;
                    string productType = NormalizeRuleProductType(material[0]);
                    for (int i = 0; i < quantity; i++)
                        products.Add(productType);
                }

                if (products.Count > 0)
                    result.Add(new RuleBufferSnapshot(bufferId, parts[1], isInput, products));
            }

            return result;
        }

        public IList<RuleAmrWipSnapshot> GetAmrWipSnapshots(JObject data)
        {
            string[] mheTypes =
            {
                "AMR_A",
                "AMR_B",
                "SHUTTLE_A",
                "LIFTER_A",
                "LIFTER_B"
            };
            string[] mheList = _common.GetMHEList(data, mheTypes) ?? new string[0];
            var result = new List<RuleAmrWipSnapshot>();
            foreach (string mheId in mheList)
            {
                string mheType = _common.GetMHEType(data, mheId);
                if (!string.Equals(mheType, "AMR_A", StringComparison.OrdinalIgnoreCase) && !string.Equals(mheType, "AMR_B", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(mheId, "AMR_A_001", StringComparison.OrdinalIgnoreCase) && !string.Equals(mheId, "AMR_B_001", StringComparison.OrdinalIgnoreCase))
                    continue;
                var position = _common.GetMHEPosition(data, mheId);
                var apProducts = new List<string>();
                var bpProducts = new List<string>();
                List<string[]> wipList = _common.GetAMRWIPProperties(data, mheId);
                if (wipList != null)
                {
                    foreach (string[] wip in wipList)
                    {
                        if (wip == null || wip.Length < 2)
                            continue;
                        string productType = NormalizeRuleProductType(wip[0]);
                        if (string.Equals(wip[1], "AP", StringComparison.OrdinalIgnoreCase))
                            apProducts.Add(productType);
                        else if (string.Equals(wip[1], "BP", StringComparison.OrdinalIgnoreCase))
                            bpProducts.Add(productType);
                    }
                }

                result.Add(new RuleAmrWipSnapshot(mheId, Convert.ToDouble(position.Item1), Convert.ToDouble(position.Item2), apProducts, bpProducts));
            }

            return result;
        }

        public IList<RuleMheSnapshot> GetShuttleSnapshots(JObject data)
        {
            string[] mheTypes =
            {
                "AMR_A",
                "AMR_B",
                "SHUTTLE_A",
                "LIFTER_A",
                "LIFTER_B"
            };
            string[] mheList = _common.GetMHEList(data, mheTypes) ?? new string[0];
            var result = new List<RuleMheSnapshot>();
            foreach (string mheId in mheList)
            {
                string type = _common.GetMHEType(data, mheId);
                if (!string.Equals(type, "SHUTTLE_A", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] wip = _common.GetShuttleWIPProperties(data, mheId) ?? new string[0];
                if (wip.Length > 1)
                    wip[1] = NormalizeRuleProductType(wip[1]);
                result.Add(new RuleMheSnapshot(mheId, type, _common.GetPropertiesOfMHE(data, mheId), wip, null, _common.GetPropertiesOfMHE(data, "LIFTER_A_001"), _common.GetPropertiesOfMHE(data, "LIFTER_B_001")));
            }

            return result;
        }

        public JObject GetPendingStorageWip(JObject data)
        {
            JArray mheInstances = data == null ? null : data["MHE_INSTANCE"] as JArray;
            if (mheInstances == null)
                return null;
            var shuttlePositions = new JObject();
            double lifterAPositionZ = 0.0;
            double lifterBPositionZ = 0.0;
            foreach (JToken itemToken in mheInstances)
            {
                JObject item = itemToken as JObject;
                if (item == null)
                    continue;
                string itemType = Convert.ToString(item["MHE_TYPE_ID"]);
                string itemId = Convert.ToString(item["MHE_INSTANCE_ID"]);
                JObject itemPosition = item["CURRENT_POSITION"] as JObject;
                if (string.Equals(itemType, "SHUTTLE_A", StringComparison.OrdinalIgnoreCase))
                {
                    int separator = itemId.LastIndexOf('_');
                    int number;
                    if (separator >= 0 && int.TryParse(itemId.Substring(separator + 1), out number) && number > 0)
                        shuttlePositions[Convert.ToString(number - 1)] = ReadDouble(itemPosition, "CURRENT_POSITION_X");
                }
                else if (string.Equals(itemType, "LIFTER_A", StringComparison.OrdinalIgnoreCase))
                    lifterAPositionZ = ReadDouble(itemPosition, "CURRENT_POSITION_Z");
                else if (string.Equals(itemType, "LIFTER_B", StringComparison.OrdinalIgnoreCase))
                    lifterBPositionZ = ReadDouble(itemPosition, "CURRENT_POSITION_Z");
            }

            JObject pendingResult = null;
            foreach (JToken mheToken in mheInstances)
            {
                JObject mhe = mheToken as JObject;
                if (mhe == null)
                    continue;
                string mheType = Convert.ToString(mhe["MHE_TYPE_ID"]);
                bool isShuttle = string.Equals(mheType, "SHUTTLE_A", StringComparison.OrdinalIgnoreCase);
                bool isStorageLifter = string.Equals(mheType, "LIFTER_B", StringComparison.OrdinalIgnoreCase);
                if (!isShuttle && !isStorageLifter)
                    continue;
                JArray statuses = mhe["MATERIAL_HANDLING_STATUS"] as JArray;
                JObject status = statuses != null && statuses.Count > 0 ? statuses[0] as JObject : null;
                JArray materials = status == null ? null : status["HANDLING_MATERIAL_INFO"] as JArray;
                JObject material = materials != null && materials.Count > 0 ? materials[0] as JObject : null;
                if (material == null)
                    continue;
                if (pendingResult != null)
                    throw new InvalidOperationException("More than one initial Shuttle/Lifter storage WIP was found.");
                string componentId = Convert.ToString(mhe["MHE_INSTANCE_ID"]);
                string productType = NormalizeRuleProductType(Convert.ToString(material["MATERIAL_ID"]));
                string productId = Convert.ToString(material["HANDLING_MATERIAL_INFO_ID"]);
                if (string.IsNullOrWhiteSpace(productId))
                    productId = Convert.ToString(material["MATERIAL_ID"]);
                JObject position = mhe["CURRENT_POSITION"] as JObject;
                int currentTier = -1;
                if (isShuttle)
                {
                    int separator = componentId.LastIndexOf('_');
                    int shuttleNumber;
                    if (separator >= 0 && int.TryParse(componentId.Substring(separator + 1), out shuttleNumber))
                        currentTier = shuttleNumber - 1;
                }

                var pending = new JObject
                {
                    ["state"] = "PENDING",
                    ["type"] = isShuttle ? "SHUTTLE_STORAGE" : "LIFTER_STORAGE",
                    ["productType"] = productType,
                    ["productId"] = productId,
                    ["jobId"] = -1,
                    ["sourceComponent"] = componentId,
                    ["currentX"] = ReadDouble(position, "CURRENT_POSITION_X"),
                    ["currentZ"] = ReadDouble(position, "CURRENT_POSITION_Z"),
                    ["currentTier"] = currentTier >= 0 ? (JToken)currentTier : JValue.CreateNull(),
                    ["shuttlePositions"] = shuttlePositions,
                    ["lifterACurrentZ"] = lifterAPositionZ,
                    ["lifterBCurrentZ"] = lifterBPositionZ,
                    ["targetTier"] = JValue.CreateNull(),
                    ["targetRow"] = JValue.CreateNull(),
                    ["targetColumn"] = JValue.CreateNull(),
                    ["lifterDelay"] = JValue.CreateNull(),
                    ["shuttleDelay"] = JValue.CreateNull(),
                    ["createdInVc"] = false
                };
                pendingResult = pending;
            }

            return pendingResult;
        }

        private static double ReadDouble(JObject value, string propertyName)
        {
            double result;
            return value != null && double.TryParse(Convert.ToString(value[propertyName]), out result) ? result : 0.0;
        }

        public IList<RuleMheSnapshot> GetLifterSnapshots(JObject data, string currentDateTime)
        {
            string[] mheTypes =
            {
                "AMR_A",
                "AMR_B",
                "SHUTTLE_A",
                "LIFTER_A",
                "LIFTER_B"
            };
            string[] mheList = _common.GetMHEList(data, mheTypes) ?? new string[0];
            var result = new List<RuleMheSnapshot>();
            foreach (string mheId in mheList)
            {
                string type = _common.GetMHEType(data, mheId);
                if (!string.Equals(type, "LIFTER_A", StringComparison.OrdinalIgnoreCase) && !string.Equals(type, "LIFTER_B", StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] wip = string.Equals(type, "LIFTER_A", StringComparison.OrdinalIgnoreCase) ? _common.GetLifterRetrievalWIPProperties(data, type) : _common.GetLifterStorageWIPProperties(data, type);
                if (wip == null)
                    wip = new string[0];
                int productIndex = string.Equals(type, "LIFTER_A", StringComparison.OrdinalIgnoreCase) ? 1 : 4;
                if (wip.Length > productIndex)
                    wip[productIndex] = NormalizeRuleProductType(wip[productIndex]);
                result.Add(new RuleMheSnapshot(mheId, type, _common.GetPropertiesOfMHE(data, mheId), wip, string.Equals(type, "LIFTER_A", StringComparison.OrdinalIgnoreCase) ? _common.GetCombinedLifterSequence(data, currentDateTime) : null, null, null));
            }

            return result;
        }

        public IList<RuleCncSnapshot> GetCncSnapshots(JObject data, int unitOfTime, string currentDateTime)
        {
            string[] cncTypes =
            {
                "CNC_A",
                "CNC_B",
                "CNC_D",
                "CNC_E"
            };
            string[] productList = _common.GetProductList(data);
            string[][] materialList = _common.GetMaterialList(data, productList);
            string[] machineList = _common.GetCNCMachineList(data, cncTypes);
            string[][] flowList = _common.GetFlowList(data, productList, materialList, cncTypes);
            double[][] processingTimes = _common.GetOperationTimeCNC(data, flowList);
            double[][] failureValues = _common.GetFailureValues(data, machineList, unitOfTime);
            var result = new List<RuleCncSnapshot>();
            for (int i = 0; i < machineList.Length; i++)
            {
                string machineType = _common.GetCNCType(data, machineList[i]);
                int typeIndex = Array.IndexOf(cncTypes, machineType);
                if (typeIndex < 0)
                    throw new InvalidOperationException("Unsupported Rule CNC type: " + machineType);
                var wip = _common.GetWorkInProcessProperties(data, machineList[i], unitOfTime);
                result.Add(new RuleCncSnapshot(machineList[i], machineType, NormalizeRuleProductTypes(flowList[typeIndex]), processingTimes[typeIndex], failureValues[i][0], failureValues[i][1], NormalizeRuleProductType(wip.Item1), wip.Item2, _common.GetFailureStatus(data, machineList[i], unitOfTime, currentDateTime)));
            }

            return result;
        }

        private static string[] NormalizeRuleProductTypes(string[] values)
        {
            if (values == null)
                return new string[0];
            string[] result = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = NormalizeRuleProductType(values[i]);
            return result;
        }

        public IList<RuleShelfSnapshot> GetShelfSnapshots(JObject data)
        {
            var result = new List<RuleShelfSnapshot>();
            JObject retrievalSchedule = ParseObject(_common.GetShuttleRetrievalSchedule(data));
            Dictionary<string, string> flexible = _common.GetFlexibleMaterialMapByLocation(data);
            Dictionary<string, string> handling = _common.GetHandlingMaterialStorageMap(data);
            Dictionary<string, string> retrieval = _common.GetShuttleRetrievalMaterialMap(data, RowCount, ColumnCount, TierCount);
            for (int tier = 0; tier < TierCount; tier++)
            {
                string[] inventory = _common.GetInventoryListforASRS(data, RowCount, ColumnCount, tier, flexible, handling);
                if (inventory == null)
                    inventory = new string[RowCount * ColumnCount];
                foreach (KeyValuePair<string, string> item in retrieval)
                {
                    string[] parts = item.Key.Split('_');
                    int itemTier;
                    int row;
                    int column;
                    if (parts.Length != 3 || !int.TryParse(parts[0], out itemTier) || !int.TryParse(parts[1], out row) || !int.TryParse(parts[2], out column) || itemTier != tier || row < 0 || row >= RowCount || column < 0 || column >= ColumnCount)
                        continue;
                    int index = row * ColumnCount + column;
                    if (string.IsNullOrWhiteSpace(inventory[index]) || string.Equals(inventory[index], "dummy", StringComparison.OrdinalIgnoreCase))
                        inventory[index] = item.Value;
                }

                for (int index = 0; index < inventory.Length; index++)
                    inventory[index] = NormalizeRuleProductType(inventory[index]);
                result.Add(new RuleShelfSnapshot(tier, inventory, retrievalSchedule[(tier + 1).ToString()] ?? new JArray()));
            }

            return result;
        }

        public static string NormalizeRuleProductType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            string trimmed = value.Trim();
            if (!trimmed.StartsWith("MT_", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            string[] parts = trimmed.Split('_');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                return trimmed;
            return "MT_" + parts[1].ToUpperInvariant();
        }

        private static JObject ParseObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new JObject();
            try
            {
                return JObject.Parse(value);
            }
            catch
            {
                return new JObject();
            }
        }
    }
}

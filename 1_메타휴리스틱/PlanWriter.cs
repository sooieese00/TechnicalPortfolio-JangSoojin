using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PDP_ASRS_WIP
{
    public static class PlanWriter
    {
        public static string Write(string scenarioPath, ScenarioInstance inst, SequenceSolution solution, DecodedSchedule schedule, string mode)
        {
            var root = JsonNode.Parse(File.ReadAllText(scenarioPath)) as JsonObject ?? throw new InvalidOperationException("Scenario JSON root is not an object.");
            int m = inst.m;
            var warn = new List<string>();
            var productOfMaterial = new Dictionary<string, string>();
            foreach (var pi in Items(root, "PRODUCT_INFO"))
            {
                foreach (var mi in Items(pi, "MATERIAL_INFO"))
                {
                    productOfMaterial[S(mi, "MATERIAL_ID")] = S(pi, "PRODUCT_ID");
                }
            }

            var opsOfProduct = new Dictionary<string, List<(string OpId, string MachineType, double Seconds)>>();
            foreach (var pp in Items(root, "PROCESS_INFO"))
            {
                string prod = S(pp, "PRODUCT_ID");
                if (!opsOfProduct.TryGetValue(prod, out var list))
                {
                    opsOfProduct[prod] = list = new List<(string, string, double)>();
                }

                foreach (var op in Items(pp, "OPERATION_INFO"))
                {
                    list.Add((S(op, "OPERATION_INFO_ID"), S(op, "MACHINE_TYPE_ID"), D(op, "PROCESSING_TIME")));
                }
            }

            var mheResource = new Dictionary<string, string>();
            foreach (var mt in Items(root, "MHE_TYPE"))
            {
                mheResource[S(mt, "MHE_TYPE_ID")] = S(mt, "RESOURCE_TYPE").ToUpperInvariant();
            }

            var mheInstancesOfType = new Dictionary<string, List<string>>();
            foreach (var mi in Items(root, "MHE_INSTANCE"))
            {
                string type = S(mi, "MHE_TYPE_ID");
                if (!mheInstancesOfType.TryGetValue(type, out var list))
                {
                    mheInstancesOfType[type] = list = new List<string>();
                }

                list.Add(S(mi, "MHE_INSTANCE_ID"));
            }

            string ResourceOf(string mheType)
            {
                return mheResource.TryGetValue(mheType, out string? res) ? res : mheType.Split('_')[0].ToUpperInvariant();
            }

            var amrTypeOfInstance = new Dictionary<string, string>();
            foreach (var mi in Items(root, "MHE_INSTANCE"))
            {
                amrTypeOfInstance[S(mi, "MHE_INSTANCE_ID")] = S(mi, "MHE_TYPE_ID");
            }

            var machineInstancesOfType = new Dictionary<string, List<string>>();
            foreach (var mi in Items(root, "MACHINE_INSTANCE"))
            {
                string type = S(mi, "MACHINE_TYPE_ID");
                if (!machineInstancesOfType.TryGetValue(type, out var list))
                {
                    machineInstancesOfType[type] = list = new List<string>();
                }

                list.Add(S(mi, "MACHINE_INSTANCE_ID"));
            }

            var machineWip = new Dictionary<string, (string OpId, double Processed)>();
            foreach (var mi in Items(root, "MACHINE_INSTANCE"))
            {
                foreach (var w in Items(mi, "WORK_IN_PROCESS_STATUS"))
                {
                    machineWip[S(w, "WORK_IN_PROCESS_STATUS_ID")] = (S(w, "OPERATION_INFO_ID"), D(w, "PROCESSED_TIME"));
                }
            }

            var handlingOfMaterial = new Dictionary<string, JsonArray>();
            foreach (var mh in Items(root, "MATERIAL_HANDLING_INFO"))
            {
                var arr = mh?["MATERIAL_HANDLING_OPERATION_INFO"] as JsonArray;
                if (arr != null)
                {
                    handlingOfMaterial[S(mh, "MATERIAL_ID")] = arr;
                }
            }

            var pinnedHandling = new Dictionary<string, (string Mhe, string CurrentOp, string CompletedOp)>();
            foreach (var mi in Items(root, "MHE_INSTANCE"))
            {
                string mheId = S(mi, "MHE_INSTANCE_ID");
                var statuses = new List<JsonNode?>();
                if (mi?["MATERIAL_HANDLING_STATUS"] is JsonArray statusArray)
                {
                    statuses.AddRange(statusArray);
                }
                else if (mi?["MATERIAL_HANDLING_STATUS"] is JsonObject statusObject)
                {
                    statuses.Add(statusObject);
                }

                foreach (var st in statuses)
                {
                    string currentOp = S(st, "HANDLING_OPERATION_INFO_ID");
                    foreach (string listName in new[]
                    {
                        "HANDLING_MATERIAL_INFO",
                        "RETRIEVING_MATERIAL_INFO"
                    }

                    )
                    {
                        foreach (var item in Items(st, listName))
                        {
                            string label = listName == "HANDLING_MATERIAL_INFO" ? S(item, "HANDLING_MATERIAL_INFO_ID") : S(item, "RETRIEVING_MATERIAL_INFO_ID");
                            if (label.Length > 0)
                            {
                                pinnedHandling[label] = (mheId, currentOp, S(item, "COMPLETED_HANDLING_OPERATION_INFO_ID"));
                            }
                        }
                    }
                }
            }

            var locInfoIdOfCell = new Dictionary<int, string>();
            foreach (var asrs in Items(root, "ASRS_INSTANCE"))
            {
                foreach (var invEntry in Items(asrs, "INVENTORY_STATUS"))
                {
                    foreach (var loc in Items(invEntry, "LOCATION_INFO"))
                    {
                        int cell = CellOf((int)D(loc, "ROW_ID"), (int)D(loc, "COLUMN_ID"), (int)D(loc, "TIER_ID"), inst);
                        string id = S(loc, "LOCATION_INFO_ID");
                        if (id.Length > 0 && !locInfoIdOfCell.ContainsKey(cell))
                        {
                            locInfoIdOfCell[cell] = id;
                        }
                    }
                }
            }

            var productionPlanOfId = new Dictionary<string, JsonObject>();
            foreach (var plan in Items(root, "PRODUCTION_PLAN"))
            {
                if (plan is JsonObject po)
                {
                    productionPlanOfId[S(po, "PRODUCTION_PLAN_ID")] = po;
                }
            }

            var remainingPlanOfKey = new Dictionary<string, JsonObject>();
            foreach (var(section, keys)in new[]
            {
                ("PRODUCTION_REMAINING_PLAN", new[] { "WORK_IN_PROCESS_STATUS_ID" }),
                ("BUFFER_REMAINING_PLAN", new[] { "SLOT_INFO_ID", "BUFFER_SLOT_INFO_ID" }),
                ("HANDLING_REMAINING_PLAN", new[] { "HANDLING_MATERIAL_INFO_ID", "RETRIEVING_MATERIAL_INFO_ID", "MATERIAL_HANDLING_STATUS_ID" }),
            }

            )
            {
                foreach (var plan in Items(root, section))
                {
                    if (plan is not JsonObject po)
                    {
                        continue;
                    }

                    string k = string.Empty;
                    foreach (string key in keys)
                    {
                        k = S(po, key);
                        if (k.Length > 0)
                        {
                            break;
                        }
                    }

                    if (k.Length > 0)
                    {
                        remainingPlanOfKey[k] = po;
                    }
                    else
                    {
                        warn.Add(section + " entry '" + S(po, section + "_ID") + "' has no state-item id (" + string.Join(" / ", keys) + " all empty); its schedule cannot be written.");
                    }
                }
            }

            foreach (string section in new[]
            {
                "PRODUCTION_PLAN",
                "PRODUCTION_REMAINING_PLAN",
                "BUFFER_REMAINING_PLAN",
                "HANDLING_REMAINING_PLAN"
            }

            )
            {
                foreach (var plan in Items(root, section))
                {
                    if (plan is JsonObject po)
                    {
                        po["PRODUCTION_SCHEDULE"] = new JsonArray();
                        po["HANDLING_SCHEDULE"] = new JsonArray();
                    }
                }
            }

            JsonArray Bucket(JsonObject plan, string field)
            {
                if (plan[field] is not JsonArray)
                {
                    plan[field] = new JsonArray();
                }

                return (JsonArray)plan[field]!;
            }

            bool[] asrsScheduled = new bool[2 * m];
            foreach (int op in solution.AsrsSequence)
            {
                if (op >= 0 && op < 2 * m)
                {
                    asrsScheduled[op] = true;
                }
            }

            var storageHsId = new string[m];
            for (int i = 0; i < m; i++)
            {
                string label = inst.JobLabel[i];
                string material = inst.JobMaterial[i];
                string san = label.Replace("#", "_");
                JsonObject? plan = null;
                if (remainingPlanOfKey.TryGetValue(label, out JsonObject? rp))
                {
                    plan = rp;
                }
                else
                {
                    int hash = label.LastIndexOf('#');
                    string planId = hash > 0 ? label.Substring(0, hash) : label;
                    if (productionPlanOfId.TryGetValue(planId, out JsonObject? pp))
                    {
                        plan = pp;
                    }
                }

                if (plan == null)
                {
                    warn.Add("Job '" + label + "' matches no plan bucket; its schedule is NOT in the output." + " (No PRODUCTION_PLAN with that id, and no *_REMAINING_PLAN naming it.)");
                    continue;
                }

                var prodArr = Bucket(plan, "PRODUCTION_SCHEDULE");
                var handArr = Bucket(plan, "HANDLING_SCHEDULE");
                int prodIdx = prodArr.Count;
                int handIdx = handArr.Count;
                string retLocId = string.Empty;
                if (asrsScheduled[i] && solution.AsrsLocation[i] >= 0)
                {
                    locInfoIdOfCell.TryGetValue(solution.AsrsLocation[i], out retLocId!);
                    retLocId ??= string.Empty;
                }

                var legs = ClassifyHandling(material, handlingOfMaterial, ResourceOf, warn);
                void AddHandling(string opId, string instanceId, double start, double end)
                {
                    handIdx++;
                    var e = new JsonObject
                    {
                        ["HANDLING_SCHEDULE_ID"] = "MHS_" + san + "_" + handIdx.ToString("000"),
                        ["HANDLING_OPERATION_INFO_ID"] = opId,
                        ["MHE_INSTANCE_ID"] = instanceId,
                        ["START_DATETIME"] = Iso(inst.HorizonStart, start),
                        ["END_DATETIME"] = Iso(inst.HorizonStart, end),
                    };
                    if (retLocId.Length > 0)
                    {
                        e["RETRIEVAL_LOCATION_INFO_ID"] = retLocId;
                    }

                    handArr.Add(e);
                }

                string ShuttleInstance(string shuttleType, int cell)
                {
                    if (!mheInstancesOfType.TryGetValue(shuttleType, out var instances) || instances.Count == 0)
                    {
                        return shuttleType;
                    }

                    int tier = inst.RackWidth * inst.RackDepth > 0 ? cell / (inst.RackWidth * inst.RackDepth) : 0;
                    return instances[Math.Min(Math.Max(tier, 0), instances.Count - 1)];
                }

                string FirstInstance(string type)
                {
                    return mheInstancesOfType.TryGetValue(type, out var instances) && instances.Count > 0 ? instances[0] : type;
                }

                pinnedHandling.TryGetValue(label, out var progress);
                void EmitCraneWindow(JsonNode? [] orderedLegs, double t0, double t1, int cell)
                {
                    var remaining = new List<JsonNode>();
                    bool skipping = progress.CompletedOp != null && progress.CompletedOp.Length > 0 && orderedLegs.Any(l => l != null && HandlingOpId(l) == progress.CompletedOp);
                    foreach (var leg in orderedLegs)
                    {
                        if (leg == null)
                        {
                            continue;
                        }

                        if (skipping)
                        {
                            if (HandlingOpId(leg) == progress.CompletedOp)
                            {
                                skipping = false;
                            }

                            continue;
                        }

                        remaining.Add(leg);
                    }

                    if (remaining.Count == 0)
                    {
                        return;
                    }

                    double[] weight = remaining.Select(l => D(l, "LOADING_TIME") + D(l, "UNLOADING_TIME")).ToArray();
                    double total = weight.Sum();
                    double window = Math.Max(0.0, t1 - t0);
                    var duration = new double[remaining.Count];
                    if (total <= 0.0)
                    {
                        for (int x = 0; x < duration.Length; x++)
                        {
                            duration[x] = window / duration.Length;
                        }
                    }
                    else if (total <= window)
                    {
                        Array.Copy(weight, duration, weight.Length);
                        int shuttleIndex = remaining.FindIndex(l => ResourceOf(S(l, "MHE_TYPE_ID")) == "SHUTTLE");
                        duration[shuttleIndex >= 0 ? shuttleIndex : duration.Length - 1] += window - total;
                    }
                    else
                    {
                        for (int x = 0; x < duration.Length; x++)
                        {
                            duration[x] = window * weight[x] / total;
                        }
                    }

                    double cursor = t0;
                    for (int x = 0; x < remaining.Count; x++)
                    {
                        string type = S(remaining[x], "MHE_TYPE_ID");
                        string opId = HandlingOpId(remaining[x]);
                        bool isInProgressLeg = progress.Mhe != null && progress.Mhe.Length > 0 && opId == progress.CurrentOp;
                        string instance = isInProgressLeg ? progress.Mhe : ResourceOf(type) == "SHUTTLE" ? ShuttleInstance(type, cell) : FirstInstance(type);
                        double end = x == remaining.Count - 1 ? t1 : cursor + duration[x];
                        double start = isInProgressLeg && x == 0 ? 0.0 : cursor;
                        AddHandling(opId, instance, start, end);
                        cursor = end;
                    }
                }

                if (asrsScheduled[i])
                {
                    EmitCraneWindow(new[] { legs.RetShuttle, legs.OutLifter }, schedule.AsrsStart[i], schedule.AsrsCompletion[i], solution.AsrsLocation[i]);
                }

                int amrOut = solution.RobotOpAmr[m + i] >= 0 ? solution.RobotOpAmr[m + i] : solution.RobotOpAmr[i];
                if (amrOut >= 0 && solution.RobotOpAmr[m + i] >= 0)
                {
                    double t0 = solution.RobotOpAmr[i] >= 0 ? schedule.RobotStart[i] : schedule.RobotStart[m + i];
                    double t1 = schedule.RobotCompletion[m + i];
                    var op = PickAmrLeg(legs, amrTypeOfInstance, inst.AmrLabel[amrOut], outbound: true);
                    if (op != null)
                    {
                        AddHandling(HandlingOpId(op), inst.AmrLabel[amrOut], t0, t1);
                    }
                }

                int k = solution.JobWorkstation[i];
                if (inst.u_ws_i[i] > 0.5 && k >= 0)
                {
                    string product = productOfMaterial.TryGetValue(material, out string? pr) ? pr : material;
                    var remaining = RemainingOps(product, label, opsOfProduct, machineWip, warn);
                    double core = schedule.ProcessStart[i] + inst.s_in_ik[i][k];
                    double total = remaining.Sum(x => x.Seconds);
                    if (Math.Abs(total - inst.p_ik[i][k]) > 1.0)
                    {
                        warn.Add("Job '" + label + "': remaining op seconds " + Convert.ToString(Math.Round(total, 1)) + " != model p " + Convert.ToString(Math.Round(inst.p_ik[i][k], 1)) + "; timeline split kept proportional to the ops.");
                    }

                    foreach (var op in remaining)
                    {
                        prodIdx++;
                        string machineInstance = ResolveMachine(op.MachineType, k, inst, machineInstancesOfType);
                        var e = new JsonObject
                        {
                            ["PRODUCTION_SCHEDULE_ID"] = "PS_" + san + "_" + prodIdx.ToString("000"),
                            ["OPERATION_INFO_ID"] = op.OpId,
                            ["MACHINE_INSTANCE_ID"] = machineInstance,
                            ["START_DATETIME"] = Iso(inst.HorizonStart, core),
                            ["END_DATETIME"] = Iso(inst.HorizonStart, core + op.Seconds),
                        };
                        if (retLocId.Length > 0)
                        {
                            e["RETRIEVAL_LOCATION_INFO_ID"] = retLocId;
                        }

                        prodArr.Add(e);
                        core += op.Seconds;
                    }
                }

                int amrBack = solution.RobotOpAmr[3 * m + i] >= 0 ? solution.RobotOpAmr[3 * m + i] : solution.RobotOpAmr[2 * m + i];
                if (amrBack >= 0 && solution.RobotOpAmr[3 * m + i] >= 0)
                {
                    double t0 = solution.RobotOpAmr[2 * m + i] >= 0 ? schedule.RobotStart[2 * m + i] : schedule.RobotStart[3 * m + i];
                    double t1 = schedule.RobotCompletion[3 * m + i];
                    var op = PickAmrLeg(legs, amrTypeOfInstance, inst.AmrLabel[amrBack], outbound: false);
                    if (op != null)
                    {
                        AddHandling(HandlingOpId(op), inst.AmrLabel[amrBack], t0, t1);
                    }
                }

                if (asrsScheduled[m + i])
                {
                    EmitCraneWindow(new[] { legs.InLifter, legs.StoShuttle }, schedule.AsrsStart[m + i], schedule.AsrsCompletion[m + i], solution.AsrsLocation[m + i]);
                    storageHsId[i] = "MHS_" + san + "_" + handIdx.ToString("000");
                }
            }

            var storageScheduleOfMaterial = new Dictionary<string, JsonObject>();
            foreach (var sp in Items(root, "STORAGE_PLAN"))
            {
                foreach (var ss in Items(sp, "STORAGE_SCHEDULE"))
                {
                    if (ss is JsonObject so && !storageScheduleOfMaterial.ContainsKey(S(so, "MATERIAL_ID")))
                    {
                        storageScheduleOfMaterial[S(so, "MATERIAL_ID")] = so;
                    }
                }
            }

            if (root["STORAGE_PLAN"] is not JsonArray)
            {
                root["STORAGE_PLAN"] = new JsonArray();
            }

            var storagePlans = (JsonArray)root["STORAGE_PLAN"]!;
            string asrsInstanceId = "ASRS_A_001";
            foreach (var ai in Items(root, "ASRS_INSTANCE"))
            {
                string aid = S(ai, "ASRS_INSTANCE_ID");
                if (!string.IsNullOrEmpty(aid))
                {
                    asrsInstanceId = aid;
                    break;
                }
            }

            var createdSchedules = new List<JsonObject>();
            JsonObject ScheduleFor(string material)
            {
                if (storageScheduleOfMaterial.TryGetValue(material, out JsonObject? found))
                {
                    return found;
                }

                string suffix = material.StartsWith("MT_", StringComparison.Ordinal) ? material.Substring(3) : material;
                var ss2 = new JsonObject
                {
                    ["STORAGE_SCHEDULE_ID"] = "SS_" + suffix,
                    ["MATERIAL_ID"] = material,
                    ["STOCK_NUM"] = "0",
                    ["STORAGE_LOCATION_INFO"] = new JsonArray(),
                };
                storagePlans.Add(new JsonObject { ["STORAGE_PLAN_ID"] = "SP_" + suffix + "_" + (storagePlans.Count + 1).ToString("000"), ["ASRS_INSTANCE_ID"] = asrsInstanceId, ["STORAGE_SCHEDULE"] = new JsonArray { ss2 }, });
                storageScheduleOfMaterial[material] = ss2;
                createdSchedules.Add(ss2);
                return ss2;
            }

            var clearedSloc = new HashSet<JsonObject>();
            for (int i = 0; i < m; i++)
            {
                if (!asrsScheduled[m + i] || solution.AsrsLocation[m + i] < 0)
                {
                    continue;
                }

                JsonObject ss = ScheduleFor(inst.JobMaterial[i]);
                if (clearedSloc.Add(ss) || ss["STORAGE_LOCATION_INFO"] is not JsonArray)
                {
                    ss["STORAGE_LOCATION_INFO"] = new JsonArray();
                }

                var arr = (JsonArray)ss["STORAGE_LOCATION_INFO"]!;
                int cell = solution.AsrsLocation[m + i];
                int wd = inst.RackWidth * inst.RackDepth;
                var e = new JsonObject
                {
                    ["STORAGE_LOCATION_INFO_ID"] = "SLOC_" + inst.JobLabel[i].Replace("#", "_"),
                    ["ROW_ID"] = cell % inst.RackWidth,
                    ["COLUMN_ID"] = (cell / inst.RackWidth) % inst.RackDepth,
                    ["TIER_ID"] = wd > 0 ? cell / wd : 0,
                };
                if (!string.IsNullOrEmpty(storageHsId[i]))
                {
                    e["COMPLETED_HANDLING_SCHEDULE_ID"] = storageHsId[i];
                }

                arr.Add(e);
            }

            foreach (var ss3 in createdSchedules)
            {
                int n = ss3["STORAGE_LOCATION_INFO"] is JsonArray a3 ? a3.Count : 0;
                ss3["STOCK_NUM"] = n.ToString();
            }

            if (createdSchedules.Count > 0)
            {
                warn.Add("STORAGE_PLAN had no schedule for " + createdSchedules.Count + " material(s); created them (ASRS " + asrsInstanceId + ").");
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".";
            string stem = Path.GetFileNameWithoutExtension(scenarioPath);
            string outPath = Path.Combine(dir, stem + "_OUTPUT_" + mode + ".json");
            File.WriteAllText(outPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            foreach (string w in warn)
            {
                Console.WriteLine("PLAN WRITEBACK WARNING: " + w);
            }

            return outPath;
        }

        sealed class HandlingLegs
        {
            public JsonNode? RetShuttle, OutLifter, InLifter, StoShuttle;
            public List<JsonNode> AmrOps = new List<JsonNode>();
        }

        static HandlingLegs ClassifyHandling(string material, Dictionary<string, JsonArray> handlingOfMaterial, Func<string, string> resourceOf, List<string> warn)
        {
            var legs = new HandlingLegs();
            if (!handlingOfMaterial.TryGetValue(material, out JsonArray? arr))
            {
                warn.Add("Material " + material + " has no MATERIAL_HANDLING_INFO; handling legs skipped.");
                return legs;
            }

            var shuttles = new List<JsonNode>();
            var lifters = new List<JsonNode>();
            foreach (var op in arr)
            {
                if (op == null)
                {
                    continue;
                }

                switch (resourceOf(S(op, "MHE_TYPE_ID")))
                {
                    case "SHUTTLE":
                        shuttles.Add(op);
                        break;
                    case "LIFTER":
                        lifters.Add(op);
                        break;
                    case "AMR":
                        legs.AmrOps.Add(op);
                        break;
                }
            }

            legs.RetShuttle = shuttles.FirstOrDefault();
            legs.StoShuttle = shuttles.LastOrDefault();
            legs.OutLifter = lifters.FirstOrDefault();
            legs.InLifter = lifters.LastOrDefault();
            return legs;
        }

        static JsonNode? PickAmrLeg(HandlingLegs legs, Dictionary<string, string> amrTypeOfInstance, string amrInstanceId, bool outbound)
        {
            string type = amrTypeOfInstance.TryGetValue(amrInstanceId, out string? t) ? t : string.Empty;
            var ofType = legs.AmrOps.Where(op => S(op, "MHE_TYPE_ID") == type).ToList();
            if (ofType.Count == 0)
            {
                ofType = legs.AmrOps;
            }

            if (ofType.Count == 0)
            {
                return null;
            }

            return outbound ? ofType[0] : ofType[Math.Min(1, ofType.Count - 1)];
        }

        static List<(string OpId, string MachineType, double Seconds)> RemainingOps(string product, string label, Dictionary<string, List<(string OpId, string MachineType, double Seconds)>> opsOfProduct, Dictionary<string, (string OpId, double Processed)> machineWip, List<string> warn)
        {
            if (!opsOfProduct.TryGetValue(product, out var full))
            {
                warn.Add("Product " + product + " has no PROCESS_INFO; production schedule of '" + label + "' skipped.");
                return new List<(string, string, double)>();
            }

            if (!machineWip.TryGetValue(label, out var wip))
            {
                return new List<(string, string, double)>(full);
            }

            var remaining = new List<(string, string, double)>();
            bool reached = false;
            foreach (var op in full)
            {
                if (!reached && op.OpId == wip.OpId)
                {
                    reached = true;
                    double left = Math.Max(0.0, op.Seconds - wip.Processed);
                    if (left > 0)
                    {
                        remaining.Add((op.OpId, op.MachineType, left));
                    }

                    continue;
                }

                if (reached)
                {
                    remaining.Add(op);
                }
            }

            if (!reached)
            {
                warn.Add("Machine WIP '" + label + "': its operation " + wip.OpId + " is not in the route of " + product + "; full route written.");
                return new List<(string, string, double)>(full);
            }

            return remaining;
        }

        static string ResolveMachine(string machineType, int station, ScenarioInstance inst, Dictionary<string, List<string>> machineInstancesOfType)
        {
            if (!machineInstancesOfType.TryGetValue(machineType, out var candidates) || candidates.Count == 0)
            {
                return machineType;
            }

            string letter = station >= 0 && station < inst.WorkstationLabel.Length ? inst.WorkstationLabel[station] : string.Empty;
            foreach (string cand in candidates)
            {
                if (inst.MachineStationMap.TryGetValue(cand, out string? st) && st == letter)
                {
                    return cand;
                }
            }

            return candidates[0];
        }

        static int CellOf(int row, int col, int tier, ScenarioInstance inst)
        {
            return tier * (inst.RackWidth * inst.RackDepth) + col * inst.RackWidth + row;
        }

        static string Iso(DateTime horizon, double seconds)
        {
            return horizon.AddSeconds(seconds).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        static IEnumerable<JsonNode?> Items(JsonNode? node, string field)
        {
            if (node?[field] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    yield return item;
                }
            }
        }

        static string HandlingOpId(JsonNode? op)
        {
            string id = S(op, "OPERATION_INFO_ID");
            return id.Length > 0 ? id : S(op, "MATERIAL_HANDLING_OPERATION_INFO_ID");
        }

        static string S(JsonNode? node, string field)
        {
            var v = node?[field];
            if (v is JsonValue jv && jv.TryGetValue(out string? s))
            {
                return s;
            }

            return v?.ToString() ?? string.Empty;
        }

        static double D(JsonNode? node, string field)
        {
            var v = node?[field];
            if (v is JsonValue jv)
            {
                if (jv.TryGetValue(out double d0))
                {
                    return d0;
                }

                if (jv.TryGetValue(out string? s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d1))
                {
                    return d1;
                }
            }

            return 0.0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    public class ScenarioConfig
    {
        public Dictionary<string, string> MachineStation { get; set; } = new Dictionary<string, string>
        {
            {
                "CNC_A_001",
                "A"
            },
            {
                "CNC_B_001",
                "B"
            },
            {
                "CNC_A_002",
                "C"
            },
            {
                "CNC_D_001",
                "D"
            },
            {
                "CNC_E_001",
                "D"
            },
        };
        public double DefaultTravelSeconds { get; set; } = 15.0;
        public Dictionary<string, double[]> NodePositions { get; set; } = new Dictionary<string, double[]>
        {
            {
                "A_IN",
                new[]
                {
                    21600.187,
                    105.890
                }
            },
            {
                "A_OUT",
                new[]
                {
                    21121.879,
                    100.910
                }
            },
            {
                "B_IN",
                new[]
                {
                    13584.788,
                    -100.466
                }
            },
            {
                "B_OUT",
                new[]
                {
                    13111.842,
                    779.127
                }
            },
            {
                "C_IN",
                new[]
                {
                    157.710,
                    -59.515
                }
            },
            {
                "C_OUT",
                new[]
                {
                    -312.742,
                    824.133
                }
            },
            {
                "D_IN",
                new[]
                {
                    -13724.444,
                    9970.285
                }
            },
            {
                "D_OUT",
                new[]
                {
                    -13724.444,
                    9589.351
                }
            },
            {
                "ASRS_OUT",
                new[]
                {
                    21502.393,
                    9463.652
                }
            },
            {
                "ASRS_IN",
                new[]
                {
                    21522.051,
                    19066.327
                }
            },
        };
        public double MachineLoadSeconds { get; set; } = 8.0;
        public double MachineUnloadSeconds { get; set; } = 8.0;
        public double CellWidthMm { get; set; } = 1000.0;
        public double CellDepthMm { get; set; } = 1000.0;
        public double LevelPitchMm { get; set; } = 2250.0;
        public double AccelMmPerS2 { get; set; } = 800.0;
        public double DecelMmPerS2 { get; set; } = 800.0;
        public bool RetrieveAllInventory { get; set; } = true;
        public string? HorizonStart { get; set; }
        public int AsrsOutBufferCapacity { get; set; } = 1;
        public int AsrsInBufferCapacity { get; set; } = 1;
        public int RackWidth { get; set; }
        public int RackDepth { get; set; }
        public int RackLevels { get; set; }
        public bool DropStockShortOrders { get; set; } = true;
    }

    public class ScenarioInstance
    {
        public int m, n, r, numL;
        public double[] c_h = Array.Empty<double>();
        public double[][] p_ik = Array.Empty<double[]>();
        public double[] q_i = Array.Empty<double>();
        public double[][][] s_ikh = Array.Empty<double[][]>();
        public double[][][] t_klh = Array.Empty<double[][]>();
        public int[][] E_ik = Array.Empty<int[]>();
        public double[][] R_if = Array.Empty<double[]>();
        public double[] d_i = Array.Empty<double>();
        public double[] w_i = Array.Empty<double>();
        public double[] u_rob_i = Array.Empty<double>();
        public double[] u_ws_i = Array.Empty<double>();
        public double[] u_asrs_i = Array.Empty<double>();
        public double[][] W_n_ik = Array.Empty<double[]>();
        public double[][] W_r_ih = Array.Empty<double[]>();
        public double[][] W_a_if = Array.Empty<double[]>();
        public double[][][][] r_ijfg = Array.Empty<double[][][]>();
        public double[][] s_in_ik = Array.Empty<double[]>();
        public double[][] s_out_ik = Array.Empty<double[]>();
        public int[] b_in_k = Array.Empty<int>();
        public int[] b_out_k = Array.Empty<int>();
        public double[][][] W_in_ikb = Array.Empty<double[][]>();
        public double[][][] W_out_ikb = Array.Empty<double[][]>();
        public int[] AsrsPinnedPrefix = Array.Empty<int>();
        public double[] MachineReadyAt = Array.Empty<double>();
        public NodeLayout Layout = NodeLayout.Compact(0);
        public int AsrsOutBufferCapacity = 1;
        public int AsrsInBufferCapacity = 1;
        public string[] JobLabel = Array.Empty<string>();
        public string[] JobMaterial = Array.Empty<string>();
        public string[] WorkstationLabel = Array.Empty<string>();
        public string[] AmrLabel = Array.Empty<string>();
        public DateTime HorizonStart;
        public List<string> Warnings = new List<string>();
        public int RackWidth = 1, RackDepth = 1, RackLevels = 1;
        public Dictionary<string, string> MachineStationMap = new Dictionary<string, string>();
    }

    public static class ScenarioLoader
    {
        public static ScenarioInstance Load(string scenarioPath, string? configPath = null)
        {
            var config = LoadConfig(scenarioPath, configPath);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(scenarioPath));
            JsonElement root = doc.RootElement;
            var inst = new ScenarioInstance();
            var warn = inst.Warnings;
            var(materialWeight, defaultWeight, weightSource) = LoadWeights(scenarioPath);
            if (weightSource.Length > 0)
            {
                warn.Add("job weights from " + weightSource + " (" + Convert.ToString(materialWeight.Count) + " materials, default " + Convert.ToString(defaultWeight) + ")");
            }

            var machineTypeOf = new Dictionary<string, string>();
            foreach (var mi in Arr(root, "MACHINE_INSTANCE"))
            {
                machineTypeOf[Str(mi, "MACHINE_INSTANCE_ID")] = Str(mi, "MACHINE_TYPE_ID");
            }

            var bufferCapOfType = new Dictionary<string, int>();
            foreach (var bt in Arr(root, "BUFFER_TYPE"))
            {
                bufferCapOfType[Str(bt, "BUFFER_TYPE_ID")] = (int)Num(bt, "CAPACITY", 1);
            }

            var stationLetters = new List<string>();
            var stationInCap = new Dictionary<string, int>();
            var stationOutCap = new Dictionary<string, int>();
            var bufferInstanceStation = new Dictionary<string, (string station, bool isIn)>();
            foreach (var bi in Arr(root, "BUFFER_INSTANCE"))
            {
                string id = Str(bi, "BUFFER_INSTANCE_ID");
                string typeId = Str(bi, "BUFFER_TYPE_ID");
                var parts = typeId.Split('_');
                if (parts.Length < 3)
                {
                    warn.Add("Buffer type id '" + typeId + "' does not follow BT_{station}_{IN|OUT}; ignored.");
                    continue;
                }

                string station = string.Join("_", parts.Skip(1).Take(parts.Length - 2));
                bool isIn = parts[^1].Equals("IN", StringComparison.OrdinalIgnoreCase);
                if (!stationLetters.Contains(station))
                {
                    stationLetters.Add(station);
                }

                int cap = bufferCapOfType.TryGetValue(typeId, out int c) ? c : 1;
                if (isIn)
                    stationInCap[station] = cap;
                else
                    stationOutCap[station] = cap;
                bufferInstanceStation[id] = (station, isIn);
            }

            stationLetters.Sort(StringComparer.Ordinal);
            inst.n = stationLetters.Count;
            var stationIndex = stationLetters.Select((s, idx) => (s, idx)).ToDictionary(x => x.s, x => x.idx);
            inst.WorkstationLabel = stationLetters.ToArray();
            var stationsOfMachineType = new Dictionary<string, HashSet<string>>();
            foreach (var(machine, type)in machineTypeOf)
            {
                if (!config.MachineStation.TryGetValue(machine, out string? st) || !stationIndex.ContainsKey(st))
                {
                    throw new ArgumentException("Machine instance '" + machine + "' has no station mapping. Add it to ScenarioConfig.MachineStation (stations: " + string.Join(",", stationLetters) + ").");
                }

                if (!stationsOfMachineType.TryGetValue(type, out var set))
                {
                    stationsOfMachineType[type] = set = new HashSet<string>();
                }

                set.Add(st);
            }

            var mheTypes = new Dictionary<string, JsonElement>();
            foreach (var mt in Arr(root, "MHE_TYPE"))
            {
                mheTypes[Str(mt, "MHE_TYPE_ID")] = mt;
            }

            var amrIds = new List<string>();
            var mheInstances = new List<JsonElement>();
            foreach (var mh in Arr(root, "MHE_INSTANCE"))
            {
                mheInstances.Add(mh);
                string typeId = Str(mh, "MHE_TYPE_ID");
                string resType = mheTypes.TryGetValue(typeId, out var mt) ? Str(mt, "RESOURCE_TYPE") : "";
                if (resType.Equals("AMR", StringComparison.OrdinalIgnoreCase))
                {
                    amrIds.Add(Str(mh, "MHE_INSTANCE_ID"));
                }
            }

            amrIds.Sort(StringComparer.Ordinal);
            inst.r = Math.Max(1, amrIds.Count);
            inst.AmrLabel = amrIds.ToArray();
            var amrIndex = amrIds.Select((a, idx) => (a, idx)).ToDictionary(x => x.a, x => x.idx);
            var stepsOfProduct = new Dictionary<string, List<(string type, double sec, string opId)>>();
            foreach (var pp in Arr(root, "PROCESS_INFO").OrderBy(p => Str(p, "PROCESS_INFO_ID"), StringComparer.Ordinal))
            {
                string product = Str(pp, "PRODUCT_ID");
                foreach (var op in Arr(pp, "OPERATION_INFO"))
                {
                    if (!stepsOfProduct.TryGetValue(product, out var list))
                    {
                        stepsOfProduct[product] = list = new List<(string, double, string)>();
                    }

                    list.Add((Str(op, "MACHINE_TYPE_ID"), Num(op, "PROCESSING_TIME", 0), Str(op, "OPERATION_INFO_ID")));
                }
            }

            var materialOfProduct = new Dictionary<string, string>();
            var productOfMaterial = new Dictionary<string, string>();
            foreach (var pi in Arr(root, "PRODUCT_INFO"))
            {
                string product = Str(pi, "PRODUCT_ID");
                foreach (var mi in Arr(pi, "MATERIAL_INFO"))
                {
                    string mat = Str(mi, "MATERIAL_ID");
                    materialOfProduct[product] = mat;
                    productOfMaterial[mat] = product;
                }
            }

            var productOfOperation = new Dictionary<string, string>();
            foreach (var(product, steps)in stepsOfProduct)
            {
                foreach (var s in steps)
                {
                    productOfOperation[s.opId] = product;
                }
            }

            var eligibleStations = new Dictionary<string, List<string>>();
            var processSeconds = new Dictionary<string, double>();
            foreach (var(product, steps)in stepsOfProduct)
            {
                HashSet<string>? common = null;
                double total = 0.0;
                foreach (var s in steps)
                {
                    total += s.sec;
                    var st = stationsOfMachineType.TryGetValue(s.type, out var set) ? set : new HashSet<string>();
                    common = common == null ? new HashSet<string>(st) : new HashSet<string>(common.Intersect(st));
                }

                if (common == null || common.Count == 0)
                {
                    throw new ArgumentException("Product '" + product + "' routes through stations with no common buffer station; multi-station routing is not supported by the model.");
                }

                eligibleStations[product] = common.OrderBy(x => x, StringComparer.Ordinal).ToList();
                processSeconds[product] = total;
            }

            var amrLoadSec = new Dictionary<string, double>();
            var amrUnloadSec = new Dictionary<string, double>();
            foreach (var mhi in Arr(root, "MATERIAL_HANDLING_INFO"))
            {
                string mat = Str(mhi, "MATERIAL_ID");
                foreach (var op in Arr(mhi, "MATERIAL_HANDLING_OPERATION_INFO"))
                {
                    string typeId = Str(op, "MHE_TYPE_ID");
                    string resType = mheTypes.TryGetValue(typeId, out var mt) ? Str(mt, "RESOURCE_TYPE") : typeId;
                    if (resType.StartsWith("AMR", StringComparison.OrdinalIgnoreCase) || typeId.StartsWith("AMR", StringComparison.OrdinalIgnoreCase))
                    {
                        amrLoadSec[mat] = Num(op, "LOADING_TIME", 8);
                        amrUnloadSec[mat] = Num(op, "UNLOADING_TIME", 8);
                    }
                }
            }

            JsonElement asrsInstance = default;
            bool hasAsrs = false;
            foreach (var key in new[]
            {
                "ASRS_INSTANCE",
                "WAREHOUSE_INSTANCE"
            }

            )
            {
                var arr = Arr(root, key).ToList();
                if (arr.Count > 0)
                {
                    asrsInstance = arr[0];
                    hasAsrs = true;
                    break;
                }
            }

            if (!hasAsrs)
            {
                throw new ArgumentException("The scenario contains no ASRS_INSTANCE / WAREHOUSE_INSTANCE.");
            }

            int maxRow = 0, maxCol = 0, maxTier = 0;
            var inventoryOfMaterial = new Dictionary<string, List<(int row, int col, int tier)>>();
            var locInfoRct = new Dictionary<string, (int row, int col, int tier)>();
            var completedCells = new List<(int row, int col, int tier)>();
            foreach (var invEntry in Arr(asrsInstance, "INVENTORY_STATUS"))
            {
                string mat = Str(invEntry, "MATERIAL_ID");
                foreach (var loc in Arr(invEntry, "LOCATION_INFO"))
                {
                    int row = (int)Num(loc, "ROW_ID", 0);
                    int col = (int)Num(loc, "COLUMN_ID", 0);
                    int tier = (int)Num(loc, "TIER_ID", 0);
                    maxRow = Math.Max(maxRow, row);
                    maxCol = Math.Max(maxCol, col);
                    maxTier = Math.Max(maxTier, tier);
                    string locInfoId = Str(loc, "LOCATION_INFO_ID");
                    if (locInfoId.Length > 0)
                    {
                        locInfoRct[locInfoId] = (row, col, tier);
                    }

                    if (StrOrNull(loc, "COMPLETED_HANDLING_OPERATION_INFO_ID") != null)
                    {
                        completedCells.Add((row, col, tier));
                        continue;
                    }

                    if (!inventoryOfMaterial.TryGetValue(mat, out var list))
                    {
                        inventoryOfMaterial[mat] = list = new List<(int, int, int)>();
                    }

                    list.Add((row, col, tier));
                }
            }

            if (completedCells.Count > 0)
            {
                warn.Add(Convert.ToString(completedCells.Count) + " inventory cell(s) hold finished products (COMPLETED_HANDLING_OPERATION_INFO_ID); kept in storage, not scheduled.");
            }

            foreach (var sp in Arr(root, "STORAGE_PLAN"))
            {
                foreach (var ss in Arr(sp, "STORAGE_SCHEDULE"))
                {
                    foreach (var loc in Arr(ss, "STORAGE_LOCATION_INFO"))
                    {
                        maxRow = Math.Max(maxRow, (int)Num(loc, "ROW_ID", 0));
                        maxCol = Math.Max(maxCol, (int)Num(loc, "COLUMN_ID", 0));
                        maxTier = Math.Max(maxTier, (int)Num(loc, "TIER_ID", 0));
                    }
                }
            }

            int width = maxRow + 1;
            int depth = maxCol + 1;
            int levels = maxTier + 1;
            bool rackConfigured = config.RackWidth > 0 && config.RackDepth > 0 && config.RackLevels > 0;
            if (rackConfigured)
            {
                width = Math.Max(width, config.RackWidth);
                depth = Math.Max(depth, config.RackDepth);
                levels = Math.Max(levels, config.RackLevels);
            }

            int declaredCapacity = 0;
            foreach (var at in Arr(root, "ASRS_TYPE"))
            {
                declaredCapacity = Math.Max(declaredCapacity, (int)Num(at, "CAPACITY", 0));
            }

            if (!rackConfigured && width > 0 && declaredCapacity > width * depth * levels)
            {
                int need = (int)Math.Ceiling((double)declaredCapacity / width);
                int bestDepth = depth, bestLevels = levels, bestScore = int.MaxValue;
                for (int d = depth; d <= need; d++)
                {
                    int l = Math.Max(levels, (int)Math.Ceiling((double)need / d));
                    int score = d * l * 100 + Math.Abs(d - l);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestDepth = d;
                        bestLevels = l;
                    }
                }

                warn.Add("AS/RS rack inferred from coordinates is " + Convert.ToString(width) + "x" + Convert.ToString(depth) + "x" + Convert.ToString(levels) + " = " + Convert.ToString(width * depth * levels) + " cell(s) but ASRS_TYPE declares CAPACITY " + Convert.ToString(declaredCapacity) + "; expanded to " + Convert.ToString(width) + "x" + Convert.ToString(bestDepth) + "x" + Convert.ToString(bestLevels) + " = " + Convert.ToString(width * bestDepth * bestLevels) + " cell(s).");
                depth = bestDepth;
                levels = bestLevels;
            }

            inst.numL = width * depth * levels;
            inst.RackWidth = width;
            inst.RackDepth = depth;
            inst.RackLevels = levels;
            inst.MachineStationMap = config.MachineStation;
            int LocId(int row, int col, int tier) => tier * (width * depth) + col * width + row;
            var storageLocsOfMaterial = new Dictionary<string, List<int>>();
            foreach (var sp in Arr(root, "STORAGE_PLAN"))
            {
                foreach (var ss in Arr(sp, "STORAGE_SCHEDULE"))
                {
                    string mat = Str(ss, "MATERIAL_ID");
                    foreach (var loc in Arr(ss, "STORAGE_LOCATION_INFO"))
                    {
                        if (!storageLocsOfMaterial.TryGetValue(mat, out var list))
                        {
                            storageLocsOfMaterial[mat] = list = new List<int>();
                        }

                        list.Add(LocId((int)Num(loc, "ROW_ID", 0), (int)Num(loc, "COLUMN_ID", 0), (int)Num(loc, "TIER_ID", 0)));
                    }
                }
            }

            DateTime? horizon = config.HorizonStart != null ? ParseDate(config.HorizonStart) : null;
            if (horizon == null)
            {
                foreach (var section in new[]
                {
                    "PRODUCTION_REMAINING_PLAN",
                    "HANDLING_REMAINING_PLAN",
                    "BUFFER_REMAINING_PLAN",
                    "PRODUCTION_PLAN"
                }

                )
                {
                    foreach (var plan in Arr(root, section))
                    {
                        foreach (var schedKey in new[]
                        {
                            "PRODUCTION_SCHEDULE",
                            "HANDLING_SCHEDULE"
                        }

                        )
                        {
                            foreach (var s in Arr(plan, schedKey))
                            {
                                DateTime? t = ParseDate(StrOrNull(s, "START_DATETIME"));
                                if (t != null && (horizon == null || t < horizon))
                                {
                                    horizon = t;
                                }
                            }
                        }
                    }
                }
            }

            if (horizon == null)
            {
                foreach (var bi in Arr(root, "BUFFER_INSTANCE"))
                {
                    foreach (var bs in Arr(bi, "BUFFER_STATUS"))
                    {
                        foreach (var si in Arr(bs, "SLOT_INFO"))
                        {
                            DateTime? q = ParseDate(StrOrNull(si, "QUEUE_DATETIME"));
                            if (q != null && (horizon == null || q > horizon))
                            {
                                horizon = q;
                            }
                        }
                    }
                }

                foreach (var mi0 in Arr(root, "MACHINE_INSTANCE"))
                {
                    foreach (var fs in Arr(mi0, "FAILURE_STATUS"))
                    {
                        DateTime? q = ParseDate(StrOrNull(fs, "EVENT_DATETIME"));
                        if (q != null && (horizon == null || q > horizon))
                        {
                            horizon = q;
                        }
                    }
                }

                if (horizon != null)
                {
                    warn.Add("Horizon start taken from state timestamps (no schedule START_DATETIME found): " + horizon.Value.ToString("o") + ".");
                }
            }

            if (horizon == null)
            {
                foreach (var section in new[]
                {
                    "PRODUCTION_PLAN",
                    "PRODUCTION_REMAINING_PLAN",
                    "BUFFER_REMAINING_PLAN",
                    "HANDLING_REMAINING_PLAN"
                }

                )
                {
                    foreach (var plan in Arr(root, section))
                    {
                        foreach (var d in Arr(plan, "DUE_INFO"))
                        {
                            DateTime? q = ParseDate(StrOrNull(d, "DUE_DATETIME"));
                            if (q != null && (horizon == null || q < horizon))
                            {
                                horizon = q;
                            }
                        }
                    }
                }

                if (horizon != null)
                {
                    warn.Add("Horizon start fell back to the EARLIEST DUE DATE (" + horizon.Value.ToString("o") + "); that job's due becomes 0 s. Provide HorizonStart in scenario_config.json for exact tardiness.");
                }
            }

            inst.HorizonStart = horizon ?? DateTime.MinValue;
            inst.MachineReadyAt = new double[inst.n];
            var machineTypeInfo = new Dictionary<string, JsonElement>();
            foreach (var mt2 in Arr(root, "MACHINE_TYPE"))
            {
                machineTypeInfo[Str(mt2, "MACHINE_TYPE_ID")] = mt2;
            }

            foreach (var mi2 in Arr(root, "MACHINE_INSTANCE"))
            {
                if (!Arr(mi2, "FAILURE_STATUS").Any())
                {
                    continue;
                }

                string machine2 = Str(mi2, "MACHINE_INSTANCE_ID");
                double mttr = machineTypeInfo.TryGetValue(Str(mi2, "MACHINE_TYPE_ID"), out var mt3) ? Num(mt3, "MEAN_TIME_TO_REPAIR", 0) : 0.0;
                DateTime? failEvent = null;
                foreach (var fs in Arr(mi2, "FAILURE_STATUS"))
                {
                    failEvent = ParseDate(StrOrNull(fs, "EVENT_DATETIME")) ?? failEvent;
                }

                double ready = failEvent != null && horizon != null ? Math.Max(0.0, (failEvent.Value - horizon.Value).TotalSeconds + mttr) : mttr;
                if (config.MachineStation.TryGetValue(machine2, out string? failSt) && stationIndex.TryGetValue(failSt, out int failK))
                {
                    inst.MachineReadyAt[failK] = Math.Max(inst.MachineReadyAt[failK], ready);
                    warn.Add("Machine " + machine2 + " is failed; station " + failSt + " is unavailable until t = " + Convert.ToString(Math.Round(ready, 1)) + " s.");
                }
            }

            string? mrEnv = Environment.GetEnvironmentVariable("PDP_MACHINE_READY");
            if (!string.IsNullOrWhiteSpace(mrEnv))
            {
                foreach (string part in mrEnv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] kv = part.Split('=');
                    if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out int mk) && double.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mt) && mk >= 0 && mk < inst.n)
                    {
                        inst.MachineReadyAt[mk] = Math.Max(inst.MachineReadyAt[mk], mt);
                        warn.Add("PDP_MACHINE_READY: station " + mk + " forced unavailable until t = " + mt + " s.");
                    }
                    else
                    {
                        throw new InvalidOperationException("PDP_MACHINE_READY entry '" + part + "' is not '<station>=<seconds>' with station < " + inst.n + ".");
                    }
                }
            }

            var jobs = new List<JobDraft>();
            double? DueOf(JsonElement plan)
            {
                foreach (var d in Arr(plan, "DUE_INFO"))
                {
                    DateTime? due = ParseDate(StrOrNull(d, "DUE_DATETIME"));
                    if (due != null && horizon != null)
                    {
                        return Math.Max(0.0, (due.Value - horizon.Value).TotalSeconds);
                    }
                }

                return null;
            }

            var dueOfWips = new Dictionary<string, double>();
            foreach (var plan in Arr(root, "PRODUCTION_REMAINING_PLAN"))
            {
                string key = Str(plan, "WORK_IN_PROCESS_STATUS_ID");
                double? due = DueOf(plan);
                if (key.Length > 0 && due != null)
                {
                    dueOfWips[key] = due.Value;
                }
            }

            var dueOfSlot = new Dictionary<string, double>();
            foreach (var plan in Arr(root, "BUFFER_REMAINING_PLAN"))
            {
                string key = StrOrNull(plan, "SLOT_INFO_ID") ?? Str(plan, "BUFFER_SLOT_INFO_ID");
                double? due = DueOf(plan);
                if (key.Length > 0 && due != null)
                {
                    dueOfSlot[key] = due.Value;
                }
            }

            var dueOfCarried = new Dictionary<string, double>();
            foreach (var plan in Arr(root, "HANDLING_REMAINING_PLAN"))
            {
                string key = StrOrNull(plan, "HANDLING_MATERIAL_INFO_ID") ?? StrOrNull(plan, "RETRIEVING_MATERIAL_INFO_ID") ?? Str(plan, "HANDLING_OPERATION_INFO_ID");
                double? due = DueOf(plan);
                if (key.Length > 0 && due != null)
                {
                    dueOfCarried[key] = due.Value;
                }
            }

            storageLocsOfMaterial.Clear();
            foreach (var mi in Arr(root, "MACHINE_INSTANCE"))
            {
                string machine = Str(mi, "MACHINE_INSTANCE_ID");
                foreach (var w in Arr(mi, "WORK_IN_PROCESS_STATUS"))
                {
                    string opId = Str(w, "OPERATION_INFO_ID");
                    string wipsId = Str(w, "WORK_IN_PROCESS_STATUS_ID");
                    if (!productOfOperation.TryGetValue(opId, out string? product))
                    {
                        warn.Add("WIP operation '" + opId + "' on " + machine + " has no PROCESS_INFO entry; skipped.");
                        continue;
                    }

                    double done = Num(w, "PROCESSED_TIME", 0);
                    var steps = stepsOfProduct[product];
                    int stepIdx = steps.FindIndex(x => x.opId == opId);
                    double before = steps.Take(Math.Max(0, stepIdx)).Sum(x => x.sec);
                    double remaining = Math.Max(0.0, processSeconds[product] - before - done);
                    jobs.Add(new JobDraft { Label = wipsId.Length > 0 ? wipsId : machine, Material = materialOfProduct.TryGetValue(product, out string? mmW) ? mmW : product, Product = product, State = JobState.Machine, Station = stationIndex[config.MachineStation[machine]], RemainingProcess = remaining, Due = dueOfWips.TryGetValue(wipsId, out double dW) ? dW : 0.0 });
                }
            }

            foreach (var bi in Arr(root, "BUFFER_INSTANCE"))
            {
                string bufferId = Str(bi, "BUFFER_INSTANCE_ID");
                if (!bufferInstanceStation.TryGetValue(bufferId, out var loc0))
                {
                    continue;
                }

                foreach (var bs in Arr(bi, "BUFFER_STATUS"))
                {
                    string mat = Str(bs, "MATERIAL_ID");
                    string product = productOfMaterial.TryGetValue(mat, out string? prS) ? prS : mat;
                    foreach (var si in Arr(bs, "SLOT_INFO"))
                    {
                        string slotId = Str(si, "SLOT_INFO_ID");
                        jobs.Add(new JobDraft { Label = slotId.Length > 0 ? slotId : bufferId, Material = mat, Product = product, State = loc0.isIn ? JobState.BufferIn : JobState.BufferOut, Station = stationIndex[loc0.station], Slot = (int)Num(si, "SLOT_ID", 0), RemainingProcess = loc0.isIn && processSeconds.TryGetValue(product, out double psS) ? psS : 0.0, Due = dueOfSlot.TryGetValue(slotId, out double dS) ? dS : 0.0 });
                    }
                }
            }

            var handlingLeg = new Dictionary<string, Dictionary<string, (string role, double dur)>>();
            foreach (var mhi in Arr(root, "MATERIAL_HANDLING_INFO"))
            {
                string mat2 = Str(mhi, "MATERIAL_ID");
                var map = new Dictionary<string, (string, double)>();
                int shuttleSeen = 0;
                int amrSeen = 0;
                foreach (var op in Arr(mhi, "MATERIAL_HANDLING_OPERATION_INFO"))
                {
                    string opId = StrOrNull(op, "OPERATION_INFO_ID") ?? Str(op, "MATERIAL_HANDLING_OPERATION_INFO_ID");
                    string opType = Str(op, "MHE_TYPE_ID");
                    string opRes = mheTypes.TryGetValue(opType, out var mtx) ? Str(mtx, "RESOURCE_TYPE") : opType;
                    double dur = Num(op, "LOADING_TIME", 0) + Num(op, "UNLOADING_TIME", 0);
                    string role;
                    if (opRes.StartsWith("AMR", StringComparison.OrdinalIgnoreCase))
                    {
                        role = amrSeen++ == 0 ? "Deliver" : "Return";
                    }
                    else if (opRes.StartsWith("LIFTER", StringComparison.OrdinalIgnoreCase) || opType.StartsWith("LIFTER", StringComparison.OrdinalIgnoreCase))
                    {
                        role = opType.EndsWith("B", StringComparison.OrdinalIgnoreCase) ? "LIn" : "LOut";
                    }
                    else
                    {
                        role = shuttleSeen++ == 0 ? "SOut" : "SIn";
                    }

                    map[opId] = (role, dur);
                }

                handlingLeg[mat2] = map;
            }

            foreach (var mh in mheInstances)
            {
                string mheId = Str(mh, "MHE_INSTANCE_ID");
                string typeId = Str(mh, "MHE_TYPE_ID");
                string resType = mheTypes.TryGetValue(typeId, out var mtC) ? Str(mtC, "RESOURCE_TYPE") : typeId;
                double processed = Num(mh, "PROCESSED_TIME", 0);
                bool isAmr = resType.StartsWith("AMR", StringComparison.OrdinalIgnoreCase);
                bool onLifter = resType.StartsWith("LIFTER", StringComparison.OrdinalIgnoreCase) || typeId.StartsWith("LIFTER", StringComparison.OrdinalIgnoreCase);
                foreach (var hs in Arr(mh, "MATERIAL_HANDLING_STATUS"))
                {
                    foreach (var key in new[]
                    {
                        "HANDLING_MATERIAL_INFO",
                        "RETRIEVING_MATERIAL_INFO"
                    }

                    )
                    {
                        bool retrievingList = key == "RETRIEVING_MATERIAL_INFO";
                        foreach (var it in Arr(hs, key))
                        {
                            string itemId = StrOrNull(it, "HANDLING_MATERIAL_INFO_ID") ?? StrOrNull(it, "RETRIEVING_MATERIAL_INFO_ID") ?? "";
                            string mat = Str(it, "MATERIAL_ID");
                            string product = productOfMaterial.TryGetValue(mat, out string? prH) ? prH : mat;
                            var legs = handlingLeg.TryGetValue(mat, out var lg) ? lg : new Dictionary<string, (string, double)>();
                            double DurOf(string role)
                            {
                                foreach (var v in legs.Values)
                                {
                                    if (v.Item1 == role)
                                    {
                                        return v.Item2;
                                    }
                                }

                                return 0.0;
                            }

                            string completedOp = StrOrNull(it, "COMPLETED_HANDLING_OPERATION_INFO_ID") ?? "";
                            string completedRole = legs.TryGetValue(completedOp, out var cr) ? cr.Item1 : "";
                            JobState state;
                            double asrsRemaining = 0.0;
                            if (isAmr)
                            {
                                state = completedRole == "SOut" || completedRole == "LOut" ? JobState.AmrOutbound : JobState.AmrReturn;
                            }
                            else
                            {
                                bool storing = !retrievingList && (completedRole == "Return" || completedRole == "LIn" || completedRole == "SIn");
                                state = storing ? JobState.AsrsStoring : JobState.AsrsRetrieving;
                                if (storing)
                                {
                                    asrsRemaining = onLifter ? Math.Max(0.0, DurOf("LIn") - processed) + DurOf("SIn") : Math.Max(0.0, DurOf("SIn") - processed);
                                }
                                else
                                {
                                    asrsRemaining = onLifter ? Math.Max(0.0, DurOf("LOut") - processed) : Math.Max(0.0, DurOf("SOut") - processed) + DurOf("LOut");
                                }

                                warn.Add("Material '" + mat + "' is on " + mheId + " (" + (storing ? "storage" : "retrieval") + " in progress); pinned first on the AS/RS with remaining " + Convert.ToString(Math.Round(asrsRemaining, 1)) + " s.");
                            }

                            jobs.Add(new JobDraft { Label = itemId.Length > 0 ? itemId : mheId, Material = mat, Product = product, State = state, Amr = amrIndex.TryGetValue(mheId, out int hIdx) ? hIdx : -1, RemainingProcess = state == JobState.AmrOutbound || state == JobState.AsrsRetrieving ? (processSeconds.TryGetValue(product, out double psH) ? psH : 0.0) : 0.0, PinnedRemaining = asrsRemaining, RetrievalLocInfoId = StrOrNull(it, "RETRIEVAL_LOCATION_INFO_ID") ?? "", Due = dueOfCarried.TryGetValue(itemId, out double dC) ? dC : 0.0 });
                        }
                    }
                }
            }

            foreach (var plan in Arr(root, "PRODUCTION_PLAN"))
            {
                string planId = Str(plan, "PRODUCTION_PLAN_ID");
                string material = StrOrNull(plan, "MATERIAL_ID") ?? (materialOfProduct.TryGetValue(StrOrNull(plan, "PRODUCT_ID") ?? "", out string? m0) ? m0 : null) ?? "";
                if (material.Length == 0)
                {
                    warn.Add("PRODUCTION_PLAN '" + planId + "' has no material; skipped.");
                    continue;
                }

                string product = productOfMaterial.TryGetValue(material, out string? pr3) ? pr3 : material;
                var dues = new List<double>();
                var dueWeights = new List<double>();
                double planWeight = Num(plan, "WEIGHT", double.NaN);
                foreach (var d in Arr(plan, "DUE_INFO"))
                {
                    DateTime? due = ParseDate(StrOrNull(d, "DUE_DATETIME"));
                    dues.Add(due != null && horizon != null ? Math.Max(0.0, (due.Value - horizon.Value).TotalSeconds) : 0.0);
                    dueWeights.Add(Num(d, "WEIGHT", planWeight));
                }

                if (dues.Count == 0)
                {
                    int outstanding = (int)Math.Max(0.0, Num(plan, "PRODUCTION_VOLUME", 0) - Num(plan, "CURRENT_VOLUME", 0));
                    if (outstanding == 0)
                    {
                        warn.Add("PRODUCTION_PLAN '" + planId + "' has neither DUE_INFO nor outstanding volume; skipped.");
                        continue;
                    }

                    warn.Add("PRODUCTION_PLAN '" + planId + "' has no DUE_INFO; " + Convert.ToString(outstanding) + " unit(s) taken from the volume with due = 0.");
                    for (int u = 0; u < outstanding; u++)
                    {
                        dues.Add(0.0);
                    }
                }

                for (int u = 0; u < dues.Count; u++)
                {
                    jobs.Add(new JobDraft { Label = planId + "#" + Convert.ToString(u + 1), Material = material, Product = product, State = JobState.New, RemainingProcess = processSeconds.TryGetValue(product, out double ps3) ? ps3 : 0.0, Due = dues[u], Weight = u < dueWeights.Count ? dueWeights[u] : planWeight });
                }
            }

            {
                var requestedNew = new Dictionary<string, int>();
                var reservedRetr = new Dictionary<string, int>();
                foreach (var j in jobs)
                {
                    if (j.State == JobState.New)
                    {
                        requestedNew[j.Material] = (requestedNew.TryGetValue(j.Material, out int q0) ? q0 : 0) + 1;
                    }
                    else if (j.State == JobState.AsrsRetrieving && j.RetrievalLocInfoId.Length > 0 && locInfoRct.TryGetValue(j.RetrievalLocInfoId, out var rctR) && inventoryOfMaterial.TryGetValue(j.Material, out var rawR) && rawR.Contains(rctR))
                    {
                        reservedRetr[j.Material] = (reservedRetr.TryGetValue(j.Material, out int q1) ? q1 : 0) + 1;
                    }
                }

                double idleDue = jobs.Count > 0 ? jobs.Max(x => x.Due) : 0.0;
                int extraTotal = 0;
                int shortTotal = 0;
                foreach (var(mat, cells)in inventoryOfMaterial.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    int already = requestedNew.TryGetValue(mat, out int a0) ? a0 : 0;
                    int reserved = reservedRetr.TryGetValue(mat, out int a1) ? a1 : 0;
                    int spare = cells.Count - already - reserved;
                    if (spare < 0)
                    {
                        shortTotal += -spare;
                        bool dropShort = config.DropStockShortOrders || Environment.GetEnvironmentVariable("PDP_DROP_STOCK_SHORT") == "1";
                        warn.Add("Material " + mat + ": orders need " + Convert.ToString(already + reserved) + " unit(s) but only " + Convert.ToString(cells.Count) + " are in stock; " + Convert.ToString(-spare) + " order(s) " + (dropShort ? "dropped from the instance." : "will lose their retrieval stage."));
                        if (dropShort)
                        {
                            int toDrop = -spare;
                            for (int idx = jobs.Count - 1; idx >= 0 && toDrop > 0; idx--)
                            {
                                if (jobs[idx].Material != mat || jobs[idx].State != JobState.New)
                                {
                                    continue;
                                }

                                warn.Add("  dropped '" + jobs[idx].Label + "' (no stock of " + mat + ").");
                                jobs.RemoveAt(idx);
                                toDrop--;
                            }
                        }

                        continue;
                    }

                    if (!config.RetrieveAllInventory || spare == 0)
                    {
                        if (spare > 0)
                        {
                            warn.Add("Material " + mat + ": " + Convert.ToString(spare) + " stocked unit(s) have no order and stay in the AS/RS (set RetrieveAllInventory to process them).");
                        }

                        continue;
                    }

                    string product = productOfMaterial.TryGetValue(mat, out string? prI) ? prI : mat;
                    for (int u = 0; u < spare; u++)
                    {
                        jobs.Add(new JobDraft { Label = "STOCK_" + mat + "#" + Convert.ToString(u + 1), Material = mat, Product = product, State = JobState.New, RemainingProcess = processSeconds.TryGetValue(product, out double psI) ? psI : 0.0, Due = idleDue });
                    }

                    extraTotal += spare;
                }

                if (extraTotal > 0)
                {
                    warn.Add("RetrieveAllInventory: " + Convert.ToString(extraTotal) + " unordered stock unit(s) added as jobs with due = " + Convert.ToString(Math.Round(idleDue, 0)) + " s (latest due in the scenario).");
                }

                if (shortTotal > 0)
                {
                    warn.Add("Stock shortage total: " + Convert.ToString(shortTotal) + " order(s).");
                }
            }

            int m = jobs.Count;
            int n = inst.n;
            int r = inst.r;
            int numL = inst.numL;
            inst.m = m;
            inst.c_h = new double[r];
            for (int h = 0; h < r; h++)
            {
                string typeId = Str(mheInstances.First(x => Str(x, "MHE_INSTANCE_ID") == amrIds[h]), "MHE_TYPE_ID");
                inst.c_h[h] = mheTypes.TryGetValue(typeId, out var mt) ? Num(mt, "CAPACITY", 1) : 1.0;
            }

            inst.q_i = new double[4 * m + 2];
            for (int i = 0; i < m; i++)
            {
                inst.q_i[i] = 1.0;
                inst.q_i[m + i] = -1.0;
                inst.q_i[2 * m + i] = 1.0;
                inst.q_i[3 * m + i] = -1.0;
            }

            inst.p_ik = NewJagged(m + 2, n);
            inst.s_in_ik = NewJagged(m + 2, n);
            inst.s_out_ik = NewJagged(m + 2, n);
            inst.E_ik = new int[m + 2][];
            for (int i = 0; i < m + 2; i++)
            {
                inst.E_ik[i] = new int[n + 2];
            }

            inst.W_n_ik = NewJagged(m, n);
            for (int i0 = 0; i0 < m; i0++)
            {
                for (int k0 = 0; k0 < n; k0++)
                {
                    inst.W_n_ik[i0][k0] = 1.0;
                }
            }

            inst.d_i = new double[m];
            inst.w_i = new double[m];
            inst.u_rob_i = new double[4 * m + 2];
            inst.u_ws_i = new double[m];
            inst.u_asrs_i = new double[2 * m + 2];
            inst.u_rob_i[4 * m] = 1.0;
            inst.u_rob_i[4 * m + 1] = 1.0;
            inst.u_asrs_i[2 * m] = 1.0;
            inst.u_asrs_i[2 * m + 1] = 1.0;
            inst.W_r_ih = NewJagged(4 * m, r);
            inst.W_a_if = NewJagged(2 * m, numL);
            inst.R_if = NewJagged(2 * m + 2, numL);
            inst.b_in_k = new int[n];
            inst.b_out_k = new int[n];
            for (int k = 0; k < n; k++)
            {
                inst.b_in_k[k] = stationInCap.TryGetValue(stationLetters[k], out int ci) ? ci : 1;
                inst.b_out_k[k] = stationOutCap.TryGetValue(stationLetters[k], out int co) ? co : 1;
            }

            inst.W_in_ikb = new double[m][][];
            inst.W_out_ikb = new double[m][][];
            for (int i = 0; i < m; i++)
            {
                inst.W_in_ikb[i] = new double[n][];
                inst.W_out_ikb[i] = new double[n][];
                for (int k = 0; k < n; k++)
                {
                    inst.W_in_ikb[i][k] = new double[inst.b_in_k[k]];
                    inst.W_out_ikb[i][k] = new double[inst.b_out_k[k]];
                }
            }

            inst.JobLabel = new string[m];
            inst.JobMaterial = new string[m];
            var occupied = new HashSet<int>();
            foreach (var list in inventoryOfMaterial.Values)
            {
                foreach (var(row, col, tier)in list)
                {
                    occupied.Add(LocId(row, col, tier));
                }
            }

            var completedSet = new HashSet<int>();
            foreach (var(row, col, tier)in completedCells)
            {
                occupied.Add(LocId(row, col, tier));
                completedSet.Add(LocId(row, col, tier));
            }

            foreach (var job0 in jobs)
            {
                if (job0.State == JobState.AsrsRetrieving && job0.RetrievalLocInfoId.Length > 0 && locInfoRct.TryGetValue(job0.RetrievalLocInfoId, out var rct0) && inventoryOfMaterial.TryGetValue(job0.Material, out var list0))
                {
                    list0.Remove(rct0);
                }
            }

            var inventoryCursor = inventoryOfMaterial.ToDictionary(x => x.Key, _ => 0);
            for (int i = 0; i < m; i++)
            {
                var job = jobs[i];
                inst.JobLabel[i] = job.Label;
                inst.JobMaterial[i] = job.Material;
                inst.d_i[i] = job.Due;
                inst.w_i[i] = !double.IsNaN(job.Weight) ? job.Weight : materialWeight.TryGetValue(job.Material, out double wM) ? wM : defaultWeight;
                var stations = eligibleStations.TryGetValue(job.Product, out var es) ? es : new List<string>();
                double pTotal = job.State == JobState.Machine ? job.RemainingProcess : (processSeconds.TryGetValue(job.Product, out double pt) ? pt : job.RemainingProcess);
                foreach (var st in stations)
                {
                    int k = stationIndex[st];
                    inst.p_ik[i][k] = job.State == JobState.Machine ? job.RemainingProcess : pTotal;
                    inst.s_in_ik[i][k] = config.MachineLoadSeconds;
                    inst.s_out_ik[i][k] = config.MachineUnloadSeconds;
                    inst.E_ik[i][k] = 1;
                }

                if (job.Station >= 0 && (job.State == JobState.AmrOutbound || job.State == JobState.AsrsRetrieving))
                {
                    for (int k = 0; k < n; k++)
                    {
                        if (k != job.Station)
                        {
                            inst.E_ik[i][k] = 0;
                        }
                    }

                    if (inst.E_ik[i][job.Station] != 1)
                    {
                        warn.Add("Job '" + job.Label + "' is pinned to station " + stationLetters[job.Station] + " which is not eligible for product " + job.Product + "; pin ignored.");
                        foreach (var st in stations)
                        {
                            inst.E_ik[i][stationIndex[st]] = 1;
                        }
                    }
                }

                if (stations.Count == 0 && job.State != JobState.BufferOut && job.State != JobState.AmrReturn)
                {
                    warn.Add("Job '" + job.Label + "' (product " + job.Product + ") has no eligible station.");
                }

                bool needsRetrieval = job.State == JobState.New || job.State == JobState.AsrsRetrieving;
                bool needsPickupPlus = needsRetrieval;
                bool needsDeliveryPlus = needsPickupPlus || job.State == JobState.AmrOutbound;
                bool needsProcess = needsDeliveryPlus || job.State == JobState.BufferIn || job.State == JobState.Machine;
                bool storingOnly = job.State == JobState.AsrsStoring;
                bool needsPickupMinus = !storingOnly;
                bool needsDeliveryMinus = !storingOnly;
                inst.u_asrs_i[i] = needsRetrieval ? 1.0 : 0.0;
                inst.u_rob_i[i] = needsPickupPlus ? 1.0 : 0.0;
                inst.u_rob_i[m + i] = needsDeliveryPlus ? 1.0 : 0.0;
                inst.u_ws_i[i] = needsProcess ? 1.0 : 0.0;
                inst.u_rob_i[2 * m + i] = job.State == JobState.AmrReturn || storingOnly ? 0.0 : (needsPickupMinus ? 1.0 : 0.0);
                inst.u_rob_i[3 * m + i] = needsDeliveryMinus ? 1.0 : 0.0;
                inst.u_asrs_i[m + i] = 1.0;
                switch (job.State)
                {
                    case JobState.Machine:
                        for (int k0 = 0; k0 < n; k0++)
                            inst.W_n_ik[i][k0] = 0.0;
                        inst.W_n_ik[i][job.Station] = 1.0;
                        break;
                    case JobState.BufferIn:
                        for (int k0 = 0; k0 < n; k0++)
                            inst.W_n_ik[i][k0] = 0.0;
                        inst.W_n_ik[i][job.Station] = 1.0;
                        if (job.Slot >= inst.b_in_k[job.Station])
                        {
                            warn.Add("Job '" + job.Label + "' occupies input slot " + Convert.ToString(job.Slot) + " beyond capacity; clamped.");
                        }

                        inst.W_in_ikb[i][job.Station][Math.Min(job.Slot, inst.b_in_k[job.Station] - 1)] = 1.0;
                        break;
                    case JobState.BufferOut:
                        for (int k0 = 0; k0 < n; k0++)
                            inst.W_n_ik[i][k0] = 0.0;
                        inst.W_n_ik[i][job.Station] = 1.0;
                        if (job.Slot >= inst.b_out_k[job.Station])
                        {
                            warn.Add("Job '" + job.Label + "' occupies output slot " + Convert.ToString(job.Slot) + " beyond capacity; clamped.");
                        }

                        inst.W_out_ikb[i][job.Station][Math.Min(job.Slot, inst.b_out_k[job.Station] - 1)] = 1.0;
                        break;
                    case JobState.AmrOutbound:
                        if (job.Amr >= 0)
                        {
                            inst.W_r_ih[m + i][job.Amr] = 1.0;
                        }

                        break;
                    case JobState.AmrReturn:
                        if (job.Amr >= 0)
                        {
                            inst.W_r_ih[3 * m + i][job.Amr] = 1.0;
                        }

                        break;
                }

                if (needsRetrieval)
                {
                    if (job.RetrievalLocInfoId.Length > 0 && locInfoRct.TryGetValue(job.RetrievalLocInfoId, out var rctPin))
                    {
                        inst.R_if[i][LocId(rctPin.row, rctPin.col, rctPin.tier)] = 1.0;
                    }
                    else if (job.State == JobState.AsrsRetrieving)
                    {
                        int freeLoc = -1;
                        for (int f = 0; f < numL; f++)
                        {
                            if (!occupied.Contains(f) && inst.R_if.All(row => row[f] < 0.5))
                            {
                                freeLoc = f;
                                break;
                            }
                        }

                        if (freeLoc >= 0)
                        {
                            inst.R_if[i][freeLoc] = 1.0;
                        }
                        else
                        {
                            warn.Add("In-progress retrieval '" + job.Label + "' found no free AS/RS cell for its location index.");
                        }
                    }
                    else if (inventoryOfMaterial.TryGetValue(job.Material, out var list) && inventoryCursor[job.Material] < list.Count)
                    {
                        var(row, col, tier) = list[inventoryCursor[job.Material]++];
                        inst.R_if[i][LocId(row, col, tier)] = 1.0;
                    }
                    else
                    {
                        warn.Add("Job '" + job.Label + "' needs retrieval but material " + job.Material + " has no remaining inventory; job dropped from the AS/RS stage.");
                        inst.u_asrs_i[i] = 0.0;
                        inst.u_rob_i[i] = 0.0;
                    }
                }

                if (storageLocsOfMaterial.TryGetValue(job.Material, out var planned) && planned.Count > 0)
                {
                    foreach (int f in planned)
                    {
                        inst.W_a_if[m + i][f] = 1.0;
                    }
                }

                for (int h = 0; h < r; h++)
                {
                    inst.W_r_ih[i][h] = 1.0;
                    inst.W_r_ih[2 * m + i][h] = 1.0;
                }
            }

            for (int i2 = 0; i2 < m; i2++)
            {
                foreach (int op in new[]
                {
                    m + i2,
                    3 * m + i2
                }

                )
                {
                    bool loadedStart = inst.u_rob_i[op] > 0.5 && inst.u_rob_i[op - m] < 0.5;
                    if (!loadedStart || inst.W_r_ih[op].Any(v => v > 0.5))
                    {
                        continue;
                    }

                    int hPick = 0;
                    int hLoad = int.MaxValue;
                    for (int h = 0; h < r; h++)
                    {
                        int load = 0;
                        for (int i3 = 0; i3 < m; i3++)
                        {
                            foreach (int op3 in new[]
                            {
                                m + i3,
                                3 * m + i3
                            }

                            )
                            {
                                if (inst.u_rob_i[op3] > 0.5 && inst.u_rob_i[op3 - m] < 0.5 && inst.W_r_ih[op3][h] > 0.5)
                                {
                                    load++;
                                }
                            }
                        }

                        if (load < hLoad)
                        {
                            hLoad = load;
                            hPick = h;
                        }
                    }

                    inst.W_r_ih[op][hPick] = 1.0;
                    warn.Add("Job " + Convert.ToString(i2) + " starts its " + (op < 2 * m ? "D+" : "D-") + " already loaded but the snapshot does not " + "identify the AMR; assigned to AMR " + Convert.ToString(hPick) + " (fewest initial loads).");
                }
            }

            {
                var freedCells = new HashSet<int>();
                for (int i2 = 0; i2 < m; i2++)
                {
                    if (inst.u_asrs_i[i2] < 0.5)
                    {
                        continue;
                    }

                    for (int f = 0; f < numL; f++)
                    {
                        if (inst.R_if[i2][f] > 0.5)
                        {
                            if (completedSet.Contains(f))
                            {
                                warn.Add("Retrieval '" + inst.JobLabel[i2] + "' references finished-product cell " + Convert.ToString(f) + " (COMPLETED_HANDLING_OPERATION_INFO_ID); the cell stays occupied and is not reused for storage.");
                                continue;
                            }

                            freedCells.Add(f);
                        }
                    }
                }

                for (int i2 = 0; i2 < m; i2++)
                {
                    if (inst.W_a_if[m + i2].Any(v => v > 0.5))
                    {
                        continue;
                    }

                    for (int f = 0; f < numL; f++)
                    {
                        inst.W_a_if[m + i2][f] = !occupied.Contains(f) || freedCells.Contains(f) ? 1.0 : 0.0;
                    }
                }
            }

            inst.Layout = NodeLayout.Split(n);
            inst.AsrsOutBufferCapacity = config.AsrsOutBufferCapacity > 0 ? config.AsrsOutBufferCapacity : int.MaxValue;
            inst.AsrsInBufferCapacity = config.AsrsInBufferCapacity > 0 ? config.AsrsInBufferCapacity : int.MaxValue;
            inst.s_ikh = new double[4 * m + 2][][];
            for (int op = 0; op < 4 * m + 2; op++)
            {
                inst.s_ikh[op] = NewJagged(inst.Layout.Count, r);
                if (op >= 4 * m)
                {
                    continue;
                }

                int job = op % m;
                string mat = inst.JobMaterial[job];
                bool isPickup = op < m || (op >= 2 * m && op < 3 * m);
                double sec = isPickup ? (amrLoadSec.TryGetValue(mat, out double ls) ? ls : 8.0) : (amrUnloadSec.TryGetValue(mat, out double us) ? us : 8.0);
                for (int k = 0; k < inst.Layout.Count; k++)
                {
                    if (k == inst.Layout.Origin)
                    {
                        continue;
                    }

                    for (int h = 0; h < r; h++)
                    {
                        inst.s_ikh[op][k][h] = sec;
                    }
                }
            }

            inst.t_klh = new double[n + 2][][];
            var amrEmptyV = new double[r];
            var amrLoadedV = new double[r];
            for (int h = 0; h < r; h++)
            {
                var mhEl = mheInstances.FirstOrDefault(x => Str(x, "MHE_INSTANCE_ID") == amrIds[h]);
                string tId = Str(mhEl, "MHE_TYPE_ID");
                mheTypes.TryGetValue(tId, out var amrType);
                amrEmptyV[h] = Num(amrType, "MOVE_SPEED", 1600);
                amrLoadedV[h] = Num(amrType, "TRANSPORTATION_SPEED", amrEmptyV[h]);
            }

            var amrStartsLoaded = new bool[r];
            for (int i = 0; i < m; i++)
            {
                for (int h = 0; h < r; h++)
                {
                    if (inst.W_r_ih[m + i][h] > 0.5 || inst.W_r_ih[3 * m + i][h] > 0.5)
                    {
                        amrStartsLoaded[h] = true;
                    }
                }
            }

            NodeLayout lay = inst.Layout;
            double[]? [] nodePos = new double[]? [lay.Count];
            double[]? Cfg(params string[] keys)
            {
                foreach (string key in keys)
                {
                    if (config.NodePositions.TryGetValue(key, out var v) && v.Length >= 2)
                    {
                        return new[]
                        {
                            v[0],
                            v[1]
                        };
                    }
                }

                return null;
            }

            for (int k = 0; k < n; k++)
            {
                string st = stationLetters[k];
                nodePos[lay.In(k)] = Cfg(st + "_IN", "BT_" + st + "_IN", st);
                nodePos[lay.Out(k)] = Cfg(st + "_OUT", "BT_" + st + "_OUT", st);
            }

            double[]? lifterPos = null;
            foreach (var mh in mheInstances)
            {
                string tId = Str(mh, "MHE_TYPE_ID");
                string rt = mheTypes.TryGetValue(tId, out var mtL) ? Str(mtL, "RESOURCE_TYPE") : tId;
                if (rt.StartsWith("LIFTER", StringComparison.OrdinalIgnoreCase) || tId.StartsWith("LIFTER", StringComparison.OrdinalIgnoreCase))
                {
                    lifterPos = Pos(mh);
                    if (lifterPos != null)
                    {
                        break;
                    }
                }
            }

            nodePos[lay.AsrsOut] = Cfg("ASRS_OUT", "ASRS") ?? lifterPos;
            nodePos[lay.AsrsIn] = Cfg("ASRS_IN", "ASRS") ?? lifterPos;
            if (Cfg("ASRS_OUT", "ASRS_IN", "ASRS") == null && lifterPos != null)
            {
                warn.Add("AS/RS transfer points taken from the lifter position (" + Convert.ToString(Math.Round(lifterPos[0])) + ", " + Convert.ToString(Math.Round(lifterPos[1])) + ") mm; retrieval and storage share it.");
            }

            double[]? [] amrPos = new double[]? [r];
            for (int h = 0; h < r; h++)
            {
                var mhEl = mheInstances.FirstOrDefault(x => Str(x, "MHE_INSTANCE_ID") == amrIds[h]);
                amrPos[h] = Pos(mhEl);
            }

            bool LoadedLeaving(int fromNode, int h)
            {
                if (fromNode == lay.Origin)
                {
                    return amrStartsLoaded[h];
                }

                if (fromNode == lay.AsrsOut)
                {
                    return true;
                }

                int st = lay.StationOf(fromNode);
                return st >= 0 && fromNode == lay.Out(st);
            }

            double? Between(double[]? a, double[]? bpos, int h, bool loaded)
            {
                if (a == null || bpos == null)
                {
                    return null;
                }

                double v = loaded ? amrLoadedV[h] : amrEmptyV[h];
                if (v <= 0)
                {
                    return null;
                }

                double dx = a[0] - bpos[0];
                double dy = a[1] - bpos[1];
                return Math.Sqrt(dx * dx + dy * dy) / v;
            }

            inst.t_klh = new double[lay.Count][][];
            for (int k = 0; k < lay.Count; k++)
            {
                inst.t_klh[k] = NewJagged(lay.Count, r);
                for (int l = 0; l < lay.Count; l++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        if (k == l || l == lay.Origin)
                        {
                            inst.t_klh[k][l][h] = 0.0;
                            continue;
                        }

                        double[]? from = k == lay.Origin ? amrPos[h] : nodePos[k];
                        double? sec = Between(from, nodePos[l], h, LoadedLeaving(k, h));
                        inst.t_klh[k][l][h] = sec == null ? config.DefaultTravelSeconds : Math.Round(sec.Value, 4);
                    }
                }
            }

            int unknown = 0;
            for (int k = 0; k < lay.Count; k++)
            {
                if (k != lay.Origin && nodePos[k] == null)
                {
                    unknown++;
                }
            }

            if (unknown > 0)
            {
                warn.Add(Convert.ToString(unknown) + " node(s) have no coordinates; those pairs use DefaultTravelSeconds = " + Convert.ToString(config.DefaultTravelSeconds) + " s. Add them to NodePositions (mm).");
            }
            else
            {
                warn.Add("Travel times from coordinates (constant speed; loaded " + Convert.ToString(Math.Round(amrLoadedV[0])) + ", empty " + Convert.ToString(Math.Round(amrEmptyV[0])) + " mm/s): ASRS_OUT->" + stationLetters[0] + "_IN(적재) " + Convert.ToString(Math.Round(inst.t_klh[lay.AsrsOut][lay.In(0)][0], 1)) + "s, " + stationLetters[0] + "_IN->ASRS_OUT(공차) " + Convert.ToString(Math.Round(inst.t_klh[lay.In(0)][lay.AsrsOut][0], 1)) + "s, " + stationLetters[n - 1] + "_OUT->ASRS_IN(적재) " + Convert.ToString(Math.Round(inst.t_klh[lay.Out(n - 1)][lay.AsrsIn][0], 1)) + "s.");
            }

            for (int h = 0; h < r; h++)
            {
                if (amrPos[h] == null)
                {
                    warn.Add("AMR " + amrIds[h] + " has no CURRENT_POSITION; its dummy-start arcs use DefaultTravelSeconds.");
                }
                else
                {
                    warn.Add("AMR " + amrIds[h] + " starts at (" + Convert.ToString(Math.Round(amrPos[h]![0])) + ", " + Convert.ToString(Math.Round(amrPos[h]![1])) + ") mm " + (amrStartsLoaded[h] ? "loaded" : "empty") + "; dummy -> ASRS_OUT " + Convert.ToString(Math.Round(inst.t_klh[lay.Origin][lay.AsrsOut][h], 1)) + " s.");
                }
            }

            double shuttleSpeed = SpeedOf(mheTypes, "SHUTTLE", 1600);
            double lifterSpeed = SpeedOf(mheTypes, "LIFTER", 1600);
            double shuttleAcc = KinOf(mheTypes, "SHUTTLE", "ACC_SPEED", config.AccelMmPerS2);
            double shuttleDec = KinOf(mheTypes, "SHUTTLE", "DEC_SPEED", shuttleAcc);
            double lifterAcc = KinOf(mheTypes, "LIFTER", "ACC_SPEED", config.AccelMmPerS2);
            double lifterDec = KinOf(mheTypes, "LIFTER", "DEC_SPEED", lifterAcc);
            inst.r_ijfg = new Parameters().generate_r_ijfg(m, width, config.CellWidthMm, shuttleAcc, shuttleDec, shuttleSpeed, depth, config.CellDepthMm, shuttleAcc, shuttleDec, shuttleSpeed, levels, config.LevelPitchMm, lifterAcc, lifterDec, lifterSpeed);
            {
                var handoverAdded = new List<string>();
                for (int op = 0; op < 2 * m; op++)
                {
                    int job = op < m ? op : op - m;
                    string mat = inst.JobMaterial[job];
                    if (!handlingLeg.TryGetValue(mat, out var legs))
                    {
                        continue;
                    }

                    double Dur(string role)
                    {
                        foreach (var v in legs.Values)
                        {
                            if (v.Item1 == role)
                            {
                                return v.Item2;
                            }
                        }

                        return 0.0;
                    }

                    double add = op < m ? Dur("SOut") + Dur("LOut") : Dur("LIn") + Dur("SIn");
                    if (add <= 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < 2 * m + 2; i++)
                    {
                        for (int f = 0; f < numL + 2; f++)
                        {
                            for (int g = 0; g < numL + 2; g++)
                            {
                                if (inst.r_ijfg[i][op][f][g] > 0)
                                {
                                    inst.r_ijfg[i][op][f][g] += add;
                                }
                            }
                        }
                    }

                    if (handoverAdded.Count < 3)
                    {
                        handoverAdded.Add(mat + (op < m ? " 출고 +" : " 입고 +") + Convert.ToString(Math.Round(add)) + "s");
                    }
                }

                warn.Add("AS/RS handover times added to r (자원 전환마다 셔틀·리프터 적재·하역): " + string.Join(", ", handoverAdded) + " ...");
            }

            var pins = new List<(int op, double end)>();
            for (int i = 0; i < m; i++)
            {
                if (jobs[i].State == JobState.AsrsRetrieving)
                {
                    pins.Add((i, jobs[i].PinnedRemaining));
                }
                else if (jobs[i].State == JobState.AsrsStoring)
                {
                    pins.Add((m + i, jobs[i].PinnedRemaining));
                }
            }

            pins.Sort((a, b) => a.end.CompareTo(b.end));
            inst.AsrsPinnedPrefix = pins.Select(x => x.op).ToArray();
            int prevPinOp = 2 * m;
            double prevPinEnd = 0.0;
            foreach (var(op, end)in pins)
            {
                double inc = Math.Max(0.0, end - prevPinEnd);
                for (int f = 0; f < numL + 2; f++)
                {
                    for (int g = 0; g < numL + 2; g++)
                    {
                        inst.r_ijfg[prevPinOp][op][f][g] = inc;
                    }
                }

                prevPinOp = op;
                prevPinEnd = end;
            }

            return inst;
        }

        enum JobState
        {
            New,
            Machine,
            BufferIn,
            BufferOut,
            AmrOutbound,
            AmrReturn,
            AsrsRetrieving,
            AsrsStoring
        }

        class JobDraft
        {
            public string Label = "";
            public string Material = "";
            public string Product = "";
            public JobState State;
            public int Station = -1;
            public int Slot = -1;
            public int Amr = -1;
            public double RemainingProcess;
            public double Due;
            public double Weight = double.NaN;
            public double PinnedRemaining;
            public string RetrievalLocInfoId = "";
        }

        static (Dictionary<string, double> byMaterial, double defaultWeight, string source) LoadWeights(string scenarioPath)
        {
            var table = new Dictionary<string, double>();
            double def = 1.0;
            string? path = Environment.GetEnvironmentVariable("PDP_WEIGHTS");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".";
                string stem = Path.GetFileNameWithoutExtension(scenarioPath);
                string a = Path.Combine(dir, stem + "_WEIGHTS.json");
                string b = Path.Combine(dir, "weights.json");
                path = File.Exists(a) ? a : File.Exists(b) ? b : null;
            }

            if (path == null)
            {
                return (table, def, string.Empty);
            }

            using JsonDocument wdoc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var prop in wdoc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                double v = prop.Value.GetDouble();
                if (v <= 0.0)
                {
                    continue;
                }

                if (prop.Name == "default")
                {
                    def = v;
                }
                else
                {
                    table[prop.Name] = v;
                }
            }

            return (table, def, Path.GetFileName(path));
        }

        static ScenarioConfig LoadConfig(string scenarioPath, string? configPath)
        {
            string? path = configPath;
            if (path == null)
            {
                string candidate = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".", "scenario_config.json");
                if (File.Exists(candidate))
                {
                    path = candidate;
                }
            }

            if (path == null)
            {
                return new ScenarioConfig();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<ScenarioConfig>(File.ReadAllText(path), options) ?? new ScenarioConfig();
        }

        static double[]? Pos(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("CURRENT_POSITION", out JsonElement cp) || cp.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            double x = Num(cp, "CURRENT_POSITION_X", double.NaN);
            double y = Num(cp, "CURRENT_POSITION_Y", double.NaN);
            return double.IsNaN(x) || double.IsNaN(y) ? null : new[]
            {
                x,
                y
            };
        }

        static double KinOf(Dictionary<string, JsonElement> mheTypes, string resourcePrefix, string key, double fallback)
        {
            foreach (var mt in mheTypes.Values)
            {
                if (Str(mt, "RESOURCE_TYPE").StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase) || Str(mt, "MHE_TYPE_ID").StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    double value = Num(mt, key, double.NaN);
                    if (!double.IsNaN(value))
                    {
                        return value;
                    }
                }
            }

            return fallback;
        }

        static double SpeedOf(Dictionary<string, JsonElement> mheTypes, string resourcePrefix, double fallback)
        {
            foreach (var mt in mheTypes.Values)
            {
                if (Str(mt, "RESOURCE_TYPE").StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase) || Str(mt, "MHE_TYPE_ID").StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return Num(mt, "MOVE_SPEED", fallback);
                }
            }

            return fallback;
        }

        static string? MaterialOfHandlingPlan(JsonElement plan, JsonElement root)
        {
            string? mat = StrOrNull(plan, "MATERIAL_ID");
            if (mat != null)
            {
                return mat;
            }

            foreach (var s in Arr(plan, "HANDLING_SCHEDULE"))
            {
                string opId = Str(s, "HANDLING_OPERATION_INFO_ID");
                foreach (var mhi in Arr(root, "MATERIAL_HANDLING_INFO"))
                {
                    foreach (var op in Arr(mhi, "MATERIAL_HANDLING_OPERATION_INFO"))
                    {
                        if (Str(op, "OPERATION_INFO_ID") == opId)
                        {
                            return Str(mhi, "MATERIAL_ID");
                        }
                    }
                }
            }

            return null;
        }

        static DateTime? ParseDate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string[] formats =
            {
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                "yyyy-MM-dd'T'HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd"
            };
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime result))
            {
                return result;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
            {
                return result;
            }

            return null;
        }

        static double[][] NewJagged(int a, int b)
        {
            var result = new double[a][];
            for (int i = 0; i < a; i++)
            {
                result[i] = new double[b];
            }

            return result;
        }

        static IEnumerable<JsonElement> Arr(JsonElement parent, string name)
        {
            if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    yield return item;
                }
            }
        }

        static string Str(JsonElement el, string name)
        {
            return StrOrNull(el, name) ?? "";
        }

        static string? StrOrNull(JsonElement el, string name)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetRawText();
                }
            }

            return null;
        }

        static double Num(JsonElement el, string name, double fallback)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetDouble();
                }

                if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }
    }
}

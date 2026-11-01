using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    internal partial class Program
    {
        static int RunDecodeSeq(string scenarioPath, string target, string? configPath)
        {
            ScenarioInstance inst = ScenarioLoader.Load(scenarioPath, configPath);
            int m = inst.m;
            if (target == "SELFTEST")
            {
                SequenceSolution greedy = InitialSolutionGenerator.Generate(inst.numL, inst.r, inst.n, m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.AsrsPinnedPrefix, inst.MachineReadyAt, inst.Layout, inst.AsrsInBufferCapacity);
                bool okA = FixedSequenceSimulator.RebuildBufferSlots(greedy.Clone(), inst.numL, inst.r, inst.n, m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, inst.AsrsPinnedPrefix.Length);
                Console.WriteLine("A) 원본 그리디 그대로 rebuild        : " + (okA ? "OK" : "DEADLOCK: " + FixedSequenceSimulator.LastFailure));
                SequenceSolution reassigned = greedy.Clone();
                AssignStorageCells(inst, reassigned);
                bool okB = FixedSequenceSimulator.RebuildBufferSlots(reassigned, inst.numL, inst.r, inst.n, m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, inst.AsrsPinnedPrefix.Length);
                Console.WriteLine("B) 같은 시퀀스 + 셀 재배정 후 rebuild : " + (okB ? "OK" : "DEADLOCK: " + Cut(FixedSequenceSimulator.LastFailure)));
                for (int i = 0; i < 2 * m; i++)
                {
                    if (greedy.AsrsLocation[i] != reassigned.AsrsLocation[i])
                    {
                        Console.WriteLine($"   첫 셀 차이: op {i} 원본 {greedy.AsrsLocation[i]} -> 재배정 {reassigned.AsrsLocation[i]}");
                        break;
                    }
                }

                SequenceSolution wiped = greedy.Clone();
                wiped.InputSlot = Enumerable.Repeat(-1, m).ToArray();
                wiped.OutputSlot = Enumerable.Repeat(-1, m).ToArray();
                for (int kk = 0; kk < inst.n; kk++)
                {
                    for (int b = 0; b < inst.b_in_k[kk]; b++)
                    {
                        wiped.InputSlotSequences[kk][b] = new List<int>();
                    }

                    for (int b = 0; b < inst.b_out_k[kk]; b++)
                    {
                        wiped.OutputSlotSequences[kk][b] = new List<int>();
                    }
                }

                bool okC = FixedSequenceSimulator.RebuildBufferSlots(wiped, inst.numL, inst.r, inst.n, m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, inst.AsrsPinnedPrefix.Length);
                Console.WriteLine("C) 같은 시퀀스 + 슬롯 비움 후 rebuild : " + (okC ? "OK" : "DEADLOCK: " + Cut(FixedSequenceSimulator.LastFailure)));
                SequenceSolution noWs = greedy.Clone();
                var wsless = new List<int>();
                for (int j = 0; j < m; j++)
                {
                    if (inst.u_ws_i[j] < 0.5 && noWs.JobWorkstation[j] >= 0)
                    {
                        wsless.Add(j);
                        noWs.JobWorkstation[j] = -1;
                    }
                }

                bool okD = FixedSequenceSimulator.RebuildBufferSlots(noWs, inst.numL, inst.r, inst.n, m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, inst.AsrsPinnedPrefix.Length);
                Console.WriteLine($"D) 가공없는 잡 {wsless.Count}개 JobWorkstation=-1 : " + (okD ? "OK" : "DEADLOCK"));
                return 0;
            }

            string[] files = Directory.Exists(target) ? Directory.GetFiles(target, "seq_*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray() : new[]
            {
                target
            };
            Console.WriteLine("DECODESEQ  scenario=" + Path.GetFileName(scenarioPath));
            foreach (string file in files)
            {
                SequenceSolution? solution = LoadSequenceSolution(inst, file, out string? loadError);
                if (solution == null)
                {
                    Console.WriteLine($"{Path.GetFileName(file), -18} {loadError}");
                    continue;
                }

                DecodedSchedule decoded = SolutionDecoder.Decode(solution, inst.numL, inst.r, inst.n, m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: false, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length, asrsPinnedPrefix: inst.AsrsPinnedPrefix);
                Console.WriteLine($"{Path.GetFileName(file), -18} feasible={decoded.IsFeasible, -5} " + $"tardiness={Math.Round(decoded.TotalTardiness, 1), 12}  makespan={Math.Round(decoded.Makespan, 1), 10}" + (decoded.Violations.Count > 0 ? $"  violations={decoded.Violations.Count} ({decoded.Violations[0]})" : ""));
            }

            return 0;
        }

        internal static SequenceSolution? LoadSequenceSolution(ScenarioInstance inst, string file, out string? error)
        {
            int m = inst.m;
            error = null;
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement root = doc.RootElement;
                if (root.GetProperty("m").GetInt32() != m)
                {
                    error = "m mismatch, skip";
                    return null;
                }

                var solution = new SequenceSolution
                {
                    NumOperations = m,
                    NumWorkstations = inst.n,
                    NumRobots = inst.r,
                    NumLocations = inst.numL,
                    AsrsSequence = root.GetProperty("asrs").EnumerateArray().Select(x => x.GetInt32()).ToList(),
                    AsrsLocation = Enumerable.Repeat(-1, 2 * m).ToArray(),
                    AmrSequences = new List<int>[inst.r],
                    RobotOpAmr = Enumerable.Repeat(-1, 4 * m).ToArray(),
                    WorkstationSequences = new List<int>[inst.n],
                    JobWorkstation = Enumerable.Repeat(-1, m).ToArray(),
                    InputSlot = Enumerable.Repeat(-1, m).ToArray(),
                    OutputSlot = Enumerable.Repeat(-1, m).ToArray(),
                    InputSlotSequences = new List<int>[inst.n][],
                    OutputSlotSequences = new List<int>[inst.n][],
                };
                int h = 0;
                foreach (var route in root.GetProperty("amr").EnumerateArray())
                {
                    solution.AmrSequences[h] = route.EnumerateArray().Select(x => x.GetInt32()).ToList();
                    foreach (int op in solution.AmrSequences[h])
                    {
                        solution.RobotOpAmr[op] = h;
                    }

                    h++;
                }

                int k = 0;
                foreach (var seq in root.GetProperty("ws").EnumerateArray())
                {
                    solution.WorkstationSequences[k] = seq.EnumerateArray().Select(x => x.GetInt32()).ToList();
                    foreach (int job in solution.WorkstationSequences[k])
                    {
                        solution.JobWorkstation[job] = k;
                    }

                    k++;
                }

                for (int kk = 0; kk < inst.n; kk++)
                {
                    solution.InputSlotSequences[kk] = new List<int>[inst.b_in_k[kk]];
                    solution.OutputSlotSequences[kk] = new List<int>[inst.b_out_k[kk]];
                    for (int b = 0; b < inst.b_in_k[kk]; b++)
                    {
                        solution.InputSlotSequences[kk][b] = new List<int>();
                    }

                    for (int b = 0; b < inst.b_out_k[kk]; b++)
                    {
                        solution.OutputSlotSequences[kk][b] = new List<int>();
                    }
                }

                for (int j = 0; j < m; j++)
                {
                    if (solution.JobWorkstation[j] >= 0)
                    {
                        continue;
                    }

                    for (int kk = 0; kk < inst.n && solution.JobWorkstation[j] < 0; kk++)
                    {
                        for (int b = 0; b < inst.b_in_k[kk]; b++)
                        {
                            if (inst.W_in_ikb[j][kk][b] > 0.5)
                            {
                                solution.JobWorkstation[j] = kk;
                                break;
                            }
                        }

                        for (int b = 0; b < inst.b_out_k[kk] && solution.JobWorkstation[j] < 0; b++)
                        {
                            if (inst.W_out_ikb[j][kk][b] > 0.5)
                            {
                                solution.JobWorkstation[j] = kk;
                                break;
                            }
                        }
                    }
                }

                AssignStorageCells(inst, solution);
                bool rebuilt = FixedSequenceSimulator.RebuildBufferSlots(solution, inst.numL, inst.r, inst.n, m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, inst.AsrsPinnedPrefix.Length);
                if (!rebuilt)
                {
                    error = "REBUILD DEADLOCK (" + FixedSequenceSimulator.LastFailure + ")";
                    return null;
                }

                return solution;
            }
        }

        static string Cut(string s)
        {
            return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
        }

        static void AssignStorageCells(ScenarioInstance inst, SequenceSolution solution)
        {
            int m = inst.m;
            int[] cellOccupant = Enumerable.Repeat(-1, inst.numL).ToArray();
            for (int i = 0; i < m; i++)
            {
                if (inst.u_asrs_i[i] > 0.5)
                {
                    for (int g = 0; g < inst.numL; g++)
                    {
                        if (inst.R_if[i][g] > 0.5)
                        {
                            solution.AsrsLocation[i] = g;
                            cellOccupant[g] = i;
                            break;
                        }
                    }
                }
            }

            var freed = new HashSet<int>();
            var used = new HashSet<int>();
            foreach (int op in solution.AsrsSequence)
            {
                if (op < m)
                {
                    if (solution.AsrsLocation[op] >= 0)
                    {
                        freed.Add(solution.AsrsLocation[op]);
                    }

                    continue;
                }

                solution.AsrsLocation[op] = -1;
                for (int g = 0; g < inst.numL; g++)
                {
                    if (inst.W_a_if[op][g] < 0.5 || used.Contains(g))
                    {
                        continue;
                    }

                    if (cellOccupant[g] >= 0 && !freed.Contains(g))
                    {
                        continue;
                    }

                    solution.AsrsLocation[op] = g;
                    used.Add(g);
                    break;
                }

                if (solution.AsrsLocation[op] < 0)
                {
                    Console.WriteLine($"  storage cell not found for op {op}");
                }
            }
        }
    }
}

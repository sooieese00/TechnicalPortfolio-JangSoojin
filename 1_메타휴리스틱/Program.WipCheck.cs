using System;
using System.Collections.Generic;
using System.Linq;

namespace PDP_ASRS_WIP
{
    internal partial class Program
    {
        static int RunWipCheck(string scenarioPath, string? configPath)
        {
            Console.WriteLine("WIP PRECEDENCE POLICY CHECK");
            Console.WriteLine("Scenario: " + scenarioPath);
            ScenarioInstance inst = ScenarioLoader.Load(scenarioPath, configPath);
            int m = inst.m, n = inst.n, r = inst.r;
            SequenceSolution baseline = InitialSolutionGenerator.Generate(inst.numL, r, n, m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.AsrsPinnedPrefix, inst.MachineReadyAt, inst.Layout, inst.AsrsInBufferCapacity);
            DecodedSchedule? baseSchedule = Decode(inst, baseline);
            if (baseSchedule == null || !baseSchedule.IsFeasible)
            {
                Console.WriteLine("The greedy baseline is infeasible; cannot run the policy check.");
                return 1;
            }

            var pinnedJobs = new HashSet<int>(inst.AsrsPinnedPrefix.Select(op => op < m ? op : op - m));
            string[] kind = new string[m];
            for (int i = 0; i < m; i++)
            {
                bool initialIn = false, initialOut = false;
                for (int k = 0; k < n; k++)
                {
                    for (int b = 0; b < inst.b_in_k[k]; b++)
                    {
                        if (inst.W_in_ikb[i][k][b] > 0.5)
                        {
                            initialIn = true;
                        }
                    }

                    for (int b = 0; b < inst.b_out_k[k]; b++)
                    {
                        if (inst.W_out_ikb[i][k][b] > 0.5)
                        {
                            initialOut = true;
                        }
                    }
                }

                bool amrLoaded = false;
                for (int h = 0; h < r; h++)
                {
                    if (inst.W_r_ih[m + i][h] > 0.5 || inst.W_r_ih[3 * m + i][h] > 0.5)
                    {
                        amrLoaded = true;
                    }
                }

                kind[i] = pinnedJobs.Contains(i) ? "ASRS-pinned" : inst.u_ws_i[i] > 0.5 && inst.u_rob_i[m + i] < 0.5 && !initialIn ? "MACHINE-WIP" : initialIn ? "in-buffer WIP" : initialOut ? "out-buffer WIP" : amrLoaded ? "AMR-loaded WIP" : "new order";
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("WIP CLASSIFICATION");
            for (int i = 0; i < m; i++)
            {
                if (kind[i] != "new order")
                {
                    Console.WriteLine($"  job {i, 3}: {inst.JobLabel[i], -24} {kind[i]}");
                }
            }

            int overConstrained = 0, underConstrained = 0, tested = 0;
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("TEST 1  move another job to the front of each workstation");
            for (int k = 0; k < n; k++)
            {
                var seq = baseline.WorkstationSequences[k];
                if (seq.Count < 2)
                {
                    continue;
                }

                int head = seq[0];
                int accepted = 0, rejected = 0;
                string firstViolation = "";
                for (int pos = 1; pos < seq.Count; pos++)
                {
                    SequenceSolution candidate = baseline.Clone();
                    var cs = candidate.WorkstationSequences[k];
                    int job = cs[pos];
                    cs.RemoveAt(pos);
                    cs.Insert(0, job);
                    tested++;
                    DecodedSchedule? decoded = Decode(inst, candidate);
                    if (decoded != null && decoded.IsFeasible)
                    {
                        accepted++;
                    }
                    else
                    {
                        rejected++;
                        if (firstViolation.Length == 0 && decoded != null && decoded.Violations.Count > 0)
                        {
                            firstViolation = decoded.Violations[0];
                        }
                    }
                }

                string headKind = kind[head];
                bool mustBeFirst = headKind == "MACHINE-WIP";
                string verdict;
                if (mustBeFirst)
                {
                    verdict = accepted == 0 ? "OK (강제 유지)" : "UNDER-CONSTRAINED";
                    if (accepted > 0)
                    {
                        underConstrained++;
                    }
                }
                else
                {
                    verdict = accepted > 0 ? "OK (자유)" : "OVER-CONSTRAINED?";
                    if (accepted == 0)
                    {
                        overConstrained++;
                    }
                }

                Console.WriteLine($"  WS {k} head=job {head} ({headKind}): accepted {accepted}, rejected {rejected}  -> {verdict}");
                if (firstViolation.Length > 0)
                {
                    Console.WriteLine($"      first rejection: {firstViolation}");
                }
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("TEST 1b delay a buffer-WIP job (try EVERY later position, not just the back)");
            int bufTested = 0;
            for (int i = 0; i < m; i++)
            {
                if (kind[i] != "in-buffer WIP" || inst.u_ws_i[i] < 0.5)
                {
                    continue;
                }

                int k = baseline.JobWorkstation[i];
                if (k < 0 || baseline.WorkstationSequences[k].Count < 2)
                {
                    continue;
                }

                int at = baseline.WorkstationSequences[k].IndexOf(i);
                if (at < 0 || at == baseline.WorkstationSequences[k].Count - 1)
                {
                    continue;
                }

                bufTested++;
                int accepted = 0, deadlocked = 0, violated = 0;
                string note = "";
                for (int to = at + 1; to < baseline.WorkstationSequences[k].Count; to++)
                {
                    SequenceSolution candidate = baseline.Clone();
                    var seq = candidate.WorkstationSequences[k];
                    seq.RemoveAt(at);
                    seq.Insert(to, i);
                    tested++;
                    DecodedSchedule? decoded = Decode(inst, candidate);
                    if (decoded == null)
                    {
                        deadlocked++;
                    }
                    else if (!decoded.IsFeasible)
                    {
                        violated++;
                        if (note.Length == 0 && decoded.Violations.Count > 0)
                        {
                            note = decoded.Violations[0];
                        }
                    }
                    else
                    {
                        accepted++;
                    }
                }

                bool free = accepted > 0;
                Console.WriteLine($"  job {i} ({inst.JobLabel[i]}) at WS {k} pos {at}: " + $"accepted {accepted}, deadlock {deadlocked}, violation {violated} -> " + (free ? "OK (뒤로 미룰 수 있음 = 자유)" : violated > 0 ? "OVER-CONSTRAINED (규칙이 막음)" : "물리적으로만 불가(교착)"));
                if (note.Length > 0)
                {
                    Console.WriteLine($"      {note}");
                    overConstrained++;
                }
            }

            if (bufTested == 0)
            {
                Console.WriteLine("  (no movable in-buffer WIP in this scenario)");
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("TEST 2  push an AMR-loaded delivery to the end of its route");
            int amrTested = 0, amrAccepted = 0;
            for (int i = 0; i < m; i++)
            {
                for (int type = 0; type < 2; type++)
                {
                    int deliveryOp = type == 0 ? m + i : 3 * m + i;
                    int pickupOp = deliveryOp - m;
                    if (inst.u_rob_i[deliveryOp] < 0.5 || inst.u_rob_i[pickupOp] > 0.5)
                    {
                        continue;
                    }

                    int h = baseline.RobotOpAmr[deliveryOp];
                    if (h < 0 || baseline.AmrSequences[h].Count < 2)
                    {
                        continue;
                    }

                    int at = baseline.AmrSequences[h].IndexOf(deliveryOp);
                    if (at < 0 || at == baseline.AmrSequences[h].Count - 1)
                    {
                        continue;
                    }

                    amrTested++;
                    int accepted = 0, deadlocked = 0, violated = 0;
                    string note = "";
                    for (int to = at + 1; to < baseline.AmrSequences[h].Count; to++)
                    {
                        SequenceSolution candidate = baseline.Clone();
                        var route = candidate.AmrSequences[h];
                        route.RemoveAt(at);
                        route.Insert(to, deliveryOp);
                        tested++;
                        DecodedSchedule? decoded = Decode(inst, candidate);
                        if (decoded == null)
                        {
                            deadlocked++;
                        }
                        else if (!decoded.IsFeasible)
                        {
                            violated++;
                            if (note.Length == 0 && decoded.Violations.Count > 0)
                            {
                                note = decoded.Violations[0];
                            }
                        }
                        else
                        {
                            accepted++;
                        }
                    }

                    if (accepted > 0)
                    {
                        amrAccepted++;
                    }

                    Console.WriteLine($"  job {i} ({inst.JobLabel[i]}) delivery op {deliveryOp} on AMR {h} pos {at}: " + $"accepted {accepted}, deadlock {deadlocked}, violation {violated} -> " + (accepted > 0 ? "OK (미룰 수 있음 = 자유)" : violated > 0 ? "OVER-CONSTRAINED (규칙이 막음)" : "물리적으로만 불가(적재량/교착)"));
                    if (note.Length > 0)
                    {
                        Console.WriteLine($"      {note}");
                    }
                }
            }

            if (amrTested == 0)
            {
                Console.WriteLine("  (this scenario has no AMR-loaded WIP)");
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("TEST 3  move a pinned AS/RS operation out of the front block");
            if (inst.AsrsPinnedPrefix.Length == 0)
            {
                Console.WriteLine("  (this scenario has no in-progress AS/RS operation)");
            }
            else
            {
                SequenceSolution candidate = baseline.Clone();
                int op = candidate.AsrsSequence[0];
                candidate.AsrsSequence.RemoveAt(0);
                candidate.AsrsSequence.Add(op);
                tested++;
                DecodedSchedule? decoded = Decode(inst, candidate);
                bool ok = decoded != null && decoded.IsFeasible;
                Console.WriteLine($"  pinned op {op} moved to the back -> {(ok ? "accepted" : "rejected")}");
                if (ok)
                {
                    Console.WriteLine("      NOTE: 디코더는 순서 자체를 막지 않는다. 핀 접두부는 연산자 쪽에서 고정한다");
                    Console.WriteLine("      (pinnedAsrsPrefix). 탐색 중에는 이 위치가 절대 바뀌지 않는다.");
                }
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"candidates tested: {tested}");
            Console.WriteLine($"over-constrained findings : {overConstrained}");
            Console.WriteLine($"under-constrained findings: {underConstrained}");
            Console.WriteLine(overConstrained == 0 && underConstrained == 0 ? "POLICY CHECK PASSED" : "POLICY CHECK FAILED");
            return overConstrained == 0 && underConstrained == 0 ? 0 : 3;
        }

        static DecodedSchedule? Decode(ScenarioInstance inst, SequenceSolution solution)
        {
            bool rebuilt = FixedSequenceSimulator.RebuildBufferSlots(solution, inst.numL, inst.r, inst.n, inst.m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, inst.AsrsPinnedPrefix.Length);
            if (!rebuilt)
            {
                return null;
            }

            return SolutionDecoder.Decode(solution, inst.numL, inst.r, inst.n, inst.m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: false, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length, asrsPinnedPrefix: inst.AsrsPinnedPrefix);
        }
    }
}

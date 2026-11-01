using System;
using System.Collections.Generic;

namespace PDP_ASRS_WIP
{
    public static partial class BufferMetaheuristics
    {
        sealed partial class Context
        {
            public (SequenceSolution sol, DecodedSchedule sch)? RepairPrefix(SequenceSolution destroyed, int[] jobs)
            {
                var order = new List<int>(jobs);
                order.Sort((a, b) => I.d_i[a].CompareTo(I.d_i[b]));
                SequenceSolution current = destroyed;
                DecodedSchedule? sch = Evaluate(current, relaxed: true);
                if (sch == null)
                {
                    return null;
                }

                foreach (int j in order)
                {
                    var built = InsertOneJobPrefix(current, sch, j);
                    if (built == null)
                    {
                        return null;
                    }

                    current = built.Value.sol;
                    sch = built.Value.sch;
                }

                return (current, sch!);
            }

            (SequenceSolution sol, DecodedSchedule sch)? InsertOneJobPrefix(SequenceSolution start, DecodedSchedule startSch, int j)
            {
                int m = I.m;
                int retLoc = 0;
                for (int f = 0; f < I.numL; f++)
                {
                    if (I.R_if[j][f] == 1)
                    {
                        retLoc = f;
                        break;
                    }
                }

                double durPplusMin = double.MaxValue, durPminusMin = double.MaxValue;
                double durDplus = double.MaxValue, durDminus = double.MaxValue;
                for (int h = 0; h < I.r; h++)
                {
                    if (I.W_r_ih[j][h] > 0.5)
                    {
                        for (int k = 0; k < I.n; k++)
                        {
                            if (I.E_ik[j][k] != 1 || I.W_n_ik[j][k] < 0.5)
                            {
                                continue;
                            }

                            durPplusMin = Math.Min(durPplusMin, I.s_ikh[j][I.Layout.AsrsOut][h] + I.t_klh[I.Layout.AsrsOut][I.Layout.In(k)][h]);
                            durDplus = Math.Min(durDplus, I.s_ikh[m + j][I.Layout.In(k)][h]);
                        }
                    }

                    if (I.W_r_ih[2 * m + j][h] > 0.5)
                    {
                        for (int k = 0; k < I.n; k++)
                        {
                            if (I.E_ik[j][k] != 1 || I.W_n_ik[j][k] < 0.5)
                            {
                                continue;
                            }

                            durPminusMin = Math.Min(durPminusMin, I.s_ikh[2 * m + j][I.Layout.Out(k)][h] + I.t_klh[I.Layout.Out(k)][I.Layout.AsrsIn][h]);
                        }

                        durDminus = Math.Min(durDminus, I.s_ikh[3 * m + j][I.Layout.AsrsIn][h]);
                    }
                }

                double durProcMin = double.MaxValue;
                for (int k = 0; k < I.n; k++)
                {
                    if (I.E_ik[j][k] == 1 && I.W_n_ik[j][k] > 0.5)
                    {
                        durProcMin = Math.Min(durProcMin, I.s_in_ik[j][k] + I.p_ik[j][k] + I.s_out_ik[j][k]);
                    }
                }

                double durSto = I.r_ijfg[2 * m][m + j][I.numL][0];
                double RemainAfter(int stage, int chosenWs)
                {
                    double proc = chosenWs >= 0 ? I.s_in_ik[j][chosenWs] + I.p_ik[j][chosenWs] + I.s_out_ik[j][chosenWs] : durProcMin;
                    return stage switch
                    {
                        0 => durPplusMin + durDplus + proc + durPminusMin + durDminus + durSto,
                        1 => durDplus + proc + durPminusMin + durDminus + durSto,
                        2 => durPminusMin + durDminus + durSto,
                        3 => durSto,
                        _ => 0.0,
                    };
                }

                double Score(DecodedSchedule s, double lastDone, int stage, int chosenWs)
                {
                    return s.TotalTardiness + Math.Max(0.0, lastDone + RemainAfter(stage, chosenWs) - I.d_i[j]);
                }

                (SequenceSolution, DecodedSchedule, double bestScore)? best = null;
                void Consider(SequenceSolution cand, Func<DecodedSchedule, double> score)
                {
                    if (!BudgetLeft)
                    {
                        return;
                    }

                    DecodedSchedule? s = Evaluate(cand, relaxed: true);
                    if (s == null)
                    {
                        return;
                    }

                    double v = score(s);
                    if (best == null || v < best.Value.bestScore - Eps || (v <= best.Value.bestScore + Eps && s.Makespan < best.Value.Item2.Makespan - Eps))
                    {
                        best = (cand, s, v);
                    }
                }

                double retTarget = Math.Max(0.0, I.d_i[j] - RemainAfter(0, -1));
                int centreA = PositionByTime(start.AsrsSequence, op => startSch.AsrsCompletion[op], retTarget, PinnedLen);
                foreach (int p in WindowAround(centreA, PinnedLen, start.AsrsSequence.Count))
                {
                    SequenceSolution cand = start.Clone();
                    cand.AsrsSequence.Insert(p, j);
                    cand.AsrsLocation[j] = retLoc;
                    Consider(cand, s => Score(s, s.AsrsCompletion[j], 0, -1));
                }

                if (best == null)
                {
                    return null;
                }

                var(curA, schA, _) = best.Value;
                best = null;
                foreach (int h in AllowedRobots(j))
                {
                    int centre = PositionByTime(curA.AmrSequences[h], op => schA.RobotCompletion[op], schA.AsrsCompletion[j], 0);
                    foreach (int p in WindowAround(centre, 0, curA.AmrSequences[h].Count))
                    {
                        SequenceSolution cand = curA.Clone();
                        cand.AmrSequences[h].Insert(p, j);
                        cand.RobotOpAmr[j] = h;
                        Consider(cand, s => Score(s, s.RobotCompletion[j], 1, -1));
                    }
                }

                if (best == null)
                {
                    return null;
                }

                var(curB, schB, _) = best.Value;
                int h1 = curB.RobotOpAmr[j];
                best = null;
                int pplusIdx = curB.AmrSequences[h1].IndexOf(j);
                for (int k = 0; k < I.n; k++)
                {
                    if (I.E_ik[j][k] != 1 || I.W_n_ik[j][k] < 0.5)
                    {
                        continue;
                    }

                    var wseq = curB.WorkstationSequences[k];
                    for (int p = 0; p <= wseq.Count; p++)
                    {
                        SequenceSolution cand = curB.Clone();
                        cand.AmrSequences[h1].Insert(pplusIdx + 1, m + j);
                        cand.RobotOpAmr[m + j] = h1;
                        cand.JobWorkstation[j] = k;
                        cand.WorkstationSequences[k].Insert(p, j);
                        int kk = k;
                        Consider(cand, s => Score(s, s.ProcessCompletion[j], 2, kk));
                    }
                }

                if (best == null)
                {
                    return null;
                }

                var(curC, schC, _) = best.Value;
                int ws = curC.JobWorkstation[j];
                best = null;
                foreach (int h in AllowedRobots(2 * m + j))
                {
                    int centre = PositionByTime(curC.AmrSequences[h], op => schC.RobotCompletion[op], schC.ProcessCompletion[j], 0);
                    foreach (int p in WindowAround(centre, 0, curC.AmrSequences[h].Count))
                    {
                        SequenceSolution cand = curC.Clone();
                        cand.AmrSequences[h].Insert(p, 2 * m + j);
                        cand.AmrSequences[h].Insert(p + 1, 3 * m + j);
                        cand.RobotOpAmr[2 * m + j] = h;
                        cand.RobotOpAmr[3 * m + j] = h;
                        Consider(cand, s => Score(s, s.RobotCompletion[3 * m + j], 3, ws));
                    }
                }

                if (best == null)
                {
                    return null;
                }

                var(curD, schD, _) = best.Value;
                best = null;
                int retIdx = curD.AsrsSequence.IndexOf(j);
                int centreE = Math.Max(retIdx + 1, PositionByTime(curD.AsrsSequence, op => schD.AsrsCompletion[op], schD.RobotCompletion[3 * m + j], 0));
                foreach (int p in WindowAround(centreE, retIdx + 1, curD.AsrsSequence.Count))
                {
                    SequenceSolution cand = curD.Clone();
                    int cell = FreeStorageCell(cand, j);
                    if (cell < 0)
                    {
                        continue;
                    }

                    cand.AsrsSequence.Insert(p, m + j);
                    cand.AsrsLocation[m + j] = cell;
                    Consider(cand, s => s.TotalTardiness);
                }

                if (best == null)
                {
                    return null;
                }

                return (best.Value.Item1, best.Value.Item2);
            }

            IEnumerable<int> AllowedRobots(int robotOp)
            {
                for (int h = 0; h < I.r; h++)
                {
                    if (I.W_r_ih[robotOp][h] > 0.5)
                    {
                        yield return h;
                    }
                }
            }

            int FreeStorageCell(SequenceSolution sol, int j)
            {
                int m = I.m;
                var used = new bool[I.numL];
                foreach (int op in sol.AsrsSequence)
                {
                    if (op >= m && op != m + j && sol.AsrsLocation[op] >= 0)
                    {
                        used[sol.AsrsLocation[op]] = true;
                    }
                }

                for (int g = 0; g < I.numL; g++)
                {
                    if (!used[g] && I.W_a_if[m + j][g] > 0.5)
                    {
                        return g;
                    }
                }

                return -1;
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace PDP_ASRS_WIP
{
    public static class FixedSequenceSimulator
    {
        public static string LastFailure = "";
        public static bool RebuildBufferSlots(SequenceSolution solution, int numL, int r, int n, int m, double[][] p_ik, double[][][] s_ikh, double[][][] t_klh, double[][] R_if, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb, double[]? machineReadyAt = null, NodeLayout? nodeLayout = null, int asrsOutCap = 1, int asrsInCap = int.MaxValue, int asrsPinnedPrefixLength = 0)
        {
            NodeLayout layout = nodeLayout ?? NodeLayout.Compact(n);
            {
                int[] rankInWs = new int[m];
                for (int i2 = 0; i2 < m; i2++)
                {
                    rankInWs[i2] = int.MaxValue;
                }

                for (int k = 0; k < n; k++)
                {
                    for (int idx = 0; idx < solution.WorkstationSequences[k].Count; idx++)
                    {
                        int job = solution.WorkstationSequences[k][idx];
                        if (job >= 0 && job < m)
                        {
                            rankInWs[job] = idx;
                        }
                    }
                }

                bool RouteOk(List<int> route)
                {
                    var at = new Dictionary<int, int>();
                    for (int q = 0; q < route.Count; q++)
                    {
                        at[route[q]] = q;
                    }

                    for (int job = 0; job < m; job++)
                    {
                        if (at.TryGetValue(job, out int pp) && at.TryGetValue(m + job, out int dp) && pp > dp)
                        {
                            return false;
                        }

                        if (at.TryGetValue(2 * m + job, out int pm) && at.TryGetValue(3 * m + job, out int dm) && pm > dm)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                bool doRepair = Environment.GetEnvironmentVariable("PDP_WSFIFO_REPAIR") == "1";
                for (int h = 0; doRepair && h < r; h++)
                {
                    var route = solution.AmrSequences[h];
                    for (int kind = 0; kind < 2; kind++)
                    {
                        int lo = kind == 0 ? m : 2 * m;
                        for (int k = 0; k < n; k++)
                        {
                            var slots = new List<int>();
                            var jobs = new List<int>();
                            for (int q = 0; q < route.Count; q++)
                            {
                                int op = route[q];
                                if (op < lo || op >= lo + m)
                                {
                                    continue;
                                }

                                int job = op - lo;
                                if (solution.JobWorkstation[job] != k)
                                {
                                    continue;
                                }

                                slots.Add(q);
                                jobs.Add(job);
                            }

                            if (slots.Count <= 1)
                            {
                                continue;
                            }

                            var wanted = new List<int>(jobs);
                            wanted.Sort((a, b) => rankInWs[a].CompareTo(rankInWs[b]));
                            bool same = true;
                            for (int t = 0; t < jobs.Count; t++)
                            {
                                if (jobs[t] != wanted[t])
                                {
                                    same = false;
                                    break;
                                }
                            }

                            if (same)
                            {
                                continue;
                            }

                            var before = new List<int>(route);
                            for (int t = 0; t < slots.Count; t++)
                            {
                                route[slots[t]] = lo + wanted[t];
                            }

                            if (!RouteOk(route))
                            {
                                route.Clear();
                                route.AddRange(before);
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < m; i++)
            {
                solution.InputSlot[i] = -1;
                solution.OutputSlot[i] = -1;
            }

            for (int k = 0; k < n; k++)
            {
                for (int b = 0; b < b_in_k[k]; b++)
                {
                    solution.InputSlotSequences[k][b].Clear();
                }

                for (int b = 0; b < b_out_k[k]; b++)
                {
                    solution.OutputSlotSequences[k][b].Clear();
                }
            }

            double[] asrsDone = Filled(2 * m);
            double[] robotDone = Filled(4 * m);
            double[] lIn = Filled(m);
            double[] cnDone = Filled(m);
            double[] lOut = Filled(m);
            bool[] asrsInSeq = new bool[2 * m];
            foreach (int op in solution.AsrsSequence)
            {
                if (op >= 0 && op < 2 * m)
                {
                    asrsInSeq[op] = true;
                }
            }

            int[] retGate = new int[2 * m];
            int[] stoGate = new int[2 * m];
            {
                for (int op = 0; op < 2 * m; op++)
                {
                    retGate[op] = -1;
                    stoGate[op] = -1;
                }

                var rets = new List<int>();
                var stos = new List<int>();
                for (int idx = 0; idx < solution.AsrsSequence.Count; idx++)
                {
                    int op = solution.AsrsSequence[idx];
                    if (op < 0 || op >= 2 * m)
                    {
                        continue;
                    }

                    if (op < m)
                    {
                        if (rets.Count >= asrsOutCap)
                        {
                            retGate[op] = rets[rets.Count - asrsOutCap];
                        }

                        rets.Add(op);
                    }
                    else
                    {
                        if (idx < asrsPinnedPrefixLength)
                        {
                            continue;
                        }

                        if (asrsInCap < int.MaxValue && stos.Count >= asrsInCap)
                        {
                            stoGate[op] = stos[stos.Count - asrsInCap];
                        }

                        stos.Add(op);
                    }
                }
            }

            double[][] inFree = new double[n][];
            double[][] outFree = new double[n][];
            for (int k = 0; k < n; k++)
            {
                inFree[k] = new double[b_in_k[k]];
                outFree[k] = new double[b_out_k[k]];
            }

            bool[] initialIn = new bool[m];
            for (int i = 0; i < m; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    for (int b = 0; b < b_in_k[k]; b++)
                    {
                        if (W_in_ikb[i][k][b] > 0.5)
                        {
                            initialIn[i] = true;
                            solution.InputSlot[i] = b;
                            solution.InputSlotSequences[k][b].Add(i);
                            inFree[k][b] = double.NaN;
                            lIn[i] = 0.0;
                        }
                    }

                    for (int b = 0; b < b_out_k[k]; b++)
                    {
                        if (W_out_ikb[i][k][b] > 0.5)
                        {
                            solution.OutputSlot[i] = b;
                            solution.OutputSlotSequences[k][b].Add(i);
                            outFree[k][b] = double.NaN;
                            lOut[i] = 0.0;
                        }
                    }
                }

                if (u_ws_i[i] > 0.5 && u_rob_i[m + i] < 0.5 && !initialIn[i])
                {
                    lIn[i] = 0.0;
                }
            }

            int[] retrievalJobAtLoc = new int[numL];
            for (int f = 0; f < numL; f++)
            {
                retrievalJobAtLoc[f] = -1;
            }

            for (int i = 0; i < m; i++)
            {
                if (u_asrs_i[i] > 0.5)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        if (R_if[i][f] > 0.5)
                        {
                            retrievalJobAtLoc[f] = i;
                        }
                    }
                }
            }

            int asrsIdx = 0;
            int asrsPrevOp = 2 * m;
            int asrsPrevLoc = numL;
            double asrsPrevDone = 0.0;
            int[] amrIdx = new int[r];
            double[] amrPrevDone = new double[r];
            int[] amrPrevNode = new int[r];
            for (int h = 0; h < r; h++)
            {
                amrPrevNode[h] = layout.Origin;
            }

            List<int>[] inLane = new List<int>[n];
            List<int>[] outLane = new List<int>[n];
            bool fifoOutGen = Environment.GetEnvironmentVariable("PDP_WSFIFO_OUT") != "0";
            List<int>[] wsDone = new List<int>[n];
            int[] wsTotal = new int[n];
            int[] wsIdx = new int[n];
            int[] wsPendingOut = new int[n];
            double[] wsLastOut = new double[n];
            for (int k = 0; k < n; k++)
            {
                inLane[k] = new List<int>();
                outLane[k] = new List<int>();
                wsDone[k] = new List<int>();
                wsTotal[k] = solution.WorkstationSequences[k].Count;
                foreach (int job in solution.WorkstationSequences[k])
                {
                    if (job >= 0 && job < m && !double.IsNaN(lIn[job]))
                    {
                        inLane[k].Add(job);
                    }
                }

                for (int i2 = 0; i2 < m; i2++)
                {
                    if (!double.IsNaN(lOut[i2]) && solution.JobWorkstation[i2] == k && !inLane[k].Contains(i2))
                    {
                        outLane[k].Add(i2);
                    }
                }

                wsPendingOut[k] = -1;
                if (machineReadyAt != null)
                {
                    wsLastOut[k] = machineReadyAt[k];
                }
            }

            int[] posInWs = new int[m];
            for (int i = 0; i < m; i++)
            {
                posInWs[i] = -1;
            }

            for (int k = 0; k < n; k++)
            {
                for (int idx = 0; idx < solution.WorkstationSequences[k].Count; idx++)
                {
                    int job = solution.WorkstationSequences[k][idx];
                    if (job >= 0 && job < m)
                    {
                        posInWs[job] = idx;
                    }
                }
            }

            int RobotOpNode(int op)
            {
                if (op < m)
                {
                    return layout.AsrsOut;
                }

                if (op >= 3 * m)
                {
                    return layout.AsrsIn;
                }

                int job = op < 2 * m ? op - m : op - 2 * m;
                int k = solution.JobWorkstation[job];
                if (k < 0)
                {
                    return layout.AsrsOut;
                }

                return op < 2 * m ? layout.In(k) : layout.Out(k);
            }

            int RobotOpStation(int op)
            {
                if (op < m || op >= 3 * m)
                {
                    return -1;
                }

                return solution.JobWorkstation[op < 2 * m ? op - m : op - 2 * m];
            }

            int BestSlot(double[] free)
            {
                int best = -1;
                for (int b = 0; b < free.Length; b++)
                {
                    if (!double.IsNaN(free[b]) && (best < 0 || free[b] < free[best]))
                    {
                        best = b;
                    }
                }

                return best;
            }

            (double start, double completion, double release) PeekAsrs()
            {
                if (asrsIdx >= solution.AsrsSequence.Count)
                {
                    return (double.NaN, double.NaN, 0);
                }

                int op = solution.AsrsSequence[asrsIdx];
                int loc = solution.AsrsLocation[op];
                if (loc < 0 || loc >= numL)
                {
                    return (double.NaN, double.NaN, 0);
                }

                double release = 0.0;
                if (op >= m)
                {
                    int job = op - m;
                    if (u_rob_i[3 * m + job] > 0.5 && solution.RobotOpAmr[3 * m + job] >= 0)
                    {
                        if (double.IsNaN(robotDone[3 * m + job]))
                        {
                            return (double.NaN, double.NaN, 0);
                        }

                        release = Math.Max(release, robotDone[3 * m + job]);
                    }
                    else if (u_asrs_i[job] > 0.5 && asrsInSeq[job])
                    {
                        if (double.IsNaN(asrsDone[job]))
                        {
                            return (double.NaN, double.NaN, 0);
                        }

                        release = Math.Max(release, asrsDone[job]);
                    }

                    int vacating = retrievalJobAtLoc[loc];
                    if (vacating >= 0 && asrsInSeq[vacating])
                    {
                        if (double.IsNaN(asrsDone[vacating]))
                        {
                            return (double.NaN, double.NaN, 0);
                        }

                        release = Math.Max(release, asrsDone[vacating]);
                    }
                }

                double blockLb = 0.0;
                if (op < m && retGate[op] >= 0 && u_rob_i[retGate[op]] > 0.5 && solution.RobotOpAmr[retGate[op]] >= 0)
                {
                    if (double.IsNaN(robotDone[retGate[op]]))
                    {
                        return (double.NaN, double.NaN, 0);
                    }

                    blockLb = robotDone[retGate[op]];
                }

                double start = Math.Max(asrsPrevDone, release);
                double completion = Math.Max(start + r_ijfg[asrsPrevOp][op][asrsPrevLoc][loc], blockLb);
                return (start, completion, release);
            }

            bool TrySwapPickup(int h, int idx, int jobFrom, int jobTo)
            {
                int pj = 2 * m + jobFrom, dj = 3 * m + jobFrom;
                int pg = 2 * m + jobTo, dg = 3 * m + jobTo;
                int hg = solution.RobotOpAmr[pg];
                if (hg < 0 || solution.RobotOpAmr[dg] != hg || solution.RobotOpAmr[dj] != h)
                {
                    return false;
                }

                var rh = solution.AmrSequences[h];
                var rg = solution.AmrSequences[hg];
                int ipg = rg.IndexOf(pg), idg = rg.IndexOf(dg), idj = rh.IndexOf(dj);
                if (ipg < amrIdx[hg] || idg < amrIdx[hg] || idj <= idx)
                {
                    return false;
                }

                if (hg == h && ipg <= idx)
                {
                    return false;
                }

                rh[idx] = pg;
                rg[ipg] = pj;
                rh[idj] = dg;
                rg[idg] = dj;
                solution.RobotOpAmr[pg] = h;
                solution.RobotOpAmr[dg] = h;
                solution.RobotOpAmr[pj] = hg;
                solution.RobotOpAmr[dj] = hg;
                bool PairsOk(int hh)
                {
                    var rr = solution.AmrSequences[hh];
                    for (int x = 0; x < m; x++)
                    {
                        int a = rr.IndexOf(2 * m + x), b = rr.IndexOf(3 * m + x);
                        if (a >= 0 != b >= 0)
                        {
                            return false;
                        }

                        if (a >= 0 && a > b)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                bool ok = PairsOk(h) && (hg == h || PairsOk(hg));
                if (!ok)
                {
                    rh[idx] = pj;
                    rg[ipg] = pg;
                    rh[idj] = dj;
                    rg[idg] = dg;
                    solution.RobotOpAmr[pg] = hg;
                    solution.RobotOpAmr[dg] = hg;
                    solution.RobotOpAmr[pj] = h;
                    solution.RobotOpAmr[dj] = h;
                    return false;
                }

                return true;
            }

            (double time, double done, int slot) PeekAmr(int h)
            {
                if (amrIdx[h] >= solution.AmrSequences[h].Count)
                {
                    return (double.NaN, double.NaN, -1);
                }

                int op = solution.AmrSequences[h][amrIdx[h]];
                int node = RobotOpNode(op);
                double release = 0.0;
                if (op < m)
                {
                    if (u_asrs_i[op] > 0.5 && asrsInSeq[op])
                    {
                        if (double.IsNaN(asrsDone[op]))
                        {
                            return (double.NaN, double.NaN, -1);
                        }

                        release = asrsDone[op];
                    }
                }
                else if (op < 2 * m)
                {
                    int job = op - m;
                    if (u_rob_i[job] > 0.5 && solution.RobotOpAmr[job] >= 0)
                    {
                        if (double.IsNaN(robotDone[job]))
                        {
                            return (double.NaN, double.NaN, -1);
                        }

                        release = robotDone[job];
                    }
                }
                else if (op < 3 * m)
                {
                    int job = op - 2 * m;
                    int stp = RobotOpStation(op);
                    if (fifoOutGen && stp >= 0 && outLane[stp].Count > 0 && outLane[stp][0] != job)
                    {
                        if (!TrySwapPickup(h, amrIdx[h], job, outLane[stp][0]))
                        {
                            return (double.NaN, double.NaN, -1);
                        }

                        op = solution.AmrSequences[h][amrIdx[h]];
                        job = op - 2 * m;
                        node = RobotOpNode(op);
                    }

                    if (double.IsNaN(lOut[job]))
                    {
                        return (double.NaN, double.NaN, -1);
                    }

                    release = lOut[job];
                }
                else
                {
                    int job = op - 3 * m;
                    if (u_rob_i[2 * m + job] > 0.5 && solution.RobotOpAmr[2 * m + job] >= 0)
                    {
                        if (double.IsNaN(robotDone[2 * m + job]))
                        {
                            return (double.NaN, double.NaN, -1);
                        }

                        release = robotDone[2 * m + job];
                    }
                }

                double start = Math.Max(amrPrevDone[h] + t_klh[amrPrevNode[h]][node][h], release);
                if (op >= m && op < 2 * m)
                {
                    int job = op - m;
                    int st = RobotOpStation(op);
                    if (st < 0)
                    {
                        return (double.NaN, double.NaN, -1);
                    }

                    if (posInWs[job] >= 0 && posInWs[job] - wsIdx[st] >= b_in_k[st] && Environment.GetEnvironmentVariable("PDP_SIM_ADMIT") == "1")
                    {
                        return (double.NaN, double.NaN, -1);
                    }

                    int slot = BestSlot(inFree[st]);
                    if (slot < 0)
                    {
                        return (double.NaN, double.NaN, -1);
                    }

                    double entry = Math.Max(start + s_ikh[op][node][h], inFree[st][slot]);
                    return (entry, entry, slot);
                }

                double done = start + s_ikh[op][node][h];
                if (op >= 3 * m)
                {
                    int stoOp = m + (op - 3 * m);
                    if (asrsInSeq[stoOp] && stoGate[stoOp] >= 0 && asrsInSeq[stoGate[stoOp]])
                    {
                        if (double.IsNaN(asrsDone[stoGate[stoOp]]))
                        {
                            return (double.NaN, double.NaN, -1);
                        }

                        done = Math.Max(done, asrsDone[stoGate[stoOp]]);
                    }
                }

                return (start, done, -1);
            }

            double PeekWsProcess(int k)
            {
                if (wsPendingOut[k] >= 0 || inLane[k].Count == 0)
                {
                    return double.NaN;
                }

                int job = inLane[k][0];
                return Math.Max(lIn[job], wsLastOut[k]);
            }

            (double entry, int slot) PeekWsOut(int k)
            {
                if (wsPendingOut[k] < 0)
                {
                    return (double.NaN, -1);
                }

                int job = wsPendingOut[k];
                int slot = BestSlot(outFree[k]);
                if (slot < 0)
                {
                    return (double.NaN, -1);
                }

                return (Math.Max(cnDone[job], outFree[k][slot]), slot);
            }

            int totalSteps = solution.AsrsSequence.Count;
            for (int h = 0; h < r; h++)
            {
                totalSteps += solution.AmrSequences[h].Count;
            }

            for (int k = 0; k < n; k++)
            {
                totalSteps += 2 * solution.WorkstationSequences[k].Count;
            }

            for (int executed = 0; executed < totalSteps; executed++)
            {
                double bestTime = double.NaN;
                int kind = -1;
                int arg = -1;
                var asrsPeek = PeekAsrs();
                if (!double.IsNaN(asrsPeek.start))
                {
                    bestTime = asrsPeek.start;
                    kind = 0;
                }

                for (int h = 0; h < r; h++)
                {
                    var peek = PeekAmr(h);
                    if (!double.IsNaN(peek.time) && (double.IsNaN(bestTime) || peek.time < bestTime))
                    {
                        bestTime = peek.time;
                        kind = 1;
                        arg = h;
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    var peek = PeekWsOut(k);
                    if (!double.IsNaN(peek.entry) && (double.IsNaN(bestTime) || peek.entry < bestTime))
                    {
                        bestTime = peek.entry;
                        kind = 2;
                        arg = k;
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    double t = PeekWsProcess(k);
                    if (!double.IsNaN(t) && (double.IsNaN(bestTime) || t < bestTime))
                    {
                        bestTime = t;
                        kind = 3;
                        arg = k;
                    }
                }

                if (kind < 0)
                {
                    var stuck = new List<string>();
                    if (asrsIdx < solution.AsrsSequence.Count)
                    {
                        stuck.Add("ASRS@op" + Convert.ToString(solution.AsrsSequence[asrsIdx]));
                    }

                    for (int h = 0; h < r; h++)
                    {
                        if (amrIdx[h] < solution.AmrSequences[h].Count)
                        {
                            stuck.Add("AMR" + Convert.ToString(h) + "@op" + Convert.ToString(solution.AmrSequences[h][amrIdx[h]]));
                        }
                    }

                    for (int k = 0; k < n; k++)
                    {
                        if (wsPendingOut[k] >= 0)
                        {
                            stuck.Add("WS" + Convert.ToString(k) + "@outEntry-job" + Convert.ToString(wsPendingOut[k]));
                        }
                        else if (wsDone[k].Count < wsTotal[k])
                        {
                            stuck.Add("WS" + Convert.ToString(k) + "@process-head" + (inLane[k].Count > 0 ? Convert.ToString(inLane[k][0]) : "(lane empty)"));
                        }
                    }

                    var detail = new List<string>();
                    for (int k = 0; k < n; k++)
                    {
                        for (int b = 0; b < b_in_k[k]; b++)
                        {
                            if (double.IsNaN(inFree[k][b]) && solution.InputSlotSequences[k][b].Count > 0)
                            {
                                detail.Add("inSlot[" + Convert.ToString(k) + "][" + Convert.ToString(b) + "] held by job " + Convert.ToString(solution.InputSlotSequences[k][b][^1]));
                            }
                        }

                        for (int b = 0; b < b_out_k[k]; b++)
                        {
                            if (double.IsNaN(outFree[k][b]) && solution.OutputSlotSequences[k][b].Count > 0)
                            {
                                detail.Add("outSlot[" + Convert.ToString(k) + "][" + Convert.ToString(b) + "] held by job " + Convert.ToString(solution.OutputSlotSequences[k][b][^1]));
                            }
                        }

                        detail.Add("WS" + Convert.ToString(k) + " order=[" + string.Join(",", solution.WorkstationSequences[k]) + "] idx=" + Convert.ToString(wsIdx[k]));
                    }

                    for (int h = 0; h < r; h++)
                    {
                        detail.Add("AMR" + Convert.ToString(h) + " route=[" + string.Join(",", solution.AmrSequences[h]) + "] idx=" + Convert.ToString(amrIdx[h]));
                    }

                    detail.Add("ASRS seq=[" + string.Join(",", solution.AsrsSequence) + "] idx=" + Convert.ToString(asrsIdx));
                    LastFailure = "no executable step; waiting: " + string.Join(", ", stuck) + " || " + string.Join(" | ", detail);
                    return false;
                }

                if (kind == 0)
                {
                    var peek = PeekAsrs();
                    int op = solution.AsrsSequence[asrsIdx];
                    asrsDone[op] = peek.completion;
                    asrsPrevOp = op;
                    asrsPrevLoc = solution.AsrsLocation[op];
                    asrsPrevDone = peek.completion;
                    asrsIdx++;
                }
                else if (kind == 1)
                {
                    int h = arg;
                    var peek = PeekAmr(h);
                    int op = solution.AmrSequences[h][amrIdx[h]];
                    int node = RobotOpNode(op);
                    int st2 = RobotOpStation(op);
                    if (op >= m && op < 2 * m && st2 >= 0)
                    {
                        int job = op - m;
                        solution.InputSlot[job] = peek.slot;
                        solution.InputSlotSequences[st2][peek.slot].Add(job);
                        inFree[st2][peek.slot] = double.NaN;
                        lIn[job] = peek.done;
                        inLane[st2].Add(job);
                    }
                    else if (op >= 2 * m && op < 3 * m && st2 >= 0)
                    {
                        int job = op - 2 * m;
                        if (solution.OutputSlot[job] >= 0)
                        {
                            outFree[st2][solution.OutputSlot[job]] = peek.done;
                        }

                        if (outLane[st2].Count > 0 && outLane[st2][0] == job)
                        {
                            outLane[st2].RemoveAt(0);
                        }
                    }

                    robotDone[op] = peek.done;
                    amrPrevDone[h] = peek.done;
                    amrPrevNode[h] = node;
                    amrIdx[h]++;
                }
                else if (kind == 2)
                {
                    int k = arg;
                    var peek = PeekWsOut(k);
                    int job = wsPendingOut[k];
                    solution.OutputSlot[job] = peek.slot;
                    solution.OutputSlotSequences[k][peek.slot].Add(job);
                    outFree[k][peek.slot] = double.NaN;
                    lOut[job] = peek.entry;
                    outLane[k].Add(job);
                    wsLastOut[k] = peek.entry;
                    wsPendingOut[k] = -1;
                }
                else
                {
                    int k = arg;
                    double ln = PeekWsProcess(k);
                    int job = inLane[k][0];
                    inLane[k].RemoveAt(0);
                    wsDone[k].Add(job);
                    if (solution.InputSlot[job] >= 0)
                    {
                        inFree[k][solution.InputSlot[job]] = ln;
                    }

                    cnDone[job] = ln + s_in_ik[job][k] + p_ik[job][k] + s_out_ik[job][k];
                    wsIdx[k]++;
                    wsPendingOut[k] = job;
                }
            }

            if (asrsIdx < solution.AsrsSequence.Count)
            {
                return false;
            }

            for (int h = 0; h < r; h++)
            {
                if (amrIdx[h] < solution.AmrSequences[h].Count)
                {
                    return false;
                }
            }

            for (int k = 0; k < n; k++)
            {
                if (wsDone[k].Count < wsTotal[k] || wsPendingOut[k] >= 0)
                {
                    return false;
                }
            }

            for (int k = 0; k < n; k++)
            {
                solution.WorkstationSequences[k].Clear();
                solution.WorkstationSequences[k].AddRange(wsDone[k]);
            }

            return true;
        }

        static double[] Filled(int length)
        {
            double[] result = new double[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = double.NaN;
            }

            return result;
        }
    }
}

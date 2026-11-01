using System;
using System.Collections.Generic;

namespace PDP_ASRS_WIP
{
    public class DecodedSchedule
    {
        public bool IsFeasible = true;
        public List<string> Violations = new List<string>();
        public double[] AsrsStart = Array.Empty<double>();
        public double[] AsrsCompletion = Array.Empty<double>();
        public double[] RobotStart = Array.Empty<double>();
        public double[] RobotCompletion = Array.Empty<double>();
        public double[] ProcessStart = Array.Empty<double>();
        public double[] ProcessCompletion = Array.Empty<double>();
        public double[] BufferInEntry = Array.Empty<double>();
        public double[] BufferOutEntry = Array.Empty<double>();
        public double[] Tardiness = Array.Empty<double>();
        public double TotalTardiness;
        public double Makespan;
        public List<string> SummaryLines = new List<string>();
        public void Print()
        {
            foreach (string line in SummaryLines)
            {
                Console.WriteLine(line);
            }
        }
    }

    public static class SolutionDecoder
    {
        const double Eps = 1e-6;
        public static DecodedSchedule Decode(SequenceSolution solution, int numL, int r, int n, int m, double[] c_h, double[][] p_ik, double[] q_i, double[][][] s_ikh, double[][][] t_klh, int[][] E_ik, double[][] R_if, double[] d_i, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] W_a_if, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb, double[]? machineReadyAt = null, NodeLayout? nodeLayout = null, int asrsOutCap = 1, int asrsInCap = int.MaxValue, bool allowMissingJobs = false, int asrsPinnedPrefixLength = 0, int[]? asrsPinnedPrefix = null)
        {
            NodeLayout layout = nodeLayout ?? NodeLayout.Compact(n);
            var schedule = new DecodedSchedule
            {
                AsrsStart = new double[2 * m],
                AsrsCompletion = new double[2 * m],
                RobotStart = new double[4 * m],
                RobotCompletion = new double[4 * m],
                ProcessStart = new double[m],
                ProcessCompletion = new double[m],
                BufferInEntry = new double[m],
                BufferOutEntry = new double[m],
                Tardiness = new double[m]
            };
            bool[] asrsScheduled = new bool[2 * m];
            foreach (int op in solution.AsrsSequence)
            {
                if (op < 0 || op >= 2 * m || asrsScheduled[op])
                {
                    schedule.Violations.Add("AS/RS sequence contains an invalid or duplicated operation " + Convert.ToString(op) + ".");
                    continue;
                }

                asrsScheduled[op] = true;
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

            bool[] wsScheduled = new bool[m];
            for (int k = 0; k < n; k++)
            {
                foreach (int job in solution.WorkstationSequences[k])
                {
                    if (job < 0 || job >= m || wsScheduled[job])
                    {
                        schedule.Violations.Add("Workstation " + Convert.ToString(k) + " sequence contains an invalid or duplicated job " + Convert.ToString(job) + ".");
                        continue;
                    }

                    wsScheduled[job] = true;
                    if (solution.JobWorkstation[job] != k)
                    {
                        schedule.Violations.Add("Job " + Convert.ToString(job) + " is sequenced on workstation " + Convert.ToString(k) + " but JobWorkstation says " + Convert.ToString(solution.JobWorkstation[job]) + ".");
                    }
                }
            }

            bool[] hasInSlot = new bool[m];
            bool[] hasOutSlot = new bool[m];
            bool[] initialIn = new bool[m];
            bool[] initialOut = new bool[m];
            for (int i = 0; i < m; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    for (int b = 0; b < b_in_k[k]; b++)
                    {
                        if (W_in_ikb[i][k][b] > 0.5)
                        {
                            initialIn[i] = true;
                        }
                    }

                    for (int b = 0; b < b_out_k[k]; b++)
                    {
                        if (W_out_ikb[i][k][b] > 0.5)
                        {
                            initialOut[i] = true;
                        }
                    }
                }

                hasInSlot[i] = initialIn[i] || (u_rob_i[m + i] > 0.5 && solution.RobotOpAmr[m + i] >= 0);
                hasOutSlot[i] = initialOut[i] || wsScheduled[i];
            }

            ValidateStructure(schedule, solution, asrsScheduled, wsScheduled, hasInSlot, hasOutSlot, numL, r, n, m, c_h, q_i, E_ik, R_if, W_a_if, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, b_in_k, b_out_k, W_in_ikb, W_out_ikb, allowMissingJobs, initialIn);
            if (asrsPinnedPrefix != null && asrsPinnedPrefix.Length > 0)
            {
                for (int j = 0; j < asrsPinnedPrefix.Length; j++)
                {
                    if (j >= solution.AsrsSequence.Count)
                    {
                        schedule.Violations.Add("In-progress AS/RS operation " + Convert.ToString(asrsPinnedPrefix[j]) + " is missing from the AS/RS sequence (it must occupy position " + Convert.ToString(j) + ").");
                    }
                    else if (solution.AsrsSequence[j] != asrsPinnedPrefix[j])
                    {
                        schedule.Violations.Add("AS/RS position " + Convert.ToString(j) + " must hold the in-progress operation " + Convert.ToString(asrsPinnedPrefix[j]) + " (already on the shuttle/lifter) but holds " + Convert.ToString(solution.AsrsSequence[j]) + ".");
                    }
                }
            }

            bool fifoInOrder = Environment.GetEnvironmentVariable("PDP_WSFIFO_INORDER") != "0";
            bool fifoOutOrder = Environment.GetEnvironmentVariable("PDP_WSFIFO_OUTORDER") != "0";
            bool fifoOut = Environment.GetEnvironmentVariable("PDP_WSFIFO_OUT") != "0";
            int[] outSlotPred = new int[m];
            for (int i = 0; i < m; i++)
            {
                outSlotPred[i] = -1;
            }

            for (int k = 0; k < n; k++)
            {
                for (int b = 0; b < b_out_k[k]; b++)
                {
                    int prev = -1;
                    foreach (int job in solution.OutputSlotSequences[k][b])
                    {
                        if (job >= 0 && job < m)
                        {
                            outSlotPred[job] = prev;
                            prev = job;
                        }
                    }
                }
            }

            int[] qPred = new int[m];
            int[] inFifoGate = new int[m];
            int[] outFifoGate = new int[m];
            for (int i = 0; i < m; i++)
            {
                qPred[i] = -1;
                inFifoGate[i] = -1;
                outFifoGate[i] = -1;
            }

            for (int k = 0; k < n; k++)
            {
                var q = new List<int>();
                foreach (int job in solution.WorkstationSequences[k])
                {
                    if (job < 0 || job >= m)
                    {
                        continue;
                    }

                    int t = q.Count;
                    if (t >= 1)
                    {
                        qPred[job] = q[t - 1];
                    }

                    if (b_in_k[k] > 0 && t >= b_in_k[k])
                    {
                        inFifoGate[job] = q[t - b_in_k[k]];
                    }

                    if (b_out_k[k] > 0 && t >= b_out_k[k])
                    {
                        outFifoGate[job] = q[t - b_out_k[k]];
                    }

                    q.Add(job);
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

                int k = solution.JobWorkstation[op < 2 * m ? op - m : op - 2 * m];
                if (k < 0)
                {
                    return layout.AsrsOut;
                }

                return op < 2 * m ? layout.In(k) : layout.Out(k);
            }

            double OutExit(int job)
            {
                if (u_rob_i[2 * m + job] > 0.5 && solution.RobotOpAmr[2 * m + job] >= 0)
                {
                    return schedule.RobotCompletion[2 * m + job];
                }

                return 0.0;
            }

            int totalOps = solution.AsrsSequence.Count + 2 * m + 4 * m;
            int iterationCap = 2 * totalOps + 10;
            if (int.TryParse(Environment.GetEnvironmentVariable("PDP_DECODE_ITER_MULT"), out int _im) && _im > 1)
            {
                iterationCap *= _im;
            }

            bool converged = false;
            string cycTag = "";
            int cycIdx = -1;
            bool diagOn = Environment.GetEnvironmentVariable("PDP_DECODE_CYCLE_DIAG") == "1";
            double[] snapProc = null, snapBufIn = null, snapBufOut = null, snapAsrs = null, snapRob = null;
            for (int iteration = 0; iteration < iterationCap; iteration++)
            {
                double delta = 0.0;
                string dTag = "";
                int dIdx = -1;
                void Bump(double v, string tag, int idx)
                {
                    if (v > delta)
                    {
                        delta = v;
                        dTag = tag;
                        dIdx = idx;
                    }
                }

                int prevOp = 2 * m;
                int prevLoc = numL;
                foreach (int op in solution.AsrsSequence)
                {
                    int loc = solution.AsrsLocation[op];
                    if (loc < 0 || loc >= numL)
                    {
                        schedule.Violations.Add("AS/RS operation " + Convert.ToString(op) + " has no valid location.");
                        loc = 0;
                    }

                    double release = 0.0;
                    if (op >= m)
                    {
                        int job = op - m;
                        if (u_rob_i[3 * m + job] > 0.5 && solution.RobotOpAmr[3 * m + job] >= 0)
                        {
                            release = Math.Max(release, schedule.RobotCompletion[3 * m + job]);
                        }
                        else if (u_asrs_i[job] > 0.5 && asrsScheduled[job])
                        {
                            release = Math.Max(release, schedule.AsrsCompletion[job]);
                        }

                        int vacatingJob = retrievalJobAtLoc[loc];
                        if (vacatingJob >= 0 && asrsScheduled[vacatingJob])
                        {
                            release = Math.Max(release, schedule.AsrsCompletion[vacatingJob]);
                        }
                    }

                    double prevCompletion = prevOp == 2 * m ? 0.0 : schedule.AsrsCompletion[prevOp];
                    double start = Math.Max(prevCompletion, release);
                    double completion = start + r_ijfg[prevOp][op][prevLoc][loc];
                    if (op < m && retGate[op] >= 0 && u_rob_i[retGate[op]] > 0.5 && solution.RobotOpAmr[retGate[op]] >= 0)
                    {
                        completion = Math.Max(completion, schedule.RobotCompletion[retGate[op]]);
                    }

                    Bump(Math.Abs(start - schedule.AsrsStart[op]), "asrsStart", op);
                    Bump(Math.Abs(completion - schedule.AsrsCompletion[op]), "asrsComp", op);
                    schedule.AsrsStart[op] = start;
                    schedule.AsrsCompletion[op] = completion;
                    prevOp = op;
                    prevLoc = loc;
                }

                for (int h = 0; h < r; h++)
                {
                    int prevNode = layout.Origin;
                    double prevCompletion = 0.0;
                    foreach (int op in solution.AmrSequences[h])
                    {
                        int node = RobotOpNode(op);
                        double release = 0.0;
                        if (op < m)
                        {
                            if (u_asrs_i[op] > 0.5 && asrsScheduled[op])
                            {
                                release = schedule.AsrsCompletion[op];
                            }
                        }
                        else if (op < 2 * m)
                        {
                            int job = op - m;
                            if (u_rob_i[job] > 0.5 && solution.RobotOpAmr[job] >= 0)
                            {
                                release = schedule.RobotCompletion[job];
                            }
                        }
                        else if (op < 3 * m)
                        {
                            int job = op - 2 * m;
                            release = schedule.BufferOutEntry[job];
                            if (fifoOut && fifoOutOrder && qPred[job] >= 0)
                            {
                                release = Math.Max(release, OutExit(qPred[job]));
                            }
                        }
                        else
                        {
                            int job = op - 3 * m;
                            if (u_rob_i[2 * m + job] > 0.5 && solution.RobotOpAmr[2 * m + job] >= 0)
                            {
                                release = schedule.RobotCompletion[2 * m + job];
                            }
                        }

                        double start = Math.Max(prevCompletion + t_klh[prevNode][node][h], release);
                        double completion;
                        if (op >= m && op < 2 * m)
                        {
                            int job = op - m;
                            double entry = start + s_ikh[op][node][h];
                            if (inFifoGate[job] >= 0 && wsScheduled[inFifoGate[job]])
                            {
                                entry = Math.Max(entry, schedule.ProcessStart[inFifoGate[job]]);
                            }

                            if (fifoInOrder && qPred[job] >= 0 && hasInSlot[qPred[job]])
                            {
                                entry = Math.Max(entry, schedule.BufferInEntry[qPred[job]]);
                            }

                            Bump(Math.Abs(entry - schedule.BufferInEntry[job]), "bufIn", job);
                            schedule.BufferInEntry[job] = entry;
                            completion = entry;
                        }
                        else
                        {
                            completion = start + s_ikh[op][node][h];
                            if (op >= 3 * m)
                            {
                                int stoOp = m + (op - 3 * m);
                                if (asrsScheduled[stoOp] && stoGate[stoOp] >= 0 && asrsScheduled[stoGate[stoOp]])
                                {
                                    completion = Math.Max(completion, schedule.AsrsCompletion[stoGate[stoOp]]);
                                }
                            }
                        }

                        Bump(Math.Abs(start - schedule.RobotStart[op]), "robStart", op);
                        Bump(Math.Abs(completion - schedule.RobotCompletion[op]), "robComp", op);
                        schedule.RobotStart[op] = start;
                        schedule.RobotCompletion[op] = completion;
                        prevNode = node;
                        prevCompletion = completion;
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    int prevJob = -1;
                    foreach (int job in solution.WorkstationSequences[k])
                    {
                        double release = hasInSlot[job] ? schedule.BufferInEntry[job] : 0.0;
                        if (prevJob >= 0)
                        {
                            release = Math.Max(release, schedule.BufferOutEntry[prevJob]);
                        }
                        else if (machineReadyAt != null)
                        {
                            release = Math.Max(release, machineReadyAt[k]);
                        }

                        double start = release;
                        double completion = start + s_in_ik[job][k] + p_ik[job][k] + s_out_ik[job][k];
                        double outEntry = completion;
                        int outGate = fifoOut ? outFifoGate[job] : outSlotPred[job];
                        if (outGate >= 0)
                        {
                            outEntry = Math.Max(outEntry, OutExit(outGate));
                        }

                        Bump(Math.Abs(start - schedule.ProcessStart[job]), "procStart", job);
                        Bump(Math.Abs(completion - schedule.ProcessCompletion[job]), "procComp", job);
                        Bump(Math.Abs(outEntry - schedule.BufferOutEntry[job]), "bufOut", job);
                        schedule.ProcessStart[job] = start;
                        schedule.ProcessCompletion[job] = completion;
                        schedule.BufferOutEntry[job] = outEntry;
                        prevJob = job;
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    if (initialOut[i] && !wsScheduled[i])
                    {
                        schedule.BufferOutEntry[i] = 0.0;
                    }

                    if (initialIn[i])
                    {
                        schedule.BufferInEntry[i] = 0.0;
                    }
                }

                cycTag = dTag;
                cycIdx = dIdx;
                if (diagOn && iteration == iterationCap - 2)
                {
                    snapProc = (double[])schedule.ProcessCompletion.Clone();
                    snapBufIn = (double[])schedule.BufferInEntry.Clone();
                    snapBufOut = (double[])schedule.BufferOutEntry.Clone();
                    snapAsrs = (double[])schedule.AsrsCompletion.Clone();
                    snapRob = (double[])schedule.RobotCompletion.Clone();
                }

                if (delta <= Eps)
                {
                    converged = true;
                    break;
                }
            }

            if (!converged)
            {
                schedule.Violations.Add("The selected disjunctive arcs contain a cycle; no consistent schedule exists.");
                if (Environment.GetEnvironmentVariable("PDP_DECODE_CYCLE_DIAG") == "1")
                {
                    Console.WriteLine($"@@CYCDIAG last={cycTag}[{cycIdx}] iterCap={iterationCap}");
                    {
                        int NV = 9 * m;
                        var adj = new List<int>[NV];
                        var lbl = new string[NV];
                        for (int i = 0; i < NV; i++)
                        {
                            adj[i] = new List<int>();
                        }

                        for (int j = 0; j < m; j++)
                        {
                            lbl[j] = $"ret{j}";
                            lbl[m + j] = $"sto{j}";
                            lbl[2 * m + j] = $"P+{j}";
                            lbl[3 * m + j] = $"D+{j}";
                            lbl[4 * m + j] = $"P-{j}";
                            lbl[5 * m + j] = $"D-{j}";
                            lbl[6 * m + j] = $"proc{j}";
                            lbl[7 * m + j] = $"bufIn{j}";
                            lbl[8 * m + j] = $"bufOut{j}";
                        }

                        int A(int op) => op;
                        int Rn(int op) => 2 * m + op;
                        int Pn(int job) => 6 * m + job;
                        int BIn(int job) => 7 * m + job;
                        int BOn(int job) => 8 * m + job;
                        void Ad(int u, int v)
                        {
                            if (u >= 0 && v >= 0 && u < NV && v < NV)
                            {
                                adj[u].Add(v);
                            }
                        }

                        int prevAsrs = -1;
                        foreach (int op in solution.AsrsSequence)
                        {
                            if (prevAsrs >= 0)
                            {
                                Ad(A(prevAsrs), A(op));
                            }

                            if (op >= m)
                            {
                                int job = op - m;
                                if (u_rob_i[3 * m + job] > 0.5 && solution.RobotOpAmr[3 * m + job] >= 0)
                                {
                                    Ad(Rn(3 * m + job), A(op));
                                }
                                else if (u_asrs_i[job] > 0.5 && asrsScheduled[job])
                                {
                                    Ad(A(job), A(op));
                                }

                                int loc = solution.AsrsLocation[op];
                                if (loc >= 0 && loc < numL)
                                {
                                    int vac = retrievalJobAtLoc[loc];
                                    if (vac >= 0 && asrsScheduled[vac])
                                    {
                                        Ad(A(vac), A(op));
                                    }
                                }
                            }

                            if (op < m && retGate[op] >= 0 && u_rob_i[retGate[op]] > 0.5 && solution.RobotOpAmr[retGate[op]] >= 0)
                            {
                                Ad(Rn(retGate[op]), A(op));
                            }

                            prevAsrs = op;
                        }

                        for (int h = 0; h < r; h++)
                        {
                            int prevR = -1;
                            foreach (int op in solution.AmrSequences[h])
                            {
                                if (prevR >= 0)
                                {
                                    Ad(Rn(prevR), Rn(op));
                                }

                                if (op < m)
                                {
                                    if (u_asrs_i[op] > 0.5 && asrsScheduled[op])
                                    {
                                        Ad(A(op), Rn(op));
                                    }
                                }
                                else if (op < 2 * m)
                                {
                                    int job = op - m;
                                    if (u_rob_i[job] > 0.5 && solution.RobotOpAmr[job] >= 0)
                                    {
                                        Ad(Rn(job), Rn(op));
                                    }

                                    Ad(Rn(op), BIn(job));
                                    if (inFifoGate[job] >= 0)
                                    {
                                        Ad(Pn(inFifoGate[job]), BIn(job));
                                    }

                                    if (fifoInOrder && qPred[job] >= 0)
                                    {
                                        Ad(BIn(qPred[job]), BIn(job));
                                    }
                                }
                                else if (op < 3 * m)
                                {
                                    int job = op - 2 * m;
                                    Ad(BOn(job), Rn(op));
                                    if (fifoOut && fifoOutOrder && qPred[job] >= 0)
                                    {
                                        Ad(Rn(2 * m + qPred[job]), Rn(op));
                                    }
                                }
                                else
                                {
                                    int job = op - 3 * m;
                                    if (u_rob_i[2 * m + job] > 0.5 && solution.RobotOpAmr[2 * m + job] >= 0)
                                    {
                                        Ad(Rn(2 * m + job), Rn(op));
                                    }

                                    int stoOp = m + job;
                                    if (asrsScheduled[stoOp] && stoGate[stoOp] >= 0 && asrsScheduled[stoGate[stoOp]])
                                    {
                                        Ad(A(stoGate[stoOp]), Rn(op));
                                    }
                                }

                                prevR = op;
                            }
                        }

                        for (int k = 0; k < n; k++)
                        {
                            int prevW = -1;
                            foreach (int job in solution.WorkstationSequences[k])
                            {
                                if (hasInSlot[job])
                                {
                                    Ad(BIn(job), Pn(job));
                                }

                                if (prevW >= 0)
                                {
                                    Ad(BOn(prevW), Pn(job));
                                }

                                Ad(Pn(job), BOn(job));
                                int outGateA = fifoOut ? outFifoGate[job] : outSlotPred[job];
                                if (outGateA >= 0)
                                {
                                    Ad(Rn(2 * m + outGateA), BOn(job));
                                }

                                prevW = job;
                            }
                        }

                        var color = new int[NV];
                        var stackPath = new List<int>();
                        bool found = false;
                        void Dfs(int u)
                        {
                            if (found)
                            {
                                return;
                            }

                            color[u] = 1;
                            stackPath.Add(u);
                            foreach (int v in adj[u])
                            {
                                if (found)
                                {
                                    return;
                                }

                                if (color[v] == 1)
                                {
                                    int at = stackPath.IndexOf(v);
                                    var ring = stackPath.GetRange(at, stackPath.Count - at);
                                    var names = new List<string>();
                                    foreach (int x in ring)
                                    {
                                        names.Add(lbl[x] ?? x.ToString());
                                    }

                                    Console.WriteLine("@@CYCDIAG RING " + string.Join(" -> ", names) + " -> " + lbl[v]);
                                    found = true;
                                    return;
                                }

                                if (color[v] == 0)
                                {
                                    Dfs(v);
                                }
                            }

                            color[u] = 2;
                            stackPath.RemoveAt(stackPath.Count - 1);
                        }

                        for (int i = 0; i < NV && !found; i++)
                        {
                            if (color[i] == 0)
                            {
                                Dfs(i);
                            }
                        }

                        if (!found)
                        {
                            Console.WriteLine("@@CYCDIAG RING none (아크 모델이 완화식과 다름)");
                        }
                    }

                    if (snapProc != null)
                    {
                        var grow = new List<(double g, string w)>();
                        void Scan(double[] now, double[] old, string tag)
                        {
                            for (int i = 0; i < now.Length; i++)
                            {
                                double d = now[i] - old[i];
                                if (d > 1e-6)
                                {
                                    grow.Add((d, $"{tag}{i}"));
                                }
                            }
                        }

                        Scan(schedule.ProcessCompletion, snapProc, "procComp job");
                        Scan(schedule.BufferInEntry, snapBufIn, "bufIn job");
                        Scan(schedule.BufferOutEntry, snapBufOut, "bufOut job");
                        Scan(schedule.AsrsCompletion, snapAsrs, "asrsComp op");
                        Scan(schedule.RobotCompletion, snapRob, "robComp op");
                        grow.Sort((x, y) => y.g.CompareTo(x.g));
                        Console.WriteLine($"@@CYCDIAG growing={grow.Count} (마지막 1회 반복에서 증가한 변수)");
                        for (int i = 0; i < Math.Min(20, grow.Count); i++)
                        {
                            Console.WriteLine($"@@CYCDIAG  +{Math.Round(grow[i].g, 2)}  {grow[i].w}");
                        }
                    }

                    var tops = new List<(double v, string w)>();
                    for (int i = 0; i < m; i++)
                    {
                        tops.Add((schedule.ProcessCompletion[i], $"procComp job{i}"));
                        tops.Add((schedule.BufferInEntry[i], $"bufIn job{i}"));
                        tops.Add((schedule.BufferOutEntry[i], $"bufOut job{i}"));
                    }

                    for (int i = 0; i < schedule.AsrsCompletion.Length; i++)
                    {
                        tops.Add((schedule.AsrsCompletion[i], $"asrsComp op{i}"));
                    }

                    for (int i = 0; i < schedule.RobotCompletion.Length; i++)
                    {
                        tops.Add((schedule.RobotCompletion[i], $"robComp op{i}"));
                    }

                    tops.Sort((x, y) => y.v.CompareTo(x.v));
                    for (int i = 0; i < Math.Min(12, tops.Count); i++)
                    {
                        Console.WriteLine($"@@CYCDIAG  {tops[i].w} = {Math.Round(tops[i].v, 1)}");
                    }

                    Console.Out.Flush();
                }
            }

            double totalTardiness = 0.0;
            for (int i = 0; i < m; i++)
            {
                double tardiness = 0.0;
                if (u_asrs_i[m + i] > 0.5 && asrsScheduled[m + i])
                {
                    tardiness = Math.Max(0.0, schedule.AsrsCompletion[m + i] - d_i[i]);
                }

                schedule.Tardiness[i] = tardiness;
                totalTardiness += tardiness;
            }

            schedule.TotalTardiness = totalTardiness;
            double makespan = 0.0;
            foreach (int op in solution.AsrsSequence)
            {
                makespan = Math.Max(makespan, schedule.AsrsCompletion[op]);
            }

            for (int op = 0; op < 4 * m; op++)
            {
                if (solution.RobotOpAmr[op] >= 0)
                {
                    makespan = Math.Max(makespan, schedule.RobotCompletion[op]);
                }
            }

            for (int i = 0; i < m; i++)
            {
                if (wsScheduled[i])
                {
                    makespan = Math.Max(makespan, schedule.ProcessCompletion[i]);
                }
            }

            schedule.Makespan = makespan;
            schedule.IsFeasible = schedule.Violations.Count == 0;
            BuildSummary(schedule, solution, asrsScheduled, wsScheduled, hasInSlot, hasOutSlot, numL, r, n, m, c_h, q_i, d_i, u_rob_i, u_ws_i, u_asrs_i, W_r_ih);
            return schedule;
        }

        static void ValidateStructure(DecodedSchedule schedule, SequenceSolution solution, bool[] asrsScheduled, bool[] wsScheduled, bool[] hasInSlot, bool[] hasOutSlot, int numL, int r, int n, int m, double[] c_h, double[] q_i, int[][] E_ik, double[][] R_if, double[][] W_a_if, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb, bool allowMissingJobs = false, bool[]? initialIn = null)
        {
            bool JobAbsent(int i)
            {
                return allowMissingJobs && u_ws_i[i] > 0.5 && !wsScheduled[i] && !asrsScheduled[i] && !asrsScheduled[m + i] && solution.RobotOpAmr[i] < 0 && solution.RobotOpAmr[m + i] < 0 && solution.RobotOpAmr[2 * m + i] < 0 && solution.RobotOpAmr[3 * m + i] < 0;
            }

            for (int op = 0; op < 2 * m; op++)
            {
                bool active = u_asrs_i[op] > 0.5;
                if (active && !asrsScheduled[op] && !JobAbsent(op < m ? op : op - m))
                {
                    schedule.Violations.Add("Active AS/RS operation " + Convert.ToString(op) + " is missing from the AS/RS sequence.");
                }

                if (!active && asrsScheduled[op])
                {
                    schedule.Violations.Add("Skipped AS/RS operation " + Convert.ToString(op) + " appears in the AS/RS sequence.");
                }
            }

            for (int i = 0; i < m; i++)
            {
                bool active = u_ws_i[i] > 0.5;
                if (active && !wsScheduled[i] && !JobAbsent(i))
                {
                    schedule.Violations.Add("Active processing of job " + Convert.ToString(i) + " is missing from the workstation sequences.");
                }

                if (!active && wsScheduled[i])
                {
                    schedule.Violations.Add("Skipped processing of job " + Convert.ToString(i) + " appears in a workstation sequence.");
                }
            }

            for (int k = 0; k < n; k++)
            {
                if (solution.WorkstationSequences[k].Count > 0)
                {
                    int first = solution.WorkstationSequences[k][0];
                    if (first >= 0 && first < m && W_n_ik[first][k] < 0.5)
                    {
                        schedule.Violations.Add("Workstation " + Convert.ToString(k) + " starts with job " + Convert.ToString(first) + " although W_n_ik forbids it.");
                    }
                }
            }

            if (initialIn != null)
            {
                for (int i = 0; i < m; i++)
                {
                    bool machineWip = u_ws_i[i] > 0.5 && u_rob_i[m + i] < 0.5 && !initialIn[i];
                    if (!machineWip || (allowMissingJobs && !wsScheduled[i]))
                    {
                        continue;
                    }

                    int k = solution.JobWorkstation[i];
                    if (k < 0 || k >= n)
                    {
                        continue;
                    }

                    var seq = solution.WorkstationSequences[k];
                    if (seq.Count > 0 && seq[0] != i)
                    {
                        schedule.Violations.Add("Machine-WIP job " + Convert.ToString(i) + " must be processed first at workstation " + Convert.ToString(k) + " (machine is occupied at t = 0), but the sequence starts with job " + Convert.ToString(seq[0]) + ".");
                    }
                }
            }

            bool[] storageLocUsed = new bool[numL];
            for (int i = 0; i < m; i++)
            {
                if (asrsScheduled[i])
                {
                    int loc = solution.AsrsLocation[i];
                    if (loc < 0 || loc >= numL || R_if[i][loc] < 0.5)
                    {
                        schedule.Violations.Add("Retrieval of job " + Convert.ToString(i) + " uses location " + Convert.ToString(loc) + " which is not its R_if location.");
                    }
                }

                if (asrsScheduled[m + i])
                {
                    int loc = solution.AsrsLocation[m + i];
                    if (loc < 0 || loc >= numL)
                    {
                        schedule.Violations.Add("Storage of job " + Convert.ToString(i) + " has no valid location.");
                    }
                    else if (storageLocUsed[loc])
                    {
                        schedule.Violations.Add("Storage location " + Convert.ToString(loc) + " is used by more than one storage operation.");
                    }
                    else
                    {
                        storageLocUsed[loc] = true;
                    }

                    if (loc >= 0 && loc < numL && W_a_if[m + i][loc] < 0.5)
                    {
                        schedule.Violations.Add("Storage of job " + Convert.ToString(i) + " uses location " + Convert.ToString(loc) + " which W_a_if forbids.");
                    }
                }
            }

            {
                int[] asrsPosition = new int[2 * m];
                Array.Fill(asrsPosition, -1);
                for (int pos = 0; pos < solution.AsrsSequence.Count; pos++)
                {
                    int op0 = solution.AsrsSequence[pos];
                    if (op0 >= 0 && op0 < 2 * m)
                    {
                        asrsPosition[op0] = pos;
                    }
                }

                int[] cellOccupant = new int[numL];
                Array.Fill(cellOccupant, -1);
                for (int j = 0; j < m; j++)
                {
                    if (u_asrs_i[j] < 0.5)
                    {
                        continue;
                    }

                    for (int f = 0; f < numL; f++)
                    {
                        if (R_if[j][f] > 0.5)
                        {
                            cellOccupant[f] = j;
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    if (!asrsScheduled[m + i])
                    {
                        continue;
                    }

                    int loc = solution.AsrsLocation[m + i];
                    if (loc < 0 || loc >= numL || cellOccupant[loc] < 0 || cellOccupant[loc] == i)
                    {
                        continue;
                    }

                    int owner = cellOccupant[loc];
                    if (!asrsScheduled[owner])
                    {
                        if (!(allowMissingJobs && JobAbsent(owner)))
                        {
                            schedule.Violations.Add("Storage of job " + Convert.ToString(i) + " reuses cell " + Convert.ToString(loc) + " whose occupant job " + Convert.ToString(owner) + " is never retrieved.");
                        }
                    }
                    else if (asrsPosition[owner] > asrsPosition[m + i])
                    {
                        schedule.Violations.Add("Storage of job " + Convert.ToString(i) + " reuses cell " + Convert.ToString(loc) + " before its occupant job " + Convert.ToString(owner) + " is retrieved.");
                    }
                }
            }

            int[] inSeqCount = new int[m];
            int[] outSeqCount = new int[m];
            for (int k = 0; k < n; k++)
            {
                for (int b = 0; b < b_in_k[k]; b++)
                {
                    foreach (int job in solution.InputSlotSequences[k][b])
                    {
                        if (job < 0 || job >= m)
                        {
                            schedule.Violations.Add("Input slot " + Convert.ToString(b) + " at workstation " + Convert.ToString(k) + " contains an invalid job " + Convert.ToString(job) + ".");
                            continue;
                        }

                        inSeqCount[job]++;
                        if (solution.JobWorkstation[job] != k || solution.InputSlot[job] != b)
                        {
                            schedule.Violations.Add("Job " + Convert.ToString(job) + " appears in input slot " + Convert.ToString(b) + " of workstation " + Convert.ToString(k) + " but its assignment says WS " + Convert.ToString(solution.JobWorkstation[job]) + " slot " + Convert.ToString(solution.InputSlot[job]) + ".");
                        }
                    }
                }

                for (int b = 0; b < b_out_k[k]; b++)
                {
                    foreach (int job in solution.OutputSlotSequences[k][b])
                    {
                        if (job < 0 || job >= m)
                        {
                            schedule.Violations.Add("Output slot " + Convert.ToString(b) + " at workstation " + Convert.ToString(k) + " contains an invalid job " + Convert.ToString(job) + ".");
                            continue;
                        }

                        outSeqCount[job]++;
                        if (solution.JobWorkstation[job] != k || solution.OutputSlot[job] != b)
                        {
                            schedule.Violations.Add("Job " + Convert.ToString(job) + " appears in output slot " + Convert.ToString(b) + " of workstation " + Convert.ToString(k) + " but its assignment says WS " + Convert.ToString(solution.JobWorkstation[job]) + " slot " + Convert.ToString(solution.OutputSlot[job]) + ".");
                        }
                    }
                }
            }

            for (int i = 0; i < m; i++)
            {
                if (hasInSlot[i] && inSeqCount[i] != 1 && !JobAbsent(i))
                {
                    schedule.Violations.Add("Job " + Convert.ToString(i) + " must occupy exactly one input-buffer slot but appears " + Convert.ToString(inSeqCount[i]) + " times (Eq. (29)).");
                }

                if (!hasInSlot[i] && inSeqCount[i] > 0)
                {
                    schedule.Violations.Add("Job " + Convert.ToString(i) + " occupies an input-buffer slot although it never enters the input buffer.");
                }

                if (hasOutSlot[i] && outSeqCount[i] != 1 && !JobAbsent(i))
                {
                    schedule.Violations.Add("Job " + Convert.ToString(i) + " must occupy exactly one output-buffer slot but appears " + Convert.ToString(outSeqCount[i]) + " times (Eq. (34)).");
                }

                if (!hasOutSlot[i] && outSeqCount[i] > 0)
                {
                    schedule.Violations.Add("Job " + Convert.ToString(i) + " occupies an output-buffer slot although it never enters the output buffer.");
                }

                int k0 = solution.JobWorkstation[i];
                if (k0 >= 0)
                {
                    for (int b = 0; b < b_in_k[k0]; b++)
                    {
                        if (W_in_ikb[i][k0][b] > 0.5 && solution.InputSlot[i] != b)
                        {
                            schedule.Violations.Add("Job " + Convert.ToString(i) + " initially occupies input slot " + Convert.ToString(b) + " but is assigned slot " + Convert.ToString(solution.InputSlot[i]) + " (Eq. (30)).");
                        }
                    }

                    for (int b = 0; b < b_out_k[k0]; b++)
                    {
                        if (W_out_ikb[i][k0][b] > 0.5 && solution.OutputSlot[i] != b)
                        {
                            schedule.Violations.Add("Job " + Convert.ToString(i) + " initially occupies output slot " + Convert.ToString(b) + " but is assigned slot " + Convert.ToString(solution.OutputSlot[i]) + " (Eq. (35)).");
                        }
                    }
                }
            }

            for (int k = 0; k < n; k++)
            {
                for (int b = 0; b < b_in_k[k]; b++)
                {
                    var seq = solution.InputSlotSequences[k][b];
                    for (int idx = 0; idx + 1 < seq.Count; idx++)
                    {
                        int pred = seq[idx];
                        if (pred >= 0 && pred < m && !wsScheduled[pred])
                        {
                            schedule.Violations.Add("Job " + Convert.ToString(seq[idx + 1]) + " follows job " + Convert.ToString(pred) + " in input slot " + Convert.ToString(b) + " of workstation " + Convert.ToString(k) + " although the predecessor is never processed.");
                        }
                    }
                }

                for (int b = 0; b < b_out_k[k]; b++)
                {
                    var seq = solution.OutputSlotSequences[k][b];
                    for (int idx = 0; idx + 1 < seq.Count; idx++)
                    {
                        int pred = seq[idx];
                        if (pred >= 0 && pred < m && !(u_rob_i[2 * m + pred] > 0.5 && solution.RobotOpAmr[2 * m + pred] >= 0))
                        {
                            schedule.Violations.Add("Job " + Convert.ToString(seq[idx + 1]) + " follows job " + Convert.ToString(pred) + " in output slot " + Convert.ToString(b) + " of workstation " + Convert.ToString(k) + " although the predecessor is never picked up.");
                        }
                    }
                }
            }

            int[] positionInAmr = new int[4 * m];
            for (int op = 0; op < 4 * m; op++)
            {
                positionInAmr[op] = -1;
            }

            for (int h = 0; h < r; h++)
            {
                for (int position = 0; position < solution.AmrSequences[h].Count; position++)
                {
                    int op = solution.AmrSequences[h][position];
                    if (op < 0 || op >= 4 * m)
                    {
                        schedule.Violations.Add("AMR " + Convert.ToString(h) + " sequence contains an invalid operation " + Convert.ToString(op) + ".");
                        continue;
                    }

                    if (positionInAmr[op] >= 0)
                    {
                        schedule.Violations.Add("Robot operation " + Convert.ToString(op) + " appears more than once in the AMR sequences.");
                    }

                    positionInAmr[op] = position;
                    if (solution.RobotOpAmr[op] != h)
                    {
                        schedule.Violations.Add("Robot operation " + Convert.ToString(op) + " is sequenced on AMR " + Convert.ToString(h) + " but RobotOpAmr says " + Convert.ToString(solution.RobotOpAmr[op]) + ".");
                    }
                }

                if (solution.AmrSequences[h].Count > 0)
                {
                    int first = solution.AmrSequences[h][0];
                    if (first >= 0 && first < 4 * m && W_r_ih[first][h] < 0.5)
                    {
                        schedule.Violations.Add("AMR " + Convert.ToString(h) + " starts with operation " + Convert.ToString(first) + " although W_r_ih forbids it.");
                    }
                }
            }

            for (int op = 0; op < 4 * m; op++)
            {
                bool active = u_rob_i[op] > 0.5;
                bool scheduled = solution.RobotOpAmr[op] >= 0;
                if (active && !scheduled && !JobAbsent(op % m))
                {
                    schedule.Violations.Add("Active robot operation " + Convert.ToString(op) + " is not assigned to any AMR.");
                }

                if (!active && scheduled)
                {
                    schedule.Violations.Add("Skipped robot operation " + Convert.ToString(op) + " is assigned to an AMR.");
                }
            }

            for (int i = 0; i < m; i++)
            {
                CheckPair(schedule, solution, positionInAmr, u_rob_i, W_r_ih, i, m + i, "P+", "D+");
                CheckPair(schedule, solution, positionInAmr, u_rob_i, W_r_ih, 2 * m + i, 3 * m + i, "P-", "D-");
            }

            for (int i = 0; i < m; i++)
            {
                bool needsStation = (u_rob_i[m + i] > 0.5 && solution.RobotOpAmr[m + i] >= 0) || wsScheduled[i] || hasOutSlot[i];
                if (!needsStation)
                {
                    continue;
                }

                int k = solution.JobWorkstation[i];
                if (k < 0 || k >= n)
                {
                    schedule.Violations.Add("Job " + Convert.ToString(i) + " needs a workstation but none is assigned.");
                }
                else if (u_rob_i[m + i] > 0.5 && E_ik[i][k] != 1)
                {
                    schedule.Violations.Add("Job " + Convert.ToString(i) + " is delivered to workstation " + Convert.ToString(k) + " although E_ik forbids it.");
                }
            }

            for (int h = 0; h < r; h++)
            {
                double load = 0.0;
                for (int j = m; j < 2 * m; j++)
                {
                    load -= q_i[j] * W_r_ih[j][h];
                }

                for (int j = 3 * m; j < 4 * m; j++)
                {
                    load -= q_i[j] * W_r_ih[j][h];
                }

                if (load > c_h[h] + Eps)
                {
                    schedule.Violations.Add("AMR " + Convert.ToString(h) + " starts with load " + Convert.ToString(load) + " exceeding its capacity " + Convert.ToString(c_h[h]) + ".");
                }

                foreach (int op in solution.AmrSequences[h])
                {
                    if (op < 0 || op >= 4 * m)
                    {
                        continue;
                    }

                    load += q_i[op];
                    if (load > c_h[h] + Eps || load < -Eps)
                    {
                        schedule.Violations.Add("AMR " + Convert.ToString(h) + " reaches load " + Convert.ToString(load) + " after operation " + Convert.ToString(op) + " (capacity " + Convert.ToString(c_h[h]) + ").");
                    }
                }
            }
        }

        static void CheckPair(DecodedSchedule schedule, SequenceSolution solution, int[] positionInAmr, double[] u_rob_i, double[][] W_r_ih, int pickupOp, int deliveryOp, string pickupName, string deliveryName)
        {
            bool pickupActive = u_rob_i[pickupOp] > 0.5;
            bool deliveryActive = u_rob_i[deliveryOp] > 0.5;
            if (pickupActive && deliveryActive && solution.RobotOpAmr[pickupOp] >= 0 && solution.RobotOpAmr[deliveryOp] >= 0)
            {
                if (solution.RobotOpAmr[pickupOp] != solution.RobotOpAmr[deliveryOp])
                {
                    schedule.Violations.Add(pickupName + " and " + deliveryName + " of operations " + Convert.ToString(pickupOp) + "/" + Convert.ToString(deliveryOp) + " use different AMRs.");
                }
                else if (positionInAmr[pickupOp] >= 0 && positionInAmr[deliveryOp] >= 0 && positionInAmr[pickupOp] > positionInAmr[deliveryOp])
                {
                    schedule.Violations.Add(deliveryName + " of operation " + Convert.ToString(deliveryOp) + " is sequenced before its pickup " + pickupName + ".");
                }
            }

            if (!pickupActive && deliveryActive && solution.RobotOpAmr[deliveryOp] >= 0 && W_r_ih[deliveryOp][solution.RobotOpAmr[deliveryOp]] < 0.5)
            {
                schedule.Violations.Add("Loaded WIP operation " + Convert.ToString(deliveryOp) + " is assigned to AMR " + Convert.ToString(solution.RobotOpAmr[deliveryOp]) + " although W_r_ih fixes another AMR.");
            }
        }

        static void BuildSummary(DecodedSchedule schedule, SequenceSolution solution, bool[] asrsScheduled, bool[] wsScheduled, bool[] hasInSlot, bool[] hasOutSlot, int numL, int r, int n, int m, double[] c_h, double[] q_i, double[] d_i, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_r_ih)
        {
            var lines = schedule.SummaryLines;
            string Round(double value)
            {
                return Convert.ToString(Math.Round(value, 4, MidpointRounding.AwayFromZero));
            }

            string Timing(double start, double end)
            {
                return "[start=" + Round(start) + ", end=" + Round(end) + "]";
            }

            string RobotOpLabel(int op)
            {
                if (op < m)
                {
                    return "P+ job " + Convert.ToString(op) + " @ASRS";
                }

                if (op < 2 * m)
                {
                    return "D+ job " + Convert.ToString(op - m) + " @WS " + Convert.ToString(solution.JobWorkstation[op - m]);
                }

                if (op < 3 * m)
                {
                    return "P- job " + Convert.ToString(op - 2 * m) + " @WS " + Convert.ToString(solution.JobWorkstation[op - 2 * m]);
                }

                return "D- job " + Convert.ToString(op - 3 * m) + " @ASRS";
            }

            lines.Add("--------------------------------------------------");
            lines.Add("DECODED SCHEDULE (buffer-extended disjunctive graph)");
            lines.Add("Feasible: " + Convert.ToString(schedule.IsFeasible));
            lines.Add("Total tardiness (objective (1)): " + Round(schedule.TotalTardiness));
            lines.Add("Makespan C_max (reference): " + Round(schedule.Makespan));
            foreach (string violation in schedule.Violations)
            {
                lines.Add("VIOLATION: " + violation);
            }

            lines.Add("--------------------------------------------------");
            lines.Add("AS/RS SEQUENCE (Pi^a)");
            foreach (int op in solution.AsrsSequence)
            {
                string label = op < m ? "RETRIEVAL job " + Convert.ToString(op) : "STORAGE   job " + Convert.ToString(op - m);
                lines.Add("  " + label + " loc " + Convert.ToString(solution.AsrsLocation[op]) + " " + Timing(schedule.AsrsStart[op], schedule.AsrsCompletion[op]));
            }

            lines.Add("--------------------------------------------------");
            lines.Add("AMR SEQUENCES (Pi^r)");
            for (int h = 0; h < r; h++)
            {
                double load = 0.0;
                for (int j = m; j < 2 * m; j++)
                {
                    load -= q_i[j] * W_r_ih[j][h];
                }

                for (int j = 3 * m; j < 4 * m; j++)
                {
                    load -= q_i[j] * W_r_ih[j][h];
                }

                lines.Add("AMR " + Convert.ToString(h) + " (capacity " + Round(c_h[h]) + ", initial load " + Round(load) + ")");
                foreach (int op in solution.AmrSequences[h])
                {
                    load += q_i[op];
                    lines.Add("  " + RobotOpLabel(op) + " " + Timing(schedule.RobotStart[op], schedule.RobotCompletion[op]) + " load=" + Round(load));
                }
            }

            lines.Add("--------------------------------------------------");
            lines.Add("WORKSTATION SEQUENCES (Pi^n with buffers)");
            for (int k = 0; k < n; k++)
            {
                lines.Add("WS " + Convert.ToString(k));
                foreach (int job in solution.WorkstationSequences[k])
                {
                    string inPart = hasInSlot[job] ? "in-slot " + Convert.ToString(solution.InputSlot[job]) + " L_in=" + Round(schedule.BufferInEntry[job]) : "no in-slot (machine WIP)";
                    string outPart = hasOutSlot[job] ? "out-slot " + Convert.ToString(solution.OutputSlot[job]) + " L_out=" + Round(schedule.BufferOutEntry[job]) : "no out-slot";
                    lines.Add("  PROCESS job " + Convert.ToString(job) + " " + Timing(schedule.ProcessStart[job], schedule.ProcessCompletion[job]) + " | " + inPart + " | " + outPart);
                }
            }

            lines.Add("--------------------------------------------------");
            lines.Add("JOB SUMMARY");
            for (int i = 0; i < m; i++)
            {
                var steps = new List<string>();
                if (u_asrs_i[i] > 0.5 && asrsScheduled[i])
                {
                    steps.Add("ASRS RETRIEVAL " + Timing(schedule.AsrsStart[i], schedule.AsrsCompletion[i]) + " loc " + Convert.ToString(solution.AsrsLocation[i]));
                }

                if (u_rob_i[i] > 0.5 && solution.RobotOpAmr[i] >= 0)
                {
                    steps.Add("ROBOT P+ " + Timing(schedule.RobotStart[i], schedule.RobotCompletion[i]) + " AMR=" + Convert.ToString(solution.RobotOpAmr[i]));
                }

                if (u_rob_i[m + i] > 0.5 && solution.RobotOpAmr[m + i] >= 0)
                {
                    steps.Add("ROBOT D+ " + Timing(schedule.RobotStart[m + i], schedule.RobotCompletion[m + i]) + " AMR=" + Convert.ToString(solution.RobotOpAmr[m + i]));
                }

                if (hasInSlot[i])
                {
                    steps.Add("IN-BUFFER slot " + Convert.ToString(solution.InputSlot[i]) + " [enter=" + Round(schedule.BufferInEntry[i]) + ", leave=" + Round(schedule.ProcessStart[i]) + "]");
                }

                if (u_ws_i[i] > 0.5 && wsScheduled[i])
                {
                    steps.Add("PROCESS " + Timing(schedule.ProcessStart[i], schedule.ProcessCompletion[i]) + " WS=" + Convert.ToString(solution.JobWorkstation[i]));
                }

                if (hasOutSlot[i])
                {
                    double exit = u_rob_i[2 * m + i] > 0.5 && solution.RobotOpAmr[2 * m + i] >= 0 ? schedule.RobotCompletion[2 * m + i] : double.NaN;
                    string exitText = double.IsNaN(exit) ? "horizon end" : Round(exit);
                    steps.Add("OUT-BUFFER slot " + Convert.ToString(solution.OutputSlot[i]) + " [enter=" + Round(schedule.BufferOutEntry[i]) + ", leave=" + exitText + "]");
                }

                if (u_rob_i[2 * m + i] > 0.5 && solution.RobotOpAmr[2 * m + i] >= 0)
                {
                    steps.Add("ROBOT P- " + Timing(schedule.RobotStart[2 * m + i], schedule.RobotCompletion[2 * m + i]) + " AMR=" + Convert.ToString(solution.RobotOpAmr[2 * m + i]));
                }

                if (u_rob_i[3 * m + i] > 0.5 && solution.RobotOpAmr[3 * m + i] >= 0)
                {
                    steps.Add("ROBOT D- " + Timing(schedule.RobotStart[3 * m + i], schedule.RobotCompletion[3 * m + i]) + " AMR=" + Convert.ToString(solution.RobotOpAmr[3 * m + i]));
                }

                if (u_asrs_i[m + i] > 0.5 && asrsScheduled[m + i])
                {
                    steps.Add("ASRS STORAGE " + Timing(schedule.AsrsStart[m + i], schedule.AsrsCompletion[m + i]) + " loc " + Convert.ToString(solution.AsrsLocation[m + i]));
                }

                string flow = steps.Count > 0 ? string.Join(" -> ", steps) : "completed before horizon";
                lines.Add("JOB " + Convert.ToString(i) + " (due " + Round(d_i[i]) + ", tardiness " + Round(schedule.Tardiness[i]) + ")");
                lines.Add("  " + flow);
            }

            lines.Add("--------------------------------------------------");
        }
    }
}

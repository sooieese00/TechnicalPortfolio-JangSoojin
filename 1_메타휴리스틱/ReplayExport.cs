using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    public static class ReplayExport
    {
        public static void Export(ScenarioInstance inst, SequenceSolution solution, DecodedSchedule schedule, string path, string tag)
        {
            int m = inst.m;
            int n = inst.n;
            int r = inst.r;
            bool AsrsActive(int op) => inst.u_asrs_i[op] > 0.5;
            bool RobActive(int op) => inst.u_rob_i[op] > 0.5;
            bool WsActive(int i) => inst.u_ws_i[i] > 0.5;
            var order = new List<(int Kind, int Op, double Start, int Tie)>();
            for (int i = 0; i < solution.AsrsSequence.Count; i++)
            {
                order.Add((0, solution.AsrsSequence[i], schedule.AsrsStart[solution.AsrsSequence[i]], i));
            }

            for (int h = 0; h < r; h++)
            {
                for (int i = 0; i < solution.AmrSequences[h].Count; i++)
                {
                    int op = solution.AmrSequences[h][i];
                    order.Add((1, op, schedule.RobotStart[op], i));
                }
            }

            for (int k = 0; k < n; k++)
            {
                for (int i = 0; i < solution.WorkstationSequences[k].Count; i++)
                {
                    int job = solution.WorkstationSequences[k][i];
                    order.Add((2, job, schedule.ProcessStart[job], i));
                }
            }

            var ordered = order.OrderBy(x => x.Start).ThenBy(x => x.Tie).ToList();
            bool[] asrsCommitted = new bool[2 * m];
            bool[] amrCommitted = new bool[4 * m];
            bool[] wsCommitted = new bool[m];
            bool Ready(int kind, int op)
            {
                int job = kind == 2 ? op : op % m;
                var chain = new (int Kind, int Op, bool Active)[]
                {
                    (0, job, AsrsActive(job)),
                    (1, job, RobActive(job)),
                    (1, m + job, RobActive(m + job)),
                    (2, job, WsActive(job)),
                    (1, 2 * m + job, RobActive(2 * m + job)),
                    (1, 3 * m + job, RobActive(3 * m + job)),
                    (0, m + job, AsrsActive(m + job))
                };
                int self = -1;
                for (int c = 0; c < chain.Length; c++)
                {
                    if (chain[c].Kind == kind && chain[c].Op == op)
                    {
                        self = c;
                        break;
                    }
                }

                for (int c = self - 1; c >= 0; c--)
                {
                    if (!chain[c].Active)
                    {
                        continue;
                    }

                    return chain[c].Kind == 0 ? asrsCommitted[chain[c].Op] : chain[c].Kind == 1 ? amrCommitted[chain[c].Op] : wsCommitted[chain[c].Op];
                }

                return true;
            }

            int NodeOf(int kind, int op) => kind == 0 ? 1 + op : kind == 1 ? 1 + 2 * m + op : 1 + 6 * m + op;
            string KindName(int kind, int op) => kind switch
            {
                0 => op < m ? "ret" : "sto",
                1 => op < m ? "pplus" : op < 2 * m ? "dplus" : op < 3 * m ? "pminus" : "dminus",
                _ => "proc"
            };
            int PinnedAmr(int op)
            {
                int pinned = -1;
                int count = 0;
                for (int h = 0; h < r; h++)
                {
                    if (inst.W_r_ih[op][h] > 0.5)
                    {
                        pinned = h;
                        count++;
                    }
                }

                return count == 1 ? pinned : -1;
            }

            var pinnedQueue = new Queue<int>(inst.AsrsPinnedPrefix);
            double craneFree = 0.0;
            int craneLoad = 0;
            var amrFree = new double[r];
            var amrLoad = new int[r];
            var wsFree = new double[n];
            var wsLoad = new int[n];
            if (inst.MachineReadyAt != null)
            {
                for (int k = 0; k < n; k++)
                {
                    wsFree[k] = inst.MachineReadyAt[k];
                }
            }

            double EndOf(int kind, int op) => kind == 0 ? schedule.AsrsCompletion[op] : kind == 1 ? schedule.RobotCompletion[op] : schedule.ProcessCompletion[op];
            double ChainReadyEnd(int kind, int op)
            {
                int job = kind == 2 ? op : op % m;
                var chain = new (int Kind, int Op, bool Active)[]
                {
                    (0, job, AsrsActive(job)),
                    (1, job, RobActive(job)),
                    (1, m + job, RobActive(m + job)),
                    (2, job, WsActive(job)),
                    (1, 2 * m + job, RobActive(2 * m + job)),
                    (1, 3 * m + job, RobActive(3 * m + job)),
                    (0, m + job, AsrsActive(m + job))
                };
                int self = -1;
                for (int c = 0; c < chain.Length; c++)
                {
                    if (chain[c].Kind == kind && chain[c].Op == op)
                    {
                        self = c;
                        break;
                    }
                }

                for (int c = self - 1; c >= 0; c--)
                {
                    if (!chain[c].Active)
                    {
                        continue;
                    }

                    bool committed = chain[c].Kind == 0 ? asrsCommitted[chain[c].Op] : chain[c].Kind == 1 ? amrCommitted[chain[c].Op] : wsCommitted[chain[c].Op];
                    if (!committed)
                    {
                        return double.PositiveInfinity;
                    }

                    if (chain[c].Kind == 2)
                    {
                        double outEntry = schedule.BufferOutEntry[chain[c].Op];
                        return outEntry > 0 ? outEntry : schedule.ProcessCompletion[chain[c].Op];
                    }

                    return EndOf(chain[c].Kind, chain[c].Op);
                }

                return 0.0;
            }

            double FreeOfRes(string res) => res == "crane" ? craneFree : res.StartsWith("amr") ? amrFree[int.Parse(res.Substring(3))] : wsFree[int.Parse(res.Substring(2))];
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            writer.WriteLine(JsonSerializer.Serialize(new { t = "run", tag, m, n, r, objective = "total_tardiness", tardiness = Math.Round(schedule.TotalTardiness, 3), makespan = Math.Round(schedule.Makespan, 3), asrsOutCap = inst.AsrsOutBufferCapacity == int.MaxValue ? -1 : inst.AsrsOutBufferCapacity, asrsInCap = inst.AsrsInBufferCapacity == int.MaxValue ? -1 : inst.AsrsInBufferCapacity, b_in = inst.b_in_k, b_out = inst.b_out_k, decisions = ordered.Count, pinnedPrefix = inst.AsrsPinnedPrefix.Length }));
            var partialAsrs = new List<int>();
            var partialAmr = new List<int>[r];
            var partialWs = new List<int>[n];
            for (int h = 0; h < r; h++)
            {
                partialAmr[h] = new List<int>();
            }

            for (int k = 0; k < n; k++)
            {
                partialWs[k] = new List<int>();
            }

            for (int step = 0; step < ordered.Count; step++)
            {
                var(labelKind, labelOp, _, _) = ordered[step];
                bool labelIsPinned = labelKind == 0 && pinnedQueue.Count > 0 && pinnedQueue.Peek() == labelOp;
                var candidates = new List<object>();
                void AddCand(int kind, int op)
                {
                    if (!Ready(kind, op))
                    {
                        return;
                    }

                    string kn = KindName(kind, op);
                    int node = NodeOf(kind, op);
                    int job = kind == 2 ? op : op % m;
                    if (kind == 0)
                    {
                        if (pinnedQueue.Count > 0 && pinnedQueue.Peek() != op)
                        {
                            return;
                        }

                        candidates.Add(new { op, node, kind = kn, job, res = "crane" });
                    }
                    else if (kind == 1)
                    {
                        int pin = PinnedAmr(op);
                        for (int h = 0; h < r; h++)
                        {
                            if (pin >= 0 && h != pin)
                            {
                                continue;
                            }

                            candidates.Add(new { op, node, kind = kn, job, res = "amr" + h });
                        }
                    }
                    else
                    {
                        for (int k = 0; k < n; k++)
                        {
                            if (inst.E_ik[op][k] == 1 && inst.W_n_ik[op][k] > 0.5)
                            {
                                candidates.Add(new { op, node, kind = kn, job, res = "ws" + k });
                            }
                        }
                    }
                }

                for (int op = 0; op < 2 * m; op++)
                {
                    if (AsrsActive(op) && !asrsCommitted[op])
                    {
                        AddCand(0, op);
                    }
                }

                for (int op = 0; op < 4 * m; op++)
                {
                    if (RobActive(op) && !amrCommitted[op])
                    {
                        AddCand(1, op);
                    }
                }

                for (int job = 0; job < m; job++)
                {
                    if (WsActive(job) && !wsCommitted[job])
                    {
                        AddCand(2, job);
                    }
                }

                string labelRes = labelKind == 0 ? "crane" : labelKind == 1 ? "amr" + solution.RobotOpAmr[labelOp] : "ws" + solution.JobWorkstation[labelOp];
                var partial = new SequenceSolution
                {
                    AsrsSequence = new List<int>(partialAsrs),
                    AsrsLocation = (int[])solution.AsrsLocation.Clone(),
                    RobotOpAmr = new int[4 * m],
                    JobWorkstation = (int[])solution.JobWorkstation.Clone(),
                    InputSlot = new int[m],
                    OutputSlot = new int[m],
                    AmrSequences = new List<int>[r],
                    WorkstationSequences = new List<int>[n],
                    InputSlotSequences = new List<int>[n][],
                    OutputSlotSequences = new List<int>[n][]
                };
                for (int op = 0; op < 4 * m; op++)
                {
                    partial.RobotOpAmr[op] = amrCommitted[op] ? solution.RobotOpAmr[op] : -1;
                }

                for (int h = 0; h < r; h++)
                {
                    partial.AmrSequences[h] = new List<int>(partialAmr[h]);
                }

                bool InInBuffer(int job)
                {
                    if (amrCommitted[m + job])
                    {
                        return true;
                    }

                    for (int k2 = 0; k2 < n; k2++)
                    {
                        for (int b = 0; b < inst.b_in_k[k2]; b++)
                        {
                            if (inst.W_in_ikb[job][k2][b] > 0.5)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }

                bool InOutBuffer(int job)
                {
                    if (wsCommitted[job])
                    {
                        return true;
                    }

                    for (int k2 = 0; k2 < n; k2++)
                    {
                        for (int b = 0; b < inst.b_out_k[k2]; b++)
                        {
                            if (inst.W_out_ikb[job][k2][b] > 0.5)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }

                for (int k = 0; k < n; k++)
                {
                    partial.WorkstationSequences[k] = new List<int>(partialWs[k]);
                    partial.InputSlotSequences[k] = new List<int>[inst.b_in_k[k]];
                    partial.OutputSlotSequences[k] = new List<int>[inst.b_out_k[k]];
                    for (int b = 0; b < inst.b_in_k[k]; b++)
                    {
                        partial.InputSlotSequences[k][b] = solution.InputSlotSequences[k][b].Where(InInBuffer).ToList();
                    }

                    for (int b = 0; b < inst.b_out_k[k]; b++)
                    {
                        partial.OutputSlotSequences[k][b] = solution.OutputSlotSequences[k][b].Where(InOutBuffer).ToList();
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    partial.InputSlot[i] = InInBuffer(i) ? solution.InputSlot[i] : -1;
                    partial.OutputSlot[i] = InOutBuffer(i) ? solution.OutputSlot[i] : -1;
                }

                DecodedSchedule? partialTimes = SolutionDecoder.Decode(partial, inst.numL, r, n, m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: true, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length);
                var nodes = new List<object>();
                void AddNode(int kind, int op, bool committed)
                {
                    int job = kind == 2 ? op : op % m;
                    double? start = null;
                    double? done = null;
                    if (committed && partialTimes != null)
                    {
                        if (kind == 0)
                        {
                            start = Math.Round(partialTimes.AsrsStart[op], 3);
                            done = Math.Round(partialTimes.AsrsCompletion[op], 3);
                        }
                        else if (kind == 1)
                        {
                            start = Math.Round(partialTimes.RobotStart[op], 3);
                            done = Math.Round(partialTimes.RobotCompletion[op], 3);
                        }
                        else
                        {
                            start = Math.Round(partialTimes.ProcessStart[op], 3);
                            done = Math.Round(partialTimes.ProcessCompletion[op], 3);
                        }
                    }

                    nodes.Add(new { id = NodeOf(kind, op), kind = KindName(kind, op), job, due = Math.Round(inst.d_i[job], 1), committed, start, done, e = committed ? (double? )Math.Round(EndOf(kind, op), 3) : null, dur = Math.Round(NodeDur(kind, op), 3) });
                }

                for (int op = 0; op < 2 * m; op++)
                {
                    if (AsrsActive(op))
                    {
                        AddNode(0, op, asrsCommitted[op]);
                    }
                }

                for (int op = 0; op < 4 * m; op++)
                {
                    if (RobActive(op))
                    {
                        AddNode(1, op, amrCommitted[op]);
                    }
                }

                for (int job = 0; job < m; job++)
                {
                    if (WsActive(job))
                    {
                        AddNode(2, job, wsCommitted[job]);
                    }
                }

                var edges = new List<object>();
                void Edge(int u, int v, double w, string type)
                {
                    edges.Add(new { u, v, w = Math.Round(w, 3), type });
                }

                int PhysNode(int op)
                {
                    if (op < m)
                    {
                        return inst.Layout.AsrsOut;
                    }

                    if (op < 2 * m)
                    {
                        return inst.Layout.In(Math.Max(solution.JobWorkstation[op - m], 0));
                    }

                    if (op < 3 * m)
                    {
                        return inst.Layout.Out(Math.Max(solution.JobWorkstation[op - 2 * m], 0));
                    }

                    return inst.Layout.AsrsIn;
                }

                double NodeDur(int kind, int op)
                {
                    if (kind == 0)
                    {
                        int loc = Math.Max(solution.AsrsLocation[op], 0);
                        return inst.r_ijfg[2 * m][op][inst.numL][loc];
                    }

                    if (kind == 2)
                    {
                        int k = Math.Max(solution.JobWorkstation[op], 0);
                        return inst.s_in_ik[op][k] + inst.p_ik[op][k] + inst.s_out_ik[op][k];
                    }

                    int h = Math.Max(solution.RobotOpAmr[op], 0);
                    int node = PhysNode(op);
                    double svc = inst.s_ikh[op][node][h];
                    if (op < m)
                    {
                        return svc + inst.t_klh[node][PhysNode(m + op)][h];
                    }

                    if (op >= 2 * m && op < 3 * m)
                    {
                        return svc + inst.t_klh[node][inst.Layout.AsrsIn][h];
                    }

                    return svc;
                }

                {
                    int prevOp = -1;
                    int prevLoc = inst.numL;
                    var rets = new List<int>();
                    var stos = new List<int>();
                    foreach (int op in partialAsrs)
                    {
                        int loc = Math.Max(solution.AsrsLocation[op], 0);
                        if (prevOp >= 0)
                        {
                            Edge(NodeOf(0, prevOp), NodeOf(0, op), inst.r_ijfg[prevOp][op][prevLoc][loc], "crane_seq");
                        }

                        if (op < m)
                        {
                            if (inst.AsrsOutBufferCapacity != int.MaxValue && rets.Count >= inst.AsrsOutBufferCapacity)
                            {
                                int gate = rets[rets.Count - inst.AsrsOutBufferCapacity];
                                if (amrCommitted[gate])
                                {
                                    Edge(NodeOf(1, gate), NodeOf(0, op), 0, "buf_asrs_out");
                                }
                            }

                            rets.Add(op);
                        }
                        else
                        {
                            if (inst.AsrsInBufferCapacity != int.MaxValue && stos.Count >= inst.AsrsInBufferCapacity)
                            {
                                int gate = stos[stos.Count - inst.AsrsInBufferCapacity];
                                int dMinus = 3 * m + (op - m);
                                if (amrCommitted[dMinus])
                                {
                                    Edge(NodeOf(0, gate), NodeOf(1, dMinus), 0, "buf_asrs_in");
                                }
                            }

                            stos.Add(op);
                        }

                        prevOp = op;
                        prevLoc = loc;
                    }
                }

                for (int h = 0; h < r; h++)
                {
                    int prev = -1;
                    foreach (int op in partialAmr[h])
                    {
                        if (prev >= 0)
                        {
                            double w = inst.s_ikh[prev][PhysNode(prev)][h] + inst.t_klh[PhysNode(prev)][PhysNode(op)][h];
                            Edge(NodeOf(1, prev), NodeOf(1, op), w, "amr_route");
                        }

                        prev = op;
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    var seq = partialWs[k];
                    for (int q = 0; q < seq.Count; q++)
                    {
                        int job = seq[q];
                        if (q > 0)
                        {
                            int prevJob = seq[q - 1];
                            Edge(NodeOf(2, prevJob), NodeOf(2, job), inst.s_in_ik[prevJob][k] + inst.p_ik[prevJob][k] + inst.s_out_ik[prevJob][k], "machine_seq");
                        }

                        int bIn = inst.b_in_k[k];
                        int bOut = inst.b_out_k[k];
                        if (q >= bIn && RobActive(m + job) && amrCommitted[m + job])
                        {
                            Edge(NodeOf(2, seq[q - bIn]), NodeOf(1, m + job), 0, "buf_ws_in");
                        }

                        if (q >= bOut && RobActive(2 * m + job) && amrCommitted[2 * m + job] && RobActive(2 * m + seq[q - bOut]) && amrCommitted[2 * m + seq[q - bOut]])
                        {
                            Edge(NodeOf(1, 2 * m + seq[q - bOut]), NodeOf(1, 2 * m + job), 0, "buf_ws_out");
                        }

                        if (q >= bOut + 1 && RobActive(2 * m + seq[q - 1 - bOut]) && amrCommitted[2 * m + seq[q - 1 - bOut]])
                        {
                            Edge(NodeOf(1, 2 * m + seq[q - 1 - bOut]), NodeOf(2, job), inst.p_ik[job][k], "buf_ws_block");
                        }
                    }
                }

                for (int job = 0; job < m; job++)
                {
                    var chain = new List<(int Kind, int Op)>();
                    if (AsrsActive(job))
                    {
                        chain.Add((0, job));
                    }

                    if (RobActive(job))
                    {
                        chain.Add((1, job));
                    }

                    if (RobActive(m + job))
                    {
                        chain.Add((1, m + job));
                    }

                    if (WsActive(job))
                    {
                        chain.Add((2, job));
                    }

                    if (RobActive(2 * m + job))
                    {
                        chain.Add((1, 2 * m + job));
                    }

                    if (RobActive(3 * m + job))
                    {
                        chain.Add((1, 3 * m + job));
                    }

                    if (AsrsActive(m + job))
                    {
                        chain.Add((0, m + job));
                    }

                    for (int c = 0; c + 1 < chain.Count; c++)
                    {
                        bool a = chain[c].Kind == 0 ? asrsCommitted[chain[c].Op] : chain[c].Kind == 1 ? amrCommitted[chain[c].Op] : wsCommitted[chain[c].Op];
                        bool b = chain[c + 1].Kind == 0 ? asrsCommitted[chain[c + 1].Op] : chain[c + 1].Kind == 1 ? amrCommitted[chain[c + 1].Op] : wsCommitted[chain[c + 1].Op];
                        Edge(NodeOf(chain[c].Kind, chain[c].Op), NodeOf(chain[c + 1].Kind, chain[c + 1].Op), 0, (!a || !b) ? "conj_open" : "conj");
                    }
                }

                var undecided = new List<object>();
                foreach (var group in candidates.Select(c => (dynamic)c).GroupBy(c => (string)c.res).Where(g => g.Count() > 1))
                {
                    var members = group.Select(c => (int)c.node).Distinct().ToArray();
                    for (int a = 0; a < members.Length; a++)
                    {
                        for (int b = a + 1; b < members.Length; b++)
                        {
                            undecided.Add(new { u = members[a], v = members[b], res = group.Key });
                        }
                    }
                }

                double now = double.PositiveInfinity;
                foreach (var cd in candidates.Select(x => (dynamic)x))
                {
                    int cKind = (string)cd.kind == "proc" ? 2 : ((string)cd.kind == "ret" || (string)cd.kind == "sto") ? 0 : 1;
                    double execAt = Math.Max(ChainReadyEnd(cKind, (int)cd.op), FreeOfRes((string)cd.res));
                    now = Math.Min(now, execAt);
                }

                if (double.IsPositiveInfinity(now))
                {
                    now = 0.0;
                }

                var resState = new Dictionary<string, object>
                {
                    ["crane"] = new
                    {
                        free = Math.Round(craneFree, 3),
                        load = craneLoad
                    }
                };
                for (int h = 0; h < r; h++)
                {
                    resState["amr" + h] = new
                    {
                        free = Math.Round(amrFree[h], 3),
                        load = amrLoad[h]
                    };
                }

                for (int k = 0; k < n; k++)
                {
                    resState["ws" + k] = new
                    {
                        free = Math.Round(wsFree[k], 3),
                        load = wsLoad[k]
                    };
                }

                writer.WriteLine(JsonSerializer.Serialize(new { t = "decision", step, pinned = labelIsPinned, now = Math.Round(now, 3), res_state = resState, label = new { op = labelOp, kind = KindName(labelKind, labelOp), node = NodeOf(labelKind, labelOp), res = labelRes }, candidates, nodes, edges, undecided }));
                if (labelKind == 0)
                {
                    asrsCommitted[labelOp] = true;
                    partialAsrs.Add(labelOp);
                    craneFree = schedule.AsrsCompletion[labelOp];
                    craneLoad++;
                    if (labelIsPinned)
                    {
                        pinnedQueue.Dequeue();
                    }
                }
                else if (labelKind == 1)
                {
                    amrCommitted[labelOp] = true;
                    int amrOfOp = solution.RobotOpAmr[labelOp];
                    partialAmr[amrOfOp].Add(labelOp);
                    amrFree[amrOfOp] = schedule.RobotCompletion[labelOp];
                    amrLoad[amrOfOp]++;
                }
                else
                {
                    wsCommitted[labelOp] = true;
                    int wsOfJob = solution.JobWorkstation[labelOp];
                    partialWs[wsOfJob].Add(labelOp);
                    double outEntry = schedule.BufferOutEntry[labelOp];
                    wsFree[wsOfJob] = outEntry > 0 ? outEntry : schedule.ProcessCompletion[labelOp];
                    wsLoad[wsOfJob]++;
                }
            }

            bool sequencesMatch = partialAsrs.SequenceEqual(solution.AsrsSequence);
            for (int h = 0; h < r && sequencesMatch; h++)
            {
                sequencesMatch = partialAmr[h].SequenceEqual(solution.AmrSequences[h]);
            }

            for (int k = 0; k < n && sequencesMatch; k++)
            {
                sequencesMatch = partialWs[k].SequenceEqual(solution.WorkstationSequences[k]);
            }

            var reconstructed = solution.Clone();
            reconstructed.AsrsSequence = new List<int>(partialAsrs);
            for (int h = 0; h < r; h++)
            {
                reconstructed.AmrSequences[h] = new List<int>(partialAmr[h]);
            }

            for (int k = 0; k < n; k++)
            {
                reconstructed.WorkstationSequences[k] = new List<int>(partialWs[k]);
            }

            DecodedSchedule reconDecoded = SolutionDecoder.Decode(reconstructed, inst.numL, r, n, m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: false, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length);
            double reconTardiness = reconDecoded.TotalTardiness;
            double reconMakespan = reconDecoded.Makespan;
            bool tardinessMatch = Math.Abs(reconTardiness - schedule.TotalTardiness) <= 1e-4;
            writer.WriteLine(JsonSerializer.Serialize(new { t = "verify", sequencesMatch, tardinessMatch, reconTardiness = double.IsNaN(reconTardiness) ? (double? )null : Math.Round(reconTardiness, 4), sourceTardiness = Math.Round(schedule.TotalTardiness, 4), reconMakespan = double.IsNaN(reconMakespan) ? (double? )null : Math.Round(reconMakespan, 4) }));
            Console.WriteLine(string.Format("REPLAY VERIFY [{0}]: sequences {1}, tardiness {2} (recon {3} vs source {4})", tag, sequencesMatch ? "OK" : "MISMATCH", tardinessMatch ? "OK" : "MISMATCH", double.IsNaN(reconTardiness) ? "n/a" : reconTardiness.ToString("F4"), schedule.TotalTardiness.ToString("F4")));
        }
    }
}

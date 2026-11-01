using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    public sealed class RuleLog : IDisposable
    {
        readonly StreamWriter writer;
        public string Path { get; }

        public RuleLog(string path)
        {
            Path = path;
            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            writer = new StreamWriter(path, false, new UTF8Encoding(false));
        }

        public void Write(object record)
        {
            writer.WriteLine(JsonSerializer.Serialize(record));
        }

        public void Dispose()
        {
            writer.Flush();
            writer.Dispose();
        }
    }

    public static partial class BufferMetaheuristics
    {
        const double Eps = 1e-6;
        const double ScoreNewGlobalBest = 33.0;
        const double ScoreImprovedCurrent = 9.0;
        const double ScoreAcceptedWorse = 13.0;
        const int SegmentLength = 25;
        const double ReactionFactor = 0.3;
        const double WorstDrawExponent = 3.0;
        const double RelatedDrawExponent = 6.0;
        const double RelTravelWeight = 5.0;
        const double RelWorkstationWeight = 3.0;
        const double RelStartTimeWeight = 2.0;
        const int RepairCandidates = 8;
        static readonly bool IgDueDestroy = Environment.GetEnvironmentVariable("PDP_IG_DD") == "1";
        static readonly int Window = int.TryParse(Environment.GetEnvironmentVariable("PDP_REPAIR_WINDOW"), out int w) ? w : 4;
        public static VnsResult Run(string algorithm, SequenceSolution initial, int seed, int maxEvaluations, ScenarioInstance inst, RuleLog? log, int noImprovementLimit = 0, int coolingBudget = 0)
        {
            var ctx = new Context(inst, seed, maxEvaluations, log, coolingBudget);
            ctx.NoImprovementLimit = noImprovementLimit;
            var result = new VnsResult();
            ctx.Result = result;
            DecodedSchedule? initialSchedule = ctx.Evaluate(initial);
            if (initialSchedule == null)
            {
                result.Log.Add(algorithm.ToUpperInvariant() + ": the initial solution is infeasible; search aborted.");
                return result;
            }

            log?.Write(new { t = "run", algo = algorithm, seed, maxEvals = maxEvaluations, m = inst.m, n = inst.n, r = inst.r, numL = inst.numL, asrsOutCap = inst.AsrsOutBufferCapacity == int.MaxValue ? -1 : inst.AsrsOutBufferCapacity, asrsInCap = inst.AsrsInBufferCapacity == int.MaxValue ? -1 : inst.AsrsInBufferCapacity, destroyable = ctx.Destroyable.Count, initTardiness = Math.Round(initialSchedule.TotalTardiness, 3), initMakespan = Math.Round(initialSchedule.Makespan, 3) });
            switch (algorithm)
            {
                case "alns":
                    RunAlns(ctx, initial, initialSchedule);
                    break;
                case "ig":
                    RunIg(ctx, initial, initialSchedule);
                    break;
                case "tabu":
                    RunTabu(ctx, initial, initialSchedule);
                    break;
                case "ablation":
                    RunRepairAblation(ctx, initial);
                    break;
                default:
                    throw new ArgumentException("Unknown metaheuristic '" + algorithm + "'.");
            }

            log?.Write(new { t = "summary", algo = algorithm, best = Math.Round(result.BestObjective, 3), bestMakespan = Math.Round(result.BestMakespan, 3), evals = result.Evaluations, iterations = ctx.Iterations, accepted = ctx.AcceptedMoves, newBests = result.AcceptedImprovements });
            return result;
        }

        sealed partial class Context
        {
            public readonly ScenarioInstance I;
            public readonly Random Rng;
            public readonly int MaxEvals;
            public readonly int CoolingBudget;
            public readonly RuleLog? Log;
            public readonly List<int> Destroyable;
            public readonly int PinnedLen;
            public VnsResult Result = new VnsResult();
            public int Iterations;
            public bool LexicographicRepair;
            public int AcceptedMoves;
            public int NoImprovementLimit;
            public int SinceImprovement;
            readonly double maxHandling;
            SequenceSolution? cachedWorstFor;
            double cachedWorstObjective;
            List<(int Job, double Gain)>? cachedWorstRanking;
            public Context(ScenarioInstance inst, int seed, int maxEvals, RuleLog? log, int coolingBudget = 0)
            {
                I = inst;
                Rng = new Random(seed);
                MaxEvals = maxEvals;
                CoolingBudget = coolingBudget > 0 ? coolingBudget : Math.Min(maxEvals, 200000);
                Log = log;
                PinnedLen = inst.AsrsPinnedPrefix.Length;
                var pinnedJobs = new HashSet<int>(inst.AsrsPinnedPrefix.Select(op => op < inst.m ? op : op - inst.m));
                Destroyable = new List<int>();
                for (int j = 0; j < inst.m; j++)
                {
                    bool fullChain = inst.u_asrs_i[j] > 0.5 && inst.u_rob_i[j] > 0.5 && inst.u_rob_i[inst.m + j] > 0.5 && inst.u_ws_i[j] > 0.5 && inst.u_rob_i[2 * inst.m + j] > 0.5 && inst.u_rob_i[3 * inst.m + j] > 0.5 && inst.u_asrs_i[inst.m + j] > 0.5;
                    if (fullChain && !pinnedJobs.Contains(j))
                    {
                        Destroyable.Add(j);
                    }
                }

                double mh = 0.0;
                for (int a = 0; a < 2 * inst.m; a++)
                {
                    for (int f = 0; f < inst.numL; f++)
                    {
                        mh = Math.Max(mh, inst.r_ijfg[a][a][f][f]);
                    }
                }

                maxHandling = Math.Max(mh, 1.0);
            }

            public string LastEvalFailure = "";
            public DecodedSchedule? Evaluate(SequenceSolution solution, bool relaxed = false)
            {
                Result.Evaluations++;
                bool rebuilt = FixedSequenceSimulator.RebuildBufferSlots(solution, I.numL, I.r, I.n, I.m, I.p_ik, I.s_ikh, I.t_klh, I.R_if, I.u_rob_i, I.u_ws_i, I.u_asrs_i, I.r_ijfg, I.s_in_ik, I.s_out_ik, I.b_in_k, I.b_out_k, I.W_in_ikb, I.W_out_ikb, I.MachineReadyAt, I.Layout, I.AsrsOutBufferCapacity, I.AsrsInBufferCapacity, PinnedLen);
                if (!rebuilt)
                {
                    LastEvalFailure = "버퍼 슬롯 재유도(DES) 실패" + (FixedSequenceSimulator.LastFailure.Length > 0 ? ": " + FixedSequenceSimulator.LastFailure : "");
                    return null;
                }

                DecodedSchedule schedule = SolutionDecoder.Decode(solution, I.numL, I.r, I.n, I.m, I.c_h, I.p_ik, I.q_i, I.s_ikh, I.t_klh, I.E_ik, I.R_if, I.d_i, I.u_rob_i, I.u_ws_i, I.u_asrs_i, I.W_n_ik, I.W_r_ih, I.W_a_if, I.r_ijfg, I.s_in_ik, I.s_out_ik, I.b_in_k, I.b_out_k, I.W_in_ikb, I.W_out_ikb, I.MachineReadyAt, I.Layout, I.AsrsOutBufferCapacity, I.AsrsInBufferCapacity, relaxed, PinnedLen, I.AsrsPinnedPrefix);
                if (!schedule.IsFeasible)
                {
                    LastEvalFailure = "디코더 기각" + (schedule.Violations.Count > 0 ? ": " + schedule.Violations[0] : "");
                    return null;
                }

                LastEvalFailure = "";
                return schedule;
            }

            public bool BudgetLeft => Result.Evaluations < MaxEvals && (NoImprovementLimit <= 0 || SinceImprovement < NoImprovementLimit);

            public object StateFeatures(SequenceSolution sol, DecodedSchedule sch)
            {
                int m = I.m;
                int late = 0;
                double maxIn = 0, sumIn = 0, maxRet = 0;
                int nIn = 0;
                for (int j = 0; j < m; j++)
                {
                    if (sch.Tardiness[j] > Eps)
                    {
                        late++;
                    }

                    if (I.u_ws_i[j] > 0.5 && I.u_rob_i[m + j] > 0.5)
                    {
                        double w = sch.ProcessStart[j] - sch.BufferInEntry[j];
                        if (w > 0 && w < 1e30)
                        {
                            maxIn = Math.Max(maxIn, w);
                            sumIn += w;
                            nIn++;
                        }
                    }

                    if (I.u_asrs_i[j] > 0.5 && sol.RobotOpAmr[j] >= 0)
                    {
                        double w = sch.RobotCompletion[j] - sch.AsrsCompletion[j];
                        if (w > 0 && w < 1e30)
                        {
                            maxRet = Math.Max(maxRet, w);
                        }
                    }
                }

                var wsBusy = new double[I.n];
                for (int k = 0; k < I.n; k++)
                {
                    foreach (int j in sol.WorkstationSequences[k])
                    {
                        wsBusy[k] += sch.ProcessCompletion[j] - sch.ProcessStart[j];
                    }

                    wsBusy[k] = Math.Round(wsBusy[k], 1);
                }

                return new
                {
                    tard = Math.Round(sch.TotalTardiness, 2),
                    mk = Math.Round(sch.Makespan, 2),
                    late,
                    maxInWait = Math.Round(maxIn, 1),
                    meanInWait = Math.Round(nIn > 0 ? sumIn / nIn : 0.0, 1),
                    maxRetDwell = Math.Round(maxRet, 1),
                    wsBusy
                };
            }

            public object JobFeatures(SequenceSolution sol, DecodedSchedule sch, int j)
            {
                int m = I.m;
                int ws = sol.JobWorkstation[j];
                int wsPos = ws >= 0 ? sol.WorkstationSequences[ws].IndexOf(j) : -1;
                double inWait = sch.ProcessStart[j] - sch.BufferInEntry[j];
                double retDwell = sol.RobotOpAmr[j] >= 0 ? sch.RobotCompletion[j] - sch.AsrsCompletion[j] : 0.0;
                double minP = double.MaxValue;
                for (int k = 0; k < I.n; k++)
                {
                    if (I.E_ik[j][k] == 1)
                    {
                        minP = Math.Min(minP, I.p_ik[j][k]);
                    }
                }

                return new
                {
                    j,
                    mat = j < I.JobMaterial.Length ? I.JobMaterial[j] : "",
                    due = Math.Round(I.d_i[j], 1),
                    tard = Math.Round(sch.Tardiness[j], 1),
                    slack = Math.Round(I.d_i[j] - sch.AsrsCompletion[m + j], 1),
                    ws,
                    wsPos,
                    retPos = sol.AsrsSequence.IndexOf(j),
                    stoPos = sol.AsrsSequence.IndexOf(m + j),
                    retCell = sol.AsrsLocation[j],
                    stoCell = sol.AsrsLocation[m + j],
                    procMin = Math.Round(minP == double.MaxValue ? 0 : minP, 1),
                    inWait = Math.Round(inWait > 0 && inWait < 1e30 ? inWait : 0.0, 1),
                    retDwell = Math.Round(retDwell > 0 ? retDwell : 0.0, 1)
                };
            }

            public void RemoveJob(SequenceSolution sol, int j)
            {
                int m = I.m;
                int ws = sol.JobWorkstation[j];
                if (ws >= 0)
                {
                    sol.WorkstationSequences[ws].Remove(j);
                }

                foreach (int op in new[]
                {
                    j,
                    m + j,
                    2 * m + j,
                    3 * m + j
                }

                )
                {
                    int h = sol.RobotOpAmr[op];
                    if (h >= 0)
                    {
                        sol.AmrSequences[h].Remove(op);
                        sol.RobotOpAmr[op] = -1;
                    }
                }

                sol.AsrsSequence.Remove(j);
                sol.AsrsSequence.Remove(m + j);
            }

            public bool InsertJobAnchored(SequenceSolution sol, DecodedSchedule sch, int j, int k, int wsPos, int h1, int h2)
            {
                int m = I.m;
                var wseq = sol.WorkstationSequences[k];
                if (wsPos < 0 || wsPos > wseq.Count)
                {
                    return false;
                }

                double anchor = wsPos > 0 ? sch.ProcessCompletion[wseq[wsPos - 1]] : 0.0;
                double anchorEnd = anchor + I.s_in_ik[j][k] + I.p_ik[j][k] + I.s_out_ik[j][k];
                sol.JobWorkstation[j] = k;
                wseq.Insert(wsPos, j);
                var aseq = sol.AsrsSequence;
                int retPos = PositionByTime(aseq, op => sch.AsrsCompletion[op], anchor, PinnedLen);
                aseq.Insert(retPos, j);
                int stoPos = PositionByTime(aseq, op => sch.AsrsCompletion[op], anchorEnd, retPos + 1);
                aseq.Insert(stoPos, m + j);
                var used = new bool[I.numL];
                foreach (int op in aseq)
                {
                    if (op >= m && op != m + j && sol.AsrsLocation[op] >= 0)
                    {
                        used[sol.AsrsLocation[op]] = true;
                    }
                }

                int cell = sol.AsrsLocation[m + j];
                if (cell < 0 || used[cell] || I.W_a_if[m + j][cell] < 0.5)
                {
                    cell = -1;
                    for (int g = 0; g < I.numL; g++)
                    {
                        if (!used[g] && I.W_a_if[m + j][g] > 0.5)
                        {
                            cell = g;
                            break;
                        }
                    }

                    if (cell < 0)
                    {
                        return false;
                    }
                }

                sol.AsrsLocation[m + j] = cell;
                var r1 = sol.AmrSequences[h1];
                int p1 = PositionByTime(r1, op => sch.RobotCompletion[op], anchor, 0);
                r1.Insert(p1, j);
                r1.Insert(p1 + 1, m + j);
                sol.RobotOpAmr[j] = h1;
                sol.RobotOpAmr[m + j] = h1;
                var r2 = sol.AmrSequences[h2];
                int floor2 = h2 == h1 ? p1 + 2 : 0;
                int p3 = Math.Max(PositionByTime(r2, op => sch.RobotCompletion[op], anchorEnd, 0), floor2);
                r2.Insert(p3, 2 * m + j);
                r2.Insert(p3 + 1, 3 * m + j);
                sol.RobotOpAmr[2 * m + j] = h2;
                sol.RobotOpAmr[3 * m + j] = h2;
                return true;
            }

            static int PositionByTime(List<int> seq, Func<int, double> doneAt, double time, int floor)
            {
                int pos = floor;
                for (int i = floor; i < seq.Count; i++)
                {
                    if (doneAt(seq[i]) <= time)
                    {
                        pos = i + 1;
                    }
                    else
                    {
                        break;
                    }
                }

                return pos;
            }

            public bool InsertJobUniform(SequenceSolution sol, int j)
            {
                int m = I.m;
                var wsOptions = new List<int>();
                for (int k = 0; k < I.n; k++)
                {
                    if (I.E_ik[j][k] == 1 && I.W_n_ik[j][k] > 0.5)
                    {
                        wsOptions.Add(k);
                    }
                }

                if (wsOptions.Count == 0)
                {
                    return false;
                }

                int wsPick = wsOptions[Rng.Next(wsOptions.Count)];
                sol.JobWorkstation[j] = wsPick;
                var wseq = sol.WorkstationSequences[wsPick];
                wseq.Insert(Rng.Next(wseq.Count + 1), j);
                var aseq = sol.AsrsSequence;
                int retPos = PinnedLen + Rng.Next(aseq.Count - PinnedLen + 1);
                aseq.Insert(retPos, j);
                int stoPos = retPos + 1 + Rng.Next(aseq.Count - retPos);
                aseq.Insert(stoPos, m + j);
                var used = new bool[I.numL];
                foreach (int op in aseq)
                {
                    if (op >= m && op != m + j && sol.AsrsLocation[op] >= 0)
                    {
                        used[sol.AsrsLocation[op]] = true;
                    }
                }

                int cell = sol.AsrsLocation[m + j];
                if (cell < 0 || used[cell] || I.W_a_if[m + j][cell] < 0.5)
                {
                    var cells = new List<int>();
                    for (int g = 0; g < I.numL; g++)
                    {
                        if (!used[g] && I.W_a_if[m + j][g] > 0.5)
                        {
                            cells.Add(g);
                        }
                    }

                    if (cells.Count == 0)
                    {
                        return false;
                    }

                    cell = cells[Rng.Next(cells.Count)];
                }

                sol.AsrsLocation[m + j] = cell;
                int h1 = Rng.Next(I.r);
                var r1 = sol.AmrSequences[h1];
                int p1 = Rng.Next(r1.Count + 1);
                r1.Insert(p1, j);
                int p2 = p1 + 1 + Rng.Next(r1.Count - p1);
                r1.Insert(p2, m + j);
                sol.RobotOpAmr[j] = h1;
                sol.RobotOpAmr[m + j] = h1;
                int h2 = Rng.Next(I.r);
                var r2 = sol.AmrSequences[h2];
                int p3 = Rng.Next(r2.Count + 1);
                int p4 = p3 + 1 + Rng.Next(r2.Count - p3);
                r2.Insert(p3, 2 * m + j);
                r2.Insert(p4, 3 * m + j);
                sol.RobotOpAmr[2 * m + j] = h2;
                sol.RobotOpAmr[3 * m + j] = h2;
                return true;
            }

            public bool InsertJobAligned(SequenceSolution sol, int j, double u)
            {
                int m = I.m;
                int Jit(int pos, int max)
                {
                    int p = pos + Rng.Next(-1, 3);
                    return Math.Clamp(p, 0, max);
                }

                int Frac(int count, double f)
                {
                    return (int)Math.Round(f * count);
                }

                var wsOptions = new List<int>();
                for (int k = 0; k < I.n; k++)
                {
                    if (I.E_ik[j][k] == 1 && I.W_n_ik[j][k] > 0.5)
                    {
                        wsOptions.Add(k);
                    }
                }

                if (wsOptions.Count == 0)
                {
                    return false;
                }

                int wsPick = wsOptions[Rng.Next(wsOptions.Count)];
                sol.JobWorkstation[j] = wsPick;
                var wseq = sol.WorkstationSequences[wsPick];
                wseq.Insert(Jit(Frac(wseq.Count, u), wseq.Count), j);
                double w = u + (1.0 - u) * Rng.NextDouble();
                var aseq = sol.AsrsSequence;
                int free = aseq.Count - PinnedLen;
                int retPos = PinnedLen + Jit(Frac(free, u), free);
                aseq.Insert(retPos, j);
                int tail = aseq.Count - (retPos + 1);
                int stoPos = retPos + 1 + Jit(Frac(tail, (w - u) / Math.Max(1.0 - u, 1e-9)), tail);
                aseq.Insert(stoPos, m + j);
                var used = new bool[I.numL];
                foreach (int op in aseq)
                {
                    if (op >= m && op != m + j && sol.AsrsLocation[op] >= 0)
                    {
                        used[sol.AsrsLocation[op]] = true;
                    }
                }

                int cell = sol.AsrsLocation[m + j];
                if (cell < 0 || used[cell] || I.W_a_if[m + j][cell] < 0.5)
                {
                    var cells = new List<int>();
                    for (int g = 0; g < I.numL; g++)
                    {
                        if (!used[g] && I.W_a_if[m + j][g] > 0.5)
                        {
                            cells.Add(g);
                        }
                    }

                    if (cells.Count == 0)
                    {
                        return false;
                    }

                    cell = cells[Rng.Next(cells.Count)];
                }

                sol.AsrsLocation[m + j] = cell;
                int h1 = Rng.Next(I.r);
                var r1 = sol.AmrSequences[h1];
                int p1 = Jit(Frac(r1.Count, u), r1.Count);
                r1.Insert(p1, j);
                r1.Insert(p1 + 1, m + j);
                sol.RobotOpAmr[j] = h1;
                sol.RobotOpAmr[m + j] = h1;
                int h2 = Rng.Next(I.r);
                var r2 = sol.AmrSequences[h2];
                int floor2 = h2 == h1 ? p1 + 2 : 0;
                int span = r2.Count - floor2;
                int p3 = floor2 + Jit(Frac(span, w), span);
                r2.Insert(p3, 2 * m + j);
                r2.Insert(p3 + 1, 3 * m + j);
                sol.RobotOpAmr[2 * m + j] = h2;
                sol.RobotOpAmr[3 * m + j] = h2;
                return true;
            }

            struct InsertChoice
            {
                public int Ws, WsPos;
                public int RetPos;
                public int H1, P1;
                public int H2, P3;
                public int StoPos;
            }

            SequenceSolution? BuildCandidate(SequenceSolution current, DecodedSchedule sch, int j, InsertChoice c)
            {
                int m = I.m;
                var sol = current.Clone();
                var wseq = sol.WorkstationSequences[c.Ws];
                if (c.WsPos < 0 || c.WsPos > wseq.Count)
                {
                    return null;
                }

                double anchor = c.WsPos > 0 ? sch.ProcessCompletion[wseq[c.WsPos - 1]] : 0.0;
                double anchorEnd = anchor + I.s_in_ik[j][c.Ws] + I.p_ik[j][c.Ws] + I.s_out_ik[j][c.Ws];
                sol.JobWorkstation[j] = c.Ws;
                wseq.Insert(c.WsPos, j);
                var aseq = sol.AsrsSequence;
                int retPos = c.RetPos >= 0 ? c.RetPos : PositionByTime(aseq, op => sch.AsrsCompletion[op], anchor, PinnedLen);
                if (retPos < PinnedLen || retPos > aseq.Count)
                {
                    return null;
                }

                aseq.Insert(retPos, j);
                var used = new bool[I.numL];
                foreach (int op in aseq)
                {
                    if (op >= m && op != m + j && sol.AsrsLocation[op] >= 0)
                    {
                        used[sol.AsrsLocation[op]] = true;
                    }
                }

                int cell = sol.AsrsLocation[m + j];
                if (cell < 0 || used[cell] || I.W_a_if[m + j][cell] < 0.5)
                {
                    cell = -1;
                    for (int g = 0; g < I.numL; g++)
                    {
                        if (!used[g] && I.W_a_if[m + j][g] > 0.5)
                        {
                            cell = g;
                            break;
                        }
                    }

                    if (cell < 0)
                    {
                        return null;
                    }
                }

                sol.AsrsLocation[m + j] = cell;
                int h1 = c.H1 >= 0 ? c.H1 : j % I.r;
                var r1 = sol.AmrSequences[h1];
                int p1 = c.P1 >= 0 ? c.P1 : PositionByTime(r1, op => sch.RobotCompletion[op], anchor, 0);
                if (p1 < 0 || p1 > r1.Count)
                {
                    return null;
                }

                r1.Insert(p1, j);
                r1.Insert(p1 + 1, m + j);
                sol.RobotOpAmr[j] = h1;
                sol.RobotOpAmr[m + j] = h1;
                int h2 = c.H2 >= 0 ? c.H2 : j % I.r;
                var r2 = sol.AmrSequences[h2];
                int floor2 = h2 == h1 ? p1 + 2 : 0;
                int p3 = c.P3 >= 0 ? c.P3 : Math.Max(PositionByTime(r2, op => sch.RobotCompletion[op], anchorEnd, 0), floor2);
                if (p3 < floor2 || p3 > r2.Count)
                {
                    return null;
                }

                r2.Insert(p3, 2 * m + j);
                r2.Insert(p3 + 1, 3 * m + j);
                sol.RobotOpAmr[2 * m + j] = h2;
                sol.RobotOpAmr[3 * m + j] = h2;
                int stoPos = c.StoPos >= 0 ? c.StoPos : PositionByTime(sol.AsrsSequence, op2 => op2 == j ? anchor : sch.AsrsCompletion[op2], anchorEnd, retPos + 1);
                if (stoPos < retPos + 1 || stoPos > sol.AsrsSequence.Count)
                {
                    return null;
                }

                sol.AsrsSequence.Insert(stoPos, m + j);
                return sol;
            }

            (SequenceSolution, DecodedSchedule)? CommitBestAxis(SequenceSolution current, DecodedSchedule sch, int j, ref InsertChoice choice, IEnumerable<InsertChoice> candidates)
            {
                SequenceSolution? best = null;
                DecodedSchedule? bestSch = null;
                InsertChoice bestChoice = choice;
                foreach (InsertChoice cand in candidates)
                {
                    if (!BudgetLeft)
                    {
                        break;
                    }

                    SequenceSolution? built = BuildCandidate(current, sch, j, cand);
                    if (built == null)
                    {
                        continue;
                    }

                    DecodedSchedule? s = Evaluate(built, relaxed: true);
                    if (s == null)
                    {
                        continue;
                    }

                    if (bestSch == null || s.TotalTardiness < bestSch.TotalTardiness - Eps || (LexicographicRepair && s.TotalTardiness <= bestSch.TotalTardiness + Eps && s.Makespan < bestSch.Makespan - Eps))
                    {
                        best = built;
                        bestSch = s;
                        bestChoice = cand;
                    }
                }

                if (best == null || bestSch == null)
                {
                    return null;
                }

                choice = bestChoice;
                return (best, bestSch);
            }

            static IEnumerable<int> WindowAround(int centre, int lo, int hi)
            {
                for (int p = Math.Max(lo, centre - Window); p <= Math.Min(hi, centre + Window); p++)
                {
                    yield return p;
                }
            }

            public (SequenceSolution sol, DecodedSchedule sch)? Repair(SequenceSolution destroyed, int[] jobs)
            {
                var order = jobs.OrderBy(j => I.d_i[j]).ToArray();
                SequenceSolution current = destroyed;
                DecodedSchedule? currentSchedule = Evaluate(current, relaxed: true);
                if (currentSchedule == null)
                {
                    return null;
                }

                foreach (int j in order)
                {
                    var choice = new InsertChoice
                    {
                        Ws = -1,
                        WsPos = -1,
                        RetPos = -1,
                        H1 = -1,
                        P1 = -1,
                        H2 = -1,
                        P3 = -1,
                        StoPos = -1
                    };
                    (SequenceSolution, DecodedSchedule)? staged = null;
                    {
                        var cands = new List<InsertChoice>();
                        for (int k = 0; k < I.n; k++)
                        {
                            if (I.E_ik[j][k] != 1 || I.W_n_ik[j][k] < 0.5)
                            {
                                continue;
                            }

                            for (int p = 0; p <= current.WorkstationSequences[k].Count; p++)
                            {
                                var c = choice;
                                c.Ws = k;
                                c.WsPos = p;
                                cands.Add(c);
                            }
                        }

                        staged = CommitBestAxis(current, currentSchedule, j, ref choice, cands);
                    }

                    double anchor = 0.0, anchorEnd = 0.0;
                    if (staged != null)
                    {
                        anchor = choice.WsPos > 0 ? currentSchedule.ProcessCompletion[current.WorkstationSequences[choice.Ws][choice.WsPos - 1]] : 0.0;
                        anchorEnd = anchor + I.s_in_ik[j][choice.Ws] + I.p_ik[j][choice.Ws] + I.s_out_ik[j][choice.Ws];
                    }

                    if (staged != null)
                    {
                        int centre = PositionByTime(current.AsrsSequence, op => currentSchedule.AsrsCompletion[op], anchor, PinnedLen);
                        var cands = new List<InsertChoice>();
                        foreach (int p in WindowAround(centre, PinnedLen, current.AsrsSequence.Count))
                        {
                            var c = choice;
                            c.RetPos = p;
                            cands.Add(c);
                        }

                        staged = CommitBestAxis(current, currentSchedule, j, ref choice, cands) ?? staged;
                    }

                    if (staged != null)
                    {
                        var cands = new List<InsertChoice>();
                        for (int h = 0; h < I.r; h++)
                        {
                            int centre = PositionByTime(current.AmrSequences[h], op => currentSchedule.RobotCompletion[op], anchor, 0);
                            foreach (int p in WindowAround(centre, 0, current.AmrSequences[h].Count))
                            {
                                var c = choice;
                                c.H1 = h;
                                c.P1 = p;
                                cands.Add(c);
                            }
                        }

                        staged = CommitBestAxis(current, currentSchedule, j, ref choice, cands) ?? staged;
                    }

                    if (staged != null)
                    {
                        var cands = new List<InsertChoice>();
                        for (int h = 0; h < I.r; h++)
                        {
                            int centre = PositionByTime(current.AmrSequences[h], op => currentSchedule.RobotCompletion[op], anchorEnd, 0);
                            foreach (int p in WindowAround(centre, 0, current.AmrSequences[h].Count + (h == choice.H1 ? 2 : 0)))
                            {
                                var c = choice;
                                c.H2 = h;
                                c.P3 = p;
                                cands.Add(c);
                            }
                        }

                        staged = CommitBestAxis(current, currentSchedule, j, ref choice, cands) ?? staged;
                    }

                    if (staged != null && choice.RetPos >= 0)
                    {
                        int centre = Math.Max(choice.RetPos + 1, PositionByTime(current.AsrsSequence, op => currentSchedule.AsrsCompletion[op], anchorEnd, 0) + 1);
                        var cands = new List<InsertChoice>();
                        foreach (int p in WindowAround(centre, choice.RetPos + 1, current.AsrsSequence.Count + 1))
                        {
                            var c = choice;
                            c.StoPos = p;
                            cands.Add(c);
                        }

                        staged = CommitBestAxis(current, currentSchedule, j, ref choice, cands) ?? staged;
                    }

                    if (staged == null)
                    {
                        SequenceSolution cand = current.Clone();
                        if (!InsertJobAligned(cand, j, 1.0))
                        {
                            return null;
                        }

                        DecodedSchedule? sch = Evaluate(cand, relaxed: true);
                        if (sch == null)
                        {
                            return null;
                        }

                        staged = (cand, sch);
                    }

                    current = staged.Value.Item1;
                    currentSchedule = staged.Value.Item2;
                }

                return currentSchedule == null ? null : (current, currentSchedule);
            }

            public int[] DestroyRandom(int d)
            {
                return Destroyable.OrderBy(_ => Rng.Next()).Take(d).ToArray();
            }

            public int[] DestroyTardy(int d, DecodedSchedule sch)
            {
                var ranked = Destroyable.OrderByDescending(j => sch.Tardiness[j]).ToList();
                return DrawBiased(ranked, d, 2.0);
            }

            public int[] DestroyWorst(int d, SequenceSolution cur, DecodedSchedule sch)
            {
                if (!ReferenceEquals(cachedWorstFor, cur) || cachedWorstRanking == null || Math.Abs(cachedWorstObjective - sch.TotalTardiness) > Eps)
                {
                    var ranked = new List<(int Job, double Gain)>();
                    foreach (int j in Destroyable)
                    {
                        if (!BudgetLeft)
                        {
                            break;
                        }

                        SequenceSolution reduced = cur.Clone();
                        RemoveJob(reduced, j);
                        DecodedSchedule? rs = Evaluate(reduced, relaxed: true);
                        double gain = rs == null ? 0.0 : sch.TotalTardiness - rs.TotalTardiness;
                        ranked.Add((j, gain));
                    }

                    ranked.Sort((a, b) => b.Gain.CompareTo(a.Gain));
                    cachedWorstFor = cur;
                    cachedWorstObjective = sch.TotalTardiness;
                    cachedWorstRanking = ranked;
                }

                var pool = cachedWorstRanking.Select(x => x.Job).ToList();
                return DrawBiased(pool, d, WorstDrawExponent);
            }

            public int[] DestroyRelated(int d, SequenceSolution cur, DecodedSchedule sch)
            {
                int seedJob = Destroyable[Rng.Next(Destroyable.Count)];
                var selected = new List<int>
                {
                    seedJob
                };
                var remaining = Destroyable.Where(j => j != seedJob).ToList();
                while (selected.Count < d && remaining.Count > 0)
                {
                    int reference = selected[Rng.Next(selected.Count)];
                    var ranked = remaining.Select(j => (Job: j, Rel: Relatedness(cur, sch, reference, j))).OrderBy(x => x.Rel).Select(x => x.Job).ToList();
                    int idx = (int)(Math.Pow(Rng.NextDouble(), RelatedDrawExponent) * ranked.Count);
                    idx = Math.Min(idx, ranked.Count - 1);
                    selected.Add(ranked[idx]);
                    remaining.Remove(ranked[idx]);
                }

                return selected.ToArray();
            }

            double Relatedness(SequenceSolution sol, DecodedSchedule sch, int a, int b)
            {
                double travel = I.r_ijfg[a][b][Math.Max(sol.AsrsLocation[a], 0)][Math.Max(sol.AsrsLocation[b], 0)];
                double travelTerm = travel / maxHandling;
                double wsTerm = sol.JobWorkstation[a] == sol.JobWorkstation[b] ? 0.0 : 1.0;
                double reference = Math.Max(sch.Makespan, 1.0);
                double startTerm = Math.Abs(sch.AsrsStart[a] - sch.AsrsStart[b]) / reference;
                return RelTravelWeight * travelTerm + RelWorkstationWeight * wsTerm + RelStartTimeWeight * startTerm;
            }

            const int KindShift = 44;
            const int ElementShift = 22;
            static long AttrKey(int kind, int element, int position)
            {
                return ((long)kind << KindShift) | ((long)element << ElementShift) | (uint)position;
            }

            public void DiffAttributes(SequenceSolution a, SequenceSolution b, List<long> created, List<long> destroyed)
            {
                created.Clear();
                destroyed.Clear();
                int m = I.m;
                DiffList(0, a.AsrsSequence, b.AsrsSequence, 0, created, destroyed);
                int amrStride = 4 * m + 1;
                for (int h = 0; h < I.r; h++)
                {
                    DiffList(1, a.AmrSequences[h], b.AmrSequences[h], h * amrStride, created, destroyed);
                }

                int wsStride = m + 1;
                for (int k = 0; k < I.n; k++)
                {
                    DiffList(2, a.WorkstationSequences[k], b.WorkstationSequences[k], k * wsStride, created, destroyed);
                }

                for (int op = m; op < 2 * m; op++)
                {
                    if (a.AsrsLocation[op] != b.AsrsLocation[op])
                    {
                        destroyed.Add(AttrKey(3, op, Math.Max(a.AsrsLocation[op], 0)));
                        created.Add(AttrKey(3, op, Math.Max(b.AsrsLocation[op], 0)));
                    }
                }
            }

            static void DiffList(int kind, List<int> before, List<int> after, int positionBase, List<long> created, List<long> destroyed)
            {
                if (before.Count != after.Count)
                {
                    for (int i = 0; i < before.Count; i++)
                    {
                        destroyed.Add(AttrKey(kind, before[i], positionBase + i));
                    }

                    for (int i = 0; i < after.Count; i++)
                    {
                        created.Add(AttrKey(kind, after[i], positionBase + i));
                    }

                    return;
                }

                for (int i = 0; i < before.Count; i++)
                {
                    if (before[i] != after[i])
                    {
                        destroyed.Add(AttrKey(kind, before[i], positionBase + i));
                        created.Add(AttrKey(kind, after[i], positionBase + i));
                    }
                }
            }

            int[] DrawBiased(List<int> ranked, int d, double exponent)
            {
                var pool = new List<int>(ranked);
                var selected = new List<int>();
                while (selected.Count < d && pool.Count > 0)
                {
                    int idx = (int)(Math.Pow(Rng.NextDouble(), exponent) * pool.Count);
                    idx = Math.Min(idx, pool.Count - 1);
                    selected.Add(pool[idx]);
                    pool.RemoveAt(idx);
                }

                return selected.ToArray();
            }

            public (bool ok, int move, int job) ApplySmallMove(SequenceSolution sol, int move)
            {
                int m = I.m;
                switch (move)
                {
                    case 1:
                    case 2:
                    {
                        var ks = Enumerable.Range(0, I.n).Where(k => sol.WorkstationSequences[k].Count >= 2).ToList();
                        if (ks.Count == 0)
                        {
                            return (false, move, -1);
                        }

                        var seq = sol.WorkstationSequences[ks[Rng.Next(ks.Count)]];
                        int a = Rng.Next(seq.Count);
                        int b = Rng.Next(seq.Count - 1);
                        if (b >= a)
                        {
                            b++;
                        }

                        int job = seq[a];
                        if (move == 1)
                        {
                            seq.RemoveAt(a);
                            seq.Insert(b, job);
                        }
                        else
                        {
                            (seq[a], seq[b]) = (seq[b], seq[a]);
                        }

                        return (true, move, job);
                    }

                    case 4:
                    {
                        var cands = new List<int>();
                        for (int i = 0; i < m; i++)
                        {
                            if (I.u_rob_i[i] > 0.5 && I.u_rob_i[m + i] > 0.5 && sol.RobotOpAmr[i] >= 0)
                            {
                                cands.Add(i * 2);
                            }

                            if (I.u_rob_i[2 * m + i] > 0.5 && I.u_rob_i[3 * m + i] > 0.5 && sol.RobotOpAmr[2 * m + i] >= 0)
                            {
                                cands.Add(i * 2 + 1);
                            }
                        }

                        if (cands.Count == 0)
                        {
                            return (false, move, -1);
                        }

                        int pick = cands[Rng.Next(cands.Count)];
                        int job = pick / 2;
                        int pOp = pick % 2 == 0 ? job : 2 * m + job;
                        int dOp = pOp + m;
                        int oldAmr = sol.RobotOpAmr[pOp];
                        sol.AmrSequences[oldAmr].Remove(pOp);
                        sol.AmrSequences[oldAmr].Remove(dOp);
                        int newAmr = Rng.Next(I.r);
                        var route = sol.AmrSequences[newAmr];
                        int pPos = Rng.Next(route.Count + 1);
                        route.Insert(pPos, pOp);
                        int dPos = pPos + 1 + Rng.Next(route.Count - pPos);
                        route.Insert(dPos, dOp);
                        sol.RobotOpAmr[pOp] = newAmr;
                        sol.RobotOpAmr[dOp] = newAmr;
                        return (true, move, job);
                    }

                    case 5:
                    {
                        var seq = sol.AsrsSequence;
                        if (seq.Count - PinnedLen < 2)
                        {
                            return (false, move, -1);
                        }

                        int a = PinnedLen + Rng.Next(seq.Count - PinnedLen);
                        int b = PinnedLen + Rng.Next(seq.Count - PinnedLen - 1);
                        if (b >= a)
                        {
                            b++;
                        }

                        int op = seq[a];
                        seq.RemoveAt(a);
                        seq.Insert(b, op);
                        return (true, move, op < m ? op : op - m);
                    }

                    case 6:
                    {
                        var storages = new List<int>();
                        for (int idx = PinnedLen; idx < sol.AsrsSequence.Count; idx++)
                        {
                            if (sol.AsrsSequence[idx] >= m)
                            {
                                storages.Add(sol.AsrsSequence[idx]);
                            }
                        }

                        if (storages.Count == 0)
                        {
                            return (false, move, -1);
                        }

                        int chosen = storages[Rng.Next(storages.Count)];
                        var used = new bool[I.numL];
                        foreach (int op in storages)
                        {
                            if (op != chosen && sol.AsrsLocation[op] >= 0)
                            {
                                used[sol.AsrsLocation[op]] = true;
                            }
                        }

                        var cells = new List<int>();
                        for (int g = 0; g < I.numL; g++)
                        {
                            if (!used[g] && g != sol.AsrsLocation[chosen] && I.W_a_if[chosen][g] > 0.5)
                            {
                                cells.Add(g);
                            }
                        }

                        if (cells.Count == 0)
                        {
                            return (false, move, -1);
                        }

                        sol.AsrsLocation[chosen] = cells[Rng.Next(cells.Count)];
                        return (true, move, chosen - m);
                    }

                    case 7:
                    {
                        var cands = new List<int>();
                        for (int i = 0; i < m; i++)
                        {
                            if (I.u_ws_i[i] > 0.5 && I.u_rob_i[m + i] > 0.5 && Enumerable.Range(0, I.n).Count(k => I.E_ik[i][k] == 1) >= 2)
                            {
                                cands.Add(i);
                            }
                        }

                        if (cands.Count == 0)
                        {
                            return (false, move, -1);
                        }

                        int job = cands[Rng.Next(cands.Count)];
                        int oldWs = sol.JobWorkstation[job];
                        var options = Enumerable.Range(0, I.n).Where(k => k != oldWs && I.E_ik[job][k] == 1).ToList();
                        if (options.Count == 0)
                        {
                            return (false, move, -1);
                        }

                        int newWs = options[Rng.Next(options.Count)];
                        sol.WorkstationSequences[oldWs].Remove(job);
                        var seq2 = sol.WorkstationSequences[newWs];
                        seq2.Insert(Rng.Next(seq2.Count + 1), job);
                        sol.JobWorkstation[job] = newWs;
                        return (true, move, job);
                    }

                    default:
                        return (false, move, -1);
                }
            }
        }

        static readonly string[] AlnsDestroyNames =
        {
            "random",
            "worst",
            "related",
            "tardy"
        };
        static void RunAlns(Context ctx, SequenceSolution initial, DecodedSchedule initialSchedule)
        {
            var result = ctx.Result;
            SequenceSolution current = initial.Clone();
            DecodedSchedule curSch = initialSchedule;
            result.BestSolution = current.Clone();
            result.BestSchedule = curSch;
            result.BestObjective = curSch.TotalTardiness;
            result.BestMakespan = curSch.Makespan;
            int dMax = Math.Max(3, ctx.Destroyable.Count * 2 / 5);
            const double FinalTempRatio = 1e-3;
            double temperature = Math.Max(0.01 * curSch.TotalTardiness, 1.0);
            int prevEvals = result.Evaluations;
            var weights = Enumerable.Repeat(1.0, AlnsDestroyNames.Length).ToArray();
            var segScores = new double[AlnsDestroyNames.Length];
            var segUsage = new int[AlnsDestroyNames.Length];
            while (ctx.BudgetLeft)
            {
                ctx.Iterations++;
                ctx.SinceImprovement++;
                int d = 2 + ctx.Rng.Next(Math.Max(1, dMax - 1));
                int opIdx = Roulette(ctx.Rng, weights);
                int[] jobs = opIdx switch
                {
                    0 => ctx.DestroyRandom(d),
                    1 => ctx.DestroyWorst(d, current, curSch),
                    2 => ctx.DestroyRelated(d, current, curSch),
                    _ => ctx.DestroyTardy(d, curSch)};
                if (jobs.Length == 0)
                {
                    break;
                }

                object before = ctx.StateFeatures(current, curSch);
                var jobFeat = jobs.Select(j => ctx.JobFeatures(current, curSch, j)).ToArray();
                SequenceSolution destroyed = current.Clone();
                foreach (int j in jobs)
                {
                    ctx.RemoveJob(destroyed, j);
                }

                var repaired = ctx.Repair(destroyed, jobs);
                double delta = double.NaN;
                bool accepted = false;
                bool newBest = false;
                if (repaired != null)
                {
                    var(candidate, candSch) = repaired.Value;
                    delta = candSch.TotalTardiness - curSch.TotalTardiness;
                    accepted = delta < -Eps || ctx.Rng.NextDouble() < Math.Clamp(Math.Exp(-delta / Math.Max(temperature, 1e-9)), 0.0, 1.0);
                    double score = 0.0;
                    if (accepted)
                    {
                        ctx.AcceptedMoves++;
                        current = candidate;
                        curSch = candSch;
                        if (candSch.TotalTardiness < result.BestObjective - Eps)
                        {
                            newBest = true;
                            ctx.SinceImprovement = 0;
                            result.BestSolution = candidate.Clone();
                            result.BestSchedule = candSch;
                            result.BestObjective = candSch.TotalTardiness;
                            result.BestMakespan = candSch.Makespan;
                            result.AcceptedImprovements++;
                            result.Log.Add("ALNS improvement (op=" + AlnsDestroyNames[opIdx] + ", eval " + result.Evaluations + "): tardiness=" + Math.Round(candSch.TotalTardiness, 4) + ", makespan=" + Math.Round(candSch.Makespan, 4));
                            score = ScoreNewGlobalBest;
                        }
                        else if (delta < -Eps)
                        {
                            score = ScoreImprovedCurrent;
                        }
                        else
                        {
                            score = ScoreAcceptedWorse;
                        }
                    }

                    segScores[opIdx] += score;
                }

                segUsage[opIdx]++;
                ctx.Log?.Write(new { t = "iter", algo = "alns", iter = ctx.Iterations, evals = result.Evaluations, op = AlnsDestroyNames[opIdx], d = jobs.Length, jobs, jobFeat, before, after = double.IsNaN(delta) ? null : (object)Math.Round(curSch.TotalTardiness, 2), delta = double.IsNaN(delta) ? null : (object)Math.Round(delta, 2), repairFailed = repaired == null, accepted, newBest, temp = Math.Round(temperature, 2), weights = weights.Select(w => Math.Round(w, 3)).ToArray() });
                temperature = Math.Max(temperature * Math.Pow(FinalTempRatio, (result.Evaluations - prevEvals) / (double)ctx.CoolingBudget), 1e-6);
                prevEvals = result.Evaluations;
                if (ctx.Iterations % SegmentLength == 0)
                {
                    for (int i = 0; i < weights.Length; i++)
                    {
                        if (segUsage[i] > 0)
                        {
                            weights[i] = (1 - ReactionFactor) * weights[i] + ReactionFactor * (segScores[i] / segUsage[i]);
                            weights[i] = Math.Max(weights[i], 0.05);
                        }

                        segScores[i] = 0;
                        segUsage[i] = 0;
                    }
                }
            }
        }

        static void RunIg(Context ctx, SequenceSolution initial, DecodedSchedule initialSchedule)
        {
            var result = ctx.Result;
            SequenceSolution current = initial.Clone();
            DecodedSchedule curSch = initialSchedule;
            result.BestSolution = current.Clone();
            result.BestSchedule = curSch;
            result.BestObjective = curSch.TotalTardiness;
            result.BestMakespan = curSch.Makespan;
            const double FinalTempRatio = 1e-3;
            double temperature = Math.Max(0.01 * curSch.TotalTardiness, 1.0);
            int prevEvals = result.Evaluations;
            const int DescentPatience = 30;
            int[] descentMoves =
            {
                1,
                2,
                5,
                6
            };
            while (ctx.BudgetLeft)
            {
                ctx.Iterations++;
                ctx.SinceImprovement++;
                int d = 3 + ctx.Rng.Next(3);
                int[] jobs = IgDueDestroy && ctx.Rng.NextDouble() < 0.5 ? ctx.DestroyTardy(d, curSch) : ctx.DestroyRandom(d);
                object before = ctx.StateFeatures(current, curSch);
                var jobFeat = jobs.Select(j => ctx.JobFeatures(current, curSch, j)).ToArray();
                SequenceSolution destroyed = current.Clone();
                foreach (int j in jobs)
                {
                    ctx.RemoveJob(destroyed, j);
                }

                var repaired = ctx.Repair(destroyed, jobs);
                double delta = double.NaN;
                bool accepted = false;
                bool newBest = false;
                int descentGainRounded = 0;
                if (repaired != null)
                {
                    var(candidate, candSch) = repaired.Value;
                    int patience = 0;
                    double preDescent = candSch.TotalTardiness;
                    while (patience < DescentPatience && ctx.BudgetLeft)
                    {
                        SequenceSolution nb = candidate.Clone();
                        var mv = ctx.ApplySmallMove(nb, descentMoves[ctx.Rng.Next(descentMoves.Length)]);
                        if (!mv.ok)
                        {
                            patience++;
                            continue;
                        }

                        DecodedSchedule? nbSch = ctx.Evaluate(nb);
                        if (nbSch != null && nbSch.TotalTardiness < candSch.TotalTardiness - Eps)
                        {
                            candidate = nb;
                            candSch = nbSch;
                            patience = 0;
                        }
                        else
                        {
                            patience++;
                        }
                    }

                    descentGainRounded = (int)Math.Round(preDescent - candSch.TotalTardiness);
                    delta = candSch.TotalTardiness - curSch.TotalTardiness;
                    accepted = delta < Eps || ctx.Rng.NextDouble() < Math.Exp(-delta / Math.Max(temperature, 1e-9));
                    if (accepted)
                    {
                        ctx.AcceptedMoves++;
                        current = candidate;
                        curSch = candSch;
                        if (candSch.TotalTardiness < result.BestObjective - Eps)
                        {
                            newBest = true;
                            ctx.SinceImprovement = 0;
                            result.BestSolution = candidate.Clone();
                            result.BestSchedule = candSch;
                            result.BestObjective = candSch.TotalTardiness;
                            result.BestMakespan = candSch.Makespan;
                            result.AcceptedImprovements++;
                            result.Log.Add("IG improvement (eval " + result.Evaluations + "): tardiness=" + Math.Round(candSch.TotalTardiness, 4) + ", makespan=" + Math.Round(candSch.Makespan, 4));
                        }
                    }
                }

                temperature = Math.Max(temperature * Math.Pow(FinalTempRatio, (result.Evaluations - prevEvals) / (double)ctx.CoolingBudget), 1e-6);
                prevEvals = result.Evaluations;
                ctx.Log?.Write(new { t = "iter", algo = "ig", iter = ctx.Iterations, evals = result.Evaluations, op = "random", d = jobs.Length, jobs, jobFeat, before, after = double.IsNaN(delta) ? null : (object)Math.Round(curSch.TotalTardiness, 2), delta = double.IsNaN(delta) ? null : (object)Math.Round(delta, 2), descentGain = descentGainRounded, repairFailed = repaired == null, accepted, newBest, temp = Math.Round(temperature, 2) });
            }
        }

        static void RunTabu(Context ctx, SequenceSolution initial, DecodedSchedule initialSchedule)
        {
            var result = ctx.Result;
            SequenceSolution current = initial.Clone();
            DecodedSchedule curSch = initialSchedule;
            result.BestSolution = current.Clone();
            result.BestSchedule = curSch;
            result.BestObjective = curSch.TotalTardiness;
            result.BestMakespan = curSch.Makespan;
            const int Tenure = 5;
            const int CandidateLimit = 50;
            int[] moves =
            {
                1,
                2,
                4,
                5,
                6,
                7
            };
            var tabuUntil = new Dictionary<long, int>();
            var created = new List<long>();
            var destroyed = new List<long>();
            var chosenDestroyed = new List<long>();
            bool IsTabu(List<long> attrs)
            {
                foreach (long a in attrs)
                {
                    if (tabuUntil.TryGetValue(a, out int expiry) && ctx.Iterations <= expiry)
                    {
                        return true;
                    }
                }

                return false;
            }

            while (ctx.BudgetLeft)
            {
                ctx.Iterations++;
                ctx.SinceImprovement++;
                object before = ctx.StateFeatures(current, curSch);
                int moveType = moves[(ctx.Iterations - 1) % moves.Length];
                SequenceSolution? bestCand = null;
                DecodedSchedule? bestCandSch = null;
                bool bestWasTabu = false;
                int feasible = 0;
                int tabuBlocked = 0;
                for (int c = 0; c < CandidateLimit && ctx.BudgetLeft; c++)
                {
                    SequenceSolution nb = current.Clone();
                    var mv = ctx.ApplySmallMove(nb, moveType);
                    if (!mv.ok)
                    {
                        continue;
                    }

                    DecodedSchedule? nbSch = ctx.Evaluate(nb);
                    if (nbSch == null)
                    {
                        continue;
                    }

                    feasible++;
                    ctx.DiffAttributes(current, nb, created, destroyed);
                    bool isTabu = IsTabu(created);
                    bool aspiration = nbSch.TotalTardiness < result.BestObjective - Eps;
                    if (isTabu && !aspiration)
                    {
                        tabuBlocked++;
                        continue;
                    }

                    if (bestCandSch == null || nbSch.TotalTardiness < bestCandSch.TotalTardiness - Eps)
                    {
                        bestCand = nb;
                        bestCandSch = nbSch;
                        bestWasTabu = isTabu;
                        chosenDestroyed.Clear();
                        chosenDestroyed.AddRange(destroyed);
                    }
                }

                double delta = double.NaN;
                bool newBest = false;
                if (bestCand != null && bestCandSch != null)
                {
                    delta = bestCandSch.TotalTardiness - curSch.TotalTardiness;
                    current = bestCand;
                    curSch = bestCandSch;
                    ctx.AcceptedMoves++;
                    int expiry = ctx.Iterations + Tenure;
                    foreach (long a in chosenDestroyed)
                    {
                        tabuUntil[a] = expiry;
                    }

                    if (curSch.TotalTardiness < result.BestObjective - Eps)
                    {
                        newBest = true;
                        ctx.SinceImprovement = 0;
                        result.BestSolution = current.Clone();
                        result.BestSchedule = curSch;
                        result.BestObjective = curSch.TotalTardiness;
                        result.BestMakespan = curSch.Makespan;
                        result.AcceptedImprovements++;
                        result.Log.Add("TS improvement (move=" + moveType + ", eval " + result.Evaluations + "): tardiness=" + Math.Round(curSch.TotalTardiness, 4) + ", makespan=" + Math.Round(curSch.Makespan, 4));
                    }
                }

                ctx.Log?.Write(new { t = "iter", algo = "tabu", iter = ctx.Iterations, evals = result.Evaluations, op = "move" + moveType, before, after = double.IsNaN(delta) ? null : (object)Math.Round(curSch.TotalTardiness, 2), delta = double.IsNaN(delta) ? null : (object)Math.Round(delta, 2), feasibleCands = feasible, tabuBlocked, usedAspiration = bestWasTabu, accepted = bestCand != null, newBest, tabuSize = tabuUntil.Count(kv => kv.Value > ctx.Iterations) });
            }
        }

        static void RunRepairAblation(Context ctx, SequenceSolution initial)
        {
            var result = ctx.Result;
            result.BestSolution = initial.Clone();
            int trials = Math.Max(100, ctx.CoolingBudget / 12);
            int[] ok = new int[3];
            int[] tried = new int[3];
            for (int t = 0; t < trials && ctx.BudgetLeft; t++)
            {
                int d = 2 + ctx.Rng.Next(4);
                int[] jobs = ctx.DestroyRandom(d);
                if (jobs.Length == 0)
                {
                    break;
                }

                SequenceSolution destroyed = initial.Clone();
                foreach (int j in jobs)
                {
                    ctx.RemoveJob(destroyed, j);
                }

                DecodedSchedule? partial = ctx.Evaluate(destroyed, relaxed: true);
                if (partial == null)
                {
                    continue;
                }

                int job = jobs.OrderBy(j => 0).First();
                for (int mode = 0; mode < 3; mode++)
                {
                    SequenceSolution cand = destroyed.Clone();
                    bool built = mode switch
                    {
                        0 => ctx.InsertJobUniform(cand, job),
                        1 => ctx.InsertJobAligned(cand, job, ctx.Rng.NextDouble()),
                        _ => ctx.InsertJobAnchored(cand, partial, job, FirstEligibleWs(ctx, job), ctx.Rng.Next(cand.WorkstationSequences[FirstEligibleWs(ctx, job)].Count + 1), ctx.Rng.Next(ctx.I.r), ctx.Rng.Next(ctx.I.r))};
                    if (!built)
                    {
                        continue;
                    }

                    tried[mode]++;
                    if (ctx.Evaluate(cand, relaxed: true) != null)
                    {
                        ok[mode]++;
                    }
                }
            }

            string[] names =
            {
                "uniform(무작위)",
                "u-정렬",
                "시간 앵커"
            };
            for (int mode = 0; mode < 3; mode++)
            {
                result.Log.Add(string.Format("ABLATION {0,-14}: 후보 {1,5}개 중 실행가능 {2,5}개 = {3,6:F1}%", names[mode], tried[mode], ok[mode], tried[mode] > 0 ? 100.0 * ok[mode] / tried[mode] : 0.0));
            }
        }

        static int FirstEligibleWs(Context ctx, int job)
        {
            for (int k = 0; k < ctx.I.n; k++)
            {
                if (ctx.I.E_ik[job][k] == 1 && ctx.I.W_n_ik[job][k] > 0.5)
                {
                    return k;
                }
            }

            return 0;
        }

        static int Roulette(Random rng, double[] weights)
        {
            double total = weights.Sum();
            double x = rng.NextDouble() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                x -= weights[i];
                if (x <= 0)
                {
                    return i;
                }
            }

            return weights.Length - 1;
        }
    }
}

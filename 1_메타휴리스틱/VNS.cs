using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PDP_ASRS_WIP
{
    public class VnsResult
    {
        public SequenceSolution BestSolution = new SequenceSolution();
        public DecodedSchedule BestSchedule = new DecodedSchedule();
        public double BestObjective = double.MaxValue;
        public double BestMakespan = double.MaxValue;
        public int Evaluations;
        public int AcceptedImprovements;
        public int Iterations;
        public string StopReason = "";
        public int ShakeFailed;
        public int ShakeInfeasible;
        public int Descended;
        public int ShakeExhausted;
        public List<string> Log = new List<string>();
    }

    public partial class VNS
    {
        const double Eps = 1e-6;
        readonly int numL, r, n, m;
        readonly double[] c_h;
        readonly double[][] p_ik;
        readonly double[] q_i;
        readonly double[][][] s_ikh, t_klh;
        readonly int[][] E_ik;
        readonly double[][] R_if;
        readonly double[] d_i;
        readonly double[] u_rob_i, u_ws_i, u_asrs_i;
        readonly double[][] W_n_ik, W_r_ih, W_a_if;
        readonly double[][][][] r_ijfg;
        readonly double[][] s_in_ik, s_out_ik;
        readonly int[] b_in_k, b_out_k;
        readonly double[][][] W_in_ikb, W_out_ikb;
        readonly Random rng;
        readonly VnsResult result = new VnsResult();
        int pinnedAsrsPrefix;
        int[]? pinnedAsrsOps;
        double[]? machineReadyAt;
        NodeLayout? layout;
        int asrsOutCap = 1;
        int asrsInCap = int.MaxValue;
        int parallelDegree = 1;
        string localSearchMode = "sampled";
        public static VnsResult Run(SequenceSolution initial, int seed, int maxEvaluations, int numL, int r, int n, int m, double[] c_h, double[][] p_ik, double[] q_i, double[][][] s_ikh, double[][][] t_klh, int[][] E_ik, double[][] R_if, double[] d_i, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] W_a_if, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb, int asrsPinnedCount = 0, double[]? machineReadyAt = null, NodeLayout? nodeLayout = null, int asrsOutBufferCapacity = 1, int asrsInBufferCapacity = int.MaxValue, int noImprovementLimit = 0, int parallelDegree = 1, string localSearch = "sampled", int[]? asrsPinnedOps = null)
        {
            var vns = new VNS(seed, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
            vns.pinnedAsrsPrefix = asrsPinnedCount;
            vns.pinnedAsrsOps = asrsPinnedOps;
            vns.machineReadyAt = machineReadyAt;
            vns.layout = nodeLayout;
            vns.asrsOutCap = asrsOutBufferCapacity;
            vns.asrsInCap = asrsInBufferCapacity;
            vns.parallelDegree = Math.Max(1, parallelDegree);
            vns.localSearchMode = localSearch;
            return vns.Search(initial, maxEvaluations, noImprovementLimit);
        }

        VNS(int seed, int numL, int r, int n, int m, double[] c_h, double[][] p_ik, double[] q_i, double[][][] s_ikh, double[][][] t_klh, int[][] E_ik, double[][] R_if, double[] d_i, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] W_a_if, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb)
        {
            rng = new Random(seed);
            this.numL = numL;
            this.r = r;
            this.n = n;
            this.m = m;
            this.c_h = c_h;
            this.p_ik = p_ik;
            this.q_i = q_i;
            this.s_ikh = s_ikh;
            this.t_klh = t_klh;
            this.E_ik = E_ik;
            this.R_if = R_if;
            this.d_i = d_i;
            this.u_rob_i = u_rob_i;
            this.u_ws_i = u_ws_i;
            this.u_asrs_i = u_asrs_i;
            this.W_n_ik = W_n_ik;
            this.W_r_ih = W_r_ih;
            this.W_a_if = W_a_if;
            this.r_ijfg = r_ijfg;
            this.s_in_ik = s_in_ik;
            this.s_out_ik = s_out_ik;
            this.b_in_k = b_in_k;
            this.b_out_k = b_out_k;
            this.W_in_ikb = W_in_ikb;
            this.W_out_ikb = W_out_ikb;
        }

        VnsResult Search(SequenceSolution initial, int maxEvaluations, int noImprovementLimit)
        {
            return localSearchMode == "vnd" ? SearchGvns(initial, maxEvaluations, noImprovementLimit) : SearchSampled(initial, maxEvaluations, noImprovementLimit);
        }

        VnsResult SearchGvns(SequenceSolution initial, int maxEvaluations, int noImprovementLimit)
        {
            const int kmax = 7;
            const int ShakeAttempts = 30;
            DecodedSchedule? initialSchedule = Evaluate(initial);
            if (initialSchedule == null)
            {
                result.Log.Add("VNS: the initial solution is infeasible; search aborted.");
                return result;
            }

            SequenceSolution current = initial.Clone();
            DecodedSchedule currentSchedule = initialSchedule;
            RunVnd(ref current, ref currentSchedule, maxEvaluations);
            SequenceSolution best = current.Clone();
            double bestObj = currentSchedule.TotalTardiness;
            double bestMk = currentSchedule.Makespan;
            RecordBest(best, currentSchedule, "initial descent");
            int k = 1;
            int sinceImprovement = 0;
            bool UseNoImprovementStop() => noImprovementLimit > 0;
            while (result.Evaluations < maxEvaluations && (!UseNoImprovementStop() || sinceImprovement < noImprovementLimit))
            {
                result.Iterations++;
                sinceImprovement++;
                SequenceSolution? candidate = null;
                DecodedSchedule? schedule = null;
                for (int attempt = 0; attempt < ShakeAttempts && candidate == null; attempt++)
                {
                    SequenceSolution trial = current.Clone();
                    bool walked = true;
                    for (int step = 0; step < k; step++)
                    {
                        if (!ApplyMove(trial, 1 + rng.Next(kmax)))
                        {
                            walked = false;
                            break;
                        }
                    }

                    if (!walked)
                    {
                        result.ShakeFailed++;
                        k = k % kmax + 1;
                        continue;
                    }

                    DecodedSchedule? trialSchedule = Evaluate(trial);
                    if (trialSchedule == null)
                    {
                        result.ShakeInfeasible++;
                        k = k % kmax + 1;
                        continue;
                    }

                    candidate = trial;
                    schedule = trialSchedule;
                }

                if (candidate == null || schedule == null)
                {
                    result.ShakeExhausted++;
                    continue;
                }

                result.Descended++;
                RunVnd(ref candidate, ref schedule, maxEvaluations);
                if (Better(schedule.TotalTardiness, schedule.Makespan, currentSchedule.TotalTardiness, currentSchedule.Makespan))
                {
                    current = candidate;
                    currentSchedule = schedule;
                    k = 1;
                }
                else
                {
                    k = k % kmax + 1;
                }

                if (Better(schedule.TotalTardiness, schedule.Makespan, bestObj, bestMk))
                {
                    best = candidate.Clone();
                    bestObj = schedule.TotalTardiness;
                    bestMk = schedule.Makespan;
                    RecordBest(best, schedule, "k=" + Convert.ToString(k));
                    sinceImprovement = 0;
                }
            }

            result.StopReason = UseNoImprovementStop() && sinceImprovement >= noImprovementLimit ? "no improvement in " + Convert.ToString(noImprovementLimit) + " iterations" : "evaluation budget";
            result.Log.Add("VNS(GVNS+VND) stopped (" + result.StopReason + "): iterations=" + Convert.ToString(result.Iterations) + ", evaluations=" + Convert.ToString(result.Evaluations));
            result.Log.Add("  iterations that ran a VND: " + Convert.ToString(result.Descended) + " of " + Convert.ToString(result.Iterations) + " (shake retries: " + Convert.ToString(result.ShakeInfeasible) + " infeasible, " + Convert.ToString(result.ShakeFailed) + " not applicable, " + Convert.ToString(result.ShakeExhausted) + " iterations gave up)");
            return result;
        }

        VnsResult SearchSampled(SequenceSolution initial, int maxEvaluations, int noImprovementLimit)
        {
            const int kmax = 7;
            const int localSearchPatience = 40;
            DecodedSchedule? initialSchedule = Evaluate(initial);
            if (initialSchedule == null)
            {
                result.Log.Add("VNS: the initial solution is infeasible; search aborted.");
                return result;
            }

            SequenceSolution best = initial.Clone();
            double bestObj = initialSchedule.TotalTardiness;
            double bestMk = initialSchedule.Makespan;
            RecordBest(best, initialSchedule, "initial");
            int k = 1;
            int sinceImprovement = 0;
            bool UseNoImprovementStop() => noImprovementLimit > 0;
            while (result.Evaluations < maxEvaluations && (!UseNoImprovementStop() || sinceImprovement < noImprovementLimit))
            {
                result.Iterations++;
                sinceImprovement++;
                SequenceSolution candidate = best.Clone();
                if (!ApplyMove(candidate, k))
                {
                    k = k % kmax + 1;
                    continue;
                }

                DecodedSchedule? schedule = Evaluate(candidate);
                if (schedule == null)
                {
                    k = k % kmax + 1;
                    continue;
                }

                double curObj = schedule.TotalTardiness;
                double curMk = schedule.Makespan;
                int patience = 0;
                while (patience < localSearchPatience && result.Evaluations < maxEvaluations)
                {
                    int batch = Math.Min(parallelDegree, localSearchPatience - patience);
                    if (batch < 1)
                    {
                        batch = 1;
                    }

                    var neighbours = new SequenceSolution? [batch];
                    for (int b = 0; b < batch; b++)
                    {
                        SequenceSolution neighbour = candidate.Clone();
                        neighbours[b] = ApplyMove(neighbour, SmallMove()) ? neighbour : null;
                    }

                    var schedules = new DecodedSchedule? [batch];
                    int evaluated = 0;
                    if (batch == 1)
                    {
                        if (neighbours[0] != null)
                        {
                            schedules[0] = EvaluateCore(neighbours[0]!);
                            evaluated = 1;
                        }
                    }
                    else
                    {
                        var options = new ParallelOptions
                        {
                            MaxDegreeOfParallelism = parallelDegree
                        };
                        Parallel.For(0, batch, options, b =>
                        {
                            if (neighbours[b] != null)
                            {
                                schedules[b] = EvaluateCore(neighbours[b]!);
                            }
                        });
                        for (int b = 0; b < batch; b++)
                        {
                            if (neighbours[b] != null)
                            {
                                evaluated++;
                            }
                        }
                    }

                    result.Evaluations += evaluated;
                    int bestIndex = -1;
                    for (int b = 0; b < batch; b++)
                    {
                        if (schedules[b] == null)
                        {
                            continue;
                        }

                        double obj = schedules[b]!.TotalTardiness;
                        double mk = schedules[b]!.Makespan;
                        bool beatsCurrent = Better(obj, mk, curObj, curMk);
                        bool beatsChosen = bestIndex < 0 || Better(obj, mk, schedules[bestIndex]!.TotalTardiness, schedules[bestIndex]!.Makespan);
                        if (beatsCurrent && beatsChosen)
                        {
                            bestIndex = b;
                        }
                    }

                    if (bestIndex >= 0)
                    {
                        candidate = neighbours[bestIndex]!;
                        schedule = schedules[bestIndex]!;
                        curObj = schedule.TotalTardiness;
                        curMk = schedule.Makespan;
                        patience = 0;
                    }
                    else
                    {
                        patience += batch;
                    }
                }

                if (Better(curObj, curMk, bestObj, bestMk))
                {
                    best = candidate;
                    bestObj = curObj;
                    bestMk = curMk;
                    RecordBest(best, schedule, "k=" + Convert.ToString(k));
                    k = 1;
                    sinceImprovement = 0;
                }
                else
                {
                    k = k % kmax + 1;
                }
            }

            result.StopReason = UseNoImprovementStop() && sinceImprovement >= noImprovementLimit ? "no improvement in " + Convert.ToString(noImprovementLimit) + " iterations" : "evaluation budget";
            result.Log.Add("VNS stopped (" + result.StopReason + "): iterations=" + Convert.ToString(result.Iterations) + ", evaluations=" + Convert.ToString(result.Evaluations));
            return result;
        }

        static bool Better(double obj, double mk, double refObj, double refMk)
        {
            return obj < refObj - Eps || (obj <= refObj + Eps && mk < refMk - Eps);
        }

        void RecordBest(SequenceSolution best, DecodedSchedule schedule, string source)
        {
            result.BestSolution = best.Clone();
            result.BestSchedule = schedule;
            result.BestObjective = schedule.TotalTardiness;
            result.BestMakespan = schedule.Makespan;
            result.AcceptedImprovements++;
            result.Log.Add("VNS improvement (" + source + ", eval " + Convert.ToString(result.Evaluations) + "): tardiness=" + Convert.ToString(Math.Round(schedule.TotalTardiness, 4)) + ", makespan=" + Convert.ToString(Math.Round(schedule.Makespan, 4)));
        }

        DecodedSchedule? Evaluate(SequenceSolution solution)
        {
            result.Evaluations++;
            return EvaluateCore(solution);
        }

        DecodedSchedule? EvaluateCore(SequenceSolution solution)
        {
            bool rebuilt = FixedSequenceSimulator.RebuildBufferSlots(solution, numL, r, n, m, p_ik, s_ikh, t_klh, R_if, u_rob_i, u_ws_i, u_asrs_i, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb, machineReadyAt, layout, asrsOutCap, asrsInCap, pinnedAsrsPrefix);
            if (!rebuilt)
            {
                return null;
            }

            DecodedSchedule schedule = SolutionDecoder.Decode(solution, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb, machineReadyAt, layout, asrsOutCap, asrsInCap, allowMissingJobs: false, asrsPinnedPrefixLength: pinnedAsrsPrefix, asrsPinnedPrefix: pinnedAsrsOps);
            return schedule.IsFeasible ? schedule : null;
        }

        int SmallMove()
        {
            int[] smallMoves =
            {
                1,
                2,
                3,
                5
            };
            return smallMoves[rng.Next(smallMoves.Length)];
        }

        bool ApplyMove(SequenceSolution solution, int neighbourhood)
        {
            switch (neighbourhood)
            {
                case 1:
                    return WsInsert(solution);
                case 2:
                    return WsSwap(solution);
                case 3:
                    return AmrInsert(solution);
                case 4:
                    return AmrPairMove(solution);
                case 5:
                    return AsrsInsert(solution);
                case 6:
                    return StorageLocationChange(solution);
                case 7:
                    return WsReassign(solution);
                default:
                    return false;
            }
        }

        bool WsInsert(SequenceSolution solution)
        {
            int k = PickWorkstation(solution, 2);
            if (k < 0)
            {
                return false;
            }

            var seq = solution.WorkstationSequences[k];
            int from = rng.Next(seq.Count);
            int to = rng.Next(seq.Count - 1);
            if (to >= from)
            {
                to++;
            }

            int job = seq[from];
            seq.RemoveAt(from);
            seq.Insert(to, job);
            return true;
        }

        bool WsSwap(SequenceSolution solution)
        {
            int k = PickWorkstation(solution, 2);
            if (k < 0)
            {
                return false;
            }

            var seq = solution.WorkstationSequences[k];
            int a = rng.Next(seq.Count);
            int b = rng.Next(seq.Count - 1);
            if (b >= a)
            {
                b++;
            }

            (seq[a], seq[b]) = (seq[b], seq[a]);
            return true;
        }

        bool AmrInsert(SequenceSolution solution)
        {
            int h = PickRobot(solution, 2);
            if (h < 0)
            {
                return false;
            }

            var route = solution.AmrSequences[h];
            int from = rng.Next(route.Count);
            int to = rng.Next(route.Count - 1);
            if (to >= from)
            {
                to++;
            }

            int op = route[from];
            route.RemoveAt(from);
            route.Insert(to, op);
            return PairsOrdered(solution, h);
        }

        bool AmrPairMove(SequenceSolution solution)
        {
            var candidates = new List<int>();
            for (int i = 0; i < m; i++)
            {
                if (u_rob_i[i] > 0.5 && u_rob_i[m + i] > 0.5 && solution.RobotOpAmr[i] >= 0)
                {
                    candidates.Add(i * 2);
                }

                if (u_rob_i[2 * m + i] > 0.5 && u_rob_i[3 * m + i] > 0.5 && solution.RobotOpAmr[2 * m + i] >= 0)
                {
                    candidates.Add(i * 2 + 1);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            int pick = candidates[rng.Next(candidates.Count)];
            int job = pick / 2;
            int pickupOp = pick % 2 == 0 ? job : 2 * m + job;
            int deliveryOp = pickupOp + m;
            int oldAmr = solution.RobotOpAmr[pickupOp];
            solution.AmrSequences[oldAmr].Remove(pickupOp);
            solution.AmrSequences[oldAmr].Remove(deliveryOp);
            int newAmr = rng.Next(r);
            var route = solution.AmrSequences[newAmr];
            int pickupPos = rng.Next(route.Count + 1);
            route.Insert(pickupPos, pickupOp);
            int deliveryPos = pickupPos + 1 + rng.Next(route.Count - pickupPos);
            route.Insert(deliveryPos, deliveryOp);
            solution.RobotOpAmr[pickupOp] = newAmr;
            solution.RobotOpAmr[deliveryOp] = newAmr;
            return true;
        }

        bool AsrsInsert(SequenceSolution solution)
        {
            var seq = solution.AsrsSequence;
            int start = pinnedAsrsPrefix;
            if (seq.Count - start < 2)
            {
                return false;
            }

            int from = start + rng.Next(seq.Count - start);
            int to = start + rng.Next(seq.Count - start - 1);
            if (to >= from)
            {
                to++;
            }

            int op = seq[from];
            seq.RemoveAt(from);
            seq.Insert(to, op);
            return true;
        }

        bool StorageLocationChange(SequenceSolution solution)
        {
            var storages = new List<int>();
            for (int idx = pinnedAsrsPrefix; idx < solution.AsrsSequence.Count; idx++)
            {
                int op = solution.AsrsSequence[idx];
                if (op >= m)
                {
                    storages.Add(op);
                }
            }

            if (storages.Count == 0)
            {
                return false;
            }

            int chosen = storages[rng.Next(storages.Count)];
            bool[] used = new bool[numL];
            foreach (int op in storages)
            {
                if (op != chosen && solution.AsrsLocation[op] >= 0)
                {
                    used[solution.AsrsLocation[op]] = true;
                }
            }

            var options = new List<int>();
            for (int g = 0; g < numL; g++)
            {
                if (!used[g] && g != solution.AsrsLocation[chosen] && W_a_if[chosen][g] > 0.5)
                {
                    options.Add(g);
                }
            }

            if (options.Count == 0)
            {
                return false;
            }

            solution.AsrsLocation[chosen] = options[rng.Next(options.Count)];
            return true;
        }

        bool WsReassign(SequenceSolution solution)
        {
            var candidates = new List<int>();
            for (int i = 0; i < m; i++)
            {
                if (u_ws_i[i] < 0.5 || u_rob_i[m + i] < 0.5)
                {
                    continue;
                }

                int eligible = 0;
                for (int k = 0; k < n; k++)
                {
                    if (E_ik[i][k] == 1)
                    {
                        eligible++;
                    }
                }

                if (eligible >= 2)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            int job = candidates[rng.Next(candidates.Count)];
            int oldWs = solution.JobWorkstation[job];
            var options = new List<int>();
            for (int k = 0; k < n; k++)
            {
                if (k != oldWs && E_ik[job][k] == 1)
                {
                    options.Add(k);
                }
            }

            if (options.Count == 0)
            {
                return false;
            }

            int newWs = options[rng.Next(options.Count)];
            solution.WorkstationSequences[oldWs].Remove(job);
            var seq = solution.WorkstationSequences[newWs];
            seq.Insert(rng.Next(seq.Count + 1), job);
            solution.JobWorkstation[job] = newWs;
            return true;
        }

        int PickWorkstation(SequenceSolution solution, int minJobs)
        {
            var options = new List<int>();
            for (int k = 0; k < n; k++)
            {
                if (solution.WorkstationSequences[k].Count >= minJobs)
                {
                    options.Add(k);
                }
            }

            return options.Count > 0 ? options[rng.Next(options.Count)] : -1;
        }

        int PickRobot(SequenceSolution solution, int minOps)
        {
            var options = new List<int>();
            for (int h = 0; h < r; h++)
            {
                if (solution.AmrSequences[h].Count >= minOps)
                {
                    options.Add(h);
                }
            }

            return options.Count > 0 ? options[rng.Next(options.Count)] : -1;
        }

        bool PairsOrdered(SequenceSolution solution, int h)
        {
            var position = new Dictionary<int, int>();
            for (int idx = 0; idx < solution.AmrSequences[h].Count; idx++)
            {
                position[solution.AmrSequences[h][idx]] = idx;
            }

            for (int i = 0; i < m; i++)
            {
                if (position.TryGetValue(i, out int pPlus) && position.TryGetValue(m + i, out int dPlus) && pPlus > dPlus)
                {
                    return false;
                }

                if (position.TryGetValue(2 * m + i, out int pMinus) && position.TryGetValue(3 * m + i, out int dMinus) && pMinus > dMinus)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

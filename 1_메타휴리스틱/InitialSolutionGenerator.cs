using System;
using System.Collections.Generic;

namespace PDP_ASRS_WIP
{
    public class SequenceSolution
    {
        public int NumOperations;
        public int NumWorkstations;
        public int NumRobots;
        public int NumLocations;
        public List<int> AsrsSequence = new List<int>();
        public int[] AsrsLocation = Array.Empty<int>();
        public List<int>[] AmrSequences = Array.Empty<List<int>>();
        public int[] RobotOpAmr = Array.Empty<int>();
        public List<int>[] WorkstationSequences = Array.Empty<List<int>>();
        public int[] JobWorkstation = Array.Empty<int>();
        public int[] InputSlot = Array.Empty<int>();
        public int[] OutputSlot = Array.Empty<int>();
        public List<int>[][] InputSlotSequences = Array.Empty<List<int>[]>();
        public List<int>[][] OutputSlotSequences = Array.Empty<List<int>[]>();
        public List<string> ResourceSequenceLines()
        {
            var lines = new List<string>();
            static string Raw(IEnumerable<int> values)
            {
                return "[" + string.Join(", ", values) + "]";
            }

            lines.Add("--------------------------------------------------");
            lines.Add("RESOURCE SEQUENCES (raw)");
            lines.Add("AsrsSequence = " + Raw(AsrsSequence));
            lines.Add("AsrsLocation = " + Raw(AsrsLocation));
            for (int h = 0; h < AmrSequences.Length; h++)
            {
                lines.Add("AmrSequences[" + Convert.ToString(h) + "] = " + Raw(AmrSequences[h]));
            }

            lines.Add("RobotOpAmr = " + Raw(RobotOpAmr));
            for (int k = 0; k < WorkstationSequences.Length; k++)
            {
                lines.Add("WorkstationSequences[" + Convert.ToString(k) + "] = " + Raw(WorkstationSequences[k]));
            }

            lines.Add("JobWorkstation = " + Raw(JobWorkstation));
            for (int k = 0; k < InputSlotSequences.Length; k++)
            {
                for (int b = 0; b < InputSlotSequences[k].Length; b++)
                {
                    lines.Add("InputSlotSequences[" + Convert.ToString(k) + "][" + Convert.ToString(b) + "] = " + Raw(InputSlotSequences[k][b]));
                }
            }

            for (int k = 0; k < OutputSlotSequences.Length; k++)
            {
                for (int b = 0; b < OutputSlotSequences[k].Length; b++)
                {
                    lines.Add("OutputSlotSequences[" + Convert.ToString(k) + "][" + Convert.ToString(b) + "] = " + Raw(OutputSlotSequences[k][b]));
                }
            }

            lines.Add("InputSlot = " + Raw(InputSlot));
            lines.Add("OutputSlot = " + Raw(OutputSlot));
            lines.Add("--------------------------------------------------");
            return lines;
        }

        public SequenceSolution Clone()
        {
            var copy = new SequenceSolution
            {
                NumOperations = NumOperations,
                NumWorkstations = NumWorkstations,
                NumRobots = NumRobots,
                NumLocations = NumLocations,
                AsrsSequence = new List<int>(AsrsSequence),
                AsrsLocation = (int[])AsrsLocation.Clone(),
                RobotOpAmr = (int[])RobotOpAmr.Clone(),
                JobWorkstation = (int[])JobWorkstation.Clone(),
                InputSlot = (int[])InputSlot.Clone(),
                OutputSlot = (int[])OutputSlot.Clone(),
                AmrSequences = new List<int>[AmrSequences.Length],
                WorkstationSequences = new List<int>[WorkstationSequences.Length],
                InputSlotSequences = new List<int>[InputSlotSequences.Length][],
                OutputSlotSequences = new List<int>[OutputSlotSequences.Length][]
            };
            for (int h = 0; h < AmrSequences.Length; h++)
            {
                copy.AmrSequences[h] = new List<int>(AmrSequences[h]);
            }

            for (int k = 0; k < WorkstationSequences.Length; k++)
            {
                copy.WorkstationSequences[k] = new List<int>(WorkstationSequences[k]);
            }

            for (int k = 0; k < InputSlotSequences.Length; k++)
            {
                copy.InputSlotSequences[k] = new List<int>[InputSlotSequences[k].Length];
                for (int b = 0; b < InputSlotSequences[k].Length; b++)
                {
                    copy.InputSlotSequences[k][b] = new List<int>(InputSlotSequences[k][b]);
                }
            }

            for (int k = 0; k < OutputSlotSequences.Length; k++)
            {
                copy.OutputSlotSequences[k] = new List<int>[OutputSlotSequences[k].Length];
                for (int b = 0; b < OutputSlotSequences[k].Length; b++)
                {
                    copy.OutputSlotSequences[k][b] = new List<int>(OutputSlotSequences[k][b]);
                }
            }

            return copy;
        }
    }

    public readonly struct PolicyAction
    {
        public readonly string Kind;
        public readonly int Job;
        public readonly int AmrOrWs;
        public PolicyAction(string kind, int job, int amrOrWs)
        {
            Kind = kind;
            Job = job;
            AmrOrWs = amrOrWs;
        }

        public string Res => Kind switch
        {
            "ret" or "sto" => "crane",
            "proc" => "ws" + AmrOrWs,
            _ => "amr" + AmrOrWs,
        };
    }

    public class InitialSolutionGenerator
    {
        const double Eps = 1e-6;
        enum Stage
        {
            Retrieval,
            PickupPlus,
            DeliveryPlus,
            Process,
            EnterOutputBuffer,
            PickupMinus,
            DeliveryMinus,
            Storage
        }

        readonly int numL, r, n, m;
        readonly double[][] p_ik;
        readonly double[][][] s_ikh;
        readonly double[][][] t_klh;
        readonly int[][] E_ik;
        readonly double[][] R_if;
        readonly double[] u_rob_i, u_ws_i, u_asrs_i;
        readonly double[][] W_n_ik, W_r_ih, W_a_if;
        readonly double[][][][] r_ijfg;
        readonly double[][] s_in_ik, s_out_ik;
        readonly int[] b_in_k, b_out_k;
        readonly double[][][] W_in_ikb, W_out_ikb;
        readonly SequenceSolution solution;
        readonly List<Stage>[] chains;
        readonly int[] nextStage;
        readonly double[] readyTime;
        readonly int[] retrievalLoc;
        double t;
        double asrsFreeAt;
        int asrsPrevOp;
        int asrsPrevLoc;
        double asrsBlockLb;
        int asrsAwaitingPickup = -1;
        int lastRetrievalJob = -1;
        int asrsInCap = int.MaxValue;
        Action<string, int, string, double>? commitSink;
        Func<List<PolicyAction>, int>? policyChooser;
        bool policyOwnsPinnedTransport;
        bool policySequenceMode;
        public sealed class PolicyProbe
        {
            public double Time;
            public double AsrsFreeAt;
            public double[] AmrFreeAt = Array.Empty<double>();
            public double[] MachineAvail = Array.Empty<double>();
        }

        public static PolicyProbe? LastProbe;
        void SetProbe()
        {
            LastProbe = new PolicyProbe
            {
                Time = t,
                AsrsFreeAt = asrsFreeAt,
                AmrFreeAt = (double[])amrFreeAt.Clone(),
                MachineAvail = (double[])machineAvail.Clone()
            };
        }

        int ProbeAndChoose(Func<List<PolicyAction>, int> choose, List<PolicyAction> wsActs)
        {
            SetProbe();
            int raw = choose(wsActs);
            if (raw < 0)
            {
                return -1;
            }

            return wsActs[Math.Min(raw, wsActs.Count - 1)].AmrOrWs;
        }

        int asrsInPending;
        readonly double[] amrFreeAt;
        readonly int[] amrNode;
        readonly HashSet<int>[] amrCarrying;
        readonly double[] machineAvail;
        readonly double[][] inSlotFree;
        readonly double[][] outSlotFree;
        readonly List<int>[] outLaneGen;
        readonly List<int>[] inLaneGen;
        readonly bool[] locBlocked;
        readonly bool[] locUsedForStorage;
        int[] asrsPinned = Array.Empty<int>();
        NodeLayout layout = NodeLayout.Compact(0);
        public static SequenceSolution Generate(int numL, int r, int n, int m, double[][] p_ik, double[][][] s_ikh, double[][][] t_klh, int[][] E_ik, double[][] R_if, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] W_a_if, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb, int[]? asrsPinnedPrefix = null, double[]? machineReadyAt = null, NodeLayout? nodeLayout = null, int asrsInBufferCapacity = int.MaxValue)
        {
            var generator = new InitialSolutionGenerator(numL, r, n, m, p_ik, s_ikh, t_klh, E_ik, R_if, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
            generator.layout = nodeLayout ?? NodeLayout.Compact(n);
            generator.asrsInCap = asrsInBufferCapacity;
            generator.pinnedPrefixLen = asrsPinnedPrefix != null ? asrsPinnedPrefix.Length : 0;
            for (int h = 0; h < r; h++)
            {
                generator.amrNode[h] = generator.layout.Origin;
            }

            generator.asrsPinned = asrsPinnedPrefix ?? Array.Empty<int>();
            if (machineReadyAt != null)
            {
                for (int k = 0; k < n; k++)
                {
                    generator.machineAvail[k] = machineReadyAt[k];
                }
            }

            return generator.Run();
        }

        InitialSolutionGenerator(int numL, int r, int n, int m, double[][] p_ik, double[][][] s_ikh, double[][][] t_klh, int[][] E_ik, double[][] R_if, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] W_a_if, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb)
        {
            this.numL = numL;
            this.r = r;
            this.n = n;
            this.m = m;
            this.p_ik = p_ik;
            this.s_ikh = s_ikh;
            this.t_klh = t_klh;
            this.E_ik = E_ik;
            this.R_if = R_if;
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
            solution = new SequenceSolution
            {
                NumOperations = m,
                NumWorkstations = n,
                NumRobots = r,
                NumLocations = numL,
                AsrsLocation = new int[2 * m],
                RobotOpAmr = new int[4 * m],
                JobWorkstation = new int[m],
                InputSlot = new int[m],
                OutputSlot = new int[m],
                AmrSequences = new List<int>[r],
                WorkstationSequences = new List<int>[n],
                InputSlotSequences = new List<int>[n][],
                OutputSlotSequences = new List<int>[n][]
            };
            for (int i = 0; i < 2 * m; i++)
            {
                solution.AsrsLocation[i] = -1;
            }

            for (int i = 0; i < 4 * m; i++)
            {
                solution.RobotOpAmr[i] = -1;
            }

            for (int i = 0; i < m; i++)
            {
                solution.JobWorkstation[i] = -1;
                solution.InputSlot[i] = -1;
                solution.OutputSlot[i] = -1;
            }

            for (int h = 0; h < r; h++)
            {
                solution.AmrSequences[h] = new List<int>();
            }

            inSlotFree = new double[n][];
            outSlotFree = new double[n][];
            outLaneGen = new List<int>[n];
            inLaneGen = new List<int>[n];
            machineAvail = new double[n];
            for (int k = 0; k < n; k++)
            {
                solution.WorkstationSequences[k] = new List<int>();
                solution.InputSlotSequences[k] = new List<int>[b_in_k[k]];
                solution.OutputSlotSequences[k] = new List<int>[b_out_k[k]];
                inSlotFree[k] = new double[b_in_k[k]];
                outSlotFree[k] = new double[b_out_k[k]];
                outLaneGen[k] = new List<int>();
                inLaneGen[k] = new List<int>();
                for (int b = 0; b < b_in_k[k]; b++)
                {
                    solution.InputSlotSequences[k][b] = new List<int>();
                }

                for (int b = 0; b < b_out_k[k]; b++)
                {
                    solution.OutputSlotSequences[k][b] = new List<int>();
                }
            }

            chains = new List<Stage>[m];
            nextStage = new int[m];
            readyTime = new double[m];
            retrievalLoc = new int[m];
            locBlocked = new bool[numL];
            locUsedForStorage = new bool[numL];
            for (int i = 0; i < m; i++)
            {
                chains[i] = new List<Stage>();
                if (u_asrs_i[i] > 0.5)
                {
                    chains[i].Add(Stage.Retrieval);
                }

                if (u_rob_i[i] > 0.5)
                {
                    chains[i].Add(Stage.PickupPlus);
                }

                if (u_rob_i[m + i] > 0.5)
                {
                    chains[i].Add(Stage.DeliveryPlus);
                }

                if (u_ws_i[i] > 0.5)
                {
                    chains[i].Add(Stage.Process);
                    chains[i].Add(Stage.EnterOutputBuffer);
                }

                if (u_rob_i[2 * m + i] > 0.5)
                {
                    chains[i].Add(Stage.PickupMinus);
                }

                if (u_rob_i[3 * m + i] > 0.5)
                {
                    chains[i].Add(Stage.DeliveryMinus);
                }

                if (u_asrs_i[m + i] > 0.5)
                {
                    chains[i].Add(Stage.Storage);
                }

                retrievalLoc[i] = -1;
                if (u_asrs_i[i] > 0.5)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        if (R_if[i][f] > 0.5)
                        {
                            retrievalLoc[i] = f;
                            locBlocked[f] = true;
                            break;
                        }
                    }

                    if (retrievalLoc[i] < 0)
                    {
                        throw new ArgumentException("Job " + Convert.ToString(i) + " requires retrieval but has no location in R_if.");
                    }
                }
            }

            for (int i = 0; i < m; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    for (int b = 0; b < b_in_k[k]; b++)
                    {
                        if (W_in_ikb[i][k][b] > 0.5)
                        {
                            solution.JobWorkstation[i] = k;
                            solution.InputSlot[i] = b;
                            solution.InputSlotSequences[k][b].Add(i);
                            inLaneGen[k].Add(i);
                            inSlotFree[k][b] = double.PositiveInfinity;
                        }
                    }

                    for (int b = 0; b < b_out_k[k]; b++)
                    {
                        if (W_out_ikb[i][k][b] > 0.5)
                        {
                            solution.JobWorkstation[i] = k;
                            solution.OutputSlot[i] = b;
                            solution.OutputSlotSequences[k][b].Add(i);
                            outSlotFree[k][b] = double.PositiveInfinity;
                            outLaneGen[k].Add(i);
                        }
                    }
                }
            }

            t = 0.0;
            asrsFreeAt = 0.0;
            asrsPrevOp = 2 * m;
            asrsPrevLoc = numL;
            asrsBlockLb = 0.0;
            amrFreeAt = new double[r];
            amrNode = new int[r];
            amrCarrying = new HashSet<int>[r];
            for (int h = 0; h < r; h++)
            {
                amrNode[h] = n + 1;
                amrCarrying[h] = new HashSet<int>();
            }

            for (int i = 0; i < m; i++)
            {
                if (u_rob_i[m + i] > 0.5 && u_rob_i[i] < 0.5)
                {
                    amrCarrying[RequiredAmr(m + i)].Add(m + i);
                }

                if (u_rob_i[3 * m + i] > 0.5 && u_rob_i[2 * m + i] < 0.5)
                {
                    amrCarrying[RequiredAmr(3 * m + i)].Add(3 * m + i);
                }
            }

            asrsInPending = 0;
        }

        SequenceSolution Run()
        {
            var pinnedSet = new HashSet<int>(asrsPinned);
            asrsInPending = 0;
            for (int i = 0; i < m; i++)
            {
                if (u_asrs_i[m + i] > 0.5 && u_rob_i[3 * m + i] < 0.5 && !pinnedSet.Contains(m + i))
                {
                    asrsInPending++;
                }
            }

            foreach (int op in asrsPinned)
            {
                ForceAsrsOperation(op);
            }

            int guard = 0;
            int guardLimit = 1000 + 200 * m;
            while (AnyPending())
            {
                if (++guard > guardLimit)
                {
                    throw new InvalidOperationException("Initial solution generation did not terminate; check the WIP input flags.");
                }

                if (TryScheduleWipEntry())
                {
                    continue;
                }

                if (TrySchedulePendingChain())
                {
                    continue;
                }

                if (TryScheduleStorage())
                {
                    continue;
                }

                if (TryScheduleReturnTransport())
                {
                    continue;
                }

                if (TryScheduleRetrieval())
                {
                    continue;
                }

                AdvanceTime();
            }

            return solution;
        }

        void ForceAsrsOperation(int op)
        {
            if (op < m)
            {
                int job = op;
                int loc = retrievalLoc[job];
                double completion = Math.Max(asrsFreeAt + r_ijfg[asrsPrevOp][job][asrsPrevLoc][loc], asrsBlockLb);
                solution.AsrsSequence.Add(job);
                solution.AsrsLocation[job] = loc;
                locBlocked[loc] = false;
                commitSink?.Invoke("ret", job, "crane", completion);
                asrsFreeAt = completion;
                asrsPrevOp = job;
                asrsPrevLoc = loc;
                asrsBlockLb = 0.0;
                lastRetrievalJob = job;
                nextStage[job]++;
                readyTime[job] = completion;
                if (policyChooser == null || !policyOwnsPinnedTransport)
                {
                    ScheduleOutboundTransport(job);
                }

                if (u_rob_i[job] > 0.5 && solution.RobotOpAmr[job] < 0)
                {
                    asrsAwaitingPickup = job;
                }
            }
            else
            {
                int job = op - m;
                int best = -1;
                for (int g = 0; g < numL; g++)
                {
                    if (!locBlocked[g] && !locUsedForStorage[g] && W_a_if[op][g] > 0.5)
                    {
                        best = g;
                        break;
                    }
                }

                if (best < 0)
                {
                    throw new InvalidOperationException("Pinned storage of job " + Convert.ToString(job) + " has no allowed free AS/RS location.");
                }

                double completion = asrsFreeAt + r_ijfg[asrsPrevOp][op][asrsPrevLoc][best];
                solution.AsrsSequence.Add(op);
                solution.AsrsLocation[op] = best;
                locUsedForStorage[best] = true;
                commitSink?.Invoke("sto", job, "crane", completion);
                asrsFreeAt = completion;
                asrsPrevOp = op;
                asrsPrevLoc = best;
                nextStage[job]++;
                readyTime[job] = completion;
            }
        }

        bool AnyPending()
        {
            for (int i = 0; i < m; i++)
            {
                if (nextStage[i] < chains[i].Count)
                {
                    return true;
                }
            }

            return false;
        }

        Stage? Next(int job)
        {
            if (nextStage[job] < chains[job].Count)
            {
                return chains[job][nextStage[job]];
            }

            return null;
        }

        bool TryScheduleWipEntry()
        {
            for (int j = 0; j < m; j++)
            {
                if (readyTime[j] > t + Eps)
                {
                    continue;
                }

                Stage? stage = Next(j);
                if (stage == Stage.PickupPlus)
                {
                    if (ScheduleOutboundTransport(j))
                    {
                        return true;
                    }

                    continue;
                }

                if (stage == Stage.DeliveryPlus && u_rob_i[j] < 0.5)
                {
                    if (TryScheduleDelivery(j))
                    {
                        return true;
                    }

                    continue;
                }

                if (stage == Stage.Process && u_rob_i[m + j] < 0.5)
                {
                    if (solution.JobWorkstation[j] < 0)
                    {
                        solution.JobWorkstation[j] = ChooseWorkstation(j);
                    }

                    if (TryScheduleProcess(j))
                    {
                        return true;
                    }

                    continue;
                }

                if (stage == Stage.DeliveryMinus && u_rob_i[2 * m + j] < 0.5)
                {
                    if (asrsInPending >= asrsInCap)
                    {
                        continue;
                    }

                    int amr = RequiredAmr(3 * m + j);
                    double completion = ScheduleRobotOperation(3 * m + j, amr, readyTime[j]);
                    amrCarrying[amr].Remove(3 * m + j);
                    asrsInPending++;
                    nextStage[j]++;
                    readyTime[j] = completion;
                    return true;
                }
            }

            return false;
        }

        bool TrySchedulePendingChain()
        {
            for (int j = 0; j < m; j++)
            {
                Stage? stage = Next(j);
                if (stage == Stage.DeliveryPlus && u_rob_i[j] > 0.5 && solution.RobotOpAmr[j] >= 0)
                {
                    if (TryScheduleDelivery(j))
                    {
                        return true;
                    }
                }

                if (stage == Stage.Process && solution.JobWorkstation[j] >= 0 && (u_rob_i[m + j] < 0.5 || solution.RobotOpAmr[m + j] >= 0))
                {
                    if (TryScheduleProcess(j))
                    {
                        return true;
                    }
                }

                if (stage == Stage.EnterOutputBuffer)
                {
                    if (TryScheduleEnterOutputBuffer(j))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        bool TryScheduleStorage()
        {
            if (asrsFreeAt > t + Eps)
            {
                return false;
            }

            int bestJob = -1;
            int bestLoc = -1;
            double bestHandling = double.MaxValue;
            for (int j = 0; j < m; j++)
            {
                if (Next(j) != Stage.Storage || readyTime[j] > t + Eps)
                {
                    continue;
                }

                for (int g = 0; g < numL; g++)
                {
                    if (locBlocked[g] || locUsedForStorage[g] || W_a_if[m + j][g] < 0.5)
                    {
                        continue;
                    }

                    double handling = r_ijfg[asrsPrevOp][m + j][asrsPrevLoc][g];
                    if (handling < bestHandling - Eps)
                    {
                        bestHandling = handling;
                        bestJob = j;
                        bestLoc = g;
                    }
                }
            }

            if (bestJob < 0)
            {
                return false;
            }

            double start = Math.Max(t, readyTime[bestJob]);
            double completion = start + bestHandling;
            solution.AsrsSequence.Add(m + bestJob);
            solution.AsrsLocation[m + bestJob] = bestLoc;
            locUsedForStorage[bestLoc] = true;
            asrsFreeAt = completion;
            asrsPrevOp = m + bestJob;
            asrsPrevLoc = bestLoc;
            if (asrsInPending > 0)
            {
                asrsInPending--;
            }

            nextStage[bestJob]++;
            readyTime[bestJob] = completion;
            return true;
        }

        bool TryScheduleRetrieval()
        {
            if (asrsAwaitingPickup >= 0 || asrsFreeAt > t + Eps)
            {
                return false;
            }

            int bestJob = -1;
            double bestScore = double.NegativeInfinity;
            double bestHandling = double.MaxValue;
            for (int j = 0; j < m; j++)
            {
                if (Next(j) != Stage.Retrieval)
                {
                    continue;
                }

                double handling = r_ijfg[asrsPrevOp][j][asrsPrevLoc][retrievalLoc[j]];
                double score = MinEligibleProcessingTime(j) - handling;
                if (score > bestScore + Eps || (score > bestScore - Eps && handling < bestHandling - Eps))
                {
                    bestScore = score;
                    bestHandling = handling;
                    bestJob = j;
                }
            }

            if (bestJob < 0)
            {
                return false;
            }

            int loc = retrievalLoc[bestJob];
            double completion = Math.Max(t + bestHandling, asrsBlockLb);
            solution.AsrsSequence.Add(bestJob);
            solution.AsrsLocation[bestJob] = loc;
            locBlocked[loc] = false;
            asrsFreeAt = completion;
            asrsPrevOp = bestJob;
            asrsPrevLoc = loc;
            asrsBlockLb = 0.0;
            lastRetrievalJob = bestJob;
            nextStage[bestJob]++;
            readyTime[bestJob] = completion;
            ScheduleOutboundTransport(bestJob);
            if (u_rob_i[bestJob] > 0.5 && solution.RobotOpAmr[bestJob] < 0)
            {
                asrsAwaitingPickup = bestJob;
            }

            return true;
        }

        bool TryScheduleReturnTransport()
        {
            int bestJob = -1;
            double bestReady = double.MaxValue;
            for (int j = 0; j < m; j++)
            {
                if (Next(j) != Stage.PickupMinus || readyTime[j] > t + Eps || readyTime[j] >= bestReady - Eps)
                {
                    continue;
                }

                if (u_rob_i[3 * m + j] > 0.5 && asrsInPending >= asrsInCap)
                {
                    continue;
                }

                bestReady = readyTime[j];
                bestJob = j;
            }

            if (bestJob < 0)
            {
                return false;
            }

            int ws = solution.JobWorkstation[bestJob];
            if (ws < 0)
            {
                ws = ChooseWorkstation(bestJob);
                solution.JobWorkstation[bestJob] = ws;
            }

            int pickupOp = 2 * m + bestJob;
            int amr = ChooseAmr(pickupOp, layout.Out(ws));
            if (amr < 0)
            {
                return false;
            }

            double crPickup = ScheduleRobotOperation(pickupOp, amr, readyTime[bestJob]);
            nextStage[bestJob]++;
            readyTime[bestJob] = crPickup;
            if (solution.OutputSlot[bestJob] >= 0)
            {
                outSlotFree[ws][solution.OutputSlot[bestJob]] = crPickup;
            }

            if (outLaneGen[ws].Count > 0 && outLaneGen[ws][0] == bestJob)
            {
                outLaneGen[ws].RemoveAt(0);
            }

            if (Next(bestJob) == Stage.DeliveryMinus)
            {
                double crDelivery = ScheduleRobotOperation(3 * m + bestJob, amr, crPickup);
                asrsInPending++;
                nextStage[bestJob]++;
                readyTime[bestJob] = crDelivery;
            }
            else if (u_rob_i[3 * m + bestJob] > 0.5)
            {
                amrCarrying[amr].Add(3 * m + bestJob);
            }

            return true;
        }

        bool ScheduleOutboundTransport(int job)
        {
            bool progressed = false;
            if (Next(job) == Stage.PickupPlus)
            {
                if (solution.JobWorkstation[job] < 0 && (u_rob_i[m + job] > 0.5 || u_ws_i[job] > 0.5))
                {
                    solution.JobWorkstation[job] = ChooseWorkstation(job);
                }

                int amr = ChooseAmr(job, layout.AsrsOut);
                if (amr < 0)
                {
                    return progressed;
                }

                if (u_rob_i[m + job] > 0.5)
                {
                    int freeAmrs = 0;
                    for (int h2 = 0; h2 < r; h2++)
                    {
                        if (amrCarrying[h2].Count == 0)
                        {
                            freeAmrs++;
                        }
                    }

                    int targetWs = solution.JobWorkstation[job];
                    if (freeAmrs <= 1 && targetWs >= 0 && BestSlot(inSlotFree[targetWs]) < 0)
                    {
                        return progressed;
                    }
                }

                double crPickup = ScheduleRobotOperation(job, amr, readyTime[job]);
                nextStage[job]++;
                readyTime[job] = crPickup;
                if (u_rob_i[m + job] > 0.5)
                {
                    amrCarrying[amr].Add(m + job);
                }

                if (lastRetrievalJob == job)
                {
                    asrsBlockLb = Math.Max(asrsBlockLb, crPickup);
                }

                if (asrsAwaitingPickup == job)
                {
                    asrsAwaitingPickup = -1;
                }

                progressed = true;
            }

            if (Next(job) == Stage.DeliveryPlus)
            {
                progressed |= TryScheduleDelivery(job);
            }

            if (Next(job) == Stage.Process)
            {
                progressed |= TryScheduleProcess(job);
            }

            if (Next(job) == Stage.EnterOutputBuffer)
            {
                progressed |= TryScheduleEnterOutputBuffer(job);
            }

            return progressed;
        }

        bool TryScheduleDelivery(int job)
        {
            int k = solution.JobWorkstation[job];
            if (k < 0)
            {
                k = ChooseWorkstation(job);
                solution.JobWorkstation[job] = k;
            }

            int slot = BestSlot(inSlotFree[k]);
            if (slot < 0)
            {
                return false;
            }

            int deliveryOp = m + job;
            int amr = u_rob_i[job] > 0.5 && solution.RobotOpAmr[job] >= 0 ? solution.RobotOpAmr[job] : RequiredAmr(deliveryOp);
            int inNode = layout.In(k);
            double arrival = amrFreeAt[amr] + t_klh[amrNode[amr]][inNode][amr];
            double entry = Math.Max(Math.Max(arrival + s_ikh[deliveryOp][inNode][amr], readyTime[job]), inSlotFree[k][slot]);
            solution.AmrSequences[amr].Add(deliveryOp);
            solution.RobotOpAmr[deliveryOp] = amr;
            amrFreeAt[amr] = entry;
            amrNode[amr] = inNode;
            amrCarrying[amr].Remove(deliveryOp);
            solution.InputSlot[job] = slot;
            solution.InputSlotSequences[k][slot].Add(job);
            inSlotFree[k][slot] = double.PositiveInfinity;
            inLaneGen[k].Add(job);
            commitSink?.Invoke("dplus", job, "amr" + Convert.ToString(amr), entry);
            nextStage[job]++;
            readyTime[job] = entry;
            if (Next(job) == Stage.Process)
            {
                TryScheduleProcess(job);
            }

            return true;
        }

        bool[]? machineWipCache;
        bool[] MachineWipJobs
        {
            get
            {
                if (machineWipCache != null)
                {
                    return machineWipCache;
                }

                var flags = new bool[m];
                for (int i = 0; i < m; i++)
                {
                    if (u_ws_i[i] < 0.5 || u_rob_i[m + i] > 0.5)
                    {
                        continue;
                    }

                    bool inBuffer = false;
                    for (int k = 0; k < n && !inBuffer; k++)
                    {
                        for (int b = 0; b < b_in_k[k]; b++)
                        {
                            if (W_in_ikb[i][k][b] > 0.5)
                            {
                                inBuffer = true;
                                break;
                            }
                        }
                    }

                    flags[i] = !inBuffer;
                }

                machineWipCache = flags;
                return machineWipCache;
            }
        }

        int PendingMachineWipAt(int k)
        {
            var wip = MachineWipJobs;
            for (int i = 0; i < m; i++)
            {
                if (!wip[i] || Next(i) != Stage.Process)
                {
                    continue;
                }

                if (W_n_ik[i][k] > 0.5)
                {
                    return i;
                }
            }

            return -1;
        }

        bool TryScheduleProcess(int job)
        {
            int k = solution.JobWorkstation[job];
            if (double.IsPositiveInfinity(machineAvail[k]))
            {
                return false;
            }

            int wipFirst = PendingMachineWipAt(k);
            if (wipFirst >= 0 && wipFirst != job)
            {
                return false;
            }

            if (!MachineWipJobs[job] && inLaneGen[k].Count > 0 && inLaneGen[k][0] != job)
            {
                return false;
            }

            double start = Math.Max(readyTime[job], machineAvail[k]);
            double completion = start + s_in_ik[job][k] + p_ik[job][k] + s_out_ik[job][k];
            solution.WorkstationSequences[k].Add(job);
            machineAvail[k] = double.PositiveInfinity;
            commitSink?.Invoke("proc", job, "ws" + Convert.ToString(k), completion);
            if (solution.InputSlot[job] >= 0)
            {
                inSlotFree[k][solution.InputSlot[job]] = start;
            }

            if (inLaneGen[k].Count > 0 && inLaneGen[k][0] == job)
            {
                inLaneGen[k].RemoveAt(0);
            }

            nextStage[job]++;
            readyTime[job] = completion;
            TryScheduleEnterOutputBuffer(job);
            return true;
        }

        bool TryScheduleEnterOutputBuffer(int job)
        {
            if (Next(job) != Stage.EnterOutputBuffer)
            {
                return false;
            }

            int k = solution.JobWorkstation[job];
            int slot = BestSlot(outSlotFree[k]);
            if (slot < 0)
            {
                return false;
            }

            double entry = Math.Max(readyTime[job], outSlotFree[k][slot]);
            solution.OutputSlot[job] = slot;
            solution.OutputSlotSequences[k][slot].Add(job);
            outSlotFree[k][slot] = double.PositiveInfinity;
            outLaneGen[k].Add(job);
            machineAvail[k] = entry;
            nextStage[job]++;
            readyTime[job] = entry;
            return true;
        }

        static int BestSlot(double[] slotFree)
        {
            int best = -1;
            for (int b = 0; b < slotFree.Length; b++)
            {
                if (!double.IsPositiveInfinity(slotFree[b]) && (best < 0 || slotFree[b] < slotFree[best] - Eps))
                {
                    best = b;
                }
            }

            return best;
        }

        double MinEligibleProcessingTime(int job)
        {
            double best = 0.0;
            bool found = false;
            for (int k = 0; k < n; k++)
            {
                if (E_ik[job][k] == 1 && (!found || p_ik[job][k] < best))
                {
                    best = p_ik[job][k];
                    found = true;
                }
            }

            return best;
        }

        int ChooseWorkstation(int job)
        {
            if (u_rob_i[m + job] < 0.5)
            {
                for (int k = 0; k < n; k++)
                {
                    if (W_n_ik[job][k] > 0.5)
                    {
                        return k;
                    }
                }

                throw new ArgumentException("Job " + Convert.ToString(job) + " is already at a workstation but W_n_ik provides no station.");
            }

            int best = -1;
            for (int k = 0; k < n; k++)
            {
                if (E_ik[job][k] != 1)
                {
                    continue;
                }

                double bestAvail = best < 0 || double.IsPositiveInfinity(machineAvail[best]) ? double.MaxValue : machineAvail[best];
                double curAvail = double.IsPositiveInfinity(machineAvail[k]) ? double.MaxValue : machineAvail[k];
                if (best < 0 || p_ik[job][k] < p_ik[job][best] - Eps || (Math.Abs(p_ik[job][k] - p_ik[job][best]) <= Eps && curAvail < bestAvail - Eps))
                {
                    best = k;
                }
            }

            if (best < 0)
            {
                throw new ArgumentException("Job " + Convert.ToString(job) + " has no eligible workstation in E_ik.");
            }

            return best;
        }

        int RequiredAmr(int robotOp)
        {
            for (int h = 0; h < r; h++)
            {
                if (W_r_ih[robotOp][h] > 0.5)
                {
                    return h;
                }
            }

            throw new ArgumentException("Robot operation " + Convert.ToString(robotOp) + " must start loaded but W_r_ih provides no AMR.");
        }

        int ChooseAmr(int robotOp, int targetNode)
        {
            int best = -1;
            bool bestAvailable = false;
            double bestKey = double.MaxValue;
            for (int h = 0; h < r; h++)
            {
                if (amrCarrying[h].Count > 0 && !amrCarrying[h].Contains(robotOp))
                {
                    continue;
                }

                bool firstOpAllowed = solution.AmrSequences[h].Count > 0 || W_r_ih[robotOp][h] > 0.5;
                if (!firstOpAllowed)
                {
                    continue;
                }

                double travel = t_klh[amrNode[h]][targetNode][h];
                bool available = amrFreeAt[h] <= t + Eps;
                double key = available ? travel : amrFreeAt[h] + travel;
                if (best < 0 || (available && !bestAvailable) || (available == bestAvailable && key < bestKey - Eps))
                {
                    best = h;
                    bestAvailable = available;
                    bestKey = key;
                }
            }

            return best;
        }

        int RobotOpNode(int robotOp)
        {
            if (robotOp < m)
            {
                return layout.AsrsOut;
            }

            if (robotOp < 2 * m)
            {
                int k = solution.JobWorkstation[robotOp - m];
                return k >= 0 ? layout.In(k) : -1;
            }

            if (robotOp < 3 * m)
            {
                int k = solution.JobWorkstation[robotOp - 2 * m];
                return k >= 0 ? layout.Out(k) : -1;
            }

            return layout.AsrsIn;
        }

        public static SequenceSolution GeneratePolicy(int numL, int r, int n, int m, double[][] p_ik, double[][][] s_ikh, double[][][] t_klh, int[][] E_ik, double[][] R_if, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] W_a_if, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb, Func<List<PolicyAction>, int> chooser, int[]? asrsPinnedPrefix = null, double[]? machineReadyAt = null, NodeLayout? nodeLayout = null, int asrsInBufferCapacity = int.MaxValue, Action<string, int, string, double>? commitSink = null, bool policyOwnsPinnedTransport = false, bool policySequenceMode = false)
        {
            var generator = new InitialSolutionGenerator(numL, r, n, m, p_ik, s_ikh, t_klh, E_ik, R_if, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
            generator.layout = nodeLayout ?? NodeLayout.Compact(n);
            generator.asrsInCap = asrsInBufferCapacity;
            for (int h = 0; h < r; h++)
            {
                generator.amrNode[h] = generator.layout.Origin;
            }

            generator.asrsPinned = asrsPinnedPrefix ?? Array.Empty<int>();
            generator.commitSink = commitSink;
            generator.policyOwnsPinnedTransport = policyOwnsPinnedTransport;
            generator.policySequenceMode = policySequenceMode;
            if (machineReadyAt != null)
            {
                for (int k = 0; k < n; k++)
                {
                    generator.machineAvail[k] = machineReadyAt[k];
                }
            }

            return generator.RunPolicy(chooser);
        }

        SequenceSolution RunPolicy(Func<List<PolicyAction>, int> choose)
        {
            policyChooser = choose;
            var pinnedSet = new HashSet<int>(asrsPinned);
            asrsInPending = 0;
            for (int i = 0; i < m; i++)
            {
                if (u_asrs_i[m + i] > 0.5 && u_rob_i[3 * m + i] < 0.5 && !pinnedSet.Contains(m + i))
                {
                    asrsInPending++;
                }
            }

            foreach (int op in asrsPinned)
            {
                ForceAsrsOperation(op);
            }

            int guard = 0;
            int guardLimit = 1000 + 200 * m;
            while (AnyPending())
            {
                if (++guard > guardLimit)
                {
                    throw new InvalidOperationException("Policy rollout did not terminate.");
                }

                if (TryScheduleWipEntryAutoOnly())
                {
                    continue;
                }

                if (TrySchedulePendingChain())
                {
                    continue;
                }

                var acts = EnumeratePolicyActions();
                if (acts.Count == 0)
                {
                    AdvanceTimePolicy();
                    continue;
                }

                SetProbe();
                int rawPick = choose(acts);
                if (rawPick < 0)
                {
                    if (TryAdvanceTimePolicy())
                    {
                        continue;
                    }

                    rawPick = choose(acts);
                    if (rawPick < 0)
                    {
                        rawPick = 0;
                    }
                }

                PolicyAction pick = acts[Math.Clamp(rawPick, 0, acts.Count - 1)];
                switch (pick.Kind)
                {
                    case "sto":
                        ExecuteStorage(pick.Job);
                        break;
                    case "ret":
                        ExecuteRetrieval(pick.Job);
                        break;
                    case "pminus":
                        ExecuteReturnTransport(pick.Job, pick.AmrOrWs);
                        break;
                    default:
                        if (!ExecutePickupPlus(pick.Job, pick.AmrOrWs, choose))
                        {
                            if (!TryAdvanceTimePolicy())
                            {
                                ExecutePickupPlus(pick.Job, pick.AmrOrWs, _ => 0);
                            }
                        }

                        break;
                }
            }

            return solution;
        }

        bool TryScheduleWipEntryAutoOnly()
        {
            for (int j = 0; j < m; j++)
            {
                if (readyTime[j] > t + Eps)
                {
                    continue;
                }

                Stage? stage = Next(j);
                if (stage == Stage.DeliveryPlus && u_rob_i[j] < 0.5)
                {
                    if (TryScheduleDelivery(j))
                    {
                        return true;
                    }

                    continue;
                }

                if (stage == Stage.Process && u_rob_i[m + j] < 0.5)
                {
                    if (solution.JobWorkstation[j] < 0)
                    {
                        solution.JobWorkstation[j] = ChooseWorkstation(j);
                    }

                    if (TryScheduleProcess(j))
                    {
                        return true;
                    }

                    continue;
                }

                if (stage == Stage.DeliveryMinus && u_rob_i[2 * m + j] < 0.5)
                {
                    if (asrsInPending >= asrsInCap)
                    {
                        continue;
                    }

                    int amr = RequiredAmr(3 * m + j);
                    double completion = ScheduleRobotOperation(3 * m + j, amr, readyTime[j]);
                    commitSink?.Invoke("dminus", j, "amr" + Convert.ToString(amr), completion);
                    amrCarrying[amr].Remove(3 * m + j);
                    asrsInPending++;
                    nextStage[j]++;
                    readyTime[j] = completion;
                    return true;
                }
            }

            return false;
        }

        List<int>[]? cycAdj;
        int cycAsrsOutCap = 1;
        int pinnedPrefixLen;
        int[] cycRetJobAtLoc = Array.Empty<int>();
        int NA(int op) => op;
        int NR(int op) => 2 * m + op;
        int NP(int job) => 6 * m + job;
        int NBI(int job) => 7 * m + job;
        int NBO(int job) => 8 * m + job;
        void CycAdd(int u, int v)
        {
            if (u < 0 || v < 0)
            {
                return;
            }

            cycAdj![u].Add(v);
        }

        void CycBuild()
        {
            int nv = 9 * m;
            cycAdj = new List<int>[nv];
            for (int i = 0; i < nv; i++)
            {
                cycAdj[i] = new List<int>();
            }

            if (cycRetJobAtLoc.Length != numL)
            {
                cycRetJobAtLoc = new int[numL];
                for (int f = 0; f < numL; f++)
                {
                    cycRetJobAtLoc[f] = -1;
                }

                for (int i = 0; i < m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        if (R_if[i][f] > 0.5)
                        {
                            cycRetJobAtLoc[f] = i;
                        }
                    }
                }
            }

            for (int j = 0; j < m; j++)
            {
                bool hasRet = u_asrs_i[j] > 0.5;
                bool hasSto = u_asrs_i[m + j] > 0.5;
                bool hasPp = u_rob_i[j] > 0.5;
                bool hasDp = u_rob_i[m + j] > 0.5;
                bool hasPm = u_rob_i[2 * m + j] > 0.5;
                bool hasDm = u_rob_i[3 * m + j] > 0.5;
                bool hasProc = u_ws_i[j] > 0.5;
                if (hasRet && hasPp)
                {
                    CycAdd(NA(j), NR(j));
                }

                if (hasPp && hasDp)
                {
                    CycAdd(NR(j), NR(m + j));
                }

                if (hasDp)
                {
                    CycAdd(NR(m + j), NBI(j));
                }

                if (hasProc)
                {
                    CycAdd(NBI(j), NP(j));
                    CycAdd(NP(j), NBO(j));
                }

                if (hasPm)
                {
                    CycAdd(NBO(j), NR(2 * m + j));
                }

                if (hasPm && hasDm)
                {
                    CycAdd(NR(2 * m + j), NR(3 * m + j));
                }

                if (hasDm && hasSto)
                {
                    CycAdd(NR(3 * m + j), NA(m + j));
                }
                else if (hasRet && hasSto)
                {
                    CycAdd(NA(j), NA(m + j));
                }
            }

            int prevA = -1;
            var retsB = new List<int>();
            var stosB = new List<int>();
            for (int idxB = 0; idxB < solution.AsrsSequence.Count; idxB++)
            {
                int op = solution.AsrsSequence[idxB];
                if (op < m)
                {
                    if (retsB.Count >= cycAsrsOutCap)
                    {
                        int gj = retsB[retsB.Count - cycAsrsOutCap];
                        if (u_rob_i[gj] > 0.5)
                        {
                            CycAdd(NR(gj), NA(op));
                        }
                    }

                    retsB.Add(op);
                }
                else if (idxB >= pinnedPrefixLen)
                {
                    if (asrsInCap < int.MaxValue && stosB.Count >= asrsInCap)
                    {
                        CycAdd(NA(stosB[stosB.Count - asrsInCap]), NR(3 * m + (op - m)));
                    }

                    stosB.Add(op);
                }
            }

            foreach (int op in solution.AsrsSequence)
            {
                if (prevA >= 0)
                {
                    CycAdd(NA(prevA), NA(op));
                }

                if (op >= m && op < solution.AsrsLocation.Length)
                {
                    int loc = solution.AsrsLocation[op];
                    if (loc >= 0 && loc < numL && cycRetJobAtLoc[loc] >= 0)
                    {
                        CycAdd(NA(cycRetJobAtLoc[loc]), NA(op));
                    }
                }

                prevA = op;
            }

            for (int h = 0; h < r; h++)
            {
                int prevR = -1;
                foreach (int op in solution.AmrSequences[h])
                {
                    if (prevR >= 0)
                    {
                        CycAdd(NR(prevR), NR(op));
                    }

                    prevR = op;
                }
            }

            for (int k = 0; k < n; k++)
            {
                int prevW = -1;
                foreach (int job in solution.WorkstationSequences[k])
                {
                    if (prevW >= 0)
                    {
                        CycAdd(NBO(prevW), NP(job));
                    }

                    prevW = job;
                }
            }

            for (int k = 0; k < n && k < solution.InputSlotSequences.Length; k++)
            {
                for (int b = 0; b < solution.InputSlotSequences[k].Length; b++)
                {
                    int prev = -1;
                    foreach (int job in solution.InputSlotSequences[k][b])
                    {
                        if (prev >= 0)
                        {
                            CycAdd(NP(prev), NBI(job));
                        }

                        prev = job;
                    }
                }

                for (int b = 0; b < solution.OutputSlotSequences[k].Length; b++)
                {
                    int prev = -1;
                    foreach (int job in solution.OutputSlotSequences[k][b])
                    {
                        if (prev >= 0)
                        {
                            CycAdd(NR(2 * m + prev), NBO(job));
                        }

                        prev = job;
                    }
                }
            }
        }

        string CycName(int v)
        {
            if (v < m)
            {
                return "ret" + Convert.ToString(v);
            }

            if (v < 2 * m)
            {
                return "sto" + Convert.ToString(v - m);
            }

            int o = v - 2 * m;
            if (o < 4 * m)
            {
                string[] kk =
                {
                    "P+",
                    "D+",
                    "P-",
                    "D-"
                };
                return kk[o / m] + Convert.ToString(o % m);
            }

            int w = v - 6 * m;
            if (w < m)
            {
                return "proc" + Convert.ToString(w);
            }

            if (w < 2 * m)
            {
                return "bufIn" + Convert.ToString(w - m);
            }

            return "bufOut" + Convert.ToString(w - 2 * m);
        }

        string? CycPath(int src, int dst)
        {
            var prev = new int[cycAdj!.Length];
            var seen = new bool[cycAdj.Length];
            for (int i = 0; i < prev.Length; i++)
            {
                prev[i] = -1;
            }

            var q = new Queue<int>();
            seen[src] = true;
            q.Enqueue(src);
            while (q.Count > 0)
            {
                int u = q.Dequeue();
                foreach (int v in cycAdj[u])
                {
                    if (seen[v])
                    {
                        continue;
                    }

                    seen[v] = true;
                    prev[v] = u;
                    if (v == dst)
                    {
                        var path = new List<string>();
                        int x = dst;
                        while (x != -1)
                        {
                            path.Add(CycName(x));
                            x = prev[x];
                        }

                        path.Reverse();
                        return string.Join(" -> ", path);
                    }

                    q.Enqueue(v);
                }
            }

            return null;
        }

        static bool CycSame(PolicyAction a, PolicyAction b) => a.Kind == b.Kind && a.Job == b.Job && a.AmrOrWs == b.AmrOrWs;
        List<(int u, int cnt)> CycPush(PolicyAction a)
        {
            var marks = new List<(int, int)>();
            foreach (var(u, v)in CycNewArcs(a))
            {
                if (u >= 0 && v >= 0)
                {
                    marks.Add((u, cycAdj![u].Count));
                    cycAdj[u].Add(v);
                }
            }

            return marks;
        }

        void CycPop(List<(int u, int cnt)> marks)
        {
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                cycAdj![marks[i].u].RemoveRange(marks[i].cnt, cycAdj[marks[i].u].Count - marks[i].cnt);
            }
        }

        bool CycHasEscape(List<PolicyAction> pool, int depth)
        {
            foreach (var a in pool)
            {
                if (CycWouldClose(a))
                {
                    continue;
                }

                if (depth <= 0)
                {
                    return true;
                }

                var marks = CycPush(a);
                var rest = new List<PolicyAction>();
                foreach (var b in pool)
                {
                    if (!CycSame(b, a))
                    {
                        rest.Add(b);
                    }
                }

                bool ok = rest.Count == 0 || CycHasEscape(rest, depth - 1);
                CycPop(marks);
                if (ok)
                {
                    return true;
                }
            }

            return false;
        }

        void CycExplain(PolicyAction a)
        {
            foreach (var(u, v)in CycNewArcs(a))
            {
                if (u < 0 || v < 0)
                {
                    continue;
                }

                string? path = CycPath(v, u);
                if (path != null)
                {
                    Console.WriteLine($"@@CYCMASK   cand={a.Kind}{a.Job}@{a.AmrOrWs} newarc={CycName(u)}->{CycName(v)} path={path}");
                }
            }
        }

        bool CycReaches(int src, int dst)
        {
            if (src == dst)
            {
                return true;
            }

            var seen = new bool[cycAdj!.Length];
            var stack = new Stack<int>();
            seen[src] = true;
            stack.Push(src);
            while (stack.Count > 0)
            {
                int u = stack.Pop();
                foreach (int v in cycAdj[u])
                {
                    if (v == dst)
                    {
                        return true;
                    }

                    if (!seen[v])
                    {
                        seen[v] = true;
                        stack.Push(v);
                    }
                }
            }

            return false;
        }

        List<(int u, int v)> CycNewArcs(PolicyAction a)
        {
            var arcs = new List<(int, int)>();
            if (a.Kind == "ret" || a.Kind == "sto")
            {
                bool isRet = a.Kind == "ret";
                int op = isRet ? a.Job : m + a.Job;
                if (solution.AsrsSequence.Count > 0)
                {
                    arcs.Add((NA(solution.AsrsSequence[solution.AsrsSequence.Count - 1]), NA(op)));
                }

                var rets = new List<int>();
                var stos = new List<int>();
                for (int idx = 0; idx < solution.AsrsSequence.Count; idx++)
                {
                    int o = solution.AsrsSequence[idx];
                    if (o < m)
                    {
                        rets.Add(o);
                    }
                    else if (idx >= pinnedPrefixLen)
                    {
                        stos.Add(o);
                    }
                }

                if (isRet)
                {
                    if (rets.Count >= cycAsrsOutCap)
                    {
                        int gate = rets[rets.Count - cycAsrsOutCap];
                        arcs.Add((NR(gate), NA(op)));
                    }
                }
                else
                {
                    if (asrsInCap < int.MaxValue && stos.Count >= asrsInCap)
                    {
                        int gate = stos[stos.Count - asrsInCap];
                        arcs.Add((NA(gate), NR(3 * m + a.Job)));
                    }
                }
            }
            else if (a.Kind == "proc")
            {
                int k = a.AmrOrWs;
                if (k >= 0 && k < n)
                {
                    var seq = solution.WorkstationSequences[k];
                    if (seq.Count > 0)
                    {
                        arcs.Add((NBO(seq[seq.Count - 1]), NP(a.Job)));
                    }
                }
            }
            else
            {
                int h = a.AmrOrWs;
                if (h >= 0 && h < r)
                {
                    int op = a.Kind == "pplus" ? a.Job : a.Kind == "dplus" ? m + a.Job : a.Kind == "pminus" ? 2 * m + a.Job : 3 * m + a.Job;
                    var seq = solution.AmrSequences[h];
                    if (seq.Count > 0)
                    {
                        arcs.Add((NR(seq[seq.Count - 1]), NR(op)));
                    }
                }
            }

            return arcs;
        }

        bool CycWouldClose(PolicyAction a)
        {
            foreach (var(u, v)in CycNewArcs(a))
            {
                if (CycReaches(v, u))
                {
                    return true;
                }
            }

            return false;
        }

        List<PolicyAction> EnumeratePolicyActions()
        {
            var acts = new List<PolicyAction>();
            if (asrsFreeAt <= t + Eps)
            {
                for (int j = 0; j < m; j++)
                {
                    if (Next(j) == Stage.Storage && (policySequenceMode || readyTime[j] <= t + Eps) && HasFreeStorageCell(m + j))
                    {
                        acts.Add(new PolicyAction("sto", j, -1));
                    }
                }

                if (asrsAwaitingPickup < 0)
                {
                    for (int j = 0; j < m; j++)
                    {
                        if (Next(j) == Stage.Retrieval)
                        {
                            if (RetrievalGuardBlocks(j))
                            {
                                continue;
                            }

                            acts.Add(new PolicyAction("ret", j, -1));
                        }
                    }
                }
            }

            for (int j = 0; j < m; j++)
            {
                if (Next(j) != Stage.PickupMinus || (!policySequenceMode && readyTime[j] > t + Eps))
                {
                    continue;
                }

                if (u_rob_i[3 * m + j] > 0.5 && asrsInPending >= asrsInCap)
                {
                    continue;
                }

                int wsOfJ = solution.JobWorkstation[j];
                if (Environment.GetEnvironmentVariable("PDP_WSFIFO_OUT") != "0" && wsOfJ >= 0 && outLaneGen[wsOfJ].Count > 0 && outLaneGen[wsOfJ][0] != j)
                {
                    continue;
                }

                for (int h = 0; h < r; h++)
                {
                    if (AmrEligible(2 * m + j, h))
                    {
                        acts.Add(new PolicyAction("pminus", j, h));
                    }
                }
            }

            for (int j = 0; j < m; j++)
            {
                if (Next(j) != Stage.PickupPlus || (!policySequenceMode && readyTime[j] > t + Eps))
                {
                    continue;
                }

                for (int h = 0; h < r; h++)
                {
                    if (!AmrEligible(j, h))
                    {
                        continue;
                    }

                    if (u_rob_i[m + j] > 0.5 && LastFreeAmrGuardBlocks(j))
                    {
                        continue;
                    }

                    if (u_rob_i[m + j] > 0.5 && PickupPlusGuardBlocks(j))
                    {
                        continue;
                    }

                    acts.Add(new PolicyAction("pplus", j, h));
                }
            }

            if (policySequenceMode && Environment.GetEnvironmentVariable("PDP_DES_STANDFIFO") != "0" && cycAsrsOutCap == 1)
            {
                var picked = new HashSet<int>();
                for (int h = 0; h < r; h++)
                {
                    foreach (int op in solution.AmrSequences[h])
                    {
                        if (op < m)
                        {
                            picked.Add(op);
                        }
                    }
                }

                int owed = -1;
                foreach (int op in solution.AsrsSequence)
                {
                    if (op < m && u_rob_i[op] > 0.5 && !picked.Contains(op))
                    {
                        owed = op;
                        break;
                    }
                }

                if (owed >= 0)
                {
                    var keepFifo = new List<PolicyAction>();
                    foreach (var a in acts)
                    {
                        if (a.Kind == "pplus" && a.Job != owed)
                        {
                            continue;
                        }

                        keepFifo.Add(a);
                    }

                    if (keepFifo.Count > 0 && keepFifo.Count < acts.Count)
                    {
                        if (Environment.GetEnvironmentVariable("PDP_DES_CYCMASK_LOG") == "1")
                        {
                            Console.WriteLine($"@@CYCMASK standfifo owed={owed} excluded={acts.Count - keepFifo.Count}/{acts.Count}");
                        }

                        acts = keepFifo;
                    }
                }
            }

            if (policySequenceMode && Environment.GetEnvironmentVariable("PDP_DES_CYCMASK") != "0" && acts.Count > 0)
            {
                CycBuild();
                bool log = Environment.GetEnvironmentVariable("PDP_DES_CYCMASK_LOG") == "1";
                var keep = new List<PolicyAction>();
                foreach (var a in acts)
                {
                    if (!CycWouldClose(a))
                    {
                        keep.Add(a);
                    }
                }

                int afterDirect = keep.Count;
                int depth = 1;
                if (int.TryParse(Environment.GetEnvironmentVariable("PDP_DES_CYCMASK_DEPTH"), out int _d) && _d >= 0)
                {
                    depth = _d;
                }

                if (depth >= 1 && keep.Count > 1)
                {
                    var survive = new List<PolicyAction>();
                    foreach (var a in keep)
                    {
                        var marks = CycPush(a);
                        var rest = new List<PolicyAction>();
                        foreach (var b in keep)
                        {
                            if (!CycSame(b, a))
                            {
                                rest.Add(b);
                            }
                        }

                        bool ok = rest.Count == 0 || CycHasEscape(rest, depth - 1);
                        CycPop(marks);
                        if (ok)
                        {
                            survive.Add(a);
                        }
                    }

                    if (survive.Count > 0)
                    {
                        keep = survive;
                    }
                }

                if (keep.Count == 0)
                {
                    if (log)
                    {
                        Console.WriteLine($"@@CYCMASK deadend n={acts.Count}");
                        foreach (var a in acts)
                        {
                            CycExplain(a);
                        }
                    }

                    throw new InvalidOperationException("cycle deadlock: every candidate closes a disjunctive cycle");
                }

                if (log && keep.Count < acts.Count)
                {
                    Console.WriteLine($"@@CYCMASK excluded={acts.Count - keep.Count}/{acts.Count} (direct={acts.Count - afterDirect})");
                }

                acts = keep;
            }

            return acts;
        }

        bool HasFreeStorageCell(int storageOp)
        {
            for (int g = 0; g < numL; g++)
            {
                if (!locBlocked[g] && !locUsedForStorage[g] && W_a_if[storageOp][g] > 0.5)
                {
                    return true;
                }
            }

            return false;
        }

        bool AmrEligible(int robotOp, int h)
        {
            if (amrCarrying[h].Count > 0 && !amrCarrying[h].Contains(robotOp))
            {
                return false;
            }

            return solution.AmrSequences[h].Count > 0 || W_r_ih[robotOp][h] > 0.5;
        }

        bool DeadlockGuardOn => Environment.GetEnvironmentVariable("PDP_POLICY_DEADLOCK_GUARD") != "0";

        int FreeAmrCount()
        {
            int free = 0;
            for (int h = 0; h < r; h++)
            {
                if (amrCarrying[h].Count == 0)
                {
                    free++;
                }
            }

            return free;
        }

        bool OutboundDemand()
        {
            for (int j = 0; j < m; j++)
            {
                var st = Next(j);
                if (st == Stage.PickupMinus || st == Stage.EnterOutputBuffer)
                {
                    return true;
                }
            }

            return false;
        }

        int UnreservedInputRoom(int k, int except)
        {
            int room = 0;
            for (int b = 0; b < inSlotFree[k].Length; b++)
            {
                if (!double.IsPositiveInfinity(inSlotFree[k][b]))
                {
                    room++;
                }
            }

            for (int j = 0; j < m; j++)
            {
                if (j == except)
                {
                    continue;
                }

                var st = Next(j);
                if (st == Stage.DeliveryPlus && solution.JobWorkstation[j] == k)
                {
                    room--;
                }
                else if (st == Stage.PickupPlus && solution.JobWorkstation[j] < 0 && E_ik[j][k] == 1 && W_n_ik[j][k] > 0.5)
                {
                    room--;
                }
            }

            return room;
        }

        bool HasUnreservedInputRoom(int job)
        {
            int k0 = solution.JobWorkstation[job];
            if (k0 >= 0)
            {
                return UnreservedInputRoom(k0, job) >= 1;
            }

            for (int k = 0; k < n; k++)
            {
                if (E_ik[job][k] == 1 && W_n_ik[job][k] > 0.5 && UnreservedInputRoom(k, job) >= 1)
                {
                    return true;
                }
            }

            return false;
        }

        bool RetrievalGuardBlocks(int job)
        {
            if (!DeadlockGuardOn)
            {
                return false;
            }

            int need = OutboundDemand() ? 2 : 1;
            if (FreeAmrCount() < need)
            {
                return true;
            }

            return !HasUnreservedInputRoom(job);
        }

        bool PickupPlusGuardBlocks(int job)
        {
            if (!DeadlockGuardOn)
            {
                return false;
            }

            if (!HasUnreservedInputRoom(job))
            {
                return true;
            }

            if (OutboundDemand() && FreeAmrCount() <= 1)
            {
                bool craneStarved = asrsAwaitingPickup == job && asrsInPending >= asrsInCap;
                return !craneStarved;
            }

            return false;
        }

        bool LastFreeAmrGuardBlocks(int job)
        {
            int freeAmrs = 0;
            for (int h = 0; h < r; h++)
            {
                if (amrCarrying[h].Count == 0)
                {
                    freeAmrs++;
                }
            }

            if (freeAmrs > 1)
            {
                return false;
            }

            int assigned = solution.JobWorkstation[job];
            if (assigned >= 0)
            {
                return BestSlot(inSlotFree[assigned]) < 0;
            }

            for (int k = 0; k < n; k++)
            {
                if (E_ik[job][k] == 1 && BestSlot(inSlotFree[k]) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        bool TryAdvanceTimePolicy()
        {
            double next = NextPolicyEvent();
            if (next >= double.MaxValue)
            {
                return false;
            }

            t = next;
            return true;
        }

        void AdvanceTimePolicy()
        {
            double next = NextPolicyEvent();
            if (next >= double.MaxValue)
            {
                var pending = new List<string>();
                for (int j = 0; j < m; j++)
                {
                    if (Next(j) != null)
                    {
                        pending.Add("job " + Convert.ToString(j) + "@" + Convert.ToString(Next(j)) + "(ready " + Convert.ToString(Math.Round(readyTime[j], 1)) + ")");
                    }
                }

                throw new InvalidOperationException("Policy rollout stalled at t = " + Convert.ToString(t) + "; no schedulable action remains. Pending: " + string.Join(", ", pending));
            }

            t = next;
        }

        double NextPolicyEvent()
        {
            double next = double.MaxValue;
            for (int j = 0; j < m; j++)
            {
                Stage? stage = Next(j);
                if (stage == null)
                {
                    continue;
                }

                double fire = (stage == Stage.Retrieval || stage == Stage.Storage) ? Math.Max(asrsFreeAt, readyTime[j]) : readyTime[j];
                if (fire > t + Eps && fire < next)
                {
                    next = fire;
                }
            }

            if (asrsFreeAt > t + Eps && asrsFreeAt < next)
            {
                next = asrsFreeAt;
            }

            for (int h = 0; h < r; h++)
            {
                if (amrFreeAt[h] > t + Eps && amrFreeAt[h] < next)
                {
                    next = amrFreeAt[h];
                }
            }

            return next;
        }

        void ExecuteStorage(int job)
        {
            int op = m + job;
            int bestLoc = -1;
            double bestHandling = double.MaxValue;
            for (int g = 0; g < numL; g++)
            {
                if (locBlocked[g] || locUsedForStorage[g] || W_a_if[op][g] < 0.5)
                {
                    continue;
                }

                double handling = r_ijfg[asrsPrevOp][op][asrsPrevLoc][g];
                if (handling < bestHandling - Eps)
                {
                    bestHandling = handling;
                    bestLoc = g;
                }
            }

            double start = Math.Max(t, readyTime[job]);
            double completion = start + bestHandling;
            solution.AsrsSequence.Add(op);
            solution.AsrsLocation[op] = bestLoc;
            locUsedForStorage[bestLoc] = true;
            commitSink?.Invoke("sto", job, "crane", completion);
            asrsFreeAt = completion;
            asrsPrevOp = op;
            asrsPrevLoc = bestLoc;
            if (asrsInPending > 0)
            {
                asrsInPending--;
            }

            nextStage[job]++;
            readyTime[job] = completion;
        }

        void ExecuteRetrieval(int job)
        {
            int loc = retrievalLoc[job];
            double handling = r_ijfg[asrsPrevOp][job][asrsPrevLoc][loc];
            double completion = Math.Max(t + handling, asrsBlockLb);
            solution.AsrsSequence.Add(job);
            solution.AsrsLocation[job] = loc;
            locBlocked[loc] = false;
            commitSink?.Invoke("ret", job, "crane", completion);
            asrsFreeAt = completion;
            asrsPrevOp = job;
            asrsPrevLoc = loc;
            asrsBlockLb = 0.0;
            lastRetrievalJob = job;
            nextStage[job]++;
            readyTime[job] = completion;
            if (u_rob_i[job] > 0.5)
            {
                asrsAwaitingPickup = job;
            }
        }

        void ExecuteReturnTransport(int job, int amr)
        {
            int ws = solution.JobWorkstation[job];
            if (ws < 0)
            {
                ws = ChooseWorkstation(job);
                solution.JobWorkstation[job] = ws;
            }

            int pickupOp = 2 * m + job;
            double crPickup = ScheduleRobotOperation(pickupOp, amr, readyTime[job]);
            commitSink?.Invoke("pminus", job, "amr" + Convert.ToString(amr), crPickup);
            nextStage[job]++;
            readyTime[job] = crPickup;
            if (solution.OutputSlot[job] >= 0)
            {
                outSlotFree[ws][solution.OutputSlot[job]] = crPickup;
            }

            if (outLaneGen[ws].Count > 0 && outLaneGen[ws][0] == job)
            {
                outLaneGen[ws].RemoveAt(0);
            }

            if (Next(job) == Stage.DeliveryMinus)
            {
                double crDelivery = ScheduleRobotOperation(3 * m + job, amr, crPickup);
                commitSink?.Invoke("dminus", job, "amr" + Convert.ToString(amr), crDelivery);
                asrsInPending++;
                nextStage[job]++;
                readyTime[job] = crDelivery;
            }
            else if (u_rob_i[3 * m + job] > 0.5)
            {
                amrCarrying[amr].Add(3 * m + job);
            }
        }

        bool ExecutePickupPlus(int job, int amr, Func<List<PolicyAction>, int> choose)
        {
            if (solution.JobWorkstation[job] < 0 && (u_rob_i[m + job] > 0.5 || u_ws_i[job] > 0.5))
            {
                var wsActs = new List<PolicyAction>();
                for (int k = 0; k < n; k++)
                {
                    if (E_ik[job][k] == 1 && W_n_ik[job][k] > 0.5)
                    {
                        wsActs.Add(new PolicyAction("proc", job, k));
                    }
                }

                int chosenWs = wsActs.Count == 1 ? wsActs[0].AmrOrWs : ProbeAndChoose(choose, wsActs);
                if (chosenWs < 0)
                {
                    return false;
                }

                solution.JobWorkstation[job] = chosenWs;
            }

            double crPickup = ScheduleRobotOperation(job, amr, readyTime[job]);
            commitSink?.Invoke("pplus", job, "amr" + Convert.ToString(amr), crPickup);
            nextStage[job]++;
            readyTime[job] = crPickup;
            if (u_rob_i[m + job] > 0.5)
            {
                amrCarrying[amr].Add(m + job);
            }

            if (lastRetrievalJob == job)
            {
                asrsBlockLb = Math.Max(asrsBlockLb, crPickup);
            }

            if (asrsAwaitingPickup == job)
            {
                asrsAwaitingPickup = -1;
            }

            return true;
        }

        double ScheduleRobotOperation(int robotOp, int h, double release)
        {
            int node = RobotOpNode(robotOp);
            if (node < 0)
            {
                throw new InvalidOperationException("Robot operation " + Convert.ToString(robotOp) + " has no target workstation.");
            }

            double arrival = amrFreeAt[h] + t_klh[amrNode[h]][node][h];
            double start = Math.Max(arrival, release);
            double completion = start + s_ikh[robotOp][node][h];
            solution.AmrSequences[h].Add(robotOp);
            solution.RobotOpAmr[robotOp] = h;
            amrFreeAt[h] = completion;
            amrNode[h] = node;
            return completion;
        }

        void AdvanceTime()
        {
            double next = double.MaxValue;
            for (int j = 0; j < m; j++)
            {
                Stage? stage = Next(j);
                if (stage == null)
                {
                    continue;
                }

                double fire;
                if (stage == Stage.Retrieval || stage == Stage.Storage)
                {
                    fire = Math.Max(asrsFreeAt, readyTime[j]);
                }
                else if (stage == Stage.PickupMinus || stage == Stage.PickupPlus || stage == Stage.DeliveryPlus || stage == Stage.Process || stage == Stage.DeliveryMinus)
                {
                    fire = readyTime[j];
                }
                else
                {
                    continue;
                }

                if (fire > t + Eps && fire < next)
                {
                    next = fire;
                }
            }

            if (next >= double.MaxValue)
            {
                var pending = new List<string>();
                for (int j = 0; j < m; j++)
                {
                    Stage? stage = Next(j);
                    if (stage != null)
                    {
                        pending.Add("job " + Convert.ToString(j) + "@" + Convert.ToString(stage) + "(ready " + Convert.ToString(Math.Round(readyTime[j], 1)) + ")");
                    }
                }

                int blocked = 0, used = 0, free = 0;
                for (int g = 0; g < numL; g++)
                {
                    if (locBlocked[g])
                    {
                        blocked++;
                    }
                    else if (locUsedForStorage[g])
                    {
                        used++;
                    }
                    else
                    {
                        free++;
                    }
                }

                int needStorage = 0;
                var eligibleFree = new List<string>();
                for (int j = 0; j < m; j++)
                {
                    if (Next(j) != Stage.Storage)
                    {
                        continue;
                    }

                    needStorage++;
                    int cnt = 0;
                    for (int g = 0; g < numL; g++)
                    {
                        if (!locBlocked[g] && !locUsedForStorage[g] && W_a_if[m + j][g] > 0.5)
                        {
                            cnt++;
                        }
                    }

                    eligibleFree.Add("job " + Convert.ToString(j) + ":" + Convert.ToString(cnt));
                }

                throw new InvalidOperationException("Initial solution generation stalled at t = " + Convert.ToString(t) + "; no schedulable operation remains." + " Storage cells: " + Convert.ToString(numL) + " total = " + Convert.ToString(blocked) + " awaiting retrieval + " + Convert.ToString(used) + " already assigned + " + Convert.ToString(free) + " free(any); " + Convert.ToString(needStorage) + " job(s) still need a cell." + " Eligible free cells per waiting job [" + string.Join(", ", eligibleFree) + "]." + " Pending: " + string.Join(", ", pending));
            }

            t = next;
        }
    }
}

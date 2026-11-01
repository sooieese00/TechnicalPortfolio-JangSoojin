using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PDP_ASRS_WIP
{
    public partial class VNS
    {
        const int VndOperatorCount = 9;
        static readonly string[] VndOperatorNames =
        {
            "AsrsInsertion",
            "AsrsSwap",
            "WsInsertion",
            "WsSwap",
            "CrossWsInsertion",
            "AmrInsertion",
            "AmrSwap",
            "AmrPairMove",
            "StorageLocationChange"
        };
        const int ParallelMinCandidates = 32;
        bool[]? machineWipFirstJob;
        void EnsureWipFlags()
        {
            if (machineWipFirstJob != null)
            {
                return;
            }

            var flags = new bool[m];
            for (int i = 0; i < m; i++)
            {
                bool initialIn = false;
                for (int k = 0; k < n && !initialIn; k++)
                {
                    for (int b = 0; b < b_in_k[k]; b++)
                    {
                        if (W_in_ikb[i][k][b] > 0.5)
                        {
                            initialIn = true;
                            break;
                        }
                    }
                }

                flags[i] = u_ws_i[i] > 0.5 && u_rob_i[m + i] < 0.5 && !initialIn;
            }

            machineWipFirstJob = flags;
        }

        readonly struct VndMove
        {
            public readonly byte Kind;
            public readonly int A, B, C, D;
            public VndMove(byte kind, int a, int b, int c = 0, int d = 0)
            {
                Kind = kind;
                A = a;
                B = b;
                C = c;
                D = d;
            }
        }

        bool RunVnd(ref SequenceSolution solution, ref DecodedSchedule schedule, int maxEvaluations)
        {
            EnsureWipFlags();
            bool anyImprovement = false;
            var moves = new List<VndMove>();
            while (result.Evaluations < maxEvaluations)
            {
                bool improved = false;
                for (int op = 0; op < VndOperatorCount; op++)
                {
                    if (result.Evaluations >= maxEvaluations)
                    {
                        return anyImprovement;
                    }

                    moves.Clear();
                    Enumerate(op, solution, moves);
                    if (moves.Count == 0)
                    {
                        continue;
                    }

                    if (ScanFirstImprovement(solution, schedule.TotalTardiness, schedule.Makespan, moves, out SequenceSolution better, out DecodedSchedule betterSchedule))
                    {
                        solution = better;
                        schedule = betterSchedule;
                        improved = true;
                        anyImprovement = true;
                        break;
                    }
                }

                if (!improved)
                {
                    return anyImprovement;
                }
            }

            return anyImprovement;
        }

        bool ScanFirstImprovement(SequenceSolution current, double curObj, double curMk, List<VndMove> moves, out SequenceSolution improved, out DecodedSchedule improvedSchedule)
        {
            improved = current;
            improvedSchedule = new DecodedSchedule();
            if (parallelDegree <= 1 || moves.Count < ParallelMinCandidates)
            {
                for (int index = 0; index < moves.Count; index++)
                {
                    SequenceSolution? candidate = Materialize(current, moves[index]);
                    if (candidate == null)
                    {
                        continue;
                    }

                    result.Evaluations++;
                    DecodedSchedule? candidateSchedule = EvaluateCore(candidate);
                    if (candidateSchedule != null && Better(candidateSchedule.TotalTardiness, candidateSchedule.Makespan, curObj, curMk))
                    {
                        improved = candidate;
                        improvedSchedule = candidateSchedule;
                        return true;
                    }
                }

                return false;
            }

            int blockSize = Math.Max(parallelDegree, 4 * parallelDegree);
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelDegree
            };
            for (int start = 0; start < moves.Count; start += blockSize)
            {
                int end = Math.Min(start + blockSize, moves.Count);
                var slotSolution = new SequenceSolution? [end - start];
                var slotSchedule = new DecodedSchedule? [end - start];
                int earliest = int.MaxValue;
                int evaluated = 0;
                Parallel.For(start, end, options, index =>
                {
                    if (Volatile.Read(ref earliest) < index)
                    {
                        return;
                    }

                    SequenceSolution? candidate = Materialize(current, moves[index]);
                    if (candidate == null)
                    {
                        return;
                    }

                    Interlocked.Increment(ref evaluated);
                    DecodedSchedule? candidateSchedule = EvaluateCore(candidate);
                    if (candidateSchedule != null && Better(candidateSchedule.TotalTardiness, candidateSchedule.Makespan, curObj, curMk))
                    {
                        slotSolution[index - start] = candidate;
                        slotSchedule[index - start] = candidateSchedule;
                        InterlockedMin(ref earliest, index);
                    }
                });
                result.Evaluations += evaluated;
                for (int index = start; index < end; index++)
                {
                    if (slotSolution[index - start] != null)
                    {
                        improved = slotSolution[index - start]!;
                        improvedSchedule = slotSchedule[index - start]!;
                        return true;
                    }
                }
            }

            return false;
        }

        static void InterlockedMin(ref int target, int value)
        {
            int current = Volatile.Read(ref target);
            while (value < current)
            {
                int observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }

        void Enumerate(int op, SequenceSolution s, List<VndMove> moves)
        {
            switch (op)
            {
                case 0:
                    EnumAsrsInsert(s, moves);
                    break;
                case 1:
                    EnumAsrsSwap(s, moves);
                    break;
                case 2:
                    EnumWsInsert(s, moves);
                    break;
                case 3:
                    EnumWsSwap(s, moves);
                    break;
                case 4:
                    EnumCrossWsInsert(s, moves);
                    break;
                case 5:
                    EnumAmrInsert(s, moves);
                    break;
                case 6:
                    EnumAmrSwap(s, moves);
                    break;
                case 7:
                    EnumAmrPairMove(s, moves);
                    break;
                case 8:
                    EnumStorageLocation(s, moves);
                    break;
            }
        }

        void EnumAsrsInsert(SequenceSolution s, List<VndMove> moves)
        {
            int count = s.AsrsSequence.Count;
            for (int from = pinnedAsrsPrefix; from < count; from++)
            {
                for (int to = pinnedAsrsPrefix; to < count; to++)
                {
                    if (to != from)
                    {
                        moves.Add(new VndMove(0, from, to));
                    }
                }
            }
        }

        void EnumAsrsSwap(SequenceSolution s, List<VndMove> moves)
        {
            int count = s.AsrsSequence.Count;
            for (int a = pinnedAsrsPrefix; a < count - 1; a++)
            {
                for (int b = a + 1; b < count; b++)
                {
                    moves.Add(new VndMove(1, a, b));
                }
            }
        }

        void EnumWsInsert(SequenceSolution s, List<VndMove> moves)
        {
            for (int k = 0; k < n; k++)
            {
                var seq = s.WorkstationSequences[k];
                int lowest = FirstMovablePosition(seq);
                for (int from = lowest; from < seq.Count; from++)
                {
                    for (int to = lowest; to < seq.Count; to++)
                    {
                        if (to != from)
                        {
                            moves.Add(new VndMove(2, k, from, to));
                        }
                    }
                }
            }
        }

        void EnumWsSwap(SequenceSolution s, List<VndMove> moves)
        {
            for (int k = 0; k < n; k++)
            {
                var seq = s.WorkstationSequences[k];
                int lowest = FirstMovablePosition(seq);
                for (int a = lowest; a < seq.Count - 1; a++)
                {
                    for (int b = a + 1; b < seq.Count; b++)
                    {
                        moves.Add(new VndMove(3, k, a, b));
                    }
                }
            }
        }

        int FirstMovablePosition(List<int> sequence)
        {
            if (sequence.Count > 0 && machineWipFirstJob![sequence[0]])
            {
                return 1;
            }

            return 0;
        }

        void EnumCrossWsInsert(SequenceSolution s, List<VndMove> moves)
        {
            for (int i = 0; i < m; i++)
            {
                if (u_ws_i[i] < 0.5 || u_rob_i[m + i] < 0.5)
                {
                    continue;
                }

                int oldWs = s.JobWorkstation[i];
                for (int k = 0; k < n; k++)
                {
                    if (k == oldWs || E_ik[i][k] != 1 || W_n_ik[i][k] < 0.5 && s.WorkstationSequences[k].Count == 0)
                    {
                        continue;
                    }

                    var target = s.WorkstationSequences[k];
                    int lowest = FirstMovablePosition(target);
                    for (int to = lowest; to <= target.Count; to++)
                    {
                        moves.Add(new VndMove(4, i, k, to));
                    }
                }
            }
        }

        void EnumAmrInsert(SequenceSolution s, List<VndMove> moves)
        {
            for (int h = 0; h < r; h++)
            {
                int count = s.AmrSequences[h].Count;
                for (int from = 0; from < count; from++)
                {
                    for (int to = 0; to < count; to++)
                    {
                        if (to != from)
                        {
                            moves.Add(new VndMove(5, h, from, to));
                        }
                    }
                }
            }
        }

        void EnumAmrSwap(SequenceSolution s, List<VndMove> moves)
        {
            for (int h = 0; h < r; h++)
            {
                int count = s.AmrSequences[h].Count;
                for (int a = 0; a < count - 1; a++)
                {
                    for (int b = a + 1; b < count; b++)
                    {
                        moves.Add(new VndMove(6, h, a, b));
                    }
                }
            }
        }

        void EnumAmrPairMove(SequenceSolution s, List<VndMove> moves)
        {
            for (int i = 0; i < m; i++)
            {
                for (int type = 0; type < 2; type++)
                {
                    int pickupOp = type == 0 ? i : 2 * m + i;
                    int deliveryOp = pickupOp + m;
                    if (u_rob_i[pickupOp] < 0.5 || u_rob_i[deliveryOp] < 0.5 || s.RobotOpAmr[pickupOp] < 0)
                    {
                        continue;
                    }

                    int oldAmr = s.RobotOpAmr[pickupOp];
                    for (int h = 0; h < r; h++)
                    {
                        if (W_r_ih[pickupOp][h] < 0.5 || W_r_ih[deliveryOp][h] < 0.5)
                        {
                            continue;
                        }

                        int slots = s.AmrSequences[h].Count - (h == oldAmr ? 2 : 0);
                        for (int pos = 0; pos <= slots; pos++)
                        {
                            moves.Add(new VndMove(7, i * 2 + type, h, pos));
                        }
                    }
                }
            }
        }

        void EnumStorageLocation(SequenceSolution s, List<VndMove> moves)
        {
            var used = new bool[numL];
            for (int idx = 0; idx < s.AsrsSequence.Count; idx++)
            {
                int op = s.AsrsSequence[idx];
                if (op >= m && s.AsrsLocation[op] >= 0)
                {
                    used[s.AsrsLocation[op]] = true;
                }
            }

            for (int idx = pinnedAsrsPrefix; idx < s.AsrsSequence.Count; idx++)
            {
                int op = s.AsrsSequence[idx];
                if (op < m)
                {
                    continue;
                }

                int currentLoc = s.AsrsLocation[op];
                for (int g = 0; g < numL; g++)
                {
                    if (g != currentLoc && !used[g] && W_a_if[op][g] > 0.5)
                    {
                        moves.Add(new VndMove(8, op, g));
                    }
                }
            }
        }

        SequenceSolution? Materialize(SequenceSolution baseSolution, VndMove move)
        {
            SequenceSolution s = baseSolution.Clone();
            switch (move.Kind)
            {
                case 0:
                {
                    var seq = s.AsrsSequence;
                    if (move.A >= seq.Count || move.B >= seq.Count)
                    {
                        return null;
                    }

                    int op = seq[move.A];
                    seq.RemoveAt(move.A);
                    seq.Insert(move.B, op);
                    return s;
                }

                case 1:
                {
                    var seq = s.AsrsSequence;
                    if (move.A >= seq.Count || move.B >= seq.Count)
                    {
                        return null;
                    }

                    (seq[move.A], seq[move.B]) = (seq[move.B], seq[move.A]);
                    return s;
                }

                case 2:
                {
                    var seq = s.WorkstationSequences[move.A];
                    if (move.B >= seq.Count || move.C >= seq.Count)
                    {
                        return null;
                    }

                    int job = seq[move.B];
                    seq.RemoveAt(move.B);
                    seq.Insert(move.C, job);
                    return s;
                }

                case 3:
                {
                    var seq = s.WorkstationSequences[move.A];
                    if (move.B >= seq.Count || move.C >= seq.Count)
                    {
                        return null;
                    }

                    (seq[move.B], seq[move.C]) = (seq[move.C], seq[move.B]);
                    return s;
                }

                case 4:
                {
                    int job = move.A;
                    int targetWs = move.B;
                    int oldWs = s.JobWorkstation[job];
                    if (oldWs < 0 || !s.WorkstationSequences[oldWs].Remove(job))
                    {
                        return null;
                    }

                    var target = s.WorkstationSequences[targetWs];
                    int at = Math.Min(move.C, target.Count);
                    target.Insert(at, job);
                    s.JobWorkstation[job] = targetWs;
                    s.InputSlot[job] = -1;
                    s.OutputSlot[job] = -1;
                    return s;
                }

                case 5:
                {
                    var route = s.AmrSequences[move.A];
                    if (move.B >= route.Count || move.C >= route.Count)
                    {
                        return null;
                    }

                    int op = route[move.B];
                    route.RemoveAt(move.B);
                    route.Insert(move.C, op);
                    return PairsOrdered(s, move.A) ? s : null;
                }

                case 6:
                {
                    var route = s.AmrSequences[move.A];
                    if (move.B >= route.Count || move.C >= route.Count)
                    {
                        return null;
                    }

                    (route[move.B], route[move.C]) = (route[move.C], route[move.B]);
                    return PairsOrdered(s, move.A) ? s : null;
                }

                case 7:
                {
                    int job = move.A / 2;
                    int type = move.A % 2;
                    int pickupOp = type == 0 ? job : 2 * m + job;
                    int deliveryOp = pickupOp + m;
                    int oldAmr = s.RobotOpAmr[pickupOp];
                    if (oldAmr < 0)
                    {
                        return null;
                    }

                    s.AmrSequences[oldAmr].Remove(pickupOp);
                    s.AmrSequences[oldAmr].Remove(deliveryOp);
                    var route = s.AmrSequences[move.B];
                    int at = Math.Min(move.C, route.Count);
                    route.Insert(at, pickupOp);
                    route.Insert(at + 1, deliveryOp);
                    s.RobotOpAmr[pickupOp] = move.B;
                    s.RobotOpAmr[deliveryOp] = move.B;
                    if (!PairsOrdered(s, move.B) || (oldAmr != move.B && !PairsOrdered(s, oldAmr)))
                    {
                        return null;
                    }

                    return s;
                }

                case 8:
                {
                    s.AsrsLocation[move.A] = move.B;
                    return s;
                }

                default:
                    return null;
            }
        }
    }
}

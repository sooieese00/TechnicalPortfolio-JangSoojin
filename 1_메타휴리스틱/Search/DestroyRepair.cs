using System;
using System.Collections.Generic;

namespace PDP_ASRS_WIP
{
    public static partial class BufferMetaheuristics
    {
        public sealed class DestroyRepairKit
        {
            readonly Context ctx;
            readonly Random rng;
            readonly int m;
            public string LastEvalFailure => ctx.LastEvalFailure;

            readonly bool prefixEval;
            public DestroyRepairKit(ScenarioInstance inst, int seed, int maxEvaluations, bool lexicographic = true, RuleLog? log = null, bool? prefixEval = null)
            {
                ctx = new Context(inst, seed, maxEvaluations, log);
                ctx.LexicographicRepair = lexicographic;
                rng = new Random(seed * 7919 + 13);
                m = inst.m;
                this.prefixEval = prefixEval ?? string.Equals(Environment.GetEnvironmentVariable("PDP_REPAIR_EVAL"), "prefix", StringComparison.OrdinalIgnoreCase);
            }

            public bool PrefixEval => prefixEval;
            public int DestroyableCount => ctx.Destroyable.Count;
            public int Evaluations => ctx.Result.Evaluations;

            public int DrawScope()
            {
                int hi = Math.Max(1, Math.Min((int)(0.4 * m), m - 1));
                hi = Math.Min(hi, Math.Max(1, ctx.Destroyable.Count));
                return 1 + rng.Next(hi);
            }

            public int[] SelectJobs(int q, SequenceSolution current, DecodedSchedule schedule)
            {
                return rng.Next(2) == 0 ? ctx.DestroyRandom(q) : ctx.DestroyWorst(q, current, schedule);
            }

            public SequenceSolution Remove(SequenceSolution current, int[] jobs)
            {
                SequenceSolution destroyed = current.Clone();
                foreach (int j in jobs)
                {
                    ctx.RemoveJob(destroyed, j);
                }

                return destroyed;
            }

            public (SequenceSolution sol, DecodedSchedule sch)? Repair(SequenceSolution destroyed, int[] jobs)
            {
                return prefixEval ? ctx.RepairPrefix(destroyed, jobs) : ctx.Repair(destroyed, jobs);
            }

            public DecodedSchedule? Evaluate(SequenceSolution solution)
            {
                return ctx.Evaluate(solution);
            }
        }
    }
}

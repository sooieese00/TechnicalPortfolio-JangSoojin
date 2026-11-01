using System;

namespace PDP_ASRS_WIP
{
    public static class Lns
    {
        const double Eps = 1e-6;
        public static VnsResult Run(SequenceSolution initial, ScenarioInstance inst, int seed, int maxEvaluations, int noImprovementLimit, RuleLog? log = null)
        {
            var kit = new BufferMetaheuristics.DestroyRepairKit(inst, seed, maxEvaluations, lexicographic: true, log: log);
            VNS engine = VNS.CreateEngine(inst, seed + 1, RunSettings.EffectiveParallelism, localSearch: "vnd");
            var result = new VnsResult();
            DecodedSchedule? curSch = kit.Evaluate(initial);
            if (curSch == null)
            {
                result.Log.Add("LNS: 초기해가 실행불가 - 탐색 중단.");
                return result;
            }

            SequenceSolution current = initial.Clone();
            result.BestSolution = current.Clone();
            result.BestSchedule = curSch;
            result.BestObjective = curSch.TotalTardiness;
            result.BestMakespan = curSch.Makespan;
            engine.Descend(ref current, ref curSch, maxEvaluations);
            Record(result, current, curSch);
            int sinceBest = 0;
            bool BudgetLeft() => kit.Evaluations + engine.EngineEvaluations < maxEvaluations;
            while (BudgetLeft() && (noImprovementLimit <= 0 || sinceBest < noImprovementLimit))
            {
                result.Iterations++;
                sinceBest++;
                int[] jobs = kit.SelectJobs(kit.DrawScope(), current, curSch);
                if (jobs.Length == 0)
                {
                    continue;
                }

                var repaired = kit.Repair(kit.Remove(current, jobs), jobs);
                if (repaired == null)
                {
                    continue;
                }

                SequenceSolution candidate = repaired.Value.sol;
                DecodedSchedule? candSch = kit.Evaluate(candidate);
                if (candSch == null)
                {
                    continue;
                }

                engine.Descend(ref candidate, ref candSch, maxEvaluations);
                if (candSch.TotalTardiness <= curSch.TotalTardiness + Eps)
                {
                    current = candidate;
                    curSch = candSch;
                }

                if (candSch.TotalTardiness < result.BestObjective - Eps)
                {
                    Record(result, candidate, candSch);
                    sinceBest = 0;
                }
            }

            result.Evaluations = kit.Evaluations + engine.EngineEvaluations;
            result.StopReason = BudgetLeft() ? "no-improvement limit" : "evaluation budget";
            return result;
        }

        static void Record(VnsResult result, SequenceSolution sol, DecodedSchedule sch)
        {
            result.BestSolution = sol.Clone();
            result.BestSchedule = sch;
            result.BestObjective = sch.TotalTardiness;
            result.BestMakespan = sch.Makespan;
            result.AcceptedImprovements++;
            result.Log.Add("LNS improvement: tardiness=" + Math.Round(sch.TotalTardiness, 4) + ", makespan=" + Math.Round(sch.Makespan, 4));
        }
    }
}

using System;

namespace PDP_ASRS_WIP
{
    public static class Hyb
    {
        const double Eps = 1e-6;
        const double Alpha = 0.8;
        const double Beta = 0.8;
        const double PMin = 0.1;
        const double PMax = 1.0 - PMin;
        public static VnsResult Run(SequenceSolution initial, ScenarioInstance inst, int seed, int maxEvaluations, int noImprovementLimit, RuleLog? log = null)
        {
            var rng = new Random(seed);
            var kit = new BufferMetaheuristics.DestroyRepairKit(inst, seed, maxEvaluations, lexicographic: true, log: log);
            VNS engine = VNS.CreateEngine(inst, seed + 1, RunSettings.EffectiveParallelism, localSearch: "vnd");
            var result = new VnsResult();
            DecodedSchedule? curSch = kit.Evaluate(initial);
            if (curSch == null)
            {
                result.Log.Add("HYB: 초기해가 실행불가 - 탐색 중단. 사유: " + (kit.LastEvalFailure.Length > 0 ? kit.LastEvalFailure : "알 수 없음"));
                return result;
            }

            SequenceSolution current = initial.Clone();
            result.BestSolution = current.Clone();
            result.BestSchedule = curSch;
            result.BestObjective = curSch.TotalTardiness;
            result.BestMakespan = curSch.Makespan;
            engine.Descend(ref current, ref curSch, maxEvaluations);
            RecordBest(result, current, curSch, "initial descent");
            double qDestroy = 0.0, qShake = 0.0;
            double pDestroy = 0.5;
            int k = 1;
            int sinceBest = 0;
            int destroyUsed = 0, shakeUsed = 0, tieSkipped = 0, failed = 0;
            bool BudgetLeft() => kit.Evaluations + engine.EngineEvaluations < maxEvaluations;
            while (BudgetLeft() && (noImprovementLimit <= 0 || sinceBest < noImprovementLimit))
            {
                result.Iterations++;
                sinceBest++;
                bool useDestroy = rng.NextDouble() < pDestroy;
                SequenceSolution? candidate = null;
                DecodedSchedule? candSch = null;
                if (useDestroy)
                {
                    destroyUsed++;
                    int q = kit.DrawScope();
                    int[] jobs = kit.SelectJobs(q, current, curSch);
                    if (jobs.Length > 0)
                    {
                        var repaired = kit.Repair(kit.Remove(current, jobs), jobs);
                        if (repaired != null)
                        {
                            candidate = repaired.Value.sol;
                            candSch = kit.Evaluate(candidate);
                        }
                    }
                }
                else
                {
                    shakeUsed++;
                    var shaken = engine.TryShakeWalk(current, k);
                    if (shaken != null)
                    {
                        candidate = shaken.Value.sol;
                        candSch = shaken.Value.sch;
                    }
                }

                if (candidate == null || candSch == null)
                {
                    failed++;
                    UpdatePursuit(ref qDestroy, ref qShake, ref pDestroy, useDestroy, 0.0);
                    if (!useDestroy)
                    {
                        k = k % VNS.ShakeNeighbourhoods + 1;
                    }

                    continue;
                }

                engine.Descend(ref candidate, ref candSch, maxEvaluations);
                double delta = curSch.TotalTardiness - candSch.TotalTardiness;
                bool isTie = Math.Abs(delta) <= Eps;
                if (delta >= -Eps)
                {
                    current = candidate;
                    curSch = candSch;
                }

                if (candSch.TotalTardiness < result.BestObjective - Eps || (candSch.TotalTardiness <= result.BestObjective + Eps && candSch.Makespan < result.BestMakespan - Eps))
                {
                    bool strictly = candSch.TotalTardiness < result.BestObjective - Eps;
                    RecordBest(result, candidate, candSch, useDestroy ? "destroy" : "shake k=" + k);
                    if (strictly)
                    {
                        sinceBest = 0;
                    }
                }

                if (isTie)
                {
                    tieSkipped++;
                }
                else
                {
                    UpdatePursuit(ref qDestroy, ref qShake, ref pDestroy, useDestroy, Math.Max(0.0, delta));
                }

                if (!useDestroy)
                {
                    k = delta > Eps ? 1 : k % VNS.ShakeNeighbourhoods + 1;
                }
            }

            result.Evaluations = kit.Evaluations + engine.EngineEvaluations;
            result.StopReason = BudgetLeft() ? "no-improvement limit" : "evaluation budget";
            result.Log.Add($"HYB: iterations={result.Iterations}, destroy/shake={destroyUsed}/{shakeUsed}, " + $"tie-skip={tieSkipped}, infeasible={failed}, p(destroy)={Math.Round(pDestroy, 3)}");
            return result;
        }

        static void UpdatePursuit(ref double qDestroy, ref double qShake, ref double pDestroy, bool usedDestroy, double reward)
        {
            if (usedDestroy)
            {
                qDestroy += Alpha * (reward - qDestroy);
            }
            else
            {
                qShake += Alpha * (reward - qShake);
            }

            double target = qDestroy >= qShake ? PMax : PMin;
            pDestroy += Beta * (target - pDestroy);
            pDestroy = Math.Clamp(pDestroy, PMin, PMax);
        }

        static void RecordBest(VnsResult result, SequenceSolution sol, DecodedSchedule sch, string source)
        {
            result.BestSolution = sol.Clone();
            result.BestSchedule = sch;
            result.BestObjective = sch.TotalTardiness;
            result.BestMakespan = sch.Makespan;
            result.AcceptedImprovements++;
            result.Log.Add("HYB improvement (" + source + "): tardiness=" + Math.Round(sch.TotalTardiness, 4) + ", makespan=" + Math.Round(sch.Makespan, 4));
        }
    }
}

using System;

namespace PDP_ASRS_WIP
{
    public partial class VNS
    {
        public const int ShakeNeighbourhoods = 7;
        public static VNS CreateEngine(ScenarioInstance inst, int seed, int parallelDegree, string localSearch = "vnd")
        {
            var engine = new VNS(seed, inst.numL, inst.r, inst.n, inst.m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb);
            engine.pinnedAsrsPrefix = inst.AsrsPinnedPrefix.Length;
            engine.pinnedAsrsOps = inst.AsrsPinnedPrefix;
            engine.machineReadyAt = inst.MachineReadyAt;
            engine.layout = inst.Layout;
            engine.asrsOutCap = inst.AsrsOutBufferCapacity;
            engine.asrsInCap = inst.AsrsInBufferCapacity;
            engine.parallelDegree = Math.Max(1, parallelDegree);
            engine.localSearchMode = localSearch;
            return engine;
        }

        public int EngineEvaluations => result.Evaluations;

        public DecodedSchedule? EvaluateSolution(SequenceSolution solution)
        {
            return Evaluate(solution);
        }

        public bool Descend(ref SequenceSolution solution, ref DecodedSchedule schedule, int maxEvaluations)
        {
            return RunVnd(ref solution, ref schedule, maxEvaluations);
        }

        public (SequenceSolution sol, DecodedSchedule sch)? TryShakeWalk(SequenceSolution current, int k, int attempts = 30)
        {
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                SequenceSolution trial = current.Clone();
                bool walked = true;
                for (int step = 0; step < k; step++)
                {
                    if (!ApplyMove(trial, 1 + rng.Next(ShakeNeighbourhoods)))
                    {
                        walked = false;
                        break;
                    }
                }

                if (!walked)
                {
                    continue;
                }

                DecodedSchedule? schedule = Evaluate(trial);
                if (schedule != null)
                {
                    return (trial, schedule);
                }
            }

            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    internal partial class Program
    {
        static int RunDesRollout(string scenarioPath, string? configPath)
        {
            ScenarioInstance inst = ScenarioLoader.Load(scenarioPath, configPath);
            string which = Environment.GetEnvironmentVariable("PDP_DES_POLICY") ?? "all";
            string[] policies = which == "all" ? new[]
            {
                "edd",
                "spt",
                "random"
            }

            : new[]
            {
                which
            };
            Console.WriteLine("DES ROLLOUT  scenario=" + System.IO.Path.GetFileName(scenarioPath));
            foreach (string policy in policies)
            {
                var rng = new Random(0);
                Action<string, int, string, double>? sink = null;
                Func<List<PolicyAction>, int> chooser;
                if (policy == "extern")
                {
                    sink = (kind, job, res, done) =>
                    {
                        Console.WriteLine("@@" + JsonSerializer.Serialize(new { t = "commit", kind, job, res, e = Math.Round(done, 3) }));
                        Console.Out.Flush();
                    };
                    chooser = acts =>
                    {
                        var cands = acts.Select(a => new { kind = a.Kind, job = a.Job, res = a.Res }).ToArray();
                        var pr = InitialSolutionGenerator.LastProbe;
                        object? free = pr == null ? null : new
                        {
                            crane = Math.Round(pr.AsrsFreeAt, 3),
                            amr = pr.AmrFreeAt.Select(v => Math.Round(v, 3)).ToArray(),
                            ws = pr.MachineAvail.Select(v => double.IsPositiveInfinity(v) ? (double? )null : Math.Round(v, 3)).ToArray()
                        };
                        Console.WriteLine("@@" + JsonSerializer.Serialize(new { t = "choose", now = pr == null ? (double? )null : Math.Round(pr.Time, 3), free, cands }));
                        Console.Out.Flush();
                        string? line = Console.In.ReadLine();
                        if (line == null)
                        {
                            throw new InvalidOperationException("extern policy: stdin closed while awaiting a choice.");
                        }

                        int idx = int.Parse(line.Trim());
                        if (idx < -1 || idx >= acts.Count)
                        {
                            throw new InvalidOperationException($"extern policy: index {idx} out of range -1..{acts.Count - 1}.");
                        }

                        return idx;
                    };
                }
                else
                {
                    chooser = acts =>
                    {
                        if (policy == "random")
                        {
                            return rng.Next(acts.Count);
                        }

                        int best = 0;
                        double bestKey = double.MaxValue;
                        for (int i = 0; i < acts.Count; i++)
                        {
                            double key = policy == "spt" ? SptKey(inst, acts[i]) : inst.d_i[acts[i].Job];
                            if (key < bestKey - 1e-9)
                            {
                                bestKey = key;
                                best = i;
                            }
                        }

                        return best;
                    };
                }

                int episodes = 1;
                if (policy == "extern" && int.TryParse(Environment.GetEnvironmentVariable("PDP_DES_EPISODES"), out int epEnv))
                {
                    episodes = Math.Max(1, epEnv);
                }

                for (int ep = 0; ep < episodes; ep++)
                    try
                    {
                        SequenceSolution sol = InitialSolutionGenerator.GeneratePolicy(inst.numL, inst.r, inst.n, inst.m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, chooser, inst.AsrsPinnedPrefix, inst.MachineReadyAt, inst.Layout, inst.AsrsInBufferCapacity, sink, policyOwnsPinnedTransport: true, policySequenceMode: Environment.GetEnvironmentVariable("PDP_DES_SEQMODE") != "0");
                        DecodedSchedule decoded = SolutionDecoder.Decode(sol, inst.numL, inst.r, inst.n, inst.m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: false, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length, asrsPinnedPrefix: inst.AsrsPinnedPrefix);
                        if (policy == "extern")
                        {
                            if (Environment.GetEnvironmentVariable("PDP_DES_TIMES") == "1")
                            {
                                double[] R2(double[] a) => Array.ConvertAll(a, v => Math.Round(v, 3));
                                Console.WriteLine("@@" + JsonSerializer.Serialize(new { t = "times", asrsS = R2(decoded.AsrsStart), asrsC = R2(decoded.AsrsCompletion), robS = R2(decoded.RobotStart), robC = R2(decoded.RobotCompletion), procS = R2(decoded.ProcessStart), procC = R2(decoded.ProcessCompletion), jobTard = R2(decoded.Tardiness), }));
                                Console.Out.Flush();
                            }

                            Console.WriteLine("@@" + JsonSerializer.Serialize(new { t = "done", feasible = decoded.IsFeasible, tardiness = Math.Round(decoded.TotalTardiness, 4), makespan = Math.Round(decoded.Makespan, 4), violations = decoded.Violations.Count, }));
                            Console.Out.Flush();
                        }

                        Console.WriteLine($"{policy, -8} feasible={decoded.IsFeasible, -5} " + $"tardiness={Math.Round(decoded.TotalTardiness, 1), 12}  makespan={Math.Round(decoded.Makespan, 1), 10}" + (decoded.Violations.Count > 0 ? $"  violations={decoded.Violations.Count} ({decoded.Violations[0]})" : ""));
                    }
                    catch (Exception ex)
                    {
                        if (policy == "extern")
                        {
                            Console.WriteLine("@@" + JsonSerializer.Serialize(new { t = "error", message = ex.Message }));
                            Console.Out.Flush();
                            continue;
                        }

                        Console.WriteLine($"{policy, -8} FAILED: {ex.Message}");
                    }
            }

            return 0;
        }

        static double SptKey(ScenarioInstance inst, PolicyAction act)
        {
            if (act.Kind == "proc")
            {
                return inst.p_ik[act.Job][act.AmrOrWs];
            }

            double best = double.MaxValue;
            for (int k = 0; k < inst.n; k++)
            {
                if (inst.E_ik[act.Job][k] == 1 && inst.p_ik[act.Job][k] < best)
                {
                    best = inst.p_ik[act.Job][k];
                }
            }

            return best == double.MaxValue ? 0.0 : best;
        }
    }
}

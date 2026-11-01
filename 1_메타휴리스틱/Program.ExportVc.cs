using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    internal partial class Program
    {
        static int RunExportVc(string scenarioPath, string? configPath)
        {
            ScenarioInstance inst = ScenarioLoader.Load(scenarioPath, configPath);
            int m = inst.m, n = inst.n, r = inst.r;
            int StationOf(int i)
            {
                if (inst.u_ws_i[i] > 0.5)
                {
                    for (int k = 0; k < n; k++)
                    {
                        if (inst.E_ik[i][k] == 1 && inst.W_n_ik[i][k] > 0.5)
                        {
                            return k;
                        }
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    for (int b = 0; b < inst.b_in_k[k]; b++)
                    {
                        if (inst.W_in_ikb[i][k][b] > 0.5)
                        {
                            return k;
                        }
                    }

                    for (int b = 0; b < inst.b_out_k[k]; b++)
                    {
                        if (inst.W_out_ikb[i][k][b] > 0.5)
                        {
                            return k;
                        }
                    }
                }

                return 0;
            }

            List<int> AllowedAmr(int robotOp)
            {
                var hs = new List<int>();
                for (int h = 0; h < r; h++)
                {
                    if (inst.W_r_ih[robotOp][h] > 0.5)
                    {
                        hs.Add(h);
                    }
                }

                if (hs.Count == 0)
                {
                    for (int h = 0; h < r; h++)
                    {
                        hs.Add(h);
                    }
                }

                return hs;
            }

            var jobs = new List<object>();
            for (int i = 0; i < m; i++)
            {
                var chain = new List<string>();
                if (inst.u_asrs_i[i] > 0.5)
                    chain.Add("ret");
                if (inst.u_rob_i[i] > 0.5)
                    chain.Add("pplus");
                if (inst.u_rob_i[m + i] > 0.5)
                    chain.Add("dplus");
                if (inst.u_ws_i[i] > 0.5)
                    chain.Add("proc");
                if (inst.u_rob_i[2 * m + i] > 0.5)
                    chain.Add("pminus");
                if (inst.u_rob_i[3 * m + i] > 0.5)
                    chain.Add("dminus");
                if (inst.u_asrs_i[m + i] > 0.5)
                    chain.Add("sto");
                if (chain.Count == 0)
                {
                    continue;
                }

                int kStar = StationOf(i);
                var eligible = new List<int>();
                var procSec = new Dictionary<string, double>();
                var setupIn = new Dictionary<string, double>();
                var setupOut = new Dictionary<string, double>();
                if (inst.u_ws_i[i] > 0.5)
                {
                    for (int k = 0; k < n; k++)
                    {
                        if (inst.E_ik[i][k] == 1 && inst.W_n_ik[i][k] > 0.5)
                        {
                            eligible.Add(k);
                            procSec[Convert.ToString(k)] = inst.p_ik[i][k];
                            setupIn[Convert.ToString(k)] = inst.s_in_ik[i][k];
                            setupOut[Convert.ToString(k)] = inst.s_out_ik[i][k];
                        }
                    }
                }

                var amr = new Dictionary<string, List<int>>();
                var svc = new Dictionary<string, double>();
                var travel = new Dictionary<string, double>();
                var kindOp = new (string kind, int op, int node)[]
                {
                    ("pplus", i, inst.Layout.AsrsOut),
                    ("dplus", m + i, inst.Layout.In(kStar)),
                    ("pminus", 2 * m + i, inst.Layout.Out(kStar)),
                    ("dminus", 3 * m + i, inst.Layout.AsrsIn),
                };
                foreach (var(kind, op, node)in kindOp)
                {
                    if (!chain.Contains(kind))
                    {
                        continue;
                    }

                    var hs = AllowedAmr(op);
                    amr[kind] = hs;
                    int h0 = hs[0];
                    svc[kind] = inst.s_ikh[op][node][h0];
                    if (kind == "pplus")
                    {
                        travel[kind] = inst.t_klh[inst.Layout.AsrsOut][inst.Layout.In(kStar)][h0];
                    }
                    else if (kind == "pminus")
                    {
                        travel[kind] = inst.t_klh[inst.Layout.Out(kStar)][inst.Layout.AsrsIn][h0];
                    }
                }

                var crane = new Dictionary<string, double>();
                if (chain.Contains("ret"))
                {
                    int loc = 0;
                    for (int f = 0; f < inst.numL; f++)
                    {
                        if (inst.R_if[i][f] == 1)
                        {
                            loc = f;
                            break;
                        }
                    }

                    crane["ret"] = inst.r_ijfg[2 * m][i][inst.numL][loc];
                }

                if (chain.Contains("sto"))
                {
                    int loc = 0;
                    for (int f = 0; f < inst.numL; f++)
                    {
                        if (inst.W_a_if[m + i][f] > 0.5)
                        {
                            loc = f;
                            break;
                        }
                    }

                    crane["sto"] = inst.r_ijfg[2 * m][m + i][inst.numL][loc];
                }

                jobs.Add(new { job = i, due = Math.Round(inst.d_i[i], 4), chain, eligible_ws = eligible, amr, proc_sec = procSec, setup_in_sec = setupIn, setup_out_sec = setupOut, svc_sec = svc, travel_sec = travel, crane_sec = crane, });
            }

            var pinned = new List<object[]>();
            foreach (int op in inst.AsrsPinnedPrefix)
            {
                pinned.Add(op < m ? new object[] { "ret", op } : new object[] { "sto", op - m });
            }

            var raw = new
            {
                m,
                n,
                r,
                jobs,
                pinned_crane = pinned,
                b_in = inst.b_in_k,
                b_out = inst.b_out_k,
                asrs_out_cap = inst.AsrsOutBufferCapacity == int.MaxValue ? -1 : inst.AsrsOutBufferCapacity,
                asrs_in_cap = inst.AsrsInBufferCapacity == int.MaxValue ? -1 : inst.AsrsInBufferCapacity,
                scale_makespan = 0.0,
            };
            string outPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".", Path.GetFileNameWithoutExtension(scenarioPath) + "_VCRAW.json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("VC 원시값 내보내기: " + outPath);
            Console.WriteLine($"  잡 {jobs.Count}개, 핀 {pinned.Count}건, 스탠드 용량 out/in = " + $"{raw.asrs_out_cap}/{raw.asrs_in_cap}");
            return 0;
        }
    }
}

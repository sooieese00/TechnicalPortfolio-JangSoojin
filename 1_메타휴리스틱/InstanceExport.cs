using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    public static class InstanceExport
    {
        public static string Write(ScenarioInstance inst, string scenarioPath)
        {
            int m = inst.m, n = inst.n, r = inst.r;
            int NodeOf(int kind, int op) => kind == 0 ? 1 + op : kind == 1 ? 1 + 2 * m + op : 1 + 6 * m + op;
            string KindName(int kind, int op) => kind switch
            {
                0 => op < m ? "ret" : "sto",
                1 => op < m ? "pplus" : op < 2 * m ? "dplus" : op < 3 * m ? "pminus" : "dminus",
                _ => "proc"
            };
            bool AsrsActive(int op) => inst.u_asrs_i[op] > 0.5;
            bool RobActive(int op) => inst.u_rob_i[op] > 0.5;
            bool WsActive(int i) => inst.u_ws_i[i] > 0.5;
            int PinnedAmr(int op)
            {
                int pinned = -1, count = 0;
                for (int h = 0; h < r; h++)
                {
                    if (inst.W_r_ih[op][h] > 0.5)
                    {
                        pinned = h;
                        count++;
                    }
                }

                return count == 1 ? pinned : -1;
            }

            List<int> WsOptions(int job)
            {
                var list = new List<int>();
                for (int k = 0; k < n; k++)
                {
                    if (inst.E_ik[job][k] == 1 && inst.W_n_ik[job][k] > 0.5)
                    {
                        list.Add(k);
                    }
                }

                return list;
            }

            var active = new List<object>();
            var resOptions = new List<object>();
            var durByRes = new List<object>();
            var dueMap = new Dictionary<string, double>();
            for (int job = 0; job < m; job++)
            {
                dueMap[job.ToString()] = Math.Round(inst.d_i[job], 1);
                var wsOpts = WsOptions(job);
                var chain = new (int Kind, int Op, bool On)[]
                {
                    (0, job, AsrsActive(job)),
                    (1, job, RobActive(job)),
                    (1, m + job, RobActive(m + job)),
                    (2, job, WsActive(job)),
                    (1, 2 * m + job, RobActive(2 * m + job)),
                    (1, 3 * m + job, RobActive(3 * m + job)),
                    (0, m + job, AsrsActive(m + job)),
                };
                foreach (var(kind, op, on)in chain)
                {
                    if (!on)
                    {
                        continue;
                    }

                    string kn = KindName(kind, op);
                    var opts = new List<string>();
                    var perRes = new Dictionary<string, double>();
                    if (kind == 0)
                    {
                        opts.Add("crane");
                        double best = double.MaxValue;
                        for (int loc = 0; loc < inst.numL; loc++)
                        {
                            double v = inst.r_ijfg[2 * m][op][inst.numL][loc];
                            if (v < best)
                            {
                                best = v;
                            }
                        }

                        perRes["crane"] = best == double.MaxValue ? 0.0 : best;
                    }
                    else if (kind == 1)
                    {
                        int pin = PinnedAmr(op);
                        var hs = pin >= 0 ? new List<int>
                        {
                            pin
                        }

                        : Enumerable.Range(0, r).ToList();
                        foreach (int h in hs)
                        {
                            opts.Add("amr" + h);
                            double best = double.MaxValue;
                            var nodes = new List<int>();
                            if (op < m)
                            {
                                nodes.Add(inst.Layout.AsrsOut);
                            }
                            else if (op < 2 * m)
                            {
                                foreach (int k in wsOpts)
                                {
                                    nodes.Add(inst.Layout.In(k));
                                }
                            }
                            else if (op < 3 * m)
                            {
                                foreach (int k in wsOpts)
                                {
                                    nodes.Add(inst.Layout.Out(k));
                                }
                            }
                            else
                            {
                                nodes.Add(inst.Layout.AsrsIn);
                            }

                            foreach (int nd in nodes)
                            {
                                double v = inst.s_ikh[op][nd][h];
                                if (v < best)
                                {
                                    best = v;
                                }
                            }

                            perRes["amr" + h] = best == double.MaxValue ? 0.0 : best;
                        }
                    }
                    else
                    {
                        foreach (int k in wsOpts)
                        {
                            opts.Add("ws" + k);
                            perRes["ws" + k] = inst.s_in_ik[job][k] + inst.p_ik[job][k] + inst.s_out_ik[job][k];
                        }
                    }

                    double dur = perRes.Count > 0 ? perRes.Values.Min() : 0.0;
                    active.Add(new { kind = kn, job, node = NodeOf(kind, op), dur = Math.Round(dur, 3) });
                    resOptions.Add(new { kind = kn, job, res = opts });
                    durByRes.Add(new { kind = kn, job, dur = perRes.ToDictionary(p => p.Key, p => Math.Round(p.Value, 3)), });
                }
            }

            var pinnedCrane = new List<object>();
            if (inst.AsrsPinnedPrefix != null)
            {
                foreach (int op in inst.AsrsPinnedPrefix)
                {
                    pinnedCrane.Add(new { kind = KindName(0, op), job = op < m ? op : op - m });
                }
            }

            var doc = new
            {
                source = Path.GetFileName(scenarioPath),
                m,
                n,
                r,
                numL = inst.numL,
                b_in = inst.b_in_k,
                b_out = inst.b_out_k,
                out_cap = inst.AsrsOutBufferCapacity,
                in_cap = inst.AsrsInBufferCapacity,
                due = dueMap,
                active,
                res_options = resOptions,
                dur_by_res = durByRes,
                pinned_crane = pinnedCrane,
                dur_convention = "허용 자원·셀에 대한 최솟값 (H 낙관 하한과 같은 방향). " + "배정 확정 후에는 dur_by_res 의 해당 값으로 바꿔 쓰는 편이 정확하다.",
            };
            string dir = Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".";
            string outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(scenarioPath) + "_INSTANCE.json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"정적 구조 내보냄: {outPath}");
            Console.WriteLine($"  단계 {active.Count}개 · 잡 {m} · WS {n} · AMR {r} · 셀 {inst.numL}" + $" · 크레인 핀 {pinnedCrane.Count}");
            return outPath;
        }
    }
}

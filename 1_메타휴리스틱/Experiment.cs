using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PDP_ASRS_WIP
{
    public static class Experiment
    {
        public static int Run(string[] args)
        {
            int seed = args.Length > 1 ? Convert.ToInt32(args[1]) : 1;
            int m = args.Length > 2 ? Convert.ToInt32(args[2]) : 2;
            int r = args.Length > 3 ? Convert.ToInt32(args[3]) : 2;
            int n = args.Length > 4 ? Convert.ToInt32(args[4]) : 2;
            int b_in_cap = args.Length > 5 ? Convert.ToInt32(args[5]) : 2;
            int b_out_cap = args.Length > 6 ? Convert.ToInt32(args[6]) : 2;
            int wipJobs = args.Length > 7 ? Convert.ToInt32(args[7]) : 0;
            double dueFactor = args.Length > 8 ? Convert.ToDouble(args[8]) : 0.5;
            var random = new Random(seed);
            var stopwatch = Stopwatch.StartNew();
            int width = m;
            int depth = 1;
            int levels = 1;
            int numL = width * depth * levels;
            var parameters = new Parameters();
            var operationSets = parameters.generate_sets_of_operations(m);
            int[] P_plus = operationSets.P_plus;
            int[] D_plus = operationSets.D_plus;
            int[] P_minus = operationSets.P_minus;
            int[] D_minus = operationSets.D_minus;
            int[] R = operationSets.R;
            int[] R_combine = operationSets.R_combine;
            var nodeSets = parameters.generate_sets_of_nodes(n);
            int[] N = nodeSets.N;
            int[] N_plus = nodeSets.N_plus;
            int[] N_minus = nodeSets.N_minus;
            double[] c_h = parameters.generate_c_h(r);
            double[][] p_ik = parameters.generate_p_ik(n, m);
            double[] q_i = parameters.generate_q_i(m);
            double[][][] s_ikh = parameters.generate_s_ikh(m, n, r);
            double[][][] t_klh = parameters.generate_t_klh(n, r);
            int[][] E_ik = parameters.generate_E_ik(m, n, 1.0);
            double[][] R_if = parameters.generate_R_if(2 * m, numL);
            double[] d_i = parameters.generate_d_i(m, 0.0);
            double[] u_rob_i = parameters.generate_u_rob_i(m);
            double[] u_ws_i = parameters.generate_u_ws_i(m);
            double[] u_asrs_i = parameters.generate_u_asrs_i(m);
            double[][] W_n_ik = parameters.generate_W_n_ik(m, n);
            double[][] W_r_ih = parameters.generate_W_r_ih(m, r);
            double[][] W_a_if = parameters.generate_W_a_if(m, numL);
            double[][] s_in_ik = parameters.generate_s_in_ik(n, m);
            double[][] s_out_ik = parameters.generate_s_out_ik(n, m);
            int[] b_in_k = parameters.generate_b_in_k(n, b_in_cap);
            int[] b_out_k = parameters.generate_b_out_k(n, b_out_cap);
            double[][][] W_in_ikb = parameters.generate_W_in_ikb(m, n, b_in_k);
            double[][][] W_out_ikb = parameters.generate_W_out_ikb(m, n, b_out_k);
            double Draw(double low, double high, double deterministic)
            {
                return seed == 0 ? deterministic : Rounding.Add(low, Rounding.Multiply(random.NextDouble(), high - low));
            }

            for (int h = 0; h < r; h++)
            {
                c_h[h] = 2.0;
            }

            double processingTotal = 0.0;
            for (int i = 0; i < m + 2; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    p_ik[i][k] = i < m ? Draw(6.0, 16.0, 8.0 + 2.0 * i + k) : 0.0;
                    if (i < m)
                    {
                        processingTotal += p_ik[i][k] / n;
                    }
                }
            }

            for (int i = 0; i < 4 * m + 2; i++)
            {
                for (int k = 0; k < n + 2; k++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        s_ikh[i][k][h] = (i == 4 * m || i == 4 * m + 1 || k == n + 1) ? 0.0 : Draw(0.5, 2.5, 1.0);
                    }
                }
            }

            for (int k = 0; k < n + 2; k++)
            {
                for (int l = 0; l < n + 2; l++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        t_klh[k][l][h] = k == l ? 0.0 : Draw(1.0, 5.0, 1.0 + Math.Abs(k - l));
                    }
                }
            }

            for (int i = 0; i < m + 2; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    s_in_ik[i][k] = i < m ? Draw(0.5, 3.0, 1.0) : 0.0;
                    s_out_ik[i][k] = i < m ? Draw(0.5, 3.0, 1.0) : 0.0;
                }
            }

            for (int i = 0; i < m + 2; i++)
            {
                for (int k = 0; k < n + 2; k++)
                {
                    E_ik[i][k] = 0;
                }
            }

            for (int i = 0; i < m; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    E_ik[i][k] = 1;
                }
            }

            for (int i = 0; i < R_if.Length; i++)
            {
                for (int f = 0; f < R_if[i].Length; f++)
                {
                    R_if[i][f] = 0.0;
                }
            }

            for (int i = 0; i < m; i++)
            {
                R_if[i][i % numL] = 1.0;
                d_i[i] = Rounding.Multiply(dueFactor, processingTotal);
            }

            for (int i = 0; i < W_n_ik.Length; i++)
            {
                for (int k = 0; k < W_n_ik[i].Length; k++)
                {
                    W_n_ik[i][k] = 1.0;
                }
            }

            for (int i = 0; i < W_r_ih.Length; i++)
            {
                for (int h = 0; h < W_r_ih[i].Length; h++)
                {
                    W_r_ih[i][h] = i < m ? 1.0 : 0.0;
                }
            }

            for (int i = 0; i < W_a_if.Length; i++)
            {
                for (int f = 0; f < W_a_if[i].Length; f++)
                {
                    W_a_if[i][f] = 1.0;
                }
            }

            var stages = new int[m];
            int[] amrLoad = new int[r];
            int[] inSlotCursor = new int[n];
            int[] outSlotCursor = new int[n];
            bool[] machineBusy = new bool[n];
            var wipDescription = new List<string>();
            for (int index = 0; index < wipJobs && index < m; index++)
            {
                int job = m - 1 - index;
                int stage = 1 + random.Next(0, 6);
                int station = random.Next(0, n);
                int vehicle = -1;
                for (int h = 0; h < r; h++)
                {
                    if (amrLoad[h] < (int)c_h[h])
                    {
                        vehicle = h;
                        break;
                    }
                }

                if ((stage == 1 || stage == 5) && vehicle < 0)
                {
                    stage = 6;
                }

                if (stage == 2 && inSlotCursor[station] >= b_in_k[station])
                {
                    stage = 6;
                }

                if (stage == 4 && outSlotCursor[station] >= b_out_k[station])
                {
                    stage = 6;
                }

                if (stage == 3)
                {
                    if (machineBusy[station])
                    {
                        stage = 6;
                    }
                    else
                    {
                        machineBusy[station] = true;
                    }
                }

                stages[job] = stage;
                wipDescription.Add(Convert.ToString(job) + ":s" + Convert.ToString(stage));
                u_asrs_i[job] = 0.0;
                u_rob_i[job] = 0.0;
                for (int f = 0; f < numL; f++)
                {
                    R_if[job][f] = 0.0;
                    W_a_if[job][f] = 0.0;
                }

                for (int h = 0; h < r; h++)
                {
                    W_r_ih[job][h] = 0.0;
                }

                if (stage == 1)
                {
                    for (int h = 0; h < r; h++)
                    {
                        W_r_ih[m + job][h] = h == vehicle ? 1.0 : 0.0;
                    }

                    amrLoad[vehicle]++;
                    continue;
                }

                u_rob_i[m + job] = 0.0;
                for (int h = 0; h < r; h++)
                {
                    W_r_ih[m + job][h] = 0.0;
                }

                for (int k = 0; k < n; k++)
                {
                    W_n_ik[job][k] = k == station ? 1.0 : 0.0;
                    E_ik[job][k] = k == station ? 1 : 0;
                }

                if (stage == 2)
                {
                    W_in_ikb[job][station][inSlotCursor[station]] = 1.0;
                    inSlotCursor[station]++;
                    continue;
                }

                if (stage == 3)
                {
                    continue;
                }

                u_ws_i[job] = 0.0;
                if (stage == 4)
                {
                    W_out_ikb[job][station][outSlotCursor[station]] = 1.0;
                    outSlotCursor[station]++;
                    for (int h = 0; h < r; h++)
                    {
                        W_r_ih[2 * m + job][h] = h == vehicle ? 1.0 : 0.0;
                    }

                    continue;
                }

                u_rob_i[2 * m + job] = 0.0;
                for (int h = 0; h < r; h++)
                {
                    W_r_ih[2 * m + job][h] = 0.0;
                }

                for (int k = 0; k < n; k++)
                {
                    W_n_ik[job][k] = 0.0;
                }

                if (stage == 5)
                {
                    for (int h = 0; h < r; h++)
                    {
                        W_r_ih[3 * m + job][h] = h == vehicle ? 1.0 : 0.0;
                    }

                    amrLoad[vehicle]++;
                    continue;
                }

                u_asrs_i[m + job] = 0.0;
                u_rob_i[3 * m + job] = 0.0;
                for (int h = 0; h < r; h++)
                {
                    W_r_ih[3 * m + job][h] = 0.0;
                }

                for (int f = 0; f < numL; f++)
                {
                    W_a_if[m + job][f] = 0.0;
                }
            }

            double[][][][] r_ijfg = parameters.generate_r_ijfg(m, width, 1.0, 1.0, 1.0, 1.0, depth, 1.0, 1.0, 1.0, 1.0, levels, 1.0, 1.0, 1.0, 1.0);
            string label = "seed=" + Convert.ToString(seed) + " m=" + Convert.ToString(m) + " r=" + Convert.ToString(r) + " n=" + Convert.ToString(n) + " bin=" + Convert.ToString(b_in_cap) + " bout=" + Convert.ToString(b_out_cap) + " wip=" + (wipDescription.Count > 0 ? string.Join("/", wipDescription) : "none") + " due=" + Convert.ToString(Rounding.Value(d_i.Length > 0 ? d_i[0] : 0.0));
            Console.WriteLine("EXPERIMENT " + label);
            double greedyObjective = double.NaN;
            bool greedyFeasible = false;
            try
            {
                SequenceSolution greedy = InitialSolutionGenerator.Generate(numL, r, n, m, p_ik, s_ikh, t_klh, E_ik, R_if, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                DecodedSchedule greedyDecoded = SolutionDecoder.Decode(greedy, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                greedyObjective = greedyDecoded.TotalTardiness;
                greedyFeasible = greedyDecoded.IsFeasible;
            }
            catch (System.Exception greedyException)
            {
                Console.WriteLine("greedy failed: " + greedyException.Message);
            }

            MilpSolveResult result = MILP.MILP_cplex(numL, r, n, m, P_plus, D_plus, P_minus, D_minus, R, R_combine, N, N_plus, N_minus, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
            stopwatch.Stop();
            string status = result.Status;
            double milpObjective = double.NaN;
            double decodedObjective = double.NaN;
            bool decodedFeasible = false;
            bool consistent = false;
            int laterTimings = -1;
            int selfViolations = -1;
            double worstSelfViolation = 0.0;
            double bigMSlack = 0.0;
            if (result.IsSolved && result.Extracted != null)
            {
                milpObjective = result.ObjectiveValue;
                bigMSlack = Rounding.Multiply(result.Extracted.BigM, 1.0 - result.Extracted.MinSelectedBinary);
                SequenceSolution milpSequence = MilpSequenceVerifier.ToSequenceSolution(result.Extracted, numL, r, n, m, b_in_k, b_out_k);
                DecodedSchedule milpDecoded = SolutionDecoder.Decode(milpSequence, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                List<string> comparison = MilpSequenceVerifier.Compare(result.Extracted, milpDecoded, milpSequence, numL, r, n, m, p_ik, s_ikh, t_klh, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, d_i, out consistent, out laterTimings, out selfViolations, out worstSelfViolation);
                foreach (string line in comparison)
                {
                    Console.WriteLine(line);
                }

                decodedObjective = milpDecoded.TotalTardiness;
                decodedFeasible = milpDecoded.IsFeasible;
            }

            string Cell(double value)
            {
                return double.IsNaN(value) ? "NA" : Convert.ToString(Rounding.Value(value));
            }

            Console.WriteLine("RESULT," + string.Join(",", new string[] { Convert.ToString(seed), Convert.ToString(m), Convert.ToString(r), Convert.ToString(n), Convert.ToString(b_in_cap), Convert.ToString(b_out_cap), Convert.ToString(wipJobs), Convert.ToString(dueFactor), status, Cell(milpObjective), Cell(decodedObjective), Cell(double.IsNaN(milpObjective) || double.IsNaN(decodedObjective) ? double.NaN : Rounding.Subtract(decodedObjective, milpObjective)), Convert.ToString(decodedFeasible), Convert.ToString(consistent), Convert.ToString(laterTimings), Convert.ToString(selfViolations), Cell(worstSelfViolation), Cell(bigMSlack), Cell(greedyObjective), Convert.ToString(greedyFeasible), Convert.ToString(Math.Round(stopwatch.Elapsed.TotalSeconds, 2)) }));
            return consistent ? 0 : 3;
        }
    }
}

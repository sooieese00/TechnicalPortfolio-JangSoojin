using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PDP_ASRS_WIP
{
    partial class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0 && Environment.GetEnvironmentVariable("PDP_PIPELINE") != "0" && FindPackageRoot() != null)
            {
                var pipeWatch = Stopwatch.StartNew();
                int pipeRc = RunAllPipeline();
                pipeWatch.Stop();
                Console.WriteLine($"Elapsed seconds: {pipeWatch.Elapsed.TotalSeconds:F2}");
                return pipeRc;
            }

            if (args.Length == 0)
            {
                try
                {
                    args = RunSettings.BuildArguments();
                    Console.WriteLine();
                }
                catch (System.Exception settingsException)
                {
                    Console.WriteLine("RunSettings.cs 설정 오류:");
                    Console.WriteLine(settingsException.Message);
                    return 1;
                }
            }

            if (args.Length > 0 && args[0].Equals("exp", StringComparison.OrdinalIgnoreCase))
            {
#if HAS_CPLEX
                return Experiment.Run(args);
#else
                Console.WriteLine("The 'exp' arm needs CPLEX, which was not found when this build was made.");
                return 1;
#endif
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (args.Length > 0 && args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    string scenarioPath = args[0];
                    string scenarioMode = args.Length > 1 ? args[1].ToLowerInvariant() : "vns";
                    int scenarioSeed = args.Length > 2 ? Convert.ToInt32(args[2]) : 1;
                    int scenarioEvals = args.Length > 3 ? Convert.ToInt32(args[3]) : 5000;
                    string? scenarioConfig = args.Length > 4 ? args[4] : null;
                    int exitCode = RunScenario(scenarioPath, scenarioMode, scenarioSeed, scenarioEvals, scenarioConfig);
                    stopwatch.Stop();
                    Console.WriteLine($"Elapsed seconds: {stopwatch.Elapsed.TotalSeconds:F2}");
                    return exitCode;
                }

                int instanceNo = args.Length > 0 ? Convert.ToInt32(args[0]) : 8;
                string instanceName = "all operations start before WIP";
                int r = 2;
                int n = 2;
                int m = 2;
                double E_ratio = 1.0;
                double dueDate = 40.0;
                int b_in_cap = 2;
                int b_out_cap = 2;
                int width = 2;
                int depth = 1;
                int levels = 1;
                if (instanceNo == 2)
                {
                    instanceName = "delivery WIP: operation 0 loaded on AMR 0";
                    dueDate = 30.0;
                }
                else if (instanceNo == 3)
                {
                    instanceName = "processing WIP: operation 0 already at workstation 0";
                    dueDate = 25.0;
                }
                else if (instanceNo == 4)
                {
                    instanceName = "post-process WIP: operation 0 waiting for P- at workstation 0";
                    dueDate = 25.0;
                }
                else if (instanceNo == 5)
                {
                    instanceName = "storage delivery WIP: operation 0 loaded on AMR 0 for D-";
                    dueDate = 25.0;
                }
                else if (instanceNo == 6)
                {
                    instanceName = "completed WIP: operation 0 finished before horizon";
                    dueDate = 40.0;
                }
                else if (instanceNo == 7)
                {
                    instanceName = "capacity check: infeasible initial AMR load";
                    dueDate = 30.0;
                }
                else if (instanceNo == 8)
                {
                    instanceName = "tight due date check: tardiness must be positive";
                    dueDate = 10.0;
                }
                else if (instanceNo == 9)
                {
                    instanceName = "buffer: b_in=1 input-buffer serialization at WS0";
                    dueDate = 0.0;
                    b_in_cap = 1;
                }
                else if (instanceNo == 10)
                {
                    instanceName = "buffer: baseline caps=2, both operations at WS0";
                    dueDate = 0.0;
                }
                else if (instanceNo == 11)
                {
                    instanceName = "buffer: b_out=1 output-buffer blocking at WS0";
                    dueDate = 0.0;
                    b_out_cap = 1;
                }
                else if (instanceNo == 12)
                {
                    instanceName = "buffer WIP: operation 0 starts in input buffer of WS0";
                    dueDate = 0.0;
                }
                else if (instanceNo == 13)
                {
                    instanceName = "buffer WIP: operation 0 starts in output buffer of WS0, b_out=1";
                    dueDate = 0.0;
                    b_out_cap = 1;
                }
                else if (instanceNo == 14)
                {
                    instanceName = "buffer: s_in=3, s_out=2 transfer-time sensitivity";
                    dueDate = 0.0;
                }
                else if (instanceNo == 15)
                {
                    instanceName = "buffer: slow P- of op 0 + b_out=1 must delay op 1";
                    dueDate = 0.0;
                    b_out_cap = 1;
                }
                else if (instanceNo == 16)
                {
                    instanceName = "buffer: slow P- of op 0 + b_out=2 relaxed baseline";
                    dueDate = 0.0;
                }
                else if (instanceNo == 17)
                {
                    instanceName = "realistic scale: 4 WS, 2 AMR, 20 operations";
                    r = 2;
                    n = 4;
                    m = 20;
                    E_ratio = 0.75;
                    dueDate = 250.0;
                    b_in_cap = 2;
                    b_out_cap = 2;
                    width = 4;
                    depth = 2;
                    levels = 3;
                }

                double H_length = 1.0;
                double H_acc = 1.0;
                double H_dcc = 1.0;
                double H_max = 1.0;
                double D_length = 1.0;
                double D_acc = 1.0;
                double D_dcc = 1.0;
                double D_max = 1.0;
                double L_length = 1.0;
                double L_acc = 1.0;
                double L_dcc = 1.0;
                double L_max = 1.0;
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
                int[][] E_ik = parameters.generate_E_ik(m, n, E_ratio);
                double[][] R_if = parameters.generate_R_if(2 * m, numL);
                double[] d_i = parameters.generate_d_i(m, dueDate);
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
                if (instanceNo == 17)
                {
                    for (int i = 0; i < m; i++)
                    {
                        d_i[i] = dueDate + 20.0 * i;
                    }
                }
                else
                {
                    for (int h = 0; h < r; h++)
                    {
                        c_h[h] = 2.0;
                    }

                    for (int i = 0; i < m + 2; i++)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            p_ik[i][k] = i < m ? 8.0 + 2.0 * i + k : 0.0;
                        }
                    }

                    for (int i = 0; i < 4 * m + 2; i++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                s_ikh[i][k][h] = (i == 4 * m || i == 4 * m + 1 || k == n + 1) ? 0.0 : 1.0;
                            }
                        }
                    }

                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                t_klh[k][l][h] = k == l ? 0.0 : 1.0 + Math.Abs(k - l);
                            }
                        }
                    }

                    for (int i = 0; i < m + 2; i++)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            s_in_ik[i][k] = i < m ? (instanceNo == 14 ? 3.0 : 1.0) : 0.0;
                            s_out_ik[i][k] = i < m ? (instanceNo == 14 ? 2.0 : 1.0) : 0.0;
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
                        d_i[i] = dueDate + 5.0 * i;
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
                }

                if (instanceNo == 2)
                {
                    int job = 0;
                    int robot = 0;
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
                        W_r_ih[m + job][h] = h == robot ? 1.0 : 0.0;
                    }
                }
                else if (instanceNo == 3)
                {
                    int job = 0;
                    int workstation = 0;
                    u_asrs_i[job] = 0.0;
                    u_rob_i[job] = 0.0;
                    u_rob_i[m + job] = 0.0;
                    for (int f = 0; f < numL; f++)
                    {
                        R_if[job][f] = 0.0;
                        W_a_if[job][f] = 0.0;
                    }

                    for (int h = 0; h < r; h++)
                    {
                        W_r_ih[job][h] = 0.0;
                        W_r_ih[m + job][h] = 0.0;
                    }

                    for (int k = 0; k < n; k++)
                    {
                        W_n_ik[job][k] = k == workstation ? 1.0 : 0.0;
                        E_ik[job][k] = k == workstation ? 1 : 0;
                    }
                }
                else if (instanceNo == 4)
                {
                    int job = 0;
                    int workstation = 0;
                    int robot = 0;
                    u_asrs_i[job] = 0.0;
                    u_rob_i[job] = 0.0;
                    u_rob_i[m + job] = 0.0;
                    u_ws_i[job] = 0.0;
                    for (int f = 0; f < numL; f++)
                    {
                        R_if[job][f] = 0.0;
                        W_a_if[job][f] = 0.0;
                    }

                    for (int h = 0; h < r; h++)
                    {
                        W_r_ih[job][h] = 0.0;
                        W_r_ih[m + job][h] = 0.0;
                        W_r_ih[2 * m + job][h] = h == robot ? 1.0 : 0.0;
                        W_r_ih[3 * m + job][h] = 0.0;
                    }

                    for (int k = 0; k < n; k++)
                    {
                        W_n_ik[job][k] = k == workstation ? 1.0 : 0.0;
                        E_ik[job][k] = k == workstation ? 1 : 0;
                    }

                    W_out_ikb[job][workstation][0] = 1.0;
                }
                else if (instanceNo == 5)
                {
                    int job = 0;
                    int robot = 0;
                    u_asrs_i[job] = 0.0;
                    u_rob_i[job] = 0.0;
                    u_rob_i[m + job] = 0.0;
                    u_ws_i[job] = 0.0;
                    u_rob_i[2 * m + job] = 0.0;
                    for (int f = 0; f < numL; f++)
                    {
                        R_if[job][f] = 0.0;
                        W_a_if[job][f] = 0.0;
                    }

                    for (int h = 0; h < r; h++)
                    {
                        W_r_ih[job][h] = 0.0;
                        W_r_ih[m + job][h] = 0.0;
                        W_r_ih[2 * m + job][h] = 0.0;
                        W_r_ih[3 * m + job][h] = h == robot ? 1.0 : 0.0;
                    }

                    for (int k = 0; k < n; k++)
                    {
                        W_n_ik[job][k] = 0.0;
                    }
                }
                else if (instanceNo == 6)
                {
                    int job = 0;
                    u_asrs_i[job] = 0.0;
                    u_asrs_i[m + job] = 0.0;
                    u_rob_i[job] = 0.0;
                    u_rob_i[m + job] = 0.0;
                    u_ws_i[job] = 0.0;
                    u_rob_i[2 * m + job] = 0.0;
                    u_rob_i[3 * m + job] = 0.0;
                    for (int f = 0; f < numL; f++)
                    {
                        R_if[job][f] = 0.0;
                        W_a_if[job][f] = 0.0;
                        W_a_if[m + job][f] = 0.0;
                    }

                    for (int h = 0; h < r; h++)
                    {
                        W_r_ih[job][h] = 0.0;
                        W_r_ih[m + job][h] = 0.0;
                        W_r_ih[2 * m + job][h] = 0.0;
                        W_r_ih[3 * m + job][h] = 0.0;
                    }

                    for (int k = 0; k < n; k++)
                    {
                        W_n_ik[job][k] = 0.0;
                    }
                }
                else if (instanceNo == 7)
                {
                    int job = 0;
                    int robot = 0;
                    c_h[robot] = 0.5;
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
                        W_r_ih[m + job][h] = h == robot ? 1.0 : 0.0;
                    }
                }
                else if (instanceNo >= 9 && instanceNo <= 16)
                {
                    for (int i = 0; i < m; i++)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            E_ik[i][k] = k == 0 ? 1 : 0;
                        }
                    }

                    if (instanceNo == 12 || instanceNo == 13)
                    {
                        int job = 0;
                        int workstation = 0;
                        u_asrs_i[job] = 0.0;
                        u_rob_i[job] = 0.0;
                        u_rob_i[m + job] = 0.0;
                        for (int f = 0; f < numL; f++)
                        {
                            R_if[job][f] = 0.0;
                            W_a_if[job][f] = 0.0;
                        }

                        for (int h = 0; h < r; h++)
                        {
                            W_r_ih[job][h] = 0.0;
                            W_r_ih[m + job][h] = 0.0;
                        }

                        for (int k = 0; k < n; k++)
                        {
                            W_n_ik[job][k] = k == workstation ? 1.0 : 0.0;
                        }

                        if (instanceNo == 12)
                        {
                            W_in_ikb[job][workstation][0] = 1.0;
                        }
                        else
                        {
                            u_ws_i[job] = 0.0;
                            W_out_ikb[job][workstation][0] = 1.0;
                        }
                    }

                    if (instanceNo == 15 || instanceNo == 16)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                s_ikh[2 * m][k][h] = 20.0;
                            }
                        }
                    }
                }

                double[][][][] r_ijfg = parameters.generate_r_ijfg(m, width, H_length, H_acc, H_dcc, H_max, depth, D_length, D_acc, D_dcc, D_max, levels, L_length, L_acc, L_dcc, L_max);
                Console.WriteLine("MILP input");
                Console.WriteLine($"Instance={instanceNo} ({instanceName})");
                Console.WriteLine($"AMR={r}, Workstation={n}, Operation={m}, ASRS locations={numL}");
                PrintWipSummary(m, r, n, numL, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, R_if);
                Console.WriteLine("u_rob_i=[" + string.Join(", ", u_rob_i) + "]");
                Console.WriteLine("u_ws_i=[" + string.Join(", ", u_ws_i) + "]");
                Console.WriteLine("u_asrs_i=[" + string.Join(", ", u_asrs_i) + "]");
                Console.WriteLine();
                string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                bool runMilp = mode != "greedy" && mode != "vns";
                bool runVns = mode == "vns" || mode == "vnsmilp";
                int vnsSeed = args.Length > 2 ? Convert.ToInt32(args[2]) : 1;
                int vnsEvaluations = args.Length > 3 ? Convert.ToInt32(args[3]) : 2000;
                double greedyObjective = double.NaN;
                double vnsObjective = double.NaN;
                SequenceSolution? initialSolution = null;
                SequenceSolution? finalSolution = null;
                try
                {
                    Console.WriteLine("GREEDY INITIAL SOLUTION (Algorithm 2, buffer-extended)");
                    initialSolution = InitialSolutionGenerator.Generate(numL, r, n, m, p_ik, s_ikh, t_klh, E_ik, R_if, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                    DecodedSchedule decoded = SolutionDecoder.Decode(initialSolution, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                    decoded.Print();
                    greedyObjective = decoded.TotalTardiness;
                    finalSolution = initialSolution;
                }
                catch (System.Exception greedyEx)
                {
                    Console.WriteLine("Greedy initial solution failed: " + greedyEx.Message);
                }

                if (runVns && initialSolution != null && !double.IsNaN(greedyObjective))
                {
                    Console.WriteLine();
                    Console.WriteLine($"VNS (seed={vnsSeed}, evaluation budget={vnsEvaluations})");
                    VnsResult vnsResult = VNS.Run(initialSolution, vnsSeed, vnsEvaluations, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                    foreach (string line in vnsResult.Log)
                    {
                        Console.WriteLine(line);
                    }

                    Console.WriteLine($"VNS evaluations: {vnsResult.Evaluations}");
                    Console.WriteLine();
                    Console.WriteLine("VNS BEST SOLUTION");
                    vnsResult.BestSchedule.Print();
                    vnsObjective = vnsResult.BestObjective;
                    finalSolution = vnsResult.BestSolution;
                }

                if (!runMilp)
                {
                    stopwatch.Stop();
                    Console.WriteLine();
                    Console.WriteLine("MILP skipped (" + (runVns ? "vns" : "greedy") + " mode).");
                    if (!double.IsNaN(greedyObjective))
                    {
                        Console.WriteLine($"Greedy objective (total tardiness): {greedyObjective}");
                    }

                    if (!double.IsNaN(vnsObjective))
                    {
                        Console.WriteLine($"VNS objective (total tardiness): {vnsObjective}");
                    }

                    if (finalSolution != null)
                    {
                        Console.WriteLine();
                        Console.WriteLine("FINAL SOLUTION" + (runVns ? " (VNS best)" : " (greedy)"));
                        foreach (string line in finalSolution.ResourceSequenceLines())
                        {
                            Console.WriteLine(line);
                        }
                    }

                    Console.WriteLine($"Elapsed seconds: {stopwatch.Elapsed.TotalSeconds:F2}");
                    return 0;
                }

#if !HAS_CPLEX
                stopwatch.Stop();
                Console.WriteLine();
                Console.WriteLine("MILP mode needs CPLEX, which was not found when this build was made.");
                Console.WriteLine("Use mode 'greedy' or 'vns' here, or run a P4R scenario file instead.");
                Console.WriteLine($"Elapsed seconds: {stopwatch.Elapsed.TotalSeconds:F2}");
                return 1;
#else
                Console.WriteLine("Start solving...");
                Console.WriteLine();
                MilpSolveResult result = MILP.MILP_cplex(numL, r, n, m, P_plus, D_plus, P_minus, D_minus, R, R_combine, N, N_plus, N_minus, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                stopwatch.Stop();
                Console.WriteLine();
                Console.WriteLine("Solve summary");
                Console.WriteLine($"Solved: {result.IsSolved}");
                Console.WriteLine($"Status: {result.Status}");
                Console.WriteLine($"Objective: {result.ObjectiveValue}");
                if (!double.IsNaN(greedyObjective))
                {
                    Console.WriteLine($"Greedy initial objective (total tardiness): {greedyObjective}");
                }

                if (!double.IsNaN(vnsObjective))
                {
                    Console.WriteLine($"VNS objective (total tardiness): {vnsObjective}");
                }

                if (finalSolution != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("FINAL HEURISTIC SOLUTION" + (runVns ? " (VNS best)" : " (greedy)"));
                    foreach (string line in finalSolution.ResourceSequenceLines())
                    {
                        Console.WriteLine(line);
                    }
                }

                Console.WriteLine($"Elapsed seconds: {stopwatch.Elapsed.TotalSeconds:F2}");
                if (result.WipTimingSummaryLines.Count > 0)
                {
                    foreach (string line in result.WipTimingSummaryLines)
                    {
                        Console.WriteLine(line);
                    }
                }
                else
                {
                    PrintWipSummary(m, r, n, numL, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, R_if);
                }

                bool decoderMatchesMilp = false;
                if (result.IsSolved && result.Extracted != null)
                {
                    Console.WriteLine();
                    SequenceSolution milpSequence = MilpSequenceVerifier.ToSequenceSolution(result.Extracted, numL, r, n, m, b_in_k, b_out_k);
                    DecodedSchedule milpDecoded = SolutionDecoder.Decode(milpSequence, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                    milpDecoded.Print();
                    List<string> comparisonLines = MilpSequenceVerifier.Compare(result.Extracted, milpDecoded, milpSequence, numL, r, n, m, p_ik, s_ikh, t_klh, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, d_i, out decoderMatchesMilp, out _, out _, out _);
                    string? verifyReplayDir = Environment.GetEnvironmentVariable("PDP_REPLAY_DIR");
                    if (!string.IsNullOrEmpty(verifyReplayDir))
                    {
                        var replayInst = new ScenarioInstance
                        {
                            m = m,
                            n = n,
                            r = r,
                            numL = numL,
                            c_h = c_h,
                            p_ik = p_ik,
                            q_i = q_i,
                            s_ikh = s_ikh,
                            t_klh = t_klh,
                            E_ik = E_ik,
                            R_if = R_if,
                            d_i = d_i,
                            u_rob_i = u_rob_i,
                            u_ws_i = u_ws_i,
                            u_asrs_i = u_asrs_i,
                            W_n_ik = W_n_ik,
                            W_r_ih = W_r_ih,
                            W_a_if = W_a_if,
                            r_ijfg = r_ijfg,
                            s_in_ik = s_in_ik,
                            s_out_ik = s_out_ik,
                            b_in_k = b_in_k,
                            b_out_k = b_out_k,
                            W_in_ikb = W_in_ikb,
                            W_out_ikb = W_out_ikb,
                            MachineReadyAt = new double[n],
                            Layout = NodeLayout.Compact(n),
                            AsrsOutBufferCapacity = 1,
                            AsrsInBufferCapacity = int.MaxValue
                        };
                        if (initialSolution != null)
                        {
                            DecodedSchedule greedyForReplay = SolutionDecoder.Decode(initialSolution, numL, r, n, m, c_h, p_ik, q_i, s_ikh, t_klh, E_ik, R_if, d_i, u_rob_i, u_ws_i, u_asrs_i, W_n_ik, W_r_ih, W_a_if, r_ijfg, s_in_ik, s_out_ik, b_in_k, b_out_k, W_in_ikb, W_out_ikb);
                            ReplayExport.Export(replayInst, initialSolution, greedyForReplay, Path.Combine(verifyReplayDir, $"replay_inst{instanceNo}_greedy.jsonl"), "greedy");
                        }

                        ReplayExport.Export(replayInst, milpSequence, milpDecoded, Path.Combine(verifyReplayDir, $"replay_inst{instanceNo}_milp.jsonl"), "milp optimum");
                    }

                    foreach (string line in comparisonLines)
                    {
                        Console.WriteLine(line);
                    }
                }

                if (!result.IsSolved)
                {
                    return 2;
                }

                return result.Extracted != null && !decoderMatchesMilp ? 3 : 0;
#endif
            }
            catch (System.Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine("Program failed.");
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine($"Elapsed seconds: {stopwatch.Elapsed.TotalSeconds:F2}");
                return 1;
            }
        }

        static int RunScenario(string scenarioPath, string mode, int seed, int evaluations, string? configPath)
        {
            Console.WriteLine("P4R SCENARIO MODE");
            Console.WriteLine("Scenario: " + scenarioPath);
            ScenarioInstance inst = ScenarioLoader.Load(scenarioPath, configPath);
            Console.WriteLine($"Horizon start: {inst.HorizonStart:yyyy-MM-ddTHH:mm:ssZ}");
            Console.WriteLine($"Jobs m={inst.m}, Workstations n={inst.n} ({string.Join(",", inst.WorkstationLabel)}), AMRs r={inst.r} ({string.Join(",", inst.AmrLabel)}), ASRS locations={inst.numL}");
            foreach (string w in inst.Warnings)
            {
                Console.WriteLine("WARNING: " + w);
            }

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("JOBS");
            for (int i = 0; i < inst.m; i++)
            {
                var chain = new List<string>();
                if (inst.u_asrs_i[i] > 0.5)
                    chain.Add("RET");
                if (inst.u_rob_i[i] > 0.5)
                    chain.Add("P+");
                if (inst.u_rob_i[inst.m + i] > 0.5)
                    chain.Add("D+");
                if (inst.u_ws_i[i] > 0.5)
                    chain.Add("PROC");
                if (inst.u_rob_i[2 * inst.m + i] > 0.5)
                    chain.Add("P-");
                if (inst.u_rob_i[3 * inst.m + i] > 0.5)
                    chain.Add("D-");
                if (inst.u_asrs_i[inst.m + i] > 0.5)
                    chain.Add("STO");
                Console.WriteLine($"  job {i}: {inst.JobLabel[i]} (mat {inst.JobMaterial[i]}, due {inst.d_i[i]:F0}s) remaining: {string.Join("->", chain)}");
            }

            Console.WriteLine("--------------------------------------------------");
            int noImprove = RunSettings.EffectiveNoImprovementLimit;
            int coolingBudget = evaluations > 0 ? evaluations : 200000;
            if (evaluations <= 0)
            {
                if (noImprove <= 0)
                {
                    evaluations = 200000;
                    Console.WriteLine("NOTE: 평가 예산과 미개선 종료가 둘 다 꺼져 있어 정지 조건이 없습니다." + " 평가 예산을 200000 으로 되돌립니다.");
                }
                else
                {
                    evaluations = int.MaxValue;
                    Console.WriteLine($"NOTE: 평가 예산 무제한 - 연속 {noImprove} 회 미개선에서만 멈춥니다.");
                }
            }

            if (mode == "params")
            {
                PrintParameters(inst);
                return 0;
            }

            if (mode == "wipcheck")
            {
                return RunWipCheck(scenarioPath, configPath);
            }

            if (mode == "desrollout")
            {
                return RunDesRollout(scenarioPath, configPath);
            }

            if (mode == "exportinst")
            {
                InstanceExport.Write(inst, scenarioPath);
                return 0;
            }

            if (mode == "exportvc")
            {
                return RunExportVc(scenarioPath, configPath);
            }

            if (mode == "exportlayout")
            {
                return RunExportLayout(scenarioPath, configPath);
            }

            if (mode == "decodeseq")
            {
                string target = Environment.GetEnvironmentVariable("PDP_DECODESEQ_TARGET") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".", "rulelogs", "rollouts");
                return RunDecodeSeq(scenarioPath, target, configPath);
            }

            Console.WriteLine("GREEDY INITIAL SOLUTION (Algorithm 2, buffer-extended)");
            SequenceSolution initial = InitialSolutionGenerator.Generate(inst.numL, inst.r, inst.n, inst.m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.AsrsPinnedPrefix, inst.MachineReadyAt, inst.Layout, inst.AsrsInBufferCapacity);
            string? seedSeqPath = Environment.GetEnvironmentVariable("PDP_INITIAL_SEQ");
            if (!string.IsNullOrEmpty(seedSeqPath))
            {
                SequenceSolution? seeded = LoadSequenceSolution(inst, seedSeqPath, out string? seqError);
                if (seeded == null)
                {
                    Console.WriteLine("PDP_INITIAL_SEQ 로드 실패 (" + seedSeqPath + "): " + seqError);
                    return 1;
                }

                Console.WriteLine("INITIAL SOLUTION REPLACED FROM SEQUENCE FILE: " + seedSeqPath);
                initial = seeded;
            }

            string seedPolicy = Environment.GetEnvironmentVariable("PDP_INITIAL_POLICY") ?? RunSettings.InitialPolicy;
            if (!string.IsNullOrEmpty(seedPolicy) && seedPolicy != "greedy" && string.IsNullOrEmpty(seedSeqPath))
            {
                if (seedPolicy != "edd")
                {
                    Console.WriteLine("PDP_INITIAL_POLICY 는 edd/greedy 만 지원: " + seedPolicy);
                    return 1;
                }

                initial = InitialSolutionGenerator.GeneratePolicy(inst.numL, inst.r, inst.n, inst.m, inst.p_ik, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, acts =>
                {
                    int best = 0;
                    double bestKey = double.MaxValue;
                    for (int i = 0; i < acts.Count; i++)
                    {
                        if (inst.d_i[acts[i].Job] < bestKey - 1e-9)
                        {
                            bestKey = inst.d_i[acts[i].Job];
                            best = i;
                        }
                    }

                    return best;
                }, inst.AsrsPinnedPrefix, inst.MachineReadyAt, inst.Layout, inst.AsrsInBufferCapacity);
                Console.WriteLine("INITIAL SOLUTION REPLACED BY DES-EDD POLICY ROLLOUT");
            }

            DecodedSchedule decoded = SolutionDecoder.Decode(initial, inst.numL, inst.r, inst.n, inst.m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: false, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length, asrsPinnedPrefix: inst.AsrsPinnedPrefix);
            decoded.Print();
            SequenceSolution final = initial;
            DecodedSchedule finalSchedule = decoded;
            double greedyObj = decoded.TotalTardiness;
            double vnsObj = double.NaN;
            bool isMeta = mode == "alns" || mode == "ig" || mode == "tabu" || mode == "ablation" || mode == "hyb" || mode == "lns";
            if (mode == "hyb" || mode == "lns")
            {
                string logPath2 = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".", "rulelogs", mode + "_s" + seed + ".jsonl");
                Console.WriteLine();
                Console.WriteLine($"{mode.ToUpperInvariant()} (seed={seed}, evaluation budget={evaluations}, " + $"no-improvement limit={noImprove})");
                VnsResult hybRes;
                using (var ruleLog2 = new RuleLog(logPath2))
                {
                    hybRes = mode == "hyb" ? Hyb.Run(initial, inst, seed, evaluations, noImprove, ruleLog2) : Lns.Run(initial, inst, seed, evaluations, noImprove, ruleLog2);
                }

                foreach (string line in hybRes.Log)
                {
                    Console.WriteLine(line);
                }

                Console.WriteLine($"{mode.ToUpperInvariant()} evaluations: {hybRes.Evaluations}, stop: {hybRes.StopReason}");
                Console.WriteLine();
                Console.WriteLine(mode.ToUpperInvariant() + " BEST SOLUTION");
                hybRes.BestSchedule.Print();
                final = hybRes.BestSolution;
                finalSchedule = hybRes.BestSchedule;
                vnsObj = hybRes.BestObjective;
            }
            else if (mode == "alns" || mode == "ig" || mode == "tabu" || mode == "ablation")
            {
                string logPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".", "rulelogs", mode + "_s" + seed + ".jsonl");
                Console.WriteLine();
                Console.WriteLine($"{mode.ToUpperInvariant()} (seed={seed}, evaluation budget={evaluations})");
                Console.WriteLine("Rule-learning log: " + logPath);
                VnsResult meta;
                using (var ruleLog = new RuleLog(logPath))
                {
                    meta = BufferMetaheuristics.Run(mode, initial, seed, evaluations, inst, ruleLog, noImprove, coolingBudget);
                }

                foreach (string line in meta.Log)
                {
                    Console.WriteLine(line);
                }

                Console.WriteLine($"{mode.ToUpperInvariant()} evaluations: {meta.Evaluations}");
                Console.WriteLine();
                Console.WriteLine(mode.ToUpperInvariant() + " BEST SOLUTION");
                meta.BestSchedule.Print();
                final = meta.BestSolution;
                finalSchedule = meta.BestSchedule;
                vnsObj = meta.BestObjective;
            }
            else if (mode != "greedy")
            {
                Console.WriteLine();
                Console.WriteLine($"VNS (seed={seed}, local search={RunSettings.EffectiveLocalSearch}, " + $"stop after {noImprove} non-improving iterations, " + $"evaluation cap={(evaluations == int.MaxValue ? "unlimited" : Convert.ToString(evaluations))}, " + $"parallel={RunSettings.EffectiveParallelism})");
                VnsResult vns = VNS.Run(initial, seed, evaluations, inst.numL, inst.r, inst.n, inst.m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.AsrsPinnedPrefix.Length, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, noImprove, RunSettings.EffectiveParallelism, RunSettings.EffectiveLocalSearch, inst.AsrsPinnedPrefix);
                foreach (string line in vns.Log)
                {
                    Console.WriteLine(line);
                }

                Console.WriteLine($"VNS iterations: {vns.Iterations}, evaluations: {vns.Evaluations}");
                Console.WriteLine();
                Console.WriteLine("VNS BEST SOLUTION");
                vns.BestSchedule.Print();
                final = vns.BestSolution;
                finalSchedule = vns.BestSchedule;
                vnsObj = vns.BestObjective;
            }

            Console.WriteLine();
            Console.WriteLine("FINAL SOLUTION" + (mode == "greedy" ? " (greedy)" : isMeta ? " (" + mode.ToUpperInvariant() + " best)" : " (VNS best)"));
            foreach (string line in final.ResourceSequenceLines())
            {
                Console.WriteLine(line);
            }

            foreach (string line in ResourceTimelineLines(final, finalSchedule, inst.JobLabel, inst.WorkstationLabel, inst.AmrLabel))
            {
                Console.WriteLine(line);
            }

            Console.WriteLine($"Greedy objective (total tardiness): {greedyObj}");
            if (!double.IsNaN(vnsObj))
            {
                Console.WriteLine($"VNS objective (total tardiness): {vnsObj}");
            }

            try
            {
                DecodedSchedule planSchedule = SolutionDecoder.Decode(final, inst.numL, inst.r, inst.n, inst.m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: false, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length, asrsPinnedPrefix: inst.AsrsPinnedPrefix);
                string planPath = PlanWriter.Write(scenarioPath, inst, final, planSchedule, mode);
                Console.WriteLine("P4R plan output: " + planPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("P4R plan writeback FAILED: " + ex.Message);
                return 1;
            }

            string? replayDir = Environment.GetEnvironmentVariable("PDP_REPLAY_DIR");
            if (string.IsNullOrEmpty(replayDir) && !string.IsNullOrEmpty(RunSettings.ReplayExportDir))
            {
                replayDir = RunSettings.ReplayExportDir;
            }

            if (string.IsNullOrEmpty(replayDir))
            {
                replayDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".", "rulelogs", "auto");
            }

            if (!string.IsNullOrEmpty(replayDir))
            {
                Directory.CreateDirectory(replayDir);
                string stem = Path.GetFileNameWithoutExtension(scenarioPath);
                DecodedSchedule greedyNow = SolutionDecoder.Decode(initial, inst.numL, inst.r, inst.n, inst.m, inst.c_h, inst.p_ik, inst.q_i, inst.s_ikh, inst.t_klh, inst.E_ik, inst.R_if, inst.d_i, inst.u_rob_i, inst.u_ws_i, inst.u_asrs_i, inst.W_n_ik, inst.W_r_ih, inst.W_a_if, inst.r_ijfg, inst.s_in_ik, inst.s_out_ik, inst.b_in_k, inst.b_out_k, inst.W_in_ikb, inst.W_out_ikb, inst.MachineReadyAt, inst.Layout, inst.AsrsOutBufferCapacity, inst.AsrsInBufferCapacity, allowMissingJobs: false, asrsPinnedPrefixLength: inst.AsrsPinnedPrefix.Length);
                ReplayExport.Export(inst, initial, greedyNow, Path.Combine(replayDir, $"replay_{stem}_s{seed}_greedy.jsonl"), "greedy");
                if (!ReferenceEquals(final, initial))
                {
                    ReplayExport.Export(inst, final, finalSchedule, Path.Combine(replayDir, $"replay_{stem}_s{seed}_{mode}.jsonl"), mode + " best");
                }

                Console.WriteLine("Replay export: " + Path.Combine(replayDir, $"replay_{stem}_s{seed}_*.jsonl"));
            }

            return 0;
        }

        static void PrintParameters(ScenarioInstance inst)
        {
            int m = inst.m, n = inst.n, r = inst.r;
            string Row(IEnumerable<double> values)
            {
                return "[" + string.Join(", ", values.Select(v => Math.Abs(v - Math.Round(v)) < 1e-9 ? Convert.ToString((long)Math.Round(v)) : v.ToString("F1"))) + "]";
            }

            Console.WriteLine("PARAMETER ARRAYS (as built from the scenario)");
            Console.WriteLine();
            Console.WriteLine("Sizes: m=" + m + " (jobs), n=" + n + " (workstations " + string.Join(",", inst.WorkstationLabel) + "), r=" + r + " (AMRs " + string.Join(",", inst.AmrLabel) + "), numL=" + inst.numL);
            Console.WriteLine("Robot op ids: [0,m)=P+, [m,2m)=D+, [2m,3m)=P-, [3m,4m)=D-   |   AS/RS op ids: [0,m)=출고, [m,2m)=입고");
            Console.WriteLine();
            Console.WriteLine("== E_ik : eligibility (1 = 가공 가능) ==");
            Console.WriteLine(string.Format("{0,-4} {1,-22} {2,-6} {3}", "job", "label", "mat", string.Join(" ", inst.WorkstationLabel.Select(w => "WS" + w))));
            for (int i = 0; i < m; i++)
            {
                var cells = new List<string>();
                for (int k = 0; k < n; k++)
                {
                    cells.Add(string.Format("{0,3}", inst.E_ik[i][k]));
                }

                Console.WriteLine(string.Format("{0,-4} {1,-22} {2,-6} {3}", i, inst.JobLabel[i], inst.JobMaterial[i], string.Join(" ", cells)));
            }

            Console.WriteLine();
            Console.WriteLine("== W_n_ik : 기계/스테이션 고정 (WIP는 한 곳만 1) ==");
            for (int i = 0; i < m; i++)
            {
                var cells = new List<string>();
                for (int k = 0; k < n; k++)
                {
                    cells.Add(string.Format("{0,3}", (int)Math.Round(inst.W_n_ik[i][k])));
                }

                Console.WriteLine(string.Format("{0,-4} {1,-22} {2}", i, inst.JobLabel[i], string.Join(" ", cells)));
            }

            Console.WriteLine();
            Console.WriteLine("== p_ik : 잔여 가공시간(초), 0 = 불가/불필요 ==");
            for (int i = 0; i < m; i++)
            {
                var cells = new List<string>();
                for (int k = 0; k < n; k++)
                {
                    cells.Add(string.Format("{0,8:F0}", inst.p_ik[i][k]));
                }

                Console.WriteLine(string.Format("{0,-4} {1,-22} {2}", i, inst.JobLabel[i], string.Join(" ", cells)));
            }

            Console.WriteLine();
            Console.WriteLine("== u 플래그 : 이 계획구간에 남은 단계 (1 = 수행) ==");
            Console.WriteLine(string.Format("{0,-4} {1,-22} {2,4} {3,4} {4,4} {5,5} {6,4} {7,4} {8,4}", "job", "label", "RET", "P+", "D+", "PROC", "P-", "D-", "STO"));
            for (int i = 0; i < m; i++)
            {
                Console.WriteLine(string.Format("{0,-4} {1,-22} {2,4} {3,4} {4,4} {5,5} {6,4} {7,4} {8,4}", i, inst.JobLabel[i], (int)inst.u_asrs_i[i], (int)inst.u_rob_i[i], (int)inst.u_rob_i[m + i], (int)inst.u_ws_i[i], (int)inst.u_rob_i[2 * m + i], (int)inst.u_rob_i[3 * m + i], (int)inst.u_asrs_i[m + i]));
            }

            Console.WriteLine();
            Console.WriteLine("== W_in_ikb / W_out_ikb : 초기 버퍼 슬롯 점유 ==");
            for (int i = 0; i < m; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    for (int b = 0; b < inst.b_in_k[k]; b++)
                    {
                        if (inst.W_in_ikb[i][k][b] > 0.5)
                        {
                            Console.WriteLine("  W_in [job " + i + " " + inst.JobLabel[i] + "] WS " + inst.WorkstationLabel[k] + " slot " + b);
                        }
                    }

                    for (int b = 0; b < inst.b_out_k[k]; b++)
                    {
                        if (inst.W_out_ikb[i][k][b] > 0.5)
                        {
                            Console.WriteLine("  W_out[job " + i + " " + inst.JobLabel[i] + "] WS " + inst.WorkstationLabel[k] + " slot " + b);
                        }
                    }
                }
            }

            Console.WriteLine("  b_in_k  = " + Row(inst.b_in_k.Select(x => (double)x)) + "   (버퍼 용량)");
            Console.WriteLine("  b_out_k = " + Row(inst.b_out_k.Select(x => (double)x)));
            Console.WriteLine();
            Console.WriteLine("== W_r_ih : AMR 초기 적재 (로봇 op -> AMR) ==");
            for (int op = 0; op < 4 * m; op++)
            {
                for (int h = 0; h < r; h++)
                {
                    if (inst.W_r_ih[op][h] > 0.5 && (op >= m && op < 2 * m || op >= 3 * m))
                    {
                        int job = op < 2 * m ? op - m : op - 3 * m;
                        string kind = op < 2 * m ? "D+ (배송 대기)" : "D- (반송 중)";
                        Console.WriteLine("  robot op " + op + " = " + kind + " of job " + job + " (" + inst.JobLabel[job] + ") -> AMR " + inst.AmrLabel[h]);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("== R_if : 출고 셀 (출고 필요 job만) ==");
            for (int i = 0; i < m; i++)
            {
                for (int f = 0; f < inst.numL; f++)
                {
                    if (inst.R_if[i][f] > 0.5)
                    {
                        Console.WriteLine("  job " + i + " (" + inst.JobLabel[i] + ", " + inst.JobMaterial[i] + ") <- 셀 index " + f);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("== 기타 ==");
            Console.WriteLine("  d_i (납기)      = " + Row(inst.d_i));
            Console.WriteLine("  c_h (AMR 용량)  = " + Row(inst.c_h));
            Console.WriteLine("  MachineReadyAt  = " + Row(inst.MachineReadyAt) + "   (고장 수리 완료시각)");
            Console.WriteLine("  AsrsPinnedPrefix= [" + string.Join(", ", inst.AsrsPinnedPrefix) + "]   (진행 중 AS/RS 작업, 시퀀스 선두 고정)");
            Console.WriteLine("  AS/RS 이송점 버퍼 용량: 반출 " + (inst.AsrsOutBufferCapacity == int.MaxValue ? "무제한" : Convert.ToString(inst.AsrsOutBufferCapacity)) + " / 반입 " + (inst.AsrsInBufferCapacity == int.MaxValue ? "무제한" : Convert.ToString(inst.AsrsInBufferCapacity)));
            Console.WriteLine("  s_in / s_out    = " + inst.s_in_ik[0][0].ToString("F0") + " / " + inst.s_out_ik[0][0].ToString("F0") + " 초 (기계 적재/하역)");
            Console.WriteLine();
            Console.WriteLine("== t_klh : AMR 이동시간 (초) ==");
            {
                var lay = inst.Layout;
                var names = new List<string>();
                for (int k = 0; k < lay.Count; k++)
                {
                    names.Add(lay.Describe(k).Replace("WS", "").Replace(" ", ""));
                }

                for (int h = 0; h < r; h++)
                {
                    Console.WriteLine("  AMR " + inst.AmrLabel[h]);
                    Console.Write(string.Format("    {0,-10}", ""));
                    for (int l = 0; l < lay.Count; l++)
                    {
                        Console.Write(string.Format("{0,9}", names[l]));
                    }

                    Console.WriteLine();
                    for (int k = 0; k < lay.Count; k++)
                    {
                        Console.Write(string.Format("    {0,-10}", names[k]));
                        for (int l = 0; l < lay.Count; l++)
                        {
                            Console.Write(string.Format("{0,9:F2}", inst.t_klh[k][l][h]));
                        }

                        Console.WriteLine();
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("== r_ijfg : AS/RS 핸들링 시간 (셀 위치·직전작업 의존) ==");
            {
                int nA = 2 * m + 2;
                int L = inst.numL;
                double lo = double.MaxValue, hi = 0, sum = 0;
                int cnt = 0;
                for (int i = 0; i < nA; i++)
                {
                    for (int j = 0; j < nA; j++)
                    {
                        for (int f = 0; f < L; f++)
                        {
                            for (int g = 0; g < L; g++)
                            {
                                double v = inst.r_ijfg[i][j][f][g];
                                if (v <= 0)
                                {
                                    continue;
                                }

                                lo = Math.Min(lo, v);
                                hi = Math.Max(hi, v);
                                sum += v;
                                cnt++;
                            }
                        }
                    }
                }

                Console.WriteLine(string.Format("  0이 아닌 항목 {0:N0}개   최소 {1:F2}s   평균 {2:F2}s   최대 {3:F2}s", cnt, cnt > 0 ? lo : 0, cnt > 0 ? sum / cnt : 0, hi));
                int retr = 0, stor = m;
                Console.WriteLine("  예시: 직전=출고 job0, 다음=출고 job1 일 때 목적 셀별 시간");
                Console.WriteLine(string.Format("    {0,-22} {1,10}", "목적 셀 (row,col,tier)", "r (초)"));
                int[] probe =
                {
                    0,
                    1,
                    9,
                    10,
                    40,
                    41,
                    80,
                    120,
                    159
                };
                foreach (int g in probe)
                {
                    if (g >= L)
                    {
                        continue;
                    }

                    int tier = g / 40, rem = g % 40, col = rem / 10, row = rem % 10;
                    Console.WriteLine(string.Format("    idx {0,-3} (r{1},c{2},t{3}){4,-6} {5,10:F2}", g, row, col, tier, "", inst.r_ijfg[retr][1][0][g]));
                }
            }

            Console.WriteLine();
            Console.WriteLine("== E 대비 W 정합성 검사 (WIP가 적격 스테이션에 있는가) ==");
            int bad = 0;
            for (int i = 0; i < m; i++)
            {
                for (int k = 0; k < n; k++)
                {
                    bool pinned = inst.W_n_ik[i][k] > 0.5;
                    bool onlyHere = pinned;
                    for (int k2 = 0; k2 < n; k2++)
                    {
                        if (k2 != k && inst.W_n_ik[i][k2] > 0.5)
                        {
                            onlyHere = false;
                        }
                    }

                    if (onlyHere && inst.E_ik[i][k] != 1)
                    {
                        Console.WriteLine("  [불일치] job " + i + " (" + inst.JobLabel[i] + ") 는 WS " + inst.WorkstationLabel[k] + " 에 고정되어 있으나 E_ik = 0");
                        bad++;
                    }
                }
            }

            Console.WriteLine(bad == 0 ? "  OK: 고정된 모든 WIP가 적격 스테이션에 있음 (W ⊆ E)" : "  불일치 " + bad + " 건");
        }

        static List<string> ResourceTimelineLines(SequenceSolution sol, DecodedSchedule sch, string[]? jobLabels, string[]? wsLabels, string[]? amrLabels)
        {
            var lines = new List<string>();
            int m = sol.NumOperations;
            int n = sol.NumWorkstations;
            int r = sol.NumRobots;
            string T(double a, double b)
            {
                return string.Format("[start={0,10:F1}, end={1,10:F1}]  dur {2,8:F1}", a, b, b - a);
            }

            string JL(int job)
            {
                string lab = jobLabels != null && job < jobLabels.Length ? jobLabels[job] : "";
                return string.Format("job {0,-3} {1,-22}", job, lab);
            }

            string WL(int k)
            {
                return wsLabels != null && k < wsLabels.Length ? wsLabels[k] : Convert.ToString(k);
            }

            lines.Add("--------------------------------------------------");
            lines.Add("RESOURCE TIMELINE (시퀀스 해석 + 시각)");
            lines.Add("");
            lines.Add("AS/RS");
            if (sol.AsrsSequence.Count == 0)
            {
                lines.Add("  (없음)");
            }

            int step = 0;
            double prevEnd = 0.0;
            foreach (int op in sol.AsrsSequence)
            {
                step++;
                int job = op < m ? op : op - m;
                string kind = op < m ? "출고 RET" : "입고 STO";
                double a = sch.AsrsStart[op];
                double b = sch.AsrsCompletion[op];
                string idle = a - prevEnd > 0.05 ? string.Format("   idle {0,7:F1}", a - prevEnd) : "";
                lines.Add(string.Format("  {0,3}. {1}  {2}  cell {3,-4} {4}{5}", step, kind, JL(job), sol.AsrsLocation[op], T(a, b), idle));
                if (op < m && sol.RobotOpAmr[op] >= 0)
                {
                    double pickup = sch.RobotCompletion[op];
                    lines.Add(string.Format("       반출버퍼 점유     {0,10:F1} -> {1,10:F1}   체류 {2,9:F1}  (P+ 픽업까지)", b, pickup, pickup - b));
                }
                else if (op >= m)
                {
                    double entry = sol.RobotOpAmr[3 * m + job] >= 0 ? sch.RobotCompletion[3 * m + job] : 0.0;
                    lines.Add(string.Format("       반입버퍼 점유     {0,10:F1} -> {1,10:F1}   체류 {2,9:F1}  ({3})", entry, b, b - entry, sol.RobotOpAmr[3 * m + job] >= 0 ? "D- 반입부터" : "초기 대기분"));
                }

                prevEnd = b;
            }

            for (int h = 0; h < r; h++)
            {
                lines.Add("");
                lines.Add("AMR " + Convert.ToString(h) + (amrLabels != null && h < amrLabels.Length ? "  [" + amrLabels[h] + "]" : ""));
                if (sol.AmrSequences[h].Count == 0)
                {
                    lines.Add("  (유휴)");
                    continue;
                }

                step = 0;
                prevEnd = 0.0;
                var route = new HashSet<int>(sol.AmrSequences[h]);
                double load = 0.0;
                foreach (int op in sol.AmrSequences[h])
                {
                    if (op >= m && op < 2 * m && !route.Contains(op - m))
                        load += 1;
                    if (op >= 3 * m && !route.Contains(op - m))
                        load += 1;
                }

                foreach (int op in sol.AmrSequences[h])
                {
                    step++;
                    int job = op < m ? op : op < 2 * m ? op - m : op < 3 * m ? op - 2 * m : op - 3 * m;
                    string kind = op < m ? "P+ 출고픽업" : op < 2 * m ? "D+ WS배송  " : op < 3 * m ? "P- WS픽업  " : "D- 입고반송";
                    load += op < m || (op >= 2 * m && op < 3 * m) ? 1 : -1;
                    string where = op < m ? "ASRS_OUT" : op >= 3 * m ? "ASRS_IN " : (op < 2 * m ? "IN  WS" : "OUT WS") + WL(sol.JobWorkstation[job]);
                    double a = sch.RobotStart[op];
                    double b = sch.RobotCompletion[op];
                    string move = a - prevEnd > 0.05 ? string.Format("   이동+대기 {0,7:F1}", a - prevEnd) : "";
                    lines.Add(string.Format("  {0,3}. {1}  {2}  @{3,-9} {4}  적재 {5}{6}", step, kind, JL(job), where, T(a, b), load, move));
                    prevEnd = b;
                }
            }

            for (int k = 0; k < n; k++)
            {
                lines.Add("");
                lines.Add("WS " + WL(k) + " (기계 + 버퍼)");
                if (sol.WorkstationSequences[k].Count == 0)
                {
                    lines.Add("  (유휴)");
                    continue;
                }

                step = 0;
                prevEnd = 0.0;
                foreach (int job in sol.WorkstationSequences[k])
                {
                    step++;
                    double a = sch.ProcessStart[job];
                    double b = sch.ProcessCompletion[job];
                    string idle = a - prevEnd > 0.05 ? string.Format("   idle {0,7:F1}", a - prevEnd) : "";
                    lines.Add(string.Format("  {0,3}. 가공     {1}  {2}{3}", step, JL(job), T(a, b), idle));
                    int sin = sol.InputSlot[job];
                    if (sin >= 0)
                    {
                        lines.Add(string.Format("       입력버퍼 slot {0}          진입 {1,10:F1}  ->  반출 {2,10:F1}   체류 {3,8:F1}", sin, sch.BufferInEntry[job], a, a - sch.BufferInEntry[job]));
                    }

                    int sout = sol.OutputSlot[job];
                    if (sout >= 0)
                    {
                        double leave = sol.RobotOpAmr[2 * m + job] >= 0 ? sch.RobotCompletion[2 * m + job] : double.NaN;
                        lines.Add(string.Format("       출력버퍼 slot {0}          진입 {1,10:F1}  ->  반출 {2}   체류 {3}", sout, sch.BufferOutEntry[job], double.IsNaN(leave) ? "      (끝)" : string.Format("{0,10:F1}", leave), double.IsNaN(leave) ? "     -" : string.Format("{0,8:F1}", leave - sch.BufferOutEntry[job])));
                    }

                    prevEnd = b;
                }
            }

            lines.Add("--------------------------------------------------");
            return lines;
        }

        static void PrintWipSummary(int m, int r, int n, int numL, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] R_if)
        {
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("WIP INPUT SUMMARY");
            for (int i = 0; i < m; i++)
            {
                var current = new List<string>();
                var skipped = new List<string>();
                var remaining = new List<string>();
                if (u_asrs_i[i] < 0.5)
                {
                    skipped.Add("ASRS RETRIEVAL");
                }

                if (u_rob_i[i] < 0.5)
                {
                    skipped.Add("ROBOT P+");
                }

                if (u_rob_i[m + i] < 0.5)
                {
                    skipped.Add("ROBOT D+");
                }

                if (u_ws_i[i] < 0.5)
                {
                    skipped.Add("PROCESS");
                }

                if (u_rob_i[2 * m + i] < 0.5)
                {
                    skipped.Add("ROBOT P-");
                }

                if (u_rob_i[3 * m + i] < 0.5)
                {
                    skipped.Add("ROBOT D-");
                }

                if (u_asrs_i[m + i] < 0.5)
                {
                    skipped.Add("ASRS STORAGE");
                }

                for (int h = 0; h < r; h++)
                {
                    if (u_rob_i[m + i] > 0.5 && u_rob_i[i] < 0.5 && W_r_ih[m + i][h] > 0.5)
                    {
                        current.Add("loaded on AMR " + h + " for D+");
                    }

                    if (u_rob_i[3 * m + i] > 0.5 && u_rob_i[2 * m + i] < 0.5 && W_r_ih[3 * m + i][h] > 0.5)
                    {
                        current.Add("loaded on AMR " + h + " for D-");
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    if (u_ws_i[i] > 0.5 && u_rob_i[m + i] < 0.5 && W_n_ik[i][k] > 0.5)
                    {
                        current.Add("processing at workstation " + k);
                    }

                    if (u_ws_i[i] < 0.5 && u_rob_i[2 * m + i] > 0.5 && W_n_ik[i][k] > 0.5)
                    {
                        current.Add("waiting at workstation " + k + " for P-");
                    }
                }

                if (u_asrs_i[i] > 0.5)
                {
                    var retrievalLocations = new List<string>();
                    for (int f = 0; f < numL; f++)
                    {
                        if (R_if[i][f] > 0.5)
                        {
                            retrievalLocations.Add(Convert.ToString(f));
                        }
                    }

                    if (retrievalLocations.Count > 0)
                    {
                        current.Add("waiting in ASRS location " + string.Join(",", retrievalLocations));
                    }
                }

                if (u_asrs_i[i] > 0.5)
                {
                    remaining.Add("ASRS RETRIEVAL");
                }

                if (u_rob_i[i] > 0.5)
                {
                    remaining.Add("ROBOT P+");
                }

                if (u_rob_i[m + i] > 0.5)
                {
                    remaining.Add("ROBOT D+");
                }

                if (u_ws_i[i] > 0.5)
                {
                    remaining.Add("PROCESS");
                }

                if (u_rob_i[2 * m + i] > 0.5)
                {
                    remaining.Add("ROBOT P-");
                }

                if (u_rob_i[3 * m + i] > 0.5)
                {
                    remaining.Add("ROBOT D-");
                }

                if (u_asrs_i[m + i] > 0.5)
                {
                    remaining.Add("ASRS STORAGE");
                }

                bool isCompleted = remaining.Count == 0;
                bool isWip = skipped.Count > 0;
                string status = isCompleted ? "DONE" : (isWip ? "WIP" : "NORMAL");
                string currentText = isCompleted ? "completed before horizon" : (current.Count > 0 ? string.Join("; ", current) : "no current location");
                string skippedText = skipped.Count > 0 ? string.Join(", ", skipped) : "none";
                string remainingText = remaining.Count > 0 ? string.Join(" -> ", remaining) : "none";
                Console.WriteLine("OPERATION " + i + " [" + status + "]");
                Console.WriteLine("  current   : " + currentText);
                Console.WriteLine("  skipped   : " + skippedText);
                Console.WriteLine("  remaining : " + remainingText);
            }

            Console.WriteLine("--------------------------------------------------");
        }
    }

#if HAS_CPLEX
    public class MilpSolveResult
    {
        public bool IsSolved { get; set; }
        public string Status { get; set; } = "";
        public double ObjectiveValue { get; set; }
        public List<string> WipTimingSummaryLines { get; set; } = new List<string>();
        public MilpExtractedSolution? Extracted { get; set; }
    }

#endif
    static class Metabase
    {
#if HAS_CPLEX
        public static MilpSolveResult ExtractMilpSolveResult(object cplex, int numL, int r, int n, int m, object Xr_ijklh, object Yn_ijk, object Ya_ijfg, object Cr_ih, object Lr_ih, object Cn_ik, object Ln_ik, object Ca_if, object La_if, object Ain_ikb, object Aout_ikb, object Yin_ijkb, object Yout_ijkb, object Lin_ik, object Cin_ik, object Lout_ik, object Cout_ik, int[] b_in_k, int[] b_out_k, double[] d_i)
        {
            dynamic solver = cplex;
            var solveResult = new MilpSolveResult
            {
                IsSolved = true,
                Status = Convert.ToString(solver.GetStatus()) ?? "Solved",
                ObjectiveValue = Rounding.Value(Convert.ToDouble(solver.GetObjValue()))
            };
            solveResult.Extracted = MilpSequenceVerifier.Extract((ILOG.CPLEX.Cplex)cplex, numL, r, n, m, (ILOG.Concert.IIntVar[][][][][])Xr_ijklh, (ILOG.Concert.IIntVar[][][])Yn_ijk, (ILOG.Concert.IIntVar[][][][])Ya_ijfg, (ILOG.Concert.INumVar[][])Cr_ih, (ILOG.Concert.INumVar[][])Lr_ih, (ILOG.Concert.INumVar[][])Cn_ik, (ILOG.Concert.INumVar[][])Ln_ik, (ILOG.Concert.INumVar[][])Ca_if, (ILOG.Concert.INumVar[][])La_if, (ILOG.Concert.IIntVar[][][])Ain_ikb, (ILOG.Concert.IIntVar[][][])Aout_ikb, (ILOG.Concert.IIntVar[][][][])Yin_ijkb, (ILOG.Concert.IIntVar[][][][])Yout_ijkb, (ILOG.Concert.INumVar[][])Lin_ik, (ILOG.Concert.INumVar[][])Cin_ik, (ILOG.Concert.INumVar[][])Lout_ik, (ILOG.Concert.INumVar[][])Cout_ik, b_in_k, b_out_k, d_i);
            return solveResult;
        }
#endif
    }

    static class Rounding
    {
        const int Digits = 4;
        public static double Value(double value)
        {
            return Math.Round(value, Digits, MidpointRounding.AwayFromZero);
        }

        public static double Add(double left, double right)
        {
            return Value(left + right);
        }

        public static double Subtract(double left, double right)
        {
            return Value(left - right);
        }

        public static double Multiply(double left, double right)
        {
            return Value(left * right);
        }

        public static double Divide(double left, double right)
        {
            return Value(left / right);
        }

        public static double Max(double left, double right)
        {
            return Value(Math.Max(left, right));
        }
    }
}

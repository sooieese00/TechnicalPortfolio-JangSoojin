using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using ILOG.Concert;
using ILOG.CPLEX;

namespace PDP_ASRS_WIP
{
    class MILP
    {
        public static MilpSolveResult MILP_cplex(int numL, int r, int n, int m, int[] P_plus, int[] D_plus, int[] P_minus, int[] D_minus, int[] R, int[] R_combine, int[] N, int[] N_plus, int[] N_minus, double[] c_h, double[][] p_ik, double[] q_i, double[][][] s_ikh, double[][][] t_klh, int[][] E_ik, double[][] R_if, double[] d_i, double[] u_rob_i, double[] u_ws_i, double[] u_asrs_i, double[][] W_n_ik, double[][] W_r_ih, double[][] W_a_if, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[][][] W_in_ikb, double[][][] W_out_ikb)
        {
            var solveResult = new MilpSolveResult
            {
                IsSolved = false,
                Status = "NotSolved"
            };
            double result = 0;
            double horizon = 0.0;
            for (int i = 0; i < 2 * m; i++)
            {
                double longestHandling = 0.0;
                for (int j = 0; j < 2 * m + 2; j++)
                {
                    for (int f = 0; f < numL + 2; f++)
                    {
                        for (int g = 0; g < numL + 2; g++)
                        {
                            longestHandling = Math.Max(longestHandling, r_ijfg[j][i][f][g]);
                        }
                    }
                }

                horizon = Rounding.Add(horizon, longestHandling);
            }

            double longestTravel = 0.0;
            for (int k = 0; k < n + 2; k++)
            {
                for (int l = 0; l < n + 2; l++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        longestTravel = Math.Max(longestTravel, t_klh[k][l][h]);
                    }
                }
            }

            for (int i = 0; i < 4 * m; i++)
            {
                double longestService = 0.0;
                for (int k = 0; k < n + 2; k++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        longestService = Math.Max(longestService, s_ikh[i][k][h]);
                    }
                }

                horizon = Rounding.Add(horizon, Rounding.Add(longestService, longestTravel));
            }

            for (int i = 0; i < m; i++)
            {
                double longestProcessing = 0.0;
                for (int k = 0; k < n; k++)
                {
                    longestProcessing = Math.Max(longestProcessing, Rounding.Add(Rounding.Add(s_in_ik[i][k], p_ik[i][k]), s_out_ik[i][k]));
                }

                horizon = Rounding.Add(horizon, longestProcessing);
            }

            double big_M = Rounding.Max(Rounding.Multiply(horizon, 2.0), 1.0);
            try
            {
                Cplex cplex = new Cplex();
                cplex.SetParam(ILOG.CPLEX.Cplex.Param.TimeLimit, 7200);
                cplex.SetParam(ILOG.CPLEX.Cplex.Param.MIP.Tolerances.Integrality, 0.0);
                INumVar[][] Cn_ik = new INumVar[m + 2][];
                INumVar[][] Ln_ik = new INumVar[m + 2][];
                IIntVar[][][] Yn_ijk = new IIntVar[m + 2][][];
                INumVar[][] Q_ih = new INumVar[4 * m + 2][];
                INumVar[][] Cr_ih = new INumVar[4 * m + 2][];
                INumVar[][] Lr_ih = new INumVar[4 * m + 2][];
                IIntVar[][][][][] Xr_ijklh = new IIntVar[4 * m + 2][][][][];
                INumVar[] T_i = cplex.NumVarArray(m, 0, double.MaxValue);
                INumVar[][] Ca_if = new INumVar[2 * m + 2][];
                INumVar[][] La_if = new INumVar[2 * m + 2][];
                IIntVar[][][][] Ya_ijfg = new IIntVar[2 * m + 2][][][];
                for (int i = 0; i < m + 2; i++)
                {
                    Cn_ik[i] = cplex.NumVarArray(n + 2, 0, double.MaxValue);
                    Ln_ik[i] = cplex.NumVarArray(n + 2, 0, double.MaxValue);
                    Yn_ijk[i] = new IIntVar[m + 2][];
                    for (int j = 0; j < m + 2; j++)
                    {
                        Yn_ijk[i][j] = cplex.IntVarArray(n, 0, 1);
                    }
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    Cr_ih[i] = cplex.NumVarArray(r, 0, double.MaxValue);
                    Lr_ih[i] = cplex.NumVarArray(r, 0, double.MaxValue);
                    Q_ih[i] = cplex.NumVarArray(r, 0, double.MaxValue);
                    Xr_ijklh[i] = new IIntVar[4 * m + 2][][][];
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        Xr_ijklh[i][j] = new IIntVar[n + 2][][];
                        for (int k = 0; k < n + 2; k++)
                        {
                            Xr_ijklh[i][j][k] = new IIntVar[n + 2][];
                            for (int l = 0; l < n + 2; l++)
                            {
                                Xr_ijklh[i][j][k][l] = cplex.IntVarArray(r, 0, 1);
                            }
                        }
                    }
                }

                for (int i = 0; i < 2 * m + 2; i++)
                {
                    Ca_if[i] = cplex.NumVarArray(numL + 2, 0, double.MaxValue);
                    La_if[i] = cplex.NumVarArray(numL + 2, 0, double.MaxValue);
                    Ya_ijfg[i] = new IIntVar[2 * m + 2][][];
                    for (int j = 0; j < 2 * m + 2; j++)
                    {
                        Ya_ijfg[i][j] = new IIntVar[numL + 2][];
                        for (int f = 0; f < numL + 2; f++)
                        {
                            Ya_ijfg[i][j][f] = cplex.IntVarArray(numL + 2, 0, 1);
                        }
                    }
                }

                INumVar[][] Lin_ik = new INumVar[m][];
                INumVar[][] Cin_ik = new INumVar[m][];
                INumVar[][] Lout_ik = new INumVar[m][];
                INumVar[][] Cout_ik = new INumVar[m][];
                IIntVar[][][] Ain_ikb = new IIntVar[m][][];
                IIntVar[][][] Aout_ikb = new IIntVar[m][][];
                IIntVar[][][][] Yin_ijkb = new IIntVar[m][][][];
                IIntVar[][][][] Yout_ijkb = new IIntVar[m][][][];
                for (int i = 0; i < m; i++)
                {
                    Lin_ik[i] = cplex.NumVarArray(n, 0, double.MaxValue);
                    Cin_ik[i] = cplex.NumVarArray(n, 0, double.MaxValue);
                    Lout_ik[i] = cplex.NumVarArray(n, 0, double.MaxValue);
                    Cout_ik[i] = cplex.NumVarArray(n, 0, double.MaxValue);
                    Ain_ikb[i] = new IIntVar[n][];
                    Aout_ikb[i] = new IIntVar[n][];
                    Yin_ijkb[i] = new IIntVar[m][][];
                    Yout_ijkb[i] = new IIntVar[m][][];
                    for (int k = 0; k < n; k++)
                    {
                        Ain_ikb[i][k] = cplex.IntVarArray(b_in_k[k], 0, 1);
                        Aout_ikb[i][k] = cplex.IntVarArray(b_out_k[k], 0, 1);
                    }

                    for (int j = 0; j < m; j++)
                    {
                        Yin_ijkb[i][j] = new IIntVar[n][];
                        Yout_ijkb[i][j] = new IIntVar[n][];
                        for (int k = 0; k < n; k++)
                        {
                            Yin_ijkb[i][j][k] = cplex.IntVarArray(b_in_k[k], 0, 1);
                            Yout_ijkb[i][j][k] = cplex.IntVarArray(b_out_k[k], 0, 1);
                        }
                    }
                }

                INumExpr T_i_objective = cplex.NumExpr();
                for (int i = 0; i < m; i++)
                {
                    T_i_objective = cplex.Sum(T_i_objective, T_i[i]);
                }

                cplex.AddMinimize(T_i_objective);
                for (int i = 0; i < 4 * m + 2; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[i][i][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[i][4 * m][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int j = 0; j < 4 * m + 2; j++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[4 * m + 1][j][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int i = 0; i < 4 * m; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            for (int l = 0; l < n + 2; l++)
                            {
                                for (int h = 0; h < r; h++)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j][k][l][h]);
                                }
                            }
                        }
                    }

                    cplex.AddEq(sum_X, u_rob_i[i]);
                }

                for (int i = 0; i < 3 * m; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[i + m][i][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int i = 0; i < 2 * m; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[i + 2 * m][i][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int i = 0; i < m; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[i + 3 * m][i][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int i = 0; i < m; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j][n][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, u_rob_i[i]);
                }

                for (int i = 3 * m; i < 4 * m; i++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j][n][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, u_rob_i[i]);
                }

                for (int j = m; j < 4 * m + 2; j++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 1; k++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[4 * m][j][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int h = 0; h < r; h++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        if (j < 4 * m || j == 4 * m + 1)
                        {
                            for (int l = 0; l < n + 2; l++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[4 * m][j][n + 1][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 1);
                }

                for (int i = 0; i < 4 * m; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        IIntExpr sum_X = cplex.IntExpr();
                        for (int l = 0; l < n + 2; l++)
                        {
                            sum_X = cplex.Sum(sum_X, Xr_ijklh[4 * m][i][n + 1][l][h]);
                        }

                        cplex.AddLe(sum_X, W_r_ih[i][h]);
                    }
                }

                for (int i = 0; i < 4 * m; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        IIntExpr sum_X = cplex.IntExpr();
                        for (int l = 0; l < n + 2; l++)
                        {
                            sum_X = cplex.Sum(sum_X, Xr_ijklh[4 * m][i][n + 1][l][h]);
                        }

                        cplex.AddLe(sum_X, u_rob_i[i]);
                    }
                }

                for (int h = 0; h < r; h++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int i = 3 * m; i < 4 * m + 1; i++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            sum_X = cplex.Sum(sum_X, Xr_ijklh[i][4 * m + 1][k][n + 1][h]);
                        }
                    }

                    cplex.AddEq(sum_X, 1);
                }

                for (int h = 0; h < r; h++)
                {
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int k = 0; k < n + 1; k++)
                    {
                        for (int j = 0; j < m; j++)
                        {
                            for (int l = 0; l < n + 2; l++)
                            {
                                sum_X = cplex.Sum(sum_X, Xr_ijklh[4 * m][j][k][l][h]);
                            }
                        }
                    }

                    cplex.AddEq(sum_X, 0);
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int j = m; j < 4 * m + 2; j++)
                    {
                        if (i != 4 * m && j != 4 * m + 1)
                        {
                            IIntExpr sum_X = cplex.IntExpr();
                            for (int l = 0; l < n + 2; l++)
                            {
                                for (int h = 0; h < r; h++)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j][n + 1][l][h]);
                                }
                            }

                            cplex.AddEq(sum_X, 0);
                        }
                    }
                }

                for (int i = m; i < 2 * m; i++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        IIntExpr sum_X_i = cplex.IntExpr();
                        IIntExpr sum_X_i_plus_m = cplex.IntExpr();
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    sum_X_i = cplex.Sum(sum_X_i, Xr_ijklh[i][j][k][l][h]);
                                }
                            }
                        }

                        for (int j_prime = 0; j_prime < 4 * m + 2; j_prime++)
                        {
                            for (int h_prime = 0; h_prime < r; h_prime++)
                            {
                                for (int l_prime = 0; l_prime < n + 2; l_prime++)
                                {
                                    sum_X_i_plus_m = cplex.Sum(sum_X_i_plus_m, Xr_ijklh[i + m][j_prime][k][l_prime][h_prime]);
                                }
                            }
                        }

                        double stationWipForPickup = Rounding.Multiply(Rounding.Multiply(W_n_ik[i - m][k], Rounding.Subtract(1.0, u_rob_i[i])), u_rob_i[i + m]);
                        cplex.AddEq(cplex.Sum(cplex.Sum(sum_X_i, stationWipForPickup), cplex.Prod(-1, sum_X_i_plus_m)), 0);
                    }
                }

                for (int i = m; i < 2 * m; i++)
                {
                    for (int k = 0; k < n + 2; k++)
                    {
                        IIntExpr sum_X = cplex.IntExpr();
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            for (int l = 0; l < n + 2; l++)
                            {
                                for (int h = 0; h < r; h++)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j][k][l][h]);
                                }
                            }
                        }

                        cplex.AddLe(sum_X, Rounding.Multiply(u_rob_i[i], E_ik[i - m][k]));
                    }
                }

                for (int i = 0; i < 4 * m; i++)
                {
                    for (int k = 0; k < n + 2; k++)
                    {
                        for (int h = 0; h < r; h++)
                        {
                            IIntExpr sum_X_Left = cplex.IntExpr();
                            IIntExpr sum_X_Right = cplex.IntExpr();
                            for (int j = 0; j < 4 * m + 2; j++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    sum_X_Left = cplex.Sum(sum_X_Left, Xr_ijklh[i][j][k][l][h]);
                                }
                            }

                            for (int j_prime = 0; j_prime < 4 * m + 2; j_prime++)
                            {
                                for (int l_prime = 0; l_prime < n + 2; l_prime++)
                                {
                                    sum_X_Right = cplex.Sum(sum_X_Right, Xr_ijklh[j_prime][i][l_prime][k][h]);
                                }
                            }

                            cplex.AddEq(cplex.Sum(sum_X_Left, cplex.Prod(-1, sum_X_Right)), 0);
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        IIntExpr sum_X_i = cplex.IntExpr();
                        IIntExpr sum_X_i_plus_m = cplex.IntExpr();
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            for (int k = 0; k < n + 2; k++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    sum_X_i = cplex.Sum(sum_X_i, Xr_ijklh[i][j][k][l][h]);
                                }
                            }
                        }

                        for (int j_prime = 0; j_prime < 4 * m + 2; j_prime++)
                        {
                            for (int k_prime = 0; k_prime < n + 2; k_prime++)
                            {
                                for (int l_prime = 0; l_prime < n + 2; l_prime++)
                                {
                                    sum_X_i_plus_m = cplex.Sum(sum_X_i_plus_m, Xr_ijklh[i + m][j_prime][k_prime][l_prime][h]);
                                }
                            }
                        }

                        cplex.AddEq(cplex.Sum(cplex.Sum(sum_X_i, W_r_ih[i + m][h]), cplex.Prod(-1, sum_X_i_plus_m)), 0);
                    }
                }

                for (int i = 2 * m; i < 3 * m; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        IIntExpr sum_X_i = cplex.IntExpr();
                        IIntExpr sum_X_i_plus_m = cplex.IntExpr();
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            for (int k = 0; k < n + 2; k++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    sum_X_i = cplex.Sum(sum_X_i, Xr_ijklh[i][j][k][l][h]);
                                }
                            }
                        }

                        for (int j_prime = 0; j_prime < 4 * m + 2; j_prime++)
                        {
                            for (int k_prime = 0; k_prime < n + 2; k_prime++)
                            {
                                for (int l_prime = 0; l_prime < n + 2; l_prime++)
                                {
                                    sum_X_i_plus_m = cplex.Sum(sum_X_i_plus_m, Xr_ijklh[i + m][j_prime][k_prime][l_prime][h]);
                                }
                            }
                        }

                        cplex.AddEq(cplex.Sum(cplex.Sum(sum_X_i, W_r_ih[i + m][h]), cplex.Prod(-1, sum_X_i_plus_m)), 0);
                    }
                }

                for (int i = m; i < 2 * m; i++)
                {
                    IIntExpr sum_Yn = cplex.IntExpr();
                    for (int k = 0; k < n; k++)
                    {
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            if (j >= m && j < 2 * m)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - m][k]);
                            }
                            else if (j == 4 * m + 1)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - 3 * m][k]);
                            }
                        }
                    }

                    cplex.AddEq(sum_Yn, u_ws_i[i - m]);
                }

                for (int j = m; j < 2 * m; j++)
                {
                    IIntExpr sum_Yn = cplex.IntExpr();
                    for (int l = 0; l < n; l++)
                    {
                        for (int i = 0; i < 4 * m + 2; i++)
                        {
                            if (i >= m && i < 2 * m)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - m][l]);
                            }
                            else if (i == 4 * m)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - 3 * m][j - m][l]);
                            }
                        }
                    }

                    cplex.AddEq(sum_Yn, u_ws_i[j - m]);
                }

                for (int k = 0; k < n; k++)
                {
                    IIntExpr sum_Yn = cplex.IntExpr();
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        if (j >= m && j < 2 * m)
                        {
                            sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[m][j - m][k]);
                        }
                        else if (j == 4 * m + 1)
                        {
                            sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[m][j - 3 * m][k]);
                        }
                    }

                    cplex.AddEq(sum_Yn, 1);
                }

                for (int j = 0; j < m; j++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        cplex.AddLe(Yn_ijk[m][j][k], W_n_ik[j][k]);
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    IIntExpr sum_Yn = cplex.IntExpr();
                    for (int i = 0; i < 4 * m + 2; i++)
                    {
                        if (i >= m && i < 2 * m)
                        {
                            sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][m + 1][k]);
                        }
                        else if (i == 4 * m)
                        {
                            sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - 3 * m][m + 1][k]);
                        }
                    }

                    cplex.AddEq(sum_Yn, 1);
                }

                for (int i = m; i < 2 * m; i++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        IIntExpr sum_Xr = cplex.IntExpr();
                        IIntExpr sum_Yn = cplex.IntExpr();
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    sum_Xr = cplex.Sum(sum_Xr, Xr_ijklh[i][j][k][l][h]);
                                }
                            }
                        }

                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            if (j >= m && j < 2 * m)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - m][k]);
                            }

                            if (j == 4 * m + 1)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - 3 * m][k]);
                            }
                        }

                        double stationWipForProcessing = Rounding.Multiply(Rounding.Multiply(W_n_ik[i - m][k], Rounding.Subtract(1.0, u_rob_i[i])), u_ws_i[i - m]);
                        cplex.AddEq(cplex.Sum(cplex.Sum(sum_Yn, cplex.Prod(-1, sum_Xr)), -1.0 * stationWipForProcessing), 0);
                    }
                }

                for (int j = m; j < 2 * m; j++)
                {
                    for (int l = 0; l < n; l++)
                    {
                        IIntExpr sum_Xr = cplex.IntExpr();
                        IIntExpr sum_Yn = cplex.IntExpr();
                        for (int i = 0; i < 4 * m + 2; i++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                for (int k = 0; k < n + 2; k++)
                                {
                                    sum_Xr = cplex.Sum(sum_Xr, Xr_ijklh[i][j][k][l][h]);
                                }
                            }
                        }

                        for (int i = 0; i < 4 * m + 2; i++)
                        {
                            if (i >= m && i < 2 * m)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - m][l]);
                            }

                            if (i == 4 * m)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - 3 * m][j - m][l]);
                            }
                        }

                        double stationWipForProcessing = Rounding.Multiply(Rounding.Multiply(W_n_ik[j - m][l], Rounding.Subtract(1.0, u_rob_i[j])), u_ws_i[j - m]);
                        cplex.AddEq(cplex.Sum(cplex.Sum(sum_Yn, cplex.Prod(-1, sum_Xr)), -1.0 * stationWipForProcessing), 0);
                    }
                }

                for (int i = m; i < 2 * m; i++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        IIntExpr sum_Yn = cplex.IntExpr();
                        IIntExpr sum_Yn_prime = cplex.IntExpr();
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            if (j >= m && j < 2 * m)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - m][k]);
                            }
                            else if (j == 4 * m + 1)
                            {
                                sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - 3 * m][k]);
                            }
                        }

                        for (int j_prime = 0; j_prime < 4 * m + 2; j_prime++)
                        {
                            if (j_prime >= m && j_prime < 2 * m)
                            {
                                sum_Yn_prime = cplex.Sum(sum_Yn_prime, Yn_ijk[j_prime - m][i - m][k]);
                            }
                            else if (j_prime == 4 * m)
                            {
                                sum_Yn_prime = cplex.Sum(sum_Yn_prime, Yn_ijk[j_prime - 3 * m][i - m][k]);
                            }
                        }

                        cplex.AddGe(cplex.Sum(sum_Yn, cplex.Prod(-1, sum_Yn_prime)), 0);
                    }
                }

                for (int i = 0; i < 2 * m + 1; i++)
                {
                    IIntExpr sum_Ya = cplex.IntExpr();
                    for (int j = 0; j < 2 * m + 2; j++)
                    {
                        if (j != 2 * m)
                        {
                            for (int f = 0; f < numL + 2; f++)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i][j][f][g]);
                                }
                            }
                        }
                    }

                    cplex.AddEq(sum_Ya, u_asrs_i[i]);
                }

                for (int j = 0; j < 2 * m + 2; j++)
                {
                    if (j != 2 * m)
                    {
                        IIntExpr sum_Ya = cplex.IntExpr();
                        for (int i = 0; i < 2 * m + 2; i++)
                        {
                            for (int f = 0; f < numL + 2; f++)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i][j][f][g]);
                                }
                            }
                        }

                        cplex.AddEq(sum_Ya, u_asrs_i[j]);
                    }
                }

                IIntExpr sum_Y_initial2 = cplex.IntExpr();
                for (int j = 0; j < m; j++)
                {
                    for (int g = 0; g < numL; g++)
                    {
                        sum_Y_initial2 = cplex.Sum(sum_Y_initial2, Ya_ijfg[2 * m][j][numL][g]);
                    }
                }

                cplex.AddEq(sum_Y_initial2, 1);
                for (int j = 0; j < m; j++)
                {
                    for (int g = 0; g < numL; g++)
                    {
                        cplex.AddLe(Ya_ijfg[2 * m][j][numL][g], u_asrs_i[j]);
                    }
                }

                for (int j = 0; j < m; j++)
                {
                    for (int g = 0; g < numL; g++)
                    {
                        cplex.AddLe(Ya_ijfg[2 * m][j][numL][g], W_a_if[j][g]);
                    }
                }

                IIntExpr sum_Y_final2 = cplex.IntExpr();
                for (int i = m; i < 2 * m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        sum_Y_final2 = cplex.Sum(sum_Y_final2, Ya_ijfg[i][2 * m + 1][f][numL + 1]);
                    }
                }

                cplex.AddEq(sum_Y_final2, 1);
                for (int i = 0; i < m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        IIntExpr sum_Ya = cplex.IntExpr();
                        for (int j = 0; j < 2 * m; j++)
                        {
                            for (int g = 0; g < numL; g++)
                            {
                                sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i][j][f][g]);
                            }
                        }

                        cplex.AddEq(sum_Ya, R_if[i][f]);
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        if (R_if[i][f] > W_a_if[i][f])
                        {
                            throw new ArgumentException("R_if must be less than or equal to W_a_if for equation (36).");
                        }
                    }
                }

                for (int f = 0; f < numL; f++)
                {
                    IIntExpr sum_Ya = cplex.IntExpr();
                    for (int i = m; i < 2 * m; i++)
                    {
                        for (int j = 0; j < 2 * m + 2; j++)
                        {
                            if (j != 2 * m)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i][j][f][g]);
                                }
                            }
                        }
                    }

                    cplex.AddLe(sum_Ya, 1);
                }

                for (int i = 0; i < 2 * m; i++)
                {
                    IIntExpr sum_Ya = cplex.IntExpr();
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int j = 0; j < 2 * m + 2; j++)
                    {
                        if (j != 2 * m)
                        {
                            for (int f = 0; f < numL; f++)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i][j][f][g]);
                                }
                            }
                        }
                    }

                    for (int j_prime = 0; j_prime < 4 * m + 2; j_prime++)
                    {
                        for (int l = 0; l < n + 2; l++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                if (i < m)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j_prime][n][l][h]);
                                }
                                else if (i >= m)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i + 2 * m][j_prime][n][l][h]);
                                }
                            }
                        }
                    }

                    cplex.AddEq(sum_Ya, sum_X);
                }

                for (int j = 0; j < 2 * m; j++)
                {
                    IIntExpr sum_Ya = cplex.IntExpr();
                    IIntExpr sum_X = cplex.IntExpr();
                    for (int i = 0; i < 2 * m + 1; i++)
                    {
                        for (int f = 0; f < numL + 2; f++)
                        {
                            for (int g = 0; g < numL; g++)
                            {
                                sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i][j][f][g]);
                            }
                        }
                    }

                    for (int i_prime = 0; i_prime < 4 * m + 2; i_prime++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                if (j < m)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i_prime][j][k][n][h]);
                                }
                                else if (j >= m)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i_prime][j + 2 * m][k][n][h]);
                                }
                            }
                        }
                    }

                    cplex.AddEq(sum_Ya, sum_X);
                }

                for (int i = 0; i < 2 * m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        IIntExpr sum_Ya = cplex.IntExpr();
                        IIntExpr sum_Ya_prime = cplex.IntExpr();
                        for (int j = 0; j < 2 * m + 2; j++)
                        {
                            if (j != 2 * m)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i][j][f][g]);
                                }
                            }
                        }

                        for (int j_prime = 0; j_prime < 2 * m + 1; j_prime++)
                        {
                            for (int g_prime = 0; g_prime < numL + 2; g_prime++)
                            {
                                sum_Ya_prime = cplex.Sum(sum_Ya_prime, Ya_ijfg[j_prime][i][g_prime][f]);
                            }
                        }

                        cplex.AddEq(sum_Ya, sum_Ya_prime);
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        cplex.AddGe(Lr_ih[i + m][h], Cr_ih[i][h]);
                    }
                }

                for (int i = 2 * m; i < 3 * m; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        cplex.AddGe(Lr_ih[i + m][h], Cr_ih[i][h]);
                    }
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            IIntExpr sum_X = cplex.IntExpr();
                            for (int j = 0; j < 4 * m + 2; j++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j][k][l][h]);
                                }
                            }

                            cplex.AddGe(Cr_ih[i][h], cplex.Sum(cplex.Sum(Lr_ih[i][h], s_ikh[i][k][h]), cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, sum_X)))));
                        }
                    }
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            for (int l = 0; l < n + 2; l++)
                            {
                                for (int h = 0; h < r; h++)
                                {
                                    cplex.AddGe(Lr_ih[j][h], cplex.Sum(Cr_ih[i][h], cplex.Sum(t_klh[k][l][h], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Xr_ijklh[i][j][k][l][h]))))));
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        IIntExpr sum_Yn = cplex.IntExpr();
                        if (i >= m && i < 2 * m)
                        {
                            for (int j = 0; j < 4 * m + 2; j++)
                            {
                                if (j >= m && j < 2 * m)
                                {
                                    sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - m][k]);
                                }

                                if (j == 4 * m + 1)
                                {
                                    sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - m][j - 3 * m][k]);
                                }
                            }

                            cplex.AddGe(Cn_ik[i - m][k], cplex.Sum(cplex.Sum(cplex.Sum(cplex.Sum(Ln_ik[i - m][k], s_in_ik[i - m][k]), p_ik[i - m][k]), s_out_ik[i - m][k]), cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, sum_Yn)))));
                        }

                        if (i == 4 * m)
                        {
                            for (int j = 0; j < 4 * m + 2; j++)
                            {
                                if (j >= m && j < 2 * m)
                                {
                                    sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - 3 * m][j - m][k]);
                                }

                                if (j == 4 * m + 1)
                                {
                                    sum_Yn = cplex.Sum(sum_Yn, Yn_ijk[i - 3 * m][j - 3 * m][k]);
                                }
                            }

                            cplex.AddGe(Cn_ik[i - 3 * m][k], cplex.Sum(cplex.Sum(cplex.Sum(cplex.Sum(Ln_ik[i - 3 * m][k], s_in_ik[i - 3 * m][k]), p_ik[i - 3 * m][k]), s_out_ik[i - 3 * m][k]), cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, sum_Yn)))));
                        }
                    }
                }

                for (int i = 0; i < m + 2; i++)
                {
                    if (i != m)
                    {
                        for (int j = 0; j < m + 2; j++)
                        {
                            if (j != m + 1)
                            {
                                for (int k = 0; k < n; k++)
                                {
                                    cplex.AddGe(Ln_ik[j][k], cplex.Sum(Cn_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Yn_ijk[i][j][k])))));
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int k = 0; k < n; k++)
                    {
                        IIntExpr procAtK = cplex.IntExpr();
                        for (int jl = 0; jl < m; jl++)
                            procAtK = cplex.Sum(procAtK, Yn_ijk[i][jl][k]);
                        procAtK = cplex.Sum(procAtK, Yn_ijk[i][m + 1][k]);
                        IIntExpr delivAtK = cplex.IntExpr();
                        for (int j = 0; j < 4 * m + 2; j++)
                            for (int l = 0; l < n + 2; l++)
                                for (int h = 0; h < r; h++)
                                    delivAtK = cplex.Sum(delivAtK, Xr_ijklh[i + m][j][k][l][h]);
                        IIntExpr sumAin = cplex.IntExpr();
                        double sumWin = 0.0;
                        for (int b = 0; b < b_in_k[k]; b++)
                        {
                            sumAin = cplex.Sum(sumAin, Ain_ikb[i][k][b]);
                            sumWin += W_in_ikb[i][k][b];
                        }

                        cplex.AddEq(cplex.Sum(sumAin, cplex.Prod(-1, delivAtK)), sumWin);
                        IIntExpr sumAout = cplex.IntExpr();
                        double sumWout = 0.0;
                        for (int b = 0; b < b_out_k[k]; b++)
                        {
                            sumAout = cplex.Sum(sumAout, Aout_ikb[i][k][b]);
                            sumWout += W_out_ikb[i][k][b];
                        }

                        cplex.AddEq(cplex.Sum(sumAout, cplex.Prod(-1, procAtK)), sumWout);
                        for (int b = 0; b < b_in_k[k]; b++)
                            cplex.AddGe(Ain_ikb[i][k][b], W_in_ikb[i][k][b]);
                        for (int b = 0; b < b_out_k[k]; b++)
                            cplex.AddGe(Aout_ikb[i][k][b], W_out_ikb[i][k][b]);
                        cplex.AddGe(Ln_ik[i][k], cplex.Sum(Lin_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, sumAin)))));
                        cplex.AddGe(Cin_ik[i][k], cplex.Sum(Ln_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, procAtK)))));
                        cplex.AddGe(Lout_ik[i][k], cplex.Sum(Cn_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, procAtK)))));
                        cplex.AddLe(Lin_ik[i][k], Rounding.Multiply(big_M, Rounding.Subtract(1.0, sumWin)));
                        cplex.AddLe(Lout_ik[i][k], Rounding.Multiply(big_M, Rounding.Subtract(1.0, sumWout)));
                        for (int h = 0; h < r; h++)
                        {
                            IIntExpr delivKH = cplex.IntExpr();
                            IIntExpr pickKH = cplex.IntExpr();
                            for (int j = 0; j < 4 * m + 2; j++)
                                for (int l = 0; l < n + 2; l++)
                                {
                                    delivKH = cplex.Sum(delivKH, Xr_ijklh[i + m][j][k][l][h]);
                                    pickKH = cplex.Sum(pickKH, Xr_ijklh[i + 2 * m][j][k][l][h]);
                                }

                            cplex.AddGe(Lin_ik[i][k], cplex.Sum(cplex.Sum(Lr_ih[i + m][h], s_ikh[i + m][k][h]), cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, delivKH)))));
                            cplex.AddGe(Cr_ih[i + m][h], cplex.Sum(Lin_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, delivKH)))));
                            cplex.AddGe(Lr_ih[i + 2 * m][h], cplex.Sum(Lout_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(2, cplex.Prod(-1, cplex.Sum(pickKH, sumAout))))));
                            cplex.AddGe(Cout_ik[i][k], cplex.Sum(Cr_ih[i + 2 * m][h], cplex.Prod(-1 * big_M, cplex.Sum(2, cplex.Prod(-1, cplex.Sum(pickKH, sumAout))))));
                        }

                        for (int jl = 0; jl < m; jl++)
                            if (jl != i)
                                cplex.AddGe(Ln_ik[jl][k], cplex.Sum(Lout_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Yn_ijk[i][jl][k])))));
                        cplex.AddGe(Ln_ik[m + 1][k], cplex.Sum(Lout_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Yn_ijk[i][m + 1][k])))));
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            if (i != j)
                            {
                                for (int b = 0; b < b_in_k[k]; b++)
                                    cplex.AddGe(Lin_ik[j][k], cplex.Sum(Cin_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Yin_ijkb[i][j][k][b])))));
                                for (int b = 0; b < b_out_k[k]; b++)
                                    cplex.AddGe(Lout_ik[j][k], cplex.Sum(Cout_ik[i][k], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Yout_ijkb[i][j][k][b])))));
                            }

                            if (i < j)
                            {
                                for (int b = 0; b < b_in_k[k]; b++)
                                {
                                    IIntExpr yyIn = cplex.Sum(Yin_ijkb[i][j][k][b], Yin_ijkb[j][i][k][b]);
                                    cplex.AddGe(yyIn, cplex.Sum(cplex.Sum(Ain_ikb[i][k][b], Ain_ikb[j][k][b]), -1.0));
                                    cplex.AddLe(yyIn, Ain_ikb[i][k][b]);
                                    cplex.AddLe(yyIn, Ain_ikb[j][k][b]);
                                }

                                for (int b = 0; b < b_out_k[k]; b++)
                                {
                                    IIntExpr yyOut = cplex.Sum(Yout_ijkb[i][j][k][b], Yout_ijkb[j][i][k][b]);
                                    cplex.AddGe(yyOut, cplex.Sum(cplex.Sum(Aout_ikb[i][k][b], Aout_ikb[j][k][b]), -1.0));
                                    cplex.AddLe(yyOut, Aout_ikb[i][k][b]);
                                    cplex.AddLe(yyOut, Aout_ikb[j][k][b]);
                                }
                            }
                        }
                    }
                }

                for (int k = 0; k < n; k++)
                {
                    for (int b = 1; b < b_in_k[k]; b++)
                    {
                        IIntExpr cur = cplex.IntExpr();
                        IIntExpr prev = cplex.IntExpr();
                        for (int i = 0; i < m; i++)
                        {
                            cur = cplex.Sum(cur, Ain_ikb[i][k][b]);
                            prev = cplex.Sum(prev, Ain_ikb[i][k][b - 1]);
                        }

                        cplex.AddLe(cur, prev);
                    }

                    for (int b = 1; b < b_out_k[k]; b++)
                    {
                        IIntExpr cur = cplex.IntExpr();
                        IIntExpr prev = cplex.IntExpr();
                        for (int i = 0; i < m; i++)
                        {
                            cur = cplex.Sum(cur, Aout_ikb[i][k][b]);
                            prev = cplex.Sum(prev, Aout_ikb[i][k][b - 1]);
                        }

                        cplex.AddLe(cur, prev);
                    }
                }

                for (int j = 0; j < m; j++)
                {
                    for (int l = 0; l < n + 2; l++)
                    {
                        for (int g = 0; g < numL; g++)
                        {
                            for (int h = 0; h < r; h++)
                            {
                                IIntExpr sum_X = cplex.IntExpr();
                                IIntExpr sum_Ya = cplex.IntExpr();
                                for (int i = 0; i < 4 * m + 2; i++)
                                {
                                    for (int k = 0; k < n + 2; k++)
                                    {
                                        sum_X = cplex.Sum(sum_X, Xr_ijklh[i][j][k][l][h]);
                                    }
                                }

                                for (int i_prime = 0; i_prime < 2 * m + 2; i_prime++)
                                {
                                    for (int f = 0; f < numL + 2; f++)
                                    {
                                        sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i_prime][j][f][g]);
                                    }
                                }

                                cplex.AddGe(Lr_ih[j][h], cplex.Sum(Ca_if[j][g], cplex.Prod(-1 * big_M, cplex.Sum(2, cplex.Prod(-1, cplex.Sum(sum_X, sum_Ya))))));
                            }
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        for (int h = 0; h < r; h++)
                        {
                            IIntExpr sum_X = cplex.IntExpr();
                            IIntExpr sum_Ya = cplex.IntExpr();
                            for (int j = 0; j < 4 * m + 2; j++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    sum_X = cplex.Sum(sum_X, Xr_ijklh[i + 3 * m][j][n][l][h]);
                                }
                            }

                            for (int j_prime = 0; j_prime < 2 * m + 2; j_prime++)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    sum_Ya = cplex.Sum(sum_Ya, Ya_ijfg[i + m][j_prime][f][g]);
                                }
                            }

                            cplex.AddGe(La_if[i + m][f], cplex.Sum(Cr_ih[i + 3 * m][h], cplex.Prod(-1 * big_M, cplex.Sum(2, cplex.Prod(-1, cplex.Sum(sum_X, sum_Ya))))));
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        for (int h = 0; h < r; h++)
                        {
                            for (int f = 0; f < numL; f++)
                            {
                                for (int l = 0; l < n; l++)
                                {
                                    for (int g = 0; g < numL; g++)
                                    {
                                        IIntExpr sumX = cplex.IntExpr();
                                        for (int j_prime = 0; j_prime < 4 * m; j_prime++)
                                        {
                                            sumX = cplex.Sum(sumX, Xr_ijklh[i][j_prime][n][l][h]);
                                        }

                                        cplex.AddGe(Ca_if[j][g], cplex.Sum(Cr_ih[i][h], cplex.Prod(-1 * big_M, cplex.Sum(2, cplex.Sum(cplex.Prod(-1, sumX), cplex.Prod(-1, Ya_ijfg[i][j][f][g]))))));
                                    }
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int j = 0; j < m; j++)
                    {
                        for (int h = 0; h < r; h++)
                        {
                            for (int f = 0; f < numL; f++)
                            {
                                for (int l = 0; l < n; l++)
                                {
                                    for (int g = 0; g < numL; g++)
                                    {
                                        IIntExpr sumX = cplex.IntExpr();
                                        for (int i_prime = 0; i_prime < 4 * m; i_prime++)
                                        {
                                            sumX = cplex.Sum(sumX, Xr_ijklh[i][i_prime][n][l][h]);
                                        }

                                        cplex.AddGe(Lr_ih[j][h], cplex.Sum(La_if[i][f], cplex.Prod(-1 * big_M, cplex.Sum(2, cplex.Sum(cplex.Prod(-1, sumX), cplex.Prod(-1, Ya_ijfg[i][j][f][g]))))));
                                    }
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < 2 * m + 2; i++)
                {
                    for (int j = 0; j < 2 * m + 2; j++)
                    {
                        for (int f = 0; f < numL + 2; f++)
                        {
                            for (int g = 0; g < numL + 2; g++)
                            {
                                cplex.AddGe(Ca_if[j][g], cplex.Sum(La_if[j][g], cplex.Sum(r_ijfg[i][j][f][g], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Ya_ijfg[i][j][f][g]))))));
                            }
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        for (int g = 0; g < numL; g++)
                        {
                            IIntExpr sum_Ya_L = cplex.IntExpr();
                            IIntExpr sum_Ya_R = cplex.IntExpr();
                            for (int j = 0; j < 2 * m; j++)
                            {
                                for (int g_prime = 0; g_prime < numL + 2; g_prime++)
                                {
                                    sum_Ya_L = cplex.Sum(sum_Ya_L, Ya_ijfg[i][j][f][g_prime]);
                                }
                            }

                            for (int j_prime = 0; j_prime < 2 * m; j_prime++)
                            {
                                for (int f_prime = 0; f_prime < numL + 2; f_prime++)
                                {
                                    sum_Ya_R = cplex.Sum(sum_Ya_R, Ya_ijfg[i + m][j_prime][g][f_prime]);
                                }
                            }

                            cplex.AddGe(La_if[i + m][g], cplex.Sum(Ca_if[i][f], cplex.Prod(-1 * big_M, cplex.Sum(2, cplex.Prod(-1, cplex.Sum(sum_Ya_L, sum_Ya_R))))));
                        }
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    for (int j = m; j < 2 * m; j++)
                    {
                        for (int f = 0; f < numL; f++)
                        {
                            IIntExpr sum_Ya_outgoing = cplex.IntExpr();
                            IIntExpr sum_Ya_incoming = cplex.IntExpr();
                            for (int j_prime = 0; j_prime < 2 * m + 2; j_prime++)
                            {
                                if (j_prime != 2 * m)
                                {
                                    for (int g_prime = 0; g_prime < numL + 2; g_prime++)
                                    {
                                        sum_Ya_outgoing = cplex.Sum(sum_Ya_outgoing, Ya_ijfg[i][j_prime][f][g_prime]);
                                    }
                                }
                            }

                            for (int i_prime = 0; i_prime < 2 * m + 1; i_prime++)
                            {
                                for (int f_prime = 0; f_prime < numL + 2; f_prime++)
                                {
                                    sum_Ya_incoming = cplex.Sum(sum_Ya_incoming, Ya_ijfg[i_prime][j][f_prime][f]);
                                }
                            }

                            cplex.AddGe(La_if[j][f], cplex.Sum(Ca_if[i][f], cplex.Prod(-1 * big_M, cplex.Sum(3.0 - R_if[i][f], cplex.Sum(cplex.Prod(-1, sum_Ya_outgoing), cplex.Prod(-1, sum_Ya_incoming))))));
                        }
                    }
                }

                for (int i = 0; i < 2 * m; i++)
                {
                    for (int j = 0; j < 2 * m + 2; j++)
                    {
                        if (j != 2 * m)
                        {
                            for (int f = 0; f < numL; f++)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    cplex.AddGe(La_if[j][g], cplex.Sum(Ca_if[i][f], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Ya_ijfg[i][j][f][g])))));
                                }
                            }
                        }
                    }
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        cplex.AddLe(Q_ih[i][h], c_h[h]);
                    }
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int h = 0; h < r; h++)
                    {
                        cplex.AddGe(Q_ih[i][h], 0);
                    }
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            for (int l = 0; l < n + 2; l++)
                            {
                                for (int h = 0; h < r; h++)
                                {
                                    cplex.AddGe(Q_ih[j][h], cplex.Sum(Q_ih[i][h], cplex.Sum(q_i[j], cplex.Prod(-1 * big_M, cplex.Sum(1, cplex.Prod(-1, Xr_ijklh[i][j][k][l][h]))))));
                                }
                            }
                        }
                    }
                }

                for (int h = 0; h < r; h++)
                {
                    double initialLoad = 0.0;
                    for (int j = m; j < 2 * m; j++)
                    {
                        initialLoad = Rounding.Add(initialLoad, Rounding.Multiply(q_i[j], W_r_ih[j][h]));
                    }

                    for (int j = 3 * m; j < 4 * m; j++)
                    {
                        initialLoad = Rounding.Add(initialLoad, Rounding.Multiply(q_i[j], W_r_ih[j][h]));
                    }

                    cplex.AddEq(Q_ih[4 * m][h], Rounding.Multiply(-1.0, initialLoad));
                }

                for (int h = 0; h < r; h++)
                {
                    cplex.AddEq(Cr_ih[4 * m][h], 0);
                }

                for (int k = 0; k < n; k++)
                {
                    cplex.AddEq(Cn_ik[m][k], 0);
                }

                for (int f = 0; f < numL + 2; f++)
                {
                    cplex.AddEq(Ca_if[2 * m][f], 0);
                }

                for (int i = 0; i < m; i++)
                {
                    for (int f = 0; f < numL; f++)
                    {
                        cplex.AddGe(T_i[i], cplex.Sum(Ca_if[m + i][f], -1.0 * d_i[i]));
                    }
                }

                for (int i = 0; i < m; i++)
                {
                    cplex.AddGe(T_i[i], 0.0);
                }

                if (cplex.Solve())
                {
                    result = Rounding.Value(cplex.GetObjValue());
                    solveResult = Metabase.ExtractMilpSolveResult(cplex, numL, r, n, m, Xr_ijklh, Yn_ijk, Ya_ijfg, Cr_ih, Lr_ih, Cn_ik, Ln_ik, Ca_if, La_if, Ain_ikb, Aout_ikb, Yin_ijkb, Yout_ijkb, Lin_ik, Cin_ik, Lout_ik, Cout_ik, b_in_k, b_out_k, d_i);
                    double SafeValue(INumVar variable)
                    {
                        try
                        {
                            return Rounding.Value(cplex.GetValue(variable));
                        }
                        catch (Cplex.UnknownObjectException)
                        {
                            return 0.0;
                        }
                    }

                    string FormatTiming(double start, double end)
                    {
                        return "[start=" + Convert.ToString(Rounding.Value(start)) + ", end=" + Convert.ToString(Rounding.Value(end)) + "]";
                    }

                    string FormatRobotTiming(double start, double arrival, double end)
                    {
                        if (Math.Abs(start - arrival) > 0.0001)
                        {
                            return "[start=" + Convert.ToString(Rounding.Value(start)) + ", arrival=" + Convert.ToString(Rounding.Value(arrival)) + ", end=" + Convert.ToString(Rounding.Value(end)) + "]";
                        }

                        return FormatTiming(start, end);
                    }

                    bool RobotOperationIsScheduled(int operation, int h)
                    {
                        for (int j = 0; j < 4 * m + 2; j++)
                        {
                            for (int k = 0; k < n + 2; k++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    if (SafeValue(Xr_ijklh[operation][j][k][l][h]) > 0.2)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }

                        return false;
                    }

                    double FindRobotStartTime(int operation, int h, double fallback)
                    {
                        for (int previous = 0; previous < 4 * m + 2; previous++)
                        {
                            for (int k = 0; k < n + 2; k++)
                            {
                                for (int l = 0; l < n + 2; l++)
                                {
                                    if (SafeValue(Xr_ijklh[previous][operation][k][l][h]) > 0.2)
                                    {
                                        return SafeValue(Cr_ih[previous][h]);
                                    }
                                }
                            }
                        }

                        return fallback;
                    }

                    string FindRobotTiming(int operation)
                    {
                        for (int h = 0; h < r; h++)
                        {
                            if (RobotOperationIsScheduled(operation, h))
                            {
                                double arrival = SafeValue(Lr_ih[operation][h]);
                                double completion = SafeValue(Cr_ih[operation][h]);
                                double start = FindRobotStartTime(operation, h, arrival);
                                return FormatRobotTiming(start, arrival, completion) + " AMR=" + Convert.ToString(h);
                            }
                        }

                        return "[not scheduled]";
                    }

                    string FindProcessTiming(int operation)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            for (int j = 0; j < m + 2; j++)
                            {
                                if (j != m && SafeValue(Yn_ijk[operation][j][k]) > 0.2)
                                {
                                    return FormatTiming(SafeValue(Ln_ik[operation][k]), SafeValue(Cn_ik[operation][k])) + " WS=" + Convert.ToString(k);
                                }
                            }
                        }

                        return "[not scheduled]";
                    }

                    string FindAsrsTiming(int operation)
                    {
                        for (int j = 0; j < 2 * m + 2; j++)
                        {
                            if (j == 2 * m)
                            {
                                continue;
                            }

                            for (int f = 0; f < numL; f++)
                            {
                                for (int g = 0; g < numL + 2; g++)
                                {
                                    if (SafeValue(Ya_ijfg[operation][j][f][g]) > 0.1)
                                    {
                                        return FormatTiming(SafeValue(La_if[operation][f]), SafeValue(Ca_if[operation][f])) + " ASRS_LOC=" + Convert.ToString(f);
                                    }
                                }
                            }
                        }

                        return "[not scheduled]";
                    }

                    List<string> BuildWipTimingSummaryLines()
                    {
                        var lines = new List<string>();
                        lines.Add("--------------------------------------------------");
                        lines.Add("WIP SUMMARY WITH TIMES");
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
                                    current.Add("loaded on AMR " + Convert.ToString(h) + " for D+");
                                }

                                if (u_rob_i[3 * m + i] > 0.5 && u_rob_i[2 * m + i] < 0.5 && W_r_ih[3 * m + i][h] > 0.5)
                                {
                                    current.Add("loaded on AMR " + Convert.ToString(h) + " for D-");
                                }
                            }

                            for (int k = 0; k < n; k++)
                            {
                                if (u_ws_i[i] > 0.5 && u_rob_i[m + i] < 0.5 && W_n_ik[i][k] > 0.5)
                                {
                                    current.Add("processing at workstation " + Convert.ToString(k));
                                }

                                if (u_ws_i[i] < 0.5 && u_rob_i[2 * m + i] > 0.5 && W_n_ik[i][k] > 0.5)
                                {
                                    current.Add("waiting at workstation " + Convert.ToString(k) + " for P-");
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
                                remaining.Add("ASRS RETRIEVAL " + FindAsrsTiming(i));
                            }

                            if (u_rob_i[i] > 0.5)
                            {
                                remaining.Add("ROBOT P+ " + FindRobotTiming(i));
                            }

                            if (u_rob_i[m + i] > 0.5)
                            {
                                remaining.Add("ROBOT D+ " + FindRobotTiming(m + i));
                            }

                            if (u_ws_i[i] > 0.5)
                            {
                                remaining.Add("PROCESS " + FindProcessTiming(i));
                            }

                            if (u_rob_i[2 * m + i] > 0.5)
                            {
                                remaining.Add("ROBOT P- " + FindRobotTiming(2 * m + i));
                            }

                            if (u_rob_i[3 * m + i] > 0.5)
                            {
                                remaining.Add("ROBOT D- " + FindRobotTiming(3 * m + i));
                            }

                            if (u_asrs_i[m + i] > 0.5)
                            {
                                remaining.Add("ASRS STORAGE " + FindAsrsTiming(m + i));
                            }

                            bool isCompleted = remaining.Count == 0;
                            bool isWip = skipped.Count > 0;
                            string status = isCompleted ? "DONE" : (isWip ? "WIP" : "NORMAL");
                            string currentText = isCompleted ? "completed before horizon" : (current.Count > 0 ? string.Join("; ", current) : "no current location");
                            string skippedText = skipped.Count > 0 ? string.Join(", ", skipped) : "none";
                            string remainingText = remaining.Count > 0 ? string.Join(" -> ", remaining) : "none";
                            lines.Add("OPERATION " + Convert.ToString(i) + " [" + status + "]");
                            lines.Add("  current   : " + currentText);
                            lines.Add("  skipped   : " + skippedText);
                            lines.Add("  remaining : " + remainingText);
                        }

                        lines.Add("--------------------------------------------------");
                        return lines;
                    }

                    solveResult.WipTimingSummaryLines = BuildWipTimingSummaryLines();
                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine("Total tardiness is " + Convert.ToString(result));
                    Console.WriteLine("The number of mobile robots is " + Convert.ToString(r));
                    Console.WriteLine("The number of workstations is " + Convert.ToString(n));
                    Console.WriteLine("The number of operations is " + Convert.ToString(m));
                    Console.WriteLine("--------------------------------------------------");
                    for (int i = 0; i < R_if.GetLength(0); i++)
                    {
                        for (int f = 0; f < R_if[i].GetLength(0); f++)
                        {
                            if (R_if[i][f] > 0.5)
                            {
                                Console.WriteLine("OPERATION " + Convert.ToString(i) + " is necessary to be picked up from LOCATION " + Convert.ToString(f));
                            }
                        }
                    }

                    Console.WriteLine("--------------------------------------------------");
                    for (int h = 0; h < r; h++)
                    {
                        for (int i = 0; i < 4 * m + 2; i++)
                        {
                            for (int j = 0; j < 4 * m + 2; j++)
                            {
                                for (int k = 0; k < n + 2; k++)
                                {
                                    for (int l = 0; l < n + 2; l++)
                                    {
                                        if (SafeValue(Xr_ijklh[i][j][k][l][h]) > 0.2)
                                        {
                                            if ((i == 4 * m) && (j == 4 * m + 1))
                                            {
                                                if (k == n + 1 && l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and the INITIAL NODE " + " immediately before a DUMMY OPERATION and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (k == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and the INITIAL NODE " + " immediately before a DUMMY OPERATION and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and NODE " + Convert.ToString(k) + " immediately before a DUMMY OPERATION and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and NODE " + Convert.ToString(k) + " immediately before a DUMMY OPERATION and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                            }
                                            else if (i == 4 * m || i == 4 * m + 1)
                                            {
                                                if (k == n + 1 && l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and the INITIAL NODE " + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (k == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and the INITIAL NODE " + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and NODE " + Convert.ToString(k) + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves a DUMMY OPERATION and NODE " + Convert.ToString(k) + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                            }
                                            else if (j == 4 * m || j == 4 * m + 1)
                                            {
                                                if (k == n + 1 && l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and the INITIAL NODE " + " immediately before a DUMMY OPERATION and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (k == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and the INITIAL NODE " + " immediately before a DUMMY OPERATION and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and NODE " + Convert.ToString(k) + " immediately before a DUMMY OPERATION and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and NODE " + Convert.ToString(k) + " immediately before a DUMMY OPERATION and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                            }
                                            else
                                            {
                                                if (k == n + 1 && l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and the INITIAL NODE " + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (k == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and the INITIAL NODE " + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else if (l == n + 1)
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and NODE " + Convert.ToString(k) + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and the FINAL NODE " + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                                else
                                                {
                                                    if (k == n)
                                                    {
                                                        Console.Write("<AS/RS>  ");
                                                    }
                                                    else if (k < n)
                                                    {
                                                        Console.Write("<MACHINE>");
                                                    }
                                                    else
                                                    {
                                                        Console.Write("<INIT>   ");
                                                    }

                                                    Console.WriteLine("AMR " + Convert.ToString(h) + " moves OPERATION " + Convert.ToString(i % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)i / m)) + " and NODE " + Convert.ToString(k) + " immediately before OPERATION " + Convert.ToString(j % m) + " DELIVERY TYPE " + Convert.ToString(System.Math.Truncate((double)j / m)) + " and NODE " + Convert.ToString(l) + " ARRIVAL TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Lr_ih[i][h]))) + " COMPLETION TIME is " + Convert.ToString(Rounding.Value(cplex.GetValue(Cr_ih[i][h]))) + ", <<LOAD is " + Convert.ToString(Rounding.Value(cplex.GetValue(Q_ih[i][h]))) + " among " + Convert.ToString(Rounding.Value(c_h[h])) + ">>");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        Console.WriteLine("");
                    }

                    Console.WriteLine("--------------------------------------------------");
                    for (int k = 0; k < n; k++)
                    {
                        for (int i = 0; i < m + 2; i++)
                        {
                            if (i != m + 1)
                            {
                                for (int j = 0; j < m + 2; j++)
                                {
                                    if (j != m)
                                    {
                                        double allocation = SafeValue(Yn_ijk[i][j][k]);
                                        if (allocation > 0.2)
                                        {
                                            if (i == m && j == m + 1)
                                            {
                                                Console.WriteLine("WORKSTATION(NODE) " + Convert.ToString(k) + " processes the INITIAL OPERATION " + " immediately before processing the FINAL OPERATION " + " from the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Ln_ik[i][k]))) + " to the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Cn_ik[i][k]))));
                                            }
                                            else if (i == m)
                                            {
                                                Console.WriteLine("WORKSTATION(NODE) " + Convert.ToString(k) + " processes the INITIAL OPERATION " + " immediately before processing the OPERATION " + Convert.ToString(j % m) + " from the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Ln_ik[i][k]))) + " to the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Cn_ik[i][k]))));
                                            }
                                            else if (j == m + 1)
                                            {
                                                Console.WriteLine("WORKSTATION(NODE) " + Convert.ToString(k) + " processes the OPERATION " + Convert.ToString(i % m) + " immediately before processing the FINAL OPERATION " + " from the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Ln_ik[i][k]))) + " to the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Cn_ik[i][k]))));
                                            }
                                            else
                                            {
                                                Console.WriteLine("WORKSTATION(NODE) " + Convert.ToString(k) + " processes the OPERATION " + Convert.ToString(i % m) + " immediately before processing the OPERATION " + Convert.ToString(j % m) + " from the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Ln_ik[i][k]))) + " to the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Cn_ik[i][k]))));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    Console.WriteLine("--------------------------------------------------");
                    for (int i = 0; i < 2 * m + 2; i++)
                    {
                        if (i != 2 * m + 1)
                        {
                            for (int j = 0; j < 2 * m + 2; j++)
                            {
                                if (j != 2 * m)
                                {
                                    for (int f = 0; f < numL + 2; f++)
                                    {
                                        for (int g = 0; g < numL + 2; g++)
                                        {
                                            double allocation = SafeValue(Ya_ijfg[i][j][f][g]);
                                            if (allocation > 0.1)
                                            {
                                                if (i < m)
                                                {
                                                    Console.Write("RETRIEVAL of OPERATION " + Convert.ToString(i % m));
                                                }
                                                else if (i >= m && i < 2 * m)
                                                {
                                                    Console.Write("STORAGE of OPERATION " + Convert.ToString(i % m) + "  ");
                                                }
                                                else if (i == 2 * m)
                                                {
                                                    Console.Write("INITIAL OPERATION       ");
                                                }
                                                else
                                                {
                                                    Console.Write("FINAL OPERATION         ");
                                                }

                                                if (f < numL)
                                                {
                                                    Console.Write(" in the LOCATION of AS/RS " + Convert.ToString(f) + "    ");
                                                }
                                                else
                                                {
                                                    Console.Write(" in the DUMMY LOCATION of AS/RS");
                                                }

                                                Console.Write(" is performed immediately before ");
                                                if (j < m)
                                                {
                                                    Console.Write("RETRIEVAL of OPERATION " + Convert.ToString(j % m));
                                                }
                                                else if (j >= m && j < 2 * m)
                                                {
                                                    Console.Write("STORAGE of OPERATION " + Convert.ToString(j % m) + "  ");
                                                }
                                                else if (j == 2 * m)
                                                {
                                                    Console.Write("INITIAL OPERATION       ");
                                                }
                                                else
                                                {
                                                    Console.Write("FINAL OPERATION         ");
                                                }

                                                if (g < numL)
                                                {
                                                    Console.Write(" in the LOCATION of AS/RS " + Convert.ToString(g) + "    ");
                                                }
                                                else
                                                {
                                                    Console.Write(" in the DUMMY LOCATION of AS/RS");
                                                }

                                                if (i >= 2 * m)
                                                {
                                                }
                                                else
                                                {
                                                    Console.Write(" from the time " + Convert.ToString(Rounding.Value(cplex.GetValue(La_if[i][f]))) + " to the time " + Convert.ToString(Rounding.Value(cplex.GetValue(Ca_if[i][f]))));
                                                }

                                                Console.Write(" << Parameter: " + Convert.ToString(r_ijfg[i][j][f][g]) + " >>");
                                                Console.WriteLine();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    solveResult.Status = cplex.GetStatus().ToString();
                }

                cplex.End();
            }
            catch (ILOG.Concert.Exception e)
            {
                solveResult.Status = "ConcertException";
                System.Console.WriteLine("Concert exception caught '" + e + "' caught");
            }

            return solveResult;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PDP_ASRS_WIP
{
    public class Parameters
    {
        public virtual (int[] P_plus, int[] D_plus, int[] P_minus, int[] D_minus, int[] R, int[] R_combine) generate_sets_of_operations(int m)
        {
            int[] P_plus = new int[m];
            int[] D_plus = new int[m];
            int[] P_minus = new int[m];
            int[] D_minus = new int[m];
            int[] R = new int[4 * m];
            int[] R_combine = new int[4 * m + 2];
            for (int i = 0; i < m; i++)
            {
                P_plus[i] = i;
                D_plus[i] = i + m;
                P_minus[i] = i + 2 * m;
                D_minus[i] = i + 3 * m;
            }

            for (int i = 0; i < R.GetLength(0); i++)
            {
                R[i] = i;
            }

            for (int i = 0; i < R_combine.GetLength(0); i++)
            {
                R_combine[i] = i;
            }

            return (P_plus, D_plus, P_minus, D_minus, R, R_combine);
        }

        public virtual (int[] N, int[] N_plus, int[] N_minus) generate_sets_of_nodes(int n)
        {
            int[] N = new int[n];
            int[] N_plus = new int[n + 1];
            int[] N_minus = new int[n + 2];
            for (int i = 0; i < n + 2; i++)
            {
                if (i < n)
                {
                    N[i] = i;
                }

                if (i < n + 1)
                {
                    N_plus[i] = i;
                }

                N_minus[i] = i;
            }

            return (N, N_plus, N_minus);
        }

        public virtual double[] generate_c_h(int r)
        {
            double[] result = new double[r];
            for (int s = 0; s < r; s++)
            {
                result[s] = Rounding.Value(Random.Shared.Next(1, 3));
            }

            return result;
        }

        public virtual double[] generate_q_i(int m)
        {
            double[] result = new double[4 * m + 2];
            for (int i = 0; i < m; i++)
            {
                result[i] = Rounding.Value(1.0);
                result[m + i] = Rounding.Multiply(-1.0, result[i]);
                result[2 * m + i] = result[i];
                result[3 * m + i] = Rounding.Multiply(-1.0, result[i]);
            }

            result[4 * m] = Rounding.Value(0.0);
            result[4 * m + 1] = Rounding.Value(0.0);
            return result;
        }

        public virtual double[][] generate_p_ik(int n, int m)
        {
            double[][] result = new double[m + 2][];
            for (int i = 0; i < m + 2; i++)
            {
                result[i] = new double[n];
                if (i < m)
                {
                    for (int k = 0; k < n; k++)
                    {
                        result[i][k] = Rounding.Add(Rounding.Multiply(Random.Shared.NextDouble(), 80), 50);
                    }
                }
                else
                {
                    for (int k = 0; k < n; k++)
                    {
                        result[i][k] = Rounding.Value(0.0);
                    }
                }
            }

            return result;
        }

        public virtual int[][] generate_E_ik(int m, int n, double E_ratio)
        {
            int[][] result = new int[m + 2][];
            for (int i = 0; i < m + 2; i++)
            {
                result[i] = new int[n + 2];
                if (i != m || i != m + 1)
                {
                    bool feasible_eligibility = false;
                    while (feasible_eligibility == false)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            double r_d = Rounding.Value(Random.Shared.NextDouble());
                            if (r_d < E_ratio)
                            {
                                result[i][k] = 1;
                                feasible_eligibility = true;
                            }
                            else
                            {
                                result[i][k] = 0;
                            }
                        }
                    }

                    result[i][n] = 0;
                    result[i][n + 1] = 0;
                }
                else
                {
                    for (int k = 0; k < n + 1; k++)
                    {
                        result[i][k] = 0;
                    }

                    result[i][n] = 1;
                }
            }

            return result;
        }

        public virtual double[][][] generate_s_ikh(int m, int n, int r)
        {
            double[][][] result = new double[4 * m + 2][][];
            for (int i = 0; i < 4 * m + 2; i++)
            {
                result[i] = new double[n + 2][];
                for (int k = 0; k < n + 2; k++)
                {
                    result[i][k] = new double[r];
                    for (int h = 0; h < r; h++)
                    {
                        if (i == 4 * m || i == 4 * m + 1)
                        {
                            result[i][k][h] = Rounding.Value(0.0);
                        }
                        else if (k == n)
                        {
                            result[i][k][h] = Rounding.Add(6.4, Rounding.Multiply(Random.Shared.NextDouble(), 3.2));
                        }
                        else if (k == n + 1)
                        {
                            result[i][k][h] = Rounding.Value(0.0);
                        }
                        else
                        {
                            result[i][k][h] = Rounding.Add(6.4, Rounding.Multiply(Random.Shared.NextDouble(), 3.2));
                        }
                    }
                }
            }

            return result;
        }

        public virtual double[][][] generate_t_klh(int n, int r)
        {
            double[][][] result = new double[n + 2][][];
            for (int k = 0; k < n + 2; k++)
            {
                result[k] = new double[n + 2][];
                for (int l = 0; l < n + 2; l++)
                {
                    result[k][l] = new double[r];
                    for (int h = 0; h < r; h++)
                    {
                        if (k == l)
                        {
                            result[k][l][h] = Rounding.Value(0.0);
                        }
                        else
                        {
                            result[k][l][h] = Rounding.Add(Rounding.Multiply(Random.Shared.NextDouble(), 23.3), 3.3);
                        }
                    }
                }
            }

            return result;
        }

        public virtual double[][][][] generate_r_ijfg(int a, int width, double H_length, double H_acc, double H_dcc, double H_max, int depth, double D_length, double D_acc, double D_dcc, double D_max, int levels, double L_length, double L_acc, double L_dcc, double L_max)
        {
            double[] Ltime = new double[levels + 1];
            double[] Stime = new double[depth + 1];
            int halfwidth;
            if (width == 1)
            {
                halfwidth = 1;
            }
            else
            {
                halfwidth = width / 2;
            }

            double[] Htime;
            if (width % 2 != 0)
            {
                Htime = new double[halfwidth + 2];
            }
            else
            {
                Htime = new double[halfwidth + 1];
            }

            static double CalcTravelTime(double dist, double acc, double dcc, double vmax)
            {
                double s_tri = Rounding.Multiply(Rounding.Multiply(0.5, Rounding.Multiply(vmax, vmax)), Rounding.Add(Rounding.Divide(1.0, acc), Rounding.Divide(1.0, dcc)));
                if (dist <= s_tri)
                {
                    return Rounding.Value(Math.Sqrt(Rounding.Multiply(Rounding.Multiply(2.0, dist), Rounding.Add(Rounding.Divide(1.0, acc), Rounding.Divide(1.0, dcc)))));
                }

                double s_cruise = Rounding.Subtract(dist, s_tri);
                return Rounding.Add(Rounding.Add(Rounding.Divide(vmax, acc), Rounding.Divide(vmax, dcc)), Rounding.Divide(s_cruise, vmax));
            }

            static double SumTimes(double left, double right)
            {
                return Rounding.Add(left, right);
            }

            static double MaxThenAdd(double left, double right, double addend)
            {
                return Rounding.Add(Rounding.Max(left, right), addend);
            }

            for (int i = 1; i <= levels; i++)
            {
                double dist = Rounding.Multiply(L_length, i);
                Ltime[i] = CalcTravelTime(dist, L_acc, L_dcc, L_max);
            }

            for (int s = 1; s <= depth; s++)
            {
                double dist = Rounding.Multiply(D_length, s);
                Stime[s] = CalcTravelTime(dist, D_acc, D_dcc, D_max);
            }

            for (int i = 0; i < Htime.Length; i++)
            {
                double dist = Rounding.Multiply(H_length, i + 1);
                Htime[i] = CalcTravelTime(dist, H_acc, H_dcc, H_max);
            }

            int m = 2 * a;
            int numLoc = width * depth * levels;
            double[][][][] r = new double[m + 2][][][];
            for (int i = 0; i < m + 2; i++)
            {
                r[i] = new double[m + 2][][];
                for (int j = 0; j < m + 2; j++)
                {
                    r[i][j] = new double[numLoc + 2][];
                    for (int s = 0; s < numLoc + 2; s++)
                    {
                        r[i][j][s] = new double[numLoc + 2];
                        for (int t = 0; t < numLoc + 2; t++)
                        {
                            int level1 = s / (depth * width);
                            int remainder1 = s % (depth * width);
                            int depth1 = remainder1 / width;
                            int level2 = t / (depth * width);
                            int remainder2 = t % (depth * width);
                            int depth2 = remainder2 / width;
                            int width2 = remainder2 % width;
                            int Hindex2 = width2 / halfwidth;
                            if (j < a && t < numLoc)
                            {
                                double targetLevelTime = Ltime[level2 + 1];
                                double targetDepthTime = Stime[depth2 + 1];
                                double targetHorizontalTime = Htime[Hindex2];
                                if (i >= a && i < m && s < numLoc)
                                {
                                    if (level1 == level2)
                                    {
                                        r[i][j][s][t] = MaxThenAdd(targetDepthTime, Ltime[Math.Abs(level1 - level2)], targetLevelTime);
                                    }
                                    else
                                    {
                                        r[i][j][s][t] = Rounding.Add(Rounding.Add(Rounding.Add(Stime[Math.Abs(depth1 - depth2)], targetDepthTime), targetHorizontalTime), targetLevelTime);
                                    }
                                }
                                else
                                {
                                    double levelMoveTime = s < numLoc ? Ltime[Math.Abs(level1 - level2)] : targetLevelTime;
                                    double retrievalShuttleTime = Rounding.Add(Rounding.Multiply(2.0, targetDepthTime), targetHorizontalTime);
                                    r[i][j][s][t] = MaxThenAdd(levelMoveTime, retrievalShuttleTime, targetLevelTime);
                                }
                            }
                            else if (j >= a && j < m && t < numLoc)
                            {
                                double targetLevelTime = Ltime[level2 + 1];
                                if (i >= a && i < m)
                                {
                                    r[i][j][s][t] = targetLevelTime;
                                }
                                else
                                {
                                    double previousLevelTime = s < numLoc ? Ltime[level1 + 1] : Rounding.Value(0.0);
                                    r[i][j][s][t] = SumTimes(previousLevelTime, targetLevelTime);
                                }
                            }
                            else
                            {
                                r[i][j][s][t] = Rounding.Value(0.0);
                            }
                        }
                    }
                }
            }

            return r;
        }

        public virtual double[][] generate_R_if(int m, int numL)
        {
            int a = m + 2;
            int retrievalJobs = m / 2;
            int fixedCol = 0;
            if (retrievalJobs > numL)
            {
                throw new ArgumentException("The number of retrieval jobs cannot exceed the number of ASRS locations.");
            }

            var R_if = new double[a][];
            var availableLocations = new int[numL];
            for (int f = 0; f < numL; f++)
            {
                availableLocations[f] = f;
            }

            for (int i = 0; i < a; i++)
            {
                R_if[i] = new double[numL];
            }

            for (int i = 0; i < retrievalJobs; i++)
            {
                int pickIndex = Random.Shared.Next(0, numL - i);
                int col = availableLocations[pickIndex];
                R_if[i][col] = 1.0;
                availableLocations[pickIndex] = availableLocations[numL - i - 1];
            }

            R_if[m][fixedCol] = 1.0;
            R_if[m + 1][fixedCol] = 1.0;
            return R_if;
        }

        public virtual double[] generate_d_i(int m, double dueDate)
        {
            double[] result = new double[m];
            for (int i = 0; i < m; i++)
            {
                result[i] = Rounding.Value(dueDate);
            }

            return result;
        }

        public virtual double[] generate_u_rob_i(int m)
        {
            double[] result = new double[4 * m + 2];
            for (int i = 0; i < 4 * m; i++)
            {
                result[i] = 1.0;
            }

            result[4 * m] = 1.0;
            result[4 * m + 1] = 1.0;
            return result;
        }

        public virtual double[] generate_u_ws_i(int m)
        {
            double[] result = new double[m];
            for (int i = 0; i < m; i++)
            {
                result[i] = 1.0;
            }

            return result;
        }

        public virtual double[] generate_u_asrs_i(int m)
        {
            double[] result = new double[2 * m + 2];
            for (int i = 0; i < 2 * m + 2; i++)
            {
                result[i] = 1.0;
            }

            return result;
        }

        public virtual double[][] generate_W_n_ik(int m, int n)
        {
            double[][] result = new double[m][];
            for (int i = 0; i < m; i++)
            {
                result[i] = new double[n];
                for (int k = 0; k < n; k++)
                {
                    result[i][k] = 1.0;
                }
            }

            return result;
        }

        public virtual double[][] generate_W_r_ih(int m, int r)
        {
            double[][] result = new double[4 * m][];
            for (int i = 0; i < 4 * m; i++)
            {
                result[i] = new double[r];
                for (int h = 0; h < r; h++)
                {
                    result[i][h] = i < m ? 1.0 : 0.0;
                }
            }

            return result;
        }

        public virtual double[][] generate_W_a_if(int m, int numL)
        {
            double[][] result = new double[2 * m][];
            for (int i = 0; i < 2 * m; i++)
            {
                result[i] = new double[numL];
                for (int f = 0; f < numL; f++)
                {
                    result[i][f] = 1.0;
                }
            }

            return result;
        }

        public virtual double[][] generate_s_in_ik(int n, int m)
        {
            double[][] result = new double[m + 2][];
            for (int i = 0; i < m + 2; i++)
            {
                result[i] = new double[n];
                for (int k = 0; k < n; k++)
                {
                    result[i][k] = (i < m) ? Rounding.Add(Rounding.Multiply(Random.Shared.NextDouble(), 4.0), 2.0) : Rounding.Value(0.0);
                }
            }

            return result;
        }

        public virtual double[][] generate_s_out_ik(int n, int m)
        {
            double[][] result = new double[m + 2][];
            for (int i = 0; i < m + 2; i++)
            {
                result[i] = new double[n];
                for (int k = 0; k < n; k++)
                {
                    result[i][k] = (i < m) ? Rounding.Add(Rounding.Multiply(Random.Shared.NextDouble(), 4.0), 2.0) : Rounding.Value(0.0);
                }
            }

            return result;
        }

        public virtual int[] generate_b_in_k(int n, int capacity)
        {
            int[] result = new int[n];
            for (int k = 0; k < n; k++)
                result[k] = capacity;
            return result;
        }

        public virtual int[] generate_b_out_k(int n, int capacity)
        {
            int[] result = new int[n];
            for (int k = 0; k < n; k++)
                result[k] = capacity;
            return result;
        }

        public virtual double[][][] generate_W_in_ikb(int m, int n, int[] b_in_k)
        {
            double[][][] result = new double[m][][];
            for (int i = 0; i < m; i++)
            {
                result[i] = new double[n][];
                for (int k = 0; k < n; k++)
                {
                    result[i][k] = new double[b_in_k[k]];
                    for (int b = 0; b < b_in_k[k]; b++)
                        result[i][k][b] = 0.0;
                }
            }

            return result;
        }

        public virtual double[][][] generate_W_out_ikb(int m, int n, int[] b_out_k)
        {
            double[][][] result = new double[m][][];
            for (int i = 0; i < m; i++)
            {
                result[i] = new double[n][];
                for (int k = 0; k < n; k++)
                {
                    result[i][k] = new double[b_out_k[k]];
                    for (int b = 0; b < b_out_k[k]; b++)
                        result[i][k][b] = 0.0;
                }
            }

            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using ILOG.Concert;
using ILOG.CPLEX;

namespace PDP_ASRS_WIP
{
    public class MilpExtractedSolution
    {
        public double Objective;
        public List<int> AsrsSequence = new List<int>();
        public int[] AsrsLocation = Array.Empty<int>();
        public List<int>[] AmrSequences = Array.Empty<List<int>>();
        public int[] RobotOpAmr = Array.Empty<int>();
        public int[] RobotOpNode = Array.Empty<int>();
        public List<int>[] WorkstationSequences = Array.Empty<List<int>>();
        public int[] JobWorkstation = Array.Empty<int>();
        public int[] InputSlot = Array.Empty<int>();
        public int[] OutputSlot = Array.Empty<int>();
        public List<int>[][] InputSlotSequences = Array.Empty<List<int>[]>();
        public List<int>[][] OutputSlotSequences = Array.Empty<List<int>[]>();
        public double[] AsrsStart = Array.Empty<double>();
        public double[] AsrsCompletion = Array.Empty<double>();
        public double[] RobotStart = Array.Empty<double>();
        public double[] RobotCompletion = Array.Empty<double>();
        public double[] ProcessStart = Array.Empty<double>();
        public double[] ProcessCompletion = Array.Empty<double>();
        public double[] BufferInEntry = Array.Empty<double>();
        public double[] BufferInExit = Array.Empty<double>();
        public double[] BufferOutEntry = Array.Empty<double>();
        public double[] BufferOutExit = Array.Empty<double>();
        public double[] Tardiness = Array.Empty<double>();
        public double TotalTardiness;
        public double MinSelectedBinary = 1.0;
        public double BigM = 99999.0;
    }

    public static class MilpSequenceVerifier
    {
        const double Tolerance = 1e-3;
        public static MilpExtractedSolution Extract(Cplex cplex, int numL, int r, int n, int m, IIntVar[][][][][] Xr_ijklh, IIntVar[][][] Yn_ijk, IIntVar[][][][] Ya_ijfg, INumVar[][] Cr_ih, INumVar[][] Lr_ih, INumVar[][] Cn_ik, INumVar[][] Ln_ik, INumVar[][] Ca_if, INumVar[][] La_if, IIntVar[][][] Ain_ikb, IIntVar[][][] Aout_ikb, IIntVar[][][][] Yin_ijkb, IIntVar[][][][] Yout_ijkb, INumVar[][] Lin_ik, INumVar[][] Cin_ik, INumVar[][] Lout_ik, INumVar[][] Cout_ik, int[] b_in_k, int[] b_out_k, double[] d_i)
        {
            double Val(INumVar variable)
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

            double minSelectedBinary = 1.0;
            bool Selected(IIntVar variable)
            {
                double value;
                try
                {
                    value = cplex.GetValue(variable);
                }
                catch (Cplex.UnknownObjectException)
                {
                    return false;
                }

                if (value <= 0.5)
                {
                    return false;
                }

                minSelectedBinary = Math.Min(minSelectedBinary, value);
                return true;
            }

            var extracted = new MilpExtractedSolution
            {
                Objective = Rounding.Value(cplex.GetObjValue()),
                AsrsLocation = new int[2 * m],
                AmrSequences = new List<int>[r],
                RobotOpAmr = new int[4 * m],
                RobotOpNode = new int[4 * m],
                WorkstationSequences = new List<int>[n],
                JobWorkstation = new int[m],
                InputSlot = new int[m],
                OutputSlot = new int[m],
                InputSlotSequences = new List<int>[n][],
                OutputSlotSequences = new List<int>[n][],
                AsrsStart = new double[2 * m],
                AsrsCompletion = new double[2 * m],
                RobotStart = new double[4 * m],
                RobotCompletion = new double[4 * m],
                ProcessStart = new double[m],
                ProcessCompletion = new double[m],
                BufferInEntry = new double[m],
                BufferInExit = new double[m],
                BufferOutEntry = new double[m],
                BufferOutExit = new double[m],
                Tardiness = new double[m]
            };
            for (int op = 0; op < 2 * m; op++)
            {
                extracted.AsrsLocation[op] = -1;
            }

            for (int op = 0; op < 4 * m; op++)
            {
                extracted.RobotOpAmr[op] = -1;
                extracted.RobotOpNode[op] = -1;
            }

            for (int i = 0; i < m; i++)
            {
                extracted.JobWorkstation[i] = -1;
                extracted.InputSlot[i] = -1;
                extracted.OutputSlot[i] = -1;
            }

            int[] asrsNext = new int[2 * m + 2];
            for (int op = 0; op < 2 * m + 2; op++)
            {
                asrsNext[op] = -1;
            }

            for (int i = 0; i < 2 * m + 2; i++)
            {
                for (int j = 0; j < 2 * m + 2; j++)
                {
                    for (int f = 0; f < numL + 2; f++)
                    {
                        for (int g = 0; g < numL + 2; g++)
                        {
                            if (!Selected(Ya_ijfg[i][j][f][g]))
                            {
                                continue;
                            }

                            asrsNext[i] = j;
                            if (i < 2 * m && f < numL)
                            {
                                extracted.AsrsLocation[i] = f;
                            }

                            if (j < 2 * m && g < numL)
                            {
                                extracted.AsrsLocation[j] = g;
                            }
                        }
                    }
                }
            }

            int current = asrsNext[2 * m];
            for (int step = 0; step <= 2 * m && current >= 0 && current < 2 * m; step++)
            {
                extracted.AsrsSequence.Add(current);
                current = asrsNext[current];
            }

            for (int op = 0; op < 2 * m; op++)
            {
                int loc = extracted.AsrsLocation[op];
                if (loc >= 0)
                {
                    extracted.AsrsStart[op] = Val(La_if[op][loc]);
                    extracted.AsrsCompletion[op] = Val(Ca_if[op][loc]);
                }
            }

            for (int h = 0; h < r; h++)
            {
                extracted.AmrSequences[h] = new List<int>();
                int[] robotNext = new int[4 * m + 2];
                for (int op = 0; op < 4 * m + 2; op++)
                {
                    robotNext[op] = -1;
                }

                for (int i = 0; i < 4 * m + 2; i++)
                {
                    for (int j = 0; j < 4 * m + 2; j++)
                    {
                        for (int k = 0; k < n + 2; k++)
                        {
                            for (int l = 0; l < n + 2; l++)
                            {
                                if (!Selected(Xr_ijklh[i][j][k][l][h]))
                                {
                                    continue;
                                }

                                robotNext[i] = j;
                                if (i < 4 * m)
                                {
                                    extracted.RobotOpNode[i] = k;
                                }

                                if (j < 4 * m)
                                {
                                    extracted.RobotOpNode[j] = l;
                                }
                            }
                        }
                    }
                }

                int op2 = robotNext[4 * m];
                for (int step = 0; step <= 4 * m && op2 >= 0 && op2 < 4 * m; step++)
                {
                    extracted.AmrSequences[h].Add(op2);
                    extracted.RobotOpAmr[op2] = h;
                    extracted.RobotStart[op2] = Val(Lr_ih[op2][h]);
                    extracted.RobotCompletion[op2] = Val(Cr_ih[op2][h]);
                    op2 = robotNext[op2];
                }
            }

            for (int k = 0; k < n; k++)
            {
                extracted.WorkstationSequences[k] = new List<int>();
                int[] jobNext = new int[m + 2];
                for (int i = 0; i < m + 2; i++)
                {
                    jobNext[i] = -1;
                }

                for (int i = 0; i < m + 2; i++)
                {
                    for (int j = 0; j < m + 2; j++)
                    {
                        if (Selected(Yn_ijk[i][j][k]))
                        {
                            jobNext[i] = j;
                        }
                    }
                }

                int job = jobNext[m];
                for (int step = 0; step <= m && job >= 0 && job < m; step++)
                {
                    extracted.WorkstationSequences[k].Add(job);
                    extracted.JobWorkstation[job] = k;
                    extracted.ProcessStart[job] = Val(Ln_ik[job][k]);
                    extracted.ProcessCompletion[job] = Val(Cn_ik[job][k]);
                    job = jobNext[job];
                }
            }

            for (int i = 0; i < m; i++)
            {
                if (extracted.JobWorkstation[i] >= 0)
                {
                    continue;
                }

                int node = extracted.RobotOpNode[m + i];
                if (node < 0 || node >= n)
                {
                    node = extracted.RobotOpNode[2 * m + i];
                }

                if (node >= 0 && node < n)
                {
                    extracted.JobWorkstation[i] = node;
                }
            }

            for (int k = 0; k < n; k++)
            {
                extracted.InputSlotSequences[k] = new List<int>[b_in_k[k]];
                extracted.OutputSlotSequences[k] = new List<int>[b_out_k[k]];
                for (int b = 0; b < b_in_k[k]; b++)
                {
                    var members = new List<int>();
                    for (int i = 0; i < m; i++)
                    {
                        if (Selected(Ain_ikb[i][k][b]))
                        {
                            members.Add(i);
                            extracted.InputSlot[i] = b;
                        }
                    }

                    var ordered = new List<int>();
                    for (int position = 0; position < members.Count; position++)
                    {
                        int best = -1;
                        int bestPredecessors = int.MaxValue;
                        foreach (int candidate in members)
                        {
                            if (ordered.Contains(candidate))
                            {
                                continue;
                            }

                            int predecessors = 0;
                            foreach (int other in members)
                            {
                                if (other != candidate && Selected(Yin_ijkb[other][candidate][k][b]))
                                {
                                    predecessors++;
                                }
                            }

                            if (predecessors < bestPredecessors)
                            {
                                bestPredecessors = predecessors;
                                best = candidate;
                            }
                        }

                        ordered.Add(best);
                    }

                    extracted.InputSlotSequences[k][b] = ordered;
                }

                for (int b = 0; b < b_out_k[k]; b++)
                {
                    var members = new List<int>();
                    for (int i = 0; i < m; i++)
                    {
                        if (Selected(Aout_ikb[i][k][b]))
                        {
                            members.Add(i);
                            extracted.OutputSlot[i] = b;
                        }
                    }

                    var ordered = new List<int>();
                    for (int position = 0; position < members.Count; position++)
                    {
                        int best = -1;
                        int bestPredecessors = int.MaxValue;
                        foreach (int candidate in members)
                        {
                            if (ordered.Contains(candidate))
                            {
                                continue;
                            }

                            int predecessors = 0;
                            foreach (int other in members)
                            {
                                if (other != candidate && Selected(Yout_ijkb[other][candidate][k][b]))
                                {
                                    predecessors++;
                                }
                            }

                            if (predecessors < bestPredecessors)
                            {
                                bestPredecessors = predecessors;
                                best = candidate;
                            }
                        }

                        ordered.Add(best);
                    }

                    extracted.OutputSlotSequences[k][b] = ordered;
                }
            }

            for (int i = 0; i < m; i++)
            {
                int k = extracted.JobWorkstation[i];
                if (k >= 0 && k < n)
                {
                    extracted.BufferInEntry[i] = Val(Lin_ik[i][k]);
                    extracted.BufferInExit[i] = Val(Cin_ik[i][k]);
                    extracted.BufferOutEntry[i] = Val(Lout_ik[i][k]);
                    extracted.BufferOutExit[i] = Val(Cout_ik[i][k]);
                }
            }

            double totalTardiness = 0.0;
            for (int i = 0; i < m; i++)
            {
                double tardiness = 0.0;
                if (extracted.AsrsLocation[m + i] >= 0)
                {
                    tardiness = Math.Max(0.0, Rounding.Subtract(extracted.AsrsCompletion[m + i], d_i[i]));
                }

                extracted.Tardiness[i] = tardiness;
                totalTardiness = Rounding.Add(totalTardiness, tardiness);
            }

            extracted.TotalTardiness = totalTardiness;
            extracted.MinSelectedBinary = minSelectedBinary;
            return extracted;
        }

        public static SequenceSolution ToSequenceSolution(MilpExtractedSolution extracted, int numL, int r, int n, int m, int[] b_in_k, int[] b_out_k)
        {
            var solution = new SequenceSolution
            {
                NumOperations = m,
                NumWorkstations = n,
                NumRobots = r,
                NumLocations = numL,
                AsrsSequence = new List<int>(extracted.AsrsSequence),
                AsrsLocation = new int[2 * m],
                AmrSequences = new List<int>[r],
                RobotOpAmr = new int[4 * m],
                WorkstationSequences = new List<int>[n],
                JobWorkstation = new int[m],
                InputSlot = new int[m],
                OutputSlot = new int[m],
                InputSlotSequences = new List<int>[n][],
                OutputSlotSequences = new List<int>[n][]
            };
            for (int op = 0; op < 2 * m; op++)
            {
                solution.AsrsLocation[op] = extracted.AsrsLocation[op];
            }

            for (int h = 0; h < r; h++)
            {
                solution.AmrSequences[h] = new List<int>(extracted.AmrSequences[h]);
            }

            for (int op = 0; op < 4 * m; op++)
            {
                solution.RobotOpAmr[op] = extracted.RobotOpAmr[op];
            }

            for (int k = 0; k < n; k++)
            {
                solution.WorkstationSequences[k] = new List<int>(extracted.WorkstationSequences[k]);
                solution.InputSlotSequences[k] = new List<int>[b_in_k[k]];
                for (int b = 0; b < b_in_k[k]; b++)
                {
                    solution.InputSlotSequences[k][b] = new List<int>(extracted.InputSlotSequences[k][b]);
                }

                solution.OutputSlotSequences[k] = new List<int>[b_out_k[k]];
                for (int b = 0; b < b_out_k[k]; b++)
                {
                    solution.OutputSlotSequences[k][b] = new List<int>(extracted.OutputSlotSequences[k][b]);
                }
            }

            for (int i = 0; i < m; i++)
            {
                solution.JobWorkstation[i] = extracted.JobWorkstation[i];
                solution.InputSlot[i] = extracted.InputSlot[i];
                solution.OutputSlot[i] = extracted.OutputSlot[i];
            }

            return solution;
        }

        public static List<string> Compare(MilpExtractedSolution extracted, DecodedSchedule decoded, SequenceSolution solution, int numL, int r, int n, int m, double[][] p_ik, double[][][] s_ikh, double[][][] t_klh, double[][][][] r_ijfg, double[][] s_in_ik, double[][] s_out_ik, int[] b_in_k, int[] b_out_k, double[] d_i, out bool isConsistent, out int laterTimings, out int milpSelfViolations, out double worstMilpSelfViolation)
        {
            var lines = new List<string>();
            int laterCount = 0;
            int earlierCount = 0;
            int equalCount = 0;
            string Round(double value)
            {
                return Convert.ToString(Rounding.Value(value));
            }

            string Row(string label, double milpValue, double decodedValue)
            {
                double difference = Rounding.Subtract(decodedValue, milpValue);
                string verdict;
                if (Math.Abs(difference) <= Tolerance)
                {
                    verdict = "MATCH";
                    equalCount++;
                }
                else if (difference < 0.0)
                {
                    verdict = "EARLIER (MILP slack)";
                    earlierCount++;
                }
                else
                {
                    verdict = "LATER  <-- MISMATCH";
                    laterCount++;
                }

                return "  " + label.PadRight(30) + " MILP=" + Round(milpValue).PadRight(12) + " DECODED=" + Round(decodedValue).PadRight(12) + " diff=" + Round(difference).PadRight(12) + verdict;
            }

            lines.Add("==================================================");
            lines.Add("MILP SOLUTION -> SEQUENCE -> DECODER VERIFICATION");
            lines.Add("==================================================");
            lines.Add("--------------------------------------------------");
            lines.Add("SEQUENCED MILP SOLUTION (Pi fed into the decoder)");
            var asrsLabels = new List<string>();
            foreach (int op in solution.AsrsSequence)
            {
                string kind = op < m ? "R" + Convert.ToString(op) : "S" + Convert.ToString(op - m);
                asrsLabels.Add(kind + "@loc" + Convert.ToString(solution.AsrsLocation[op]));
            }

            lines.Add("  Pi^a   : " + (asrsLabels.Count > 0 ? string.Join(" -> ", asrsLabels) : "(empty)"));
            for (int h = 0; h < r; h++)
            {
                var robotLabels = new List<string>();
                foreach (int op in solution.AmrSequences[h])
                {
                    string kind = op < m ? "P+" : (op < 2 * m ? "D+" : (op < 3 * m ? "P-" : "D-"));
                    robotLabels.Add(kind + Convert.ToString(op % m));
                }

                lines.Add("  Pi^r[" + Convert.ToString(h) + "]: " + (robotLabels.Count > 0 ? string.Join(" -> ", robotLabels) : "(empty)"));
            }

            for (int k = 0; k < n; k++)
            {
                lines.Add("  Pi^n[" + Convert.ToString(k) + "]: " + (solution.WorkstationSequences[k].Count > 0 ? string.Join(" -> ", solution.WorkstationSequences[k]) : "(empty)"));
                for (int b = 0; b < b_in_k[k]; b++)
                {
                    lines.Add("  Pi^bin[" + Convert.ToString(k) + "][" + Convert.ToString(b) + "]: " + (solution.InputSlotSequences[k][b].Count > 0 ? string.Join(" -> ", solution.InputSlotSequences[k][b]) : "(empty)"));
                }

                for (int b = 0; b < b_out_k[k]; b++)
                {
                    lines.Add("  Pi^bout[" + Convert.ToString(k) + "][" + Convert.ToString(b) + "]: " + (solution.OutputSlotSequences[k][b].Count > 0 ? string.Join(" -> ", solution.OutputSlotSequences[k][b]) : "(empty)"));
                }
            }

            lines.Add("--------------------------------------------------");
            lines.Add("STRUCTURAL FEASIBILITY OF THE SEQUENCED SOLUTION");
            lines.Add("  Decoder feasible: " + Convert.ToString(decoded.IsFeasible));
            foreach (string violation in decoded.Violations)
            {
                lines.Add("  VIOLATION: " + violation);
            }

            lines.Add("--------------------------------------------------");
            lines.Add("AS/RS OPERATIONS (La_if / Ca_if)");
            foreach (int op in solution.AsrsSequence)
            {
                string label = op < m ? "RETRIEVAL job " + Convert.ToString(op) : "STORAGE job " + Convert.ToString(op - m);
                lines.Add(Row(label + " start", extracted.AsrsStart[op], decoded.AsrsStart[op]));
                lines.Add(Row(label + " end", extracted.AsrsCompletion[op], decoded.AsrsCompletion[op]));
            }

            lines.Add("--------------------------------------------------");
            lines.Add("ROBOT OPERATIONS (Lr_ih / Cr_ih)");
            for (int h = 0; h < r; h++)
            {
                foreach (int op in solution.AmrSequences[h])
                {
                    string kind = op < m ? "P+" : (op < 2 * m ? "D+" : (op < 3 * m ? "P-" : "D-"));
                    string label = kind + " job " + Convert.ToString(op % m) + " (AMR " + Convert.ToString(h) + ")";
                    lines.Add(Row(label + " start", extracted.RobotStart[op], decoded.RobotStart[op]));
                    lines.Add(Row(label + " end", extracted.RobotCompletion[op], decoded.RobotCompletion[op]));
                }
            }

            lines.Add("--------------------------------------------------");
            lines.Add("WORKSTATION PROCESSING (Ln_ik / Cn_ik)");
            for (int k = 0; k < n; k++)
            {
                foreach (int job in solution.WorkstationSequences[k])
                {
                    string label = "PROCESS job " + Convert.ToString(job) + " (WS " + Convert.ToString(k) + ")";
                    lines.Add(Row(label + " start", extracted.ProcessStart[job], decoded.ProcessStart[job]));
                    lines.Add(Row(label + " end", extracted.ProcessCompletion[job], decoded.ProcessCompletion[job]));
                }
            }

            lines.Add("--------------------------------------------------");
            lines.Add("BUFFER ENTRY EVENTS (Lin_ik / Lout_ik)");
            for (int i = 0; i < m; i++)
            {
                if (solution.InputSlot[i] >= 0)
                {
                    lines.Add(Row("L_in job " + Convert.ToString(i) + " (slot " + Convert.ToString(solution.InputSlot[i]) + ")", extracted.BufferInEntry[i], decoded.BufferInEntry[i]));
                }

                if (solution.OutputSlot[i] >= 0)
                {
                    lines.Add(Row("L_out job " + Convert.ToString(i) + " (slot " + Convert.ToString(solution.OutputSlot[i]) + ")", extracted.BufferOutEntry[i], decoded.BufferOutEntry[i]));
                }
            }

            lines.Add("--------------------------------------------------");
            lines.Add("TARDINESS PER JOB (T_i)");
            for (int i = 0; i < m; i++)
            {
                lines.Add(Row("T job " + Convert.ToString(i) + " (due " + Round(d_i[i]) + ")", extracted.Tardiness[i], decoded.Tardiness[i]));
            }

            int selfViolations = 0;
            double worstSelfViolation = 0.0;
            void CheckSelf(string label, double left, double right)
            {
                double shortfall = Rounding.Subtract(right, left);
                if (shortfall <= Tolerance)
                {
                    return;
                }

                selfViolations++;
                worstSelfViolation = Math.Max(worstSelfViolation, shortfall);
                lines.Add("  " + label.PadRight(48) + " short by " + Round(shortfall));
            }

            lines.Add("--------------------------------------------------");
            lines.Add("MILP SELF-CONSISTENCY (reported times vs the model's own constraints)");
            int previousAsrsOp = 2 * m;
            int previousAsrsLoc = numL;
            foreach (int op in solution.AsrsSequence)
            {
                int loc = solution.AsrsLocation[op];
                if (loc < 0)
                {
                    continue;
                }

                CheckSelf("(70) Ca - La >= r_ijfg, AS/RS op " + Convert.ToString(op), Rounding.Subtract(extracted.AsrsCompletion[op], extracted.AsrsStart[op]), r_ijfg[previousAsrsOp][op][previousAsrsLoc][loc]);
                CheckSelf("(73) La >= Ca of AS/RS predecessor, op " + Convert.ToString(op), extracted.AsrsStart[op], previousAsrsOp == 2 * m ? 0.0 : extracted.AsrsCompletion[previousAsrsOp]);
                previousAsrsOp = op;
                previousAsrsLoc = loc;
            }

            for (int h = 0; h < r; h++)
            {
                int previousNode = n + 1;
                double previousCompletion = 0.0;
                foreach (int op in solution.AmrSequences[h])
                {
                    int node = extracted.RobotOpNode[op];
                    if (node < 0)
                    {
                        continue;
                    }

                    CheckSelf("(42) Cr - Lr >= s_ikh, robot op " + Convert.ToString(op), Rounding.Subtract(extracted.RobotCompletion[op], extracted.RobotStart[op]), s_ikh[op][node][h]);
                    CheckSelf("(44) Lr >= Cr(prev) + t_klh, robot op " + Convert.ToString(op), extracted.RobotStart[op], Rounding.Add(previousCompletion, t_klh[previousNode][node][h]));
                    previousNode = node;
                    previousCompletion = extracted.RobotCompletion[op];
                }
            }

            for (int k = 0; k < n; k++)
            {
                foreach (int job in solution.WorkstationSequences[k])
                {
                    CheckSelf("(59) Cn - Ln >= s_in + p + s_out, job " + Convert.ToString(job), Rounding.Subtract(extracted.ProcessCompletion[job], extracted.ProcessStart[job]), Rounding.Add(Rounding.Add(s_in_ik[job][k], p_ik[job][k]), s_out_ik[job][k]));
                }
            }

            double bigMSlack = Rounding.Multiply(extracted.BigM, 1.0 - extracted.MinSelectedBinary);
            lines.Add("  Smallest selected binary value : " + Convert.ToString(extracted.MinSelectedBinary));
            lines.Add("  Implied big-M slack (M * eps)  : " + Round(bigMSlack));
            lines.Add(selfViolations == 0 ? "  No self-violation: the MILP times satisfy the checked constraints." : "  " + Convert.ToString(selfViolations) + " self-violation(s), worst " + Round(worstSelfViolation) + " -> the MILP incumbent breaks its own constraints, decoded times are the reliable ones.");
            double objectiveDifference = Rounding.Subtract(decoded.TotalTardiness, extracted.Objective);
            bool objectiveMatches = Math.Abs(objectiveDifference) <= Tolerance;
            bool tardinessMatches = Math.Abs(Rounding.Subtract(decoded.TotalTardiness, extracted.TotalTardiness)) <= Tolerance;
            lines.Add("--------------------------------------------------");
            lines.Add("RESULT");
            lines.Add("  MILP objective (cplex.GetObjValue)   : " + Round(extracted.Objective));
            lines.Add("  MILP total tardiness (from Ca_if)    : " + Round(extracted.TotalTardiness));
            lines.Add("  Decoded total tardiness              : " + Round(decoded.TotalTardiness));
            lines.Add("  Difference (decoded - MILP)          : " + Round(objectiveDifference));
            lines.Add("  Decoded makespan (reference)         : " + Round(decoded.Makespan));
            lines.Add("  Timing rows matching / earlier / later: " + Convert.ToString(equalCount) + " / " + Convert.ToString(earlierCount) + " / " + Convert.ToString(laterCount));
            isConsistent = decoded.IsFeasible && objectiveMatches && tardinessMatches && laterCount == 0;
            laterTimings = laterCount;
            milpSelfViolations = selfViolations;
            worstMilpSelfViolation = worstSelfViolation;
            if (isConsistent)
            {
                lines.Add("  VERDICT: OK - the decoder reproduces the MILP solution.");
            }
            else
            {
                lines.Add("  VERDICT: MISMATCH");
                if (!decoded.IsFeasible)
                {
                    lines.Add("    - the sequenced MILP solution is rejected by the decoder's structural checks.");
                }

                if (!tardinessMatches)
                {
                    lines.Add("    - total tardiness differs from the MILP schedule.");
                }

                if (!objectiveMatches)
                {
                    lines.Add(objectiveDifference > 0.0 ? "    - decoded objective is WORSE: the decoder adds arcs the MILP does not have, or Pi was misread." : "    - decoded objective is BETTER: the decoder misses a MILP constraint, or CPLEX stopped before optimality.");
                }

                if (laterCount > 0)
                {
                    lines.Add("    - " + Convert.ToString(laterCount) + " operation time(s) are later than in the MILP schedule.");
                }

                if (selfViolations > 0)
                {
                    lines.Add("    - CAUSE: the MILP incumbent itself violates " + Convert.ToString(selfViolations) + " of its own constraints by up to " + Round(worstSelfViolation) + " (big-M " + Round(extracted.BigM) + " times the integrality tolerance).");
                    lines.Add("      Lower big_M or tighten Cplex.Param.MIP.Tolerances.Integrality before trusting the MILP times.");
                }
            }

            lines.Add("==================================================");
            return lines;
        }
    }
}

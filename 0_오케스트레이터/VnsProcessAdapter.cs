using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DT_KETI
{
    public sealed class VnsProcessAdapter
    {
        private readonly PipelineRunConfig _config;
        public event Action<string> OutputReceived;
        public VnsProcessAdapter(PipelineRunConfig config)
        {
            _config = config ?? throw new ArgumentNullException("config");
        }

        public string Run(string snapshotPath, string replayDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
                throw new FileNotFoundException("P4R input/snapshot was not found.", snapshotPath);
            EnsureExecutable();
            string mode = (_config.PlannerMode ?? "hyb").ToLowerInvariant();
            if (mode != "hyb" && mode != "vns")
                throw new InvalidOperationException("PlannerMode must be hyb or vns.");
            string runDirectory = Path.Combine(_config.ReportDirectory, "planner", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runDirectory);
            string stagedInput = Path.Combine(runDirectory, Path.GetFileName(snapshotPath));
            File.Copy(snapshotPath, stagedInput);
            WritePlannerHorizonConfig(runDirectory);
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.VnsExecutablePath,
                Arguments = Quote(stagedInput) + " " + mode + " " + _config.PlannerSeed + " " + _config.PlannerEvaluations,
                WorkingDirectory = Path.GetDirectoryName(_config.VnsExecutablePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (!string.IsNullOrWhiteSpace(replayDirectory))
            {
                Directory.CreateDirectory(replayDirectory);
                startInfo.EnvironmentVariables["PDP_REPLAY_DIR"] = Path.GetFullPath(replayDirectory);
                Publish("[VNS] Replay output: " + Path.GetFullPath(replayDirectory));
            }

            string processLog = RunProcess(startInfo, "VNS");
            string outputPath = Path.Combine(runDirectory, Path.GetFileNameWithoutExtension(stagedInput) + "_OUTPUT_" + mode + ".json");
            if (!File.Exists(outputPath))
                throw new FileNotFoundException("VNS completed but its P4R output was not found.\n" + processLog, outputPath);
            Publish("[VNS] Result P4R: " + outputPath);
            return outputPath;
        }

        private void WritePlannerHorizonConfig(string runDirectory)
        {
            string horizon = Synchronization.CurrentDateTimeUtc;
            WritePlannerConfig(runDirectory, "  \"HorizonStart\": \"" + horizon + "\",\r\n");
            Publish("[VNS] HorizonStart synchronized to " + horizon);
        }

        private const int RackRows = 10, RackColumns = 4, RackTiers = 4;
        private static void WritePlannerConfig(string directory, string extraEntries)
        {
            string json = "{\r\n" + extraEntries + "  \"RackWidth\": " + RackRows + ",\r\n" + "  \"RackDepth\": " + RackColumns + ",\r\n" + "  \"RackLevels\": " + RackTiers + "\r\n}\r\n";
            File.WriteAllText(Path.Combine(directory, "scenario_config.json"), json, Encoding.UTF8);
        }

        public string ExportVc(string snapshotPath)
        {
            return Export(snapshotPath, "exportvc", "_VCRAW.json");
        }

        public string ExportInstance(string snapshotPath)
        {
            return Export(snapshotPath, "exportinst", "_INSTANCE.json");
        }

        private string Export(string snapshotPath, string mode, string suffix)
        {
            if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
                throw new FileNotFoundException("P4R snapshot was not found.", snapshotPath);
            EnsureExecutable();
            string exportDirectory = Path.Combine(_config.ReportDirectory, mode, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportDirectory);
            snapshotPath = CopySnapshot(snapshotPath, exportDirectory);
            WritePlannerConfig(exportDirectory, "");
            var startInfo = new ProcessStartInfo
            {
                FileName = _config.VnsExecutablePath,
                Arguments = Quote(Path.GetFullPath(snapshotPath)) + " " + mode,
                WorkingDirectory = Path.GetDirectoryName(_config.VnsExecutablePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            RunProcess(startInfo, "P4R exportvc");
            string vcrawPath = Path.Combine(Path.GetDirectoryName(snapshotPath), Path.GetFileNameWithoutExtension(snapshotPath) + suffix);
            if (!File.Exists(vcrawPath))
                throw new FileNotFoundException("P4R exportvc output was not found.", vcrawPath);
            Publish("[P4R exportvc] Result VCRAW: " + vcrawPath);
            return vcrawPath;
        }

        private static string CopySnapshot(string input, string directory)
        {
            string target = Path.Combine(directory, Path.GetFileName(input));
            File.Copy(input, target);
            return target;
        }

        private string RunProcess(ProcessStartInfo startInfo, string label)
        {
            var combined = new StringBuilder();
            var summary = label == "VNS" ? new VnsProgressSummary(startInfo.EnvironmentVariables.ContainsKey("PDP_REPLAY_DIR")) : null;
            Publish("[" + label + "] Start: " + startInfo.FileName + " " + startInfo.Arguments);
            using (var process = new Process
            {
                StartInfo = startInfo
            }

            )
            {
                process.OutputDataReceived += delegate (object sender, DataReceivedEventArgs args)
                {
                    if (args.Data == null)
                        return;
                    lock (combined)
                        combined.AppendLine(args.Data);
                    Publish("[" + label + "] " + args.Data);
                    if (summary != null)
                    {
                        string progress = summary.Translate(args.Data);
                        if (progress != null)
                            PipelineConsole.Progress(progress);
                    }
                };
                process.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs args)
                {
                    if (args.Data == null)
                        return;
                    lock (combined)
                        combined.AppendLine(args.Data);
                    Publish("[" + label + "][stderr] " + args.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                int timeout = (int)Math.Min((_config.VnsTimeoutSeconds > 0 ? _config.VnsTimeoutSeconds : 600.0) * 1000.0, int.MaxValue);
                if (!process.WaitForExit(timeout))
                {
                    process.Kill();
                    throw new TimeoutException(label + " execution timed out.");
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(label + " failed (exit " + process.ExitCode + ").\n" + combined);
                Publish("[" + label + "] Completed.");
            }

            return combined.ToString();
        }

        private void Publish(string message)
        {
            Action<string> handler = OutputReceived;
            if (handler != null)
                handler(message);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void EnsureExecutable()
        {
            if (!string.IsNullOrWhiteSpace(_config.VnsExecutablePath) && File.Exists(_config.VnsExecutablePath))
                return;
            if (string.IsNullOrWhiteSpace(_config.VnsProjectPath) || !File.Exists(_config.VnsProjectPath))
                throw new FileNotFoundException("Neither the VNS executable nor its project was found.", _config.VnsProjectPath);
            var buildInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build " + Quote(_config.VnsProjectPath) + " -c Debug --nologo -v q",
                WorkingDirectory = Path.GetDirectoryName(_config.VnsProjectPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Publish("[VNS build] Executable not found; building the VNS project.");
            using (var process = Process.Start(buildInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("VNS project build failed.\n" + error + "\n" + output);
                Publish("[VNS build] Completed.");
            }

            if (!File.Exists(_config.VnsExecutablePath))
                throw new FileNotFoundException("VNS project built, but the configured executable was not found.", _config.VnsExecutablePath);
        }
    }

    public sealed class VnsProgressSummary
    {
        private readonly bool _trainingData;
        private bool _best, _initialShown;
        private string _tardiness;
        public VnsProgressSummary(bool trainingData)
        {
            _trainingData = trainingData;
        }

        public string Translate(string line)
        {
            string text = (line ?? "").Trim();
            Match m = Regex.Match(text, @"^Jobs m=(\d+), Workstations n=(\d+).*AMRs r=(\d+).*ASRS locations=(\d+)");
            if (m.Success)
                return "  VNS problem: " + m.Groups[1].Value + " jobs, " + m.Groups[2].Value + " workstations, " + m.Groups[3].Value + " AMRs, " + m.Groups[4].Value + " ASRS cells";
            if (Regex.IsMatch(text, @"^(HYB|VNS) BEST SOLUTION$"))
            {
                _best = true;
                return null;
            }

            m = Regex.Match(text, @"^Total tardiness \(objective \(1\)\):\s*([\d.]+)");
            if (m.Success)
            {
                _tardiness = Seconds(m.Groups[1].Value);
                return null;
            }

            m = Regex.Match(text, @"^Makespan C_max \(reference\):\s*([\d.]+)");
            if (m.Success && _tardiness != null && (_best || !_initialShown))
            {
                string result = (_best ? "  Best schedule: " : "  Initial schedule (dispatching rule): ") + "total tardiness " + _tardiness + " s, makespan " + Seconds(m.Groups[1].Value) + " s";
                _initialShown = true;
                _tardiness = null;
                return result;
            }

            m = Regex.Match(text, @"^(HYB|VNS) \(seed=(\d+), evaluation budget=(\d+)");
            if (m.Success)
                return "  Search: " + m.Groups[1].Value + " metaheuristic, seed " + m.Groups[2].Value + ", budget " + m.Groups[3].Value + " evaluations";
            m = Regex.Match(text, @"^(HYB|VNS) improvement \(initial descent\): tardiness=([\d.]+), makespan=([\d.]+)");
            if (m.Success)
                return "  Local search improved: total tardiness " + Seconds(m.Groups[2].Value) + " s, makespan " + Seconds(m.Groups[3].Value) + " s";
            m = Regex.Match(text, @"^(HYB|VNS) improvement \(([^)]+)\): tardiness=([\d.]+), makespan=([\d.]+)");
            if (m.Success)
                return "  New best found (" + Move(m.Groups[2].Value) + "): total tardiness " + Seconds(m.Groups[3].Value) + " s, makespan " + Seconds(m.Groups[4].Value) + " s";
            m = Regex.Match(text, @"^(HYB|VNS): iterations=(\d+), destroy/shake=(\d+)/(\d+)");
            if (m.Success)
                return m.Groups[2].Value == "0" ? null : "  Search iterations: " + m.Groups[2].Value + " (destroy-and-repair " + m.Groups[3].Value + ", shake " + m.Groups[4].Value + ")";
            m = Regex.Match(text, @"^(HYB|VNS) evaluations: (\d+), stop: (.+)$");
            if (m.Success)
                return "  Candidate schedules evaluated: " + long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture);
            if (Regex.IsMatch(text, @"^REPLAY VERIFY \[(hyb|vns)[^\]]*\]: sequences OK, tardiness OK"))
                return "  Schedule verified by replay.";
            if (_trainingData && text.StartsWith("Replay export:", StringComparison.Ordinal))
                return "  GNN training data exported (decision replay logs).";
            m = Regex.Match(text, @"^Elapsed seconds: ([\d.]+)");
            if (m.Success)
                return "  VNS finished in " + Seconds(m.Groups[1].Value) + " s.";
            return null;
        }

        private static string Move(string move)
        {
            return move == "destroy" ? "destroy & repair" : move;
        }

        private static string Seconds(string value)
        {
            double number;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number.ToString("0.0", CultureInfo.InvariantCulture) : value;
        }
    }
}

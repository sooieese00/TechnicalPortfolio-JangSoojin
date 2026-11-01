using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public sealed class TrainedRuleModelPipeline
    {
        private readonly PipelineRunConfig config;
        public event Action<string> OutputReceived;
        public TrainedRuleModelPipeline(PipelineRunConfig config)
        {
            this.config = config ?? throw new ArgumentNullException("config");
        }

        public string Build(string vcRawPath)
        {
            RequireFile(config.PythonExecutablePath, "Python executable");
            RequireFile(vcRawPath, "Initial VCRAW");
            RequireFile(config.GnnInstancePath, "Initial GNN INSTANCE");
            if (config.ReuseTrainedRuleOnInitial && CanReuse(vcRawPath))
            {
                Publish("[TRAINED RULE] Reusing validated model for identical VCRAW: " + config.TrainedRuleModelPath);
                return config.TrainedRuleModelPath;
            }

            RequireFile(config.GnnRuleTemplatePath, "Rule VCMX template");
            RequireFile(Path.Combine(config.WorkspaceToolsDirectory, "train_gnn_for_vcmx.py"), "GNN training wrapper");
            string[] replayFiles = Directory.Exists(config.GnnTrainingDataDirectory) ? Directory.GetFiles(config.GnnTrainingDataDirectory, "replay_*.jsonl") : new string[0];
            if (replayFiles.Length == 0)
                throw new InvalidOperationException("Initial VNS produced no replay_*.jsonl training data in " + config.GnnTrainingDataDirectory);
            Directory.CreateDirectory(config.GnnWeightArtifactDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(config.TrainedRuleModelPath));
            string tag = "initial_" + config.RunId;
            Publish("[TRAINED RULE] Training GNN from " + replayFiles.Length + " replay file(s). Epochs=" + config.GnnTrainingEpochs);
            PipelineConsole.Progress("GNN training: " + replayFiles.Length + " replay file(s), " + config.GnnTrainingEpochs + " epochs.");
            RunPython("GNN training", config.GnnTrainingTimeoutSeconds, Path.Combine(config.WorkspaceToolsDirectory, "train_gnn_for_vcmx.py"), "--gnn-dir", config.GnnDirectory, "--data", config.GnnTrainingDataDirectory, "--output", config.GnnTrainingCheckpointPath, "--tag", tag, "--epochs", config.GnnTrainingEpochs.ToString());
            string weightPrefix = Path.Combine(config.GnnWeightArtifactDirectory, "lk-gnn-weights");
            PipelineConsole.Progress("Exporting trained GNN weights...");
            Publish("[TRAINED RULE] Exporting LkGnn checkpoint weights.");
            RunPython("GNN weight export", 600.0, Path.Combine(config.WorkspaceToolsDirectory, "export_lk_gnn_weights.py"), config.GnnTrainingCheckpointPath, weightPrefix);
            string runtimePath = Path.Combine(config.GnnWeightArtifactDirectory, "vc-lk-runtime.py");
            RunPython("VC LkGnn runtime generation", 600.0, Path.Combine(config.WorkspaceToolsDirectory, "build_vc_lk_runtime.py"), "--gnn-dir", config.GnnDirectory, "--kernel", Path.Combine(config.WorkspaceToolsDirectory, "vc_lk_gnn_runtime.py"), "--output", runtimePath);
            string packagedModel = config.TrainedRuleModelPath + ".embedded.vcmx";
            string temporaryModel = config.TrainedRuleModelPath + ".building.vcmx";
            if (File.Exists(packagedModel))
                File.Delete(packagedModel);
            if (File.Exists(temporaryModel))
                File.Delete(temporaryModel);
            Publish("[TRAINED RULE] Embedding Python runtime and weights into VCMX.");
            PipelineConsole.Progress("Embedding the GNN (weights + inference runtime) into the Rule VC model...");
            RunPython("Rule VCMX packaging", 600.0, Path.Combine(config.WorkspaceToolsDirectory, "modify_vcmx_python_embedded_gnn.py"), config.GnnRuleTemplatePath, packagedModel, weightPrefix + ".manifest.json", weightPrefix + ".bin", config.GnnInstancePath, runtimePath);
            Publish("[TRAINED RULE] Applying logical pending-Storage recovery contract.");
            RunPython("Rule VCMX storage recovery patch", 600.0, Path.Combine(config.WorkspaceToolsDirectory, "modify_vcmx_pending_storage_recovery.py"), packagedModel, temporaryModel);
            if (File.Exists(packagedModel))
                File.Delete(packagedModel);
            PipelineConsole.Progress("Validating the GNN-embedded Rule VC model...");
            RunPython("Rule VCMX validation", 600.0, Path.Combine(config.WorkspaceToolsDirectory, "validate_vcmx_python_embedded_gnn.py"), temporaryModel);
            PublishModelAtomically(temporaryModel, config.TrainedRuleModelPath);
            WriteManifest(vcRawPath, replayFiles.Length);
            Publish("[TRAINED RULE] READY: " + config.TrainedRuleModelPath);
            return config.TrainedRuleModelPath;
        }

        private void PublishModelAtomically(string temporaryModel, string targetModel)
        {
            string previous = targetModel + ".previous.vcmx";
            if (File.Exists(targetModel))
            {
                if (File.Exists(previous))
                    File.Delete(previous);
                File.Replace(temporaryModel, targetModel, previous);
                Publish("[TRAINED RULE] Previous model retained: " + previous);
            }
            else
            {
                File.Move(temporaryModel, targetModel);
            }
        }

        private void WriteManifest(string vcRawPath, int replayCount)
        {
            var manifest = new JObject
            {
                ["format"] = "dt-keti-trained-rule/v1",
                ["runId"] = config.RunId,
                ["createdAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["trainingEpochs"] = config.GnnTrainingEpochs,
                ["replayCount"] = replayCount,
                ["checkpoint"] = config.GnnTrainingCheckpointPath,
                ["checkpointSha256"] = Sha256(config.GnnTrainingCheckpointPath),
                ["vcraw"] = Path.GetFullPath(vcRawPath),
                ["vcrawSha256"] = Sha256(vcRawPath),
                ["instance"] = Path.GetFullPath(config.GnnInstancePath),
                ["trainedRuleModel"] = config.TrainedRuleModelPath,
                ["trainedRuleSha256"] = Sha256(config.TrainedRuleModelPath),
                ["runtime"] = "vcmx-python-loop",
            };
            File.WriteAllText(config.TrainedRuleModelPath + ".manifest.json", manifest.ToString(Formatting.Indented), Encoding.UTF8);
        }

        private bool CanReuse(string currentVcRawPath)
        {
            string manifestPath = config.TrainedRuleModelPath + ".manifest.json";
            if (!File.Exists(config.TrainedRuleModelPath) || !File.Exists(manifestPath))
                return false;
            try
            {
                JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                string modelHash = (string)manifest["trainedRuleSha256"];
                if (string.IsNullOrWhiteSpace(modelHash) || !string.Equals(modelHash, Sha256(config.TrainedRuleModelPath), StringComparison.OrdinalIgnoreCase))
                    return false;
                string vcRawHash = (string)manifest["vcrawSha256"];
                if (string.IsNullOrWhiteSpace(vcRawHash))
                {
                    string previousVcRaw = (string)manifest["vcraw"];
                    if (string.IsNullOrWhiteSpace(previousVcRaw) || !File.Exists(previousVcRaw))
                        return false;
                    vcRawHash = Sha256(previousVcRaw);
                }

                return string.Equals(vcRawHash, Sha256(currentVcRawPath), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Publish("[TRAINED RULE] Existing model cannot be reused: " + ex.Message);
                return false;
            }
        }

        private void RunPython(string label, double timeoutSeconds, params string[] arguments)
        {
            var command = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (command.Length > 0)
                    command.Append(' ');
                command.Append(Quote(argument));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = config.PythonExecutablePath,
                Arguments = command.ToString(),
                WorkingDirectory = config.WorkspaceToolsDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            var log = new StringBuilder();
            Publish("[" + label + "] Start");
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
                    lock (log)
                        log.AppendLine(args.Data);
                    Publish("[" + label + "] " + args.Data);
                    if (label == "GNN training")
                    {
                        string progress = TranslateTrainingLine(args.Data, config.GnnTrainingEpochs);
                        if (progress != null)
                            PipelineConsole.Progress(progress);
                    }
                };
                process.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs args)
                {
                    if (args.Data == null)
                        return;
                    lock (log)
                        log.AppendLine(args.Data);
                    Publish("[" + label + "][stderr] " + args.Data);
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                int timeout = (int)Math.Min(Math.Max(timeoutSeconds, 1.0) * 1000.0, int.MaxValue);
                if (!process.WaitForExit(timeout))
                {
                    process.Kill();
                    throw new TimeoutException(label + " timed out.");
                }

                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(label + " failed (exit " + process.ExitCode + ").\n" + log);
            }

            Publish("[" + label + "] Completed");
        }

        public static string TranslateTrainingLine(string line, int epochs)
        {
            string text = (line ?? "").Trim();
            Match m = Regex.Match(text, @"^epoch\s+(\d+)\s+loss\s+([\d.]+)(?:\D+([\d.]+)\s+top-1\s+([\d.]+)\s+MRR\s+([\d.]+))?(.*<--)?");
            if (m.Success)
            {
                string result = "  Epoch " + m.Groups[1].Value + "/" + epochs + "  loss " + m.Groups[2].Value;
                if (m.Groups[3].Success)
                    result += "  val pairwise acc " + m.Groups[3].Value + "  top-1 " + m.Groups[4].Value + "  MRR " + m.Groups[5].Value;
                return m.Groups[6].Success ? result + "  (best)" : result;
            }

            if (text.StartsWith("분할"))
            {
                var numbers = Regex.Matches(text, @"\d+").Cast<Match>().Select(x => x.Value).ToArray();
                return numbers.Length < 2 ? null : "  Training data: " + numbers[numbers.Length - 2] + " training / " + numbers[numbers.Length - 1] + " validation decisions";
            }

            if (text.StartsWith("모델"))
            {
                Match shape = Regex.Match(text, @"(\d+)\s*·\s*d=(\d+)\s+L=(\d+)");
                Match parameters = Regex.Match(text, @"(\d{1,3}(?:,\d{3})+|\d{4,})");
                return !shape.Success ? null : "  Model: pairwise GNN scorer, " + shape.Groups[1].Value + " candidate features, d=" + shape.Groups[2].Value + ", L=" + shape.Groups[3].Value + (parameters.Success ? ", " + parameters.Value + " parameters" : "");
            }

            m = Regex.Match(text, @"^채택\D+(\d+)\D+([\d.]+)");
            if (m.Success)
                return "  Selected epoch " + m.Groups[1].Value + " (val pairwise acc " + m.Groups[2].Value + ")";
            m = Regex.Match(text, @"^학습\s+([\d.]+)s");
            if (m.Success)
                return "  Training finished in " + m.Groups[1].Value + " s.";
            return null;
        }

        private void Publish(string message)
        {
            Action<string> handler = OutputReceived;
            if (handler != null)
                handler(message);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static void RequireFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException(label + " was not found.", path);
        }

        private static string Sha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}

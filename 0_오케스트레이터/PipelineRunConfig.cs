using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public class PipelineRunConfig
    {
        public string WorkspaceRoot { get; set; }
        public string EmbeddedGnnArtifactDirectory { get; set; }

        public void Prepare(string executionMode)
        {
            if (string.IsNullOrWhiteSpace(WorkspaceRoot))
                throw new InvalidOperationException("WORKSPACE_ROOT must be set in AbstractMain.cs.");
            WorkspaceRoot = Environment.ExpandEnvironmentVariables(WorkspaceRoot);
            if (!Path.IsPathRooted(WorkspaceRoot))
                throw new InvalidOperationException("WORKSPACE_ROOT must be an absolute path.");
            WorkspaceRoot = Path.GetFullPath(WorkspaceRoot);
            if (!Directory.Exists(WorkspaceRoot))
                throw new DirectoryNotFoundException("WorkspaceRoot was not found: " + WorkspaceRoot);
            if (string.IsNullOrWhiteSpace(executionMode))
                executionMode = "ModelOnly";
            if (!string.Equals(executionMode, "ModelOnly", StringComparison.OrdinalIgnoreCase))
                PipelineExecutionModeParser.Parse(executionMode);
            ExecutionMode = executionMode;
            if (string.IsNullOrWhiteSpace(RunId))
                RunId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string[] pathProperties =
            {
                "InputPath",
                "SchedulePath",
                "ModelPath",
                "ModelOutputPath",
                "ReportDirectory",
                "InitialP4rPath",
                "AbnormalSnapshotP4rPath",
                "InitialSequenceModelPath",
                "ValidationSequenceModelPath",
                "AbnormalSequenceOutputPath",
                "FinalSnapshotOutputPath",
                "VnsExecutablePath",
                "VnsProjectPath",
                "SnapshotPath",
                "GnnCheckpointPath",
                "GnnVcRawPath",
                "GnnInstancePath",
                "GnnDirectory",
                "GnnServicePath",
                "PythonExecutablePath",
                "GnnTrainingDataDirectory",
                "GnnTrainingCheckpointPath",
                "GnnWeightArtifactDirectory",
                "GnnRuleTemplatePath",
                "TrainedRuleModelPath",
                "WorkspaceToolsDirectory",
                "EmbeddedGnnArtifactDirectory",
                "TrainedRuleOutputPath",
                "RecoverySnapshotP4rPath",
                "InitialReportPath"
            };
            foreach (string name in pathProperties)
            {
                var property = typeof(PipelineRunConfig).GetProperty(name);
                string value = (string)property.GetValue(this, null);
                if (string.IsNullOrWhiteSpace(value))
                    continue;
                value = value.Replace("{RunId}", RunId).Replace("{ExecutionMode}", ExecutionMode.ToLowerInvariant());
                property.SetValue(this, ResolvePath(WorkspaceRoot, value), null);
            }

            if (string.Equals(ExecutionMode, "Abnormal", StringComparison.OrdinalIgnoreCase) || string.Equals(ExecutionMode, "AbnormalPlan", StringComparison.OrdinalIgnoreCase))
                UseAbnormalSimulationSettings();
            else
                UseInitialSimulationSettings();
        }

        private static string ResolvePath(string root, string value)
        {
            string expanded = Environment.ExpandEnvironmentVariables(value);
            return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(root, expanded));
        }

        public string RunId { get; set; }
        public string InputPath { get; set; }
        public string SchedulePath { get; set; }
        public string ModelPath { get; set; }
        public string ModelOutputPath { get; set; }
        public string ReportDirectory { get; set; }
        public string Scenario { get; set; }
        public double SimulationHorizonSeconds { get; set; }
        public double SpeedFactor { get; set; }
        public double InitialSimulationHorizonSeconds { get; set; }
        public double InitialSimulationSpeedFactor { get; set; }
        public double AbnormalSimulationHorizonSeconds { get; set; }
        public double AbnormalSimulationSpeedFactor { get; set; }
        public string ExecutionMode { get; set; }
        public string InitialP4rPath { get; set; }
        public string AbnormalSnapshotP4rPath { get; set; }
        public string InitialSequenceModelPath { get; set; }
        public string ValidationSequenceModelPath { get; set; }
        public string AbnormalSequenceOutputPath { get; set; }
        public string FinalSnapshotOutputPath { get; set; }
        public string VnsExecutablePath { get; set; }
        public string VnsProjectPath { get; set; }
        public double VnsTimeoutSeconds { get; set; }
        public string PlannerMode { get; set; } = "hyb";
        public int PlannerSeed { get; set; } = 1;
        public int PlannerEvaluations { get; set; } = 5000;
        public bool TrainRuleOnInitial { get; set; } = true;
        public bool ReuseTrainedRuleOnInitial { get; set; } = true;
        public string SnapshotPath { get; set; }
        public string GnnCheckpointPath { get; set; }
        public string GnnVcRawPath { get; set; }
        public string GnnInstancePath { get; set; }
        public string GnnDirectory { get; set; }
        public string GnnServicePath { get; set; }
        public string PythonExecutablePath { get; set; }
        public double GnnScaleMakespan { get; set; }
        public double GnnTimeoutSeconds { get; set; }
        public string GnnTrainingDataDirectory { get; set; }
        public string GnnTrainingCheckpointPath { get; set; }
        public string GnnWeightArtifactDirectory { get; set; }
        public string GnnRuleTemplatePath { get; set; }
        public string TrainedRuleModelPath { get; set; }
        public string TrainedRuleOutputPath { get; set; }
        public string RecoverySnapshotP4rPath { get; set; }
        public string InitialReportPath { get; set; }
        public string WorkspaceToolsDirectory { get; set; }
        public int GnnTrainingEpochs { get; set; }
        public double GnnTrainingTimeoutSeconds { get; set; }

        public PipelineExecutionMode Mode
        {
            get
            {
                return PipelineExecutionModeParser.Parse(ExecutionMode);
            }
        }

        public void ConfigureSimulationSettings(double initialHorizonSeconds, double initialSpeedFactor, double abnormalHorizonSeconds, double abnormalSpeedFactor)
        {
            if (initialHorizonSeconds <= 0.0 || initialSpeedFactor <= 0.0 || abnormalHorizonSeconds <= 0.0 || abnormalSpeedFactor <= 0.0)
                throw new ArgumentOutOfRangeException("Simulation horizon and speed factor must be positive.");
            InitialSimulationHorizonSeconds = initialHorizonSeconds;
            InitialSimulationSpeedFactor = initialSpeedFactor;
            AbnormalSimulationHorizonSeconds = abnormalHorizonSeconds;
            AbnormalSimulationSpeedFactor = abnormalSpeedFactor;
            if (string.Equals(ExecutionMode, "Abnormal", StringComparison.OrdinalIgnoreCase) || string.Equals(ExecutionMode, "AbnormalPlan", StringComparison.OrdinalIgnoreCase))
                UseAbnormalSimulationSettings();
            else
                UseInitialSimulationSettings();
        }

        public void UseInitialSimulationSettings()
        {
            SimulationHorizonSeconds = InitialSimulationHorizonSeconds;
            SpeedFactor = InitialSimulationSpeedFactor;
        }

        public void UseAbnormalSimulationSettings()
        {
            SimulationHorizonSeconds = AbnormalSimulationHorizonSeconds;
            SpeedFactor = AbnormalSimulationSpeedFactor;
        }

        public double RescheduledSimulationHorizonSeconds { get; set; }
        public double RescheduledSimulationSpeedFactor { get; set; }

        public void UseRescheduledValidationSettings()
        {
            UseInitialSimulationSettings();
            if (RescheduledSimulationHorizonSeconds > 0.0)
                SimulationHorizonSeconds = RescheduledSimulationHorizonSeconds;
            if (RescheduledSimulationSpeedFactor > 0.0)
                SpeedFactor = RescheduledSimulationSpeedFactor;
        }

        public void ValidateForExecution()
        {
            if (string.IsNullOrWhiteSpace(ReportDirectory))
                throw new InvalidOperationException("ReportDirectory is required.");
            if (Mode == PipelineExecutionMode.Initial)
            {
                RequireFile(InitialP4rPath, "InitialP4rPath");
                RequireFile(InitialSequenceModelPath, "InitialSequenceModelPath");
            }
            else
            {
                RequireFile(AbnormalSnapshotP4rPath, "AbnormalSnapshotP4rPath");
                var snapshot = JObject.Parse(File.ReadAllText(AbnormalSnapshotP4rPath));
                var exportMetadata = snapshot["_DT_RULE_EXPORT"];
                if (exportMetadata != null && (bool? )exportMetadata["SnapshotReadyForVns"] != true)
                    throw new InvalidDataException("The abnormal snapshot is an incomplete runtime export.");
                RequireFile(TrainedRuleModelPath, "TrainedRuleModelPath");
                RequireFile(ValidationSequenceModelPath, "ValidationSequenceModelPath");
            }
        }

        private static void RequireFile(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException(name + " was not found.", path);
        }

        public void WriteStatus(string stage, int progress, string message)
        {
            PipelineConsole.ReportStage(stage);
            Directory.CreateDirectory(ReportDirectory);
            JObject status = new JObject
            {
                ["RunId"] = RunId,
                ["Stage"] = stage,
                ["Progress"] = progress,
                ["Message"] = message,
                ["UpdatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            File.WriteAllText(Path.Combine(ReportDirectory, "status.json"), status.ToString(Formatting.Indented));
        }

        public void WriteOrchestratorStatus(OrchestratorState previous, OrchestratorState current, string message)
        {
            Directory.CreateDirectory(ReportDirectory);
            JObject status = new JObject
            {
                ["RunId"] = RunId,
                ["PreviousState"] = previous.ToString(),
                ["State"] = current.ToString(),
                ["ExecutionMode"] = ExecutionMode ?? "InitialPlan",
                ["PlanVersion"] = PlanVersion,
                ["Message"] = message,
                ["UpdatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
            };
            File.WriteAllText(Path.Combine(ReportDirectory, "orchestrator-status.json"), status.ToString(Formatting.Indented));
        }

        public int PlanVersion { get; set; }
    }
}

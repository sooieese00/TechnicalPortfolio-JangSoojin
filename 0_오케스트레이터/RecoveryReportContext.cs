using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public static class RecoveryReportContext
    {
        public static JObject Build(PipelineRunConfig config)
        {
            try
            {
                return Build(ResolveInitialReport(config), config.AbnormalSnapshotP4rPath, config.RecoverySnapshotP4rPath, Synchronization.CurrentDateTimeUtc);
            }
            catch (Exception ex)
            {
                PipelineConsole.Log("Recovery Gantt context unavailable: " + ex.Message);
                return null;
            }
        }

        public static JObject Build(string initialReportPath, string abnormalInputPath, string recoverySnapshotPath, string planStartUtc)
        {
            JObject abnormal = Read(abnormalInputPath);
            JObject recovery = Read(recoverySnapshotPath);
            DateTimeOffset planStart = ParseDate(planStartUtc);
            DateTimeOffset failure = ParseDate((string)abnormal["CURRENT_DATETIME"]);
            DateTimeOffset resumed = ParseDate((string)recovery["CURRENT_DATETIME"]);
            JArray initialTimeline = new JArray();
            if (!string.IsNullOrWhiteSpace(initialReportPath) && File.Exists(initialReportPath))
            {
                JObject initial = Read(initialReportPath);
                initialTimeline = initial["CompletionTimeline"] as JArray ?? new JArray();
                SimulationReporting.NormalizeCompletionHistory(initialTimeline);
            }

            JArray window = recovery.SelectToken("_DT_RECONSTRUCTION.ObservedTimeline") as JArray ?? new JArray();
            window = (JArray)window.DeepClone();
            SimulationReporting.NormalizeCompletionHistory(window);
            JArray failedResources = new JArray((abnormal["MACHINE_INSTANCE"] as JArray ?? new JArray()).OfType<JObject>().Where(m => m["FAILURE_STATUS"] is JArray && ((JArray)m["FAILURE_STATUS"]).Count > 0).Select(m => (string)m["MACHINE_INSTANCE_ID"]));
            return new JObject
            {
                ["InitialReportPath"] = initialReportPath ?? "",
                ["AbnormalInputPath"] = abnormalInputPath,
                ["RecoverySnapshotPath"] = recoverySnapshotPath,
                ["FailureTimeSeconds"] = Math.Max(0, (failure - planStart).TotalSeconds),
                ["RecoveryWindowSeconds"] = Math.Max(0, (resumed - failure).TotalSeconds),
                ["FailedResources"] = failedResources,
                ["InitialTimeline"] = initialTimeline,
                ["RecoveryTimeline"] = window
            };
        }

        private static string ResolveInitialReport(PipelineRunConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.InitialReportPath))
                return config.InitialReportPath;
            string runs = Path.Combine(config.WorkspaceRoot, "artifacts", "runs");
            if (!Directory.Exists(runs))
                return null;
            return Directory.GetDirectories(runs, "initial-*").Select(d => Path.Combine(d, "SimulationReport.json")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        private static JObject Read(string path)
        {
            return JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
        }

        private static DateTimeOffset ParseDate(string value)
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }
    }
}

using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public sealed class AbnormalSimulationReporting
    {
        public JObject CreateReport(ISimWorld simWorld, PipelineRunConfig config)
        {
            ISimComponent controller = simWorld.FindComponent("Central Controller");
            string raw = "[]";
            if (controller != null)
            {
                var property = controller.GetProperty("DispatchInterface::OperationEventLogJSON");
                if (property != null && property.Value != null)
                    raw = Convert.ToString(property.Value);
            }

            JArray events;
            try
            {
                events = JArray.Parse(string.IsNullOrWhiteSpace(raw) ? "[]" : raw);
            }
            catch
            {
                events = new JArray();
            }

            var report = new JObject
            {
                ["RunId"] = config.RunId,
                ["Scenario"] = config.Scenario,
                ["ReportType"] = "AbnormalRuleSimulation",
                ["CreatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["EventCount"] = events.Count,
                ["OperationEvents"] = events,
                ["Notes"] = new JArray("Rule-specific equipment and recovery KPI hooks can be added here.", "The rescheduled Sequence validation uses SimulationReporting instead.")
            };
            Directory.CreateDirectory(config.ReportDirectory);
            File.WriteAllText(Path.Combine(config.ReportDirectory, "abnormal-rule-report.json"), report.ToString(Formatting.Indented));
            return report;
        }
    }
}

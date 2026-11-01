using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public sealed class AbnormalExportResult
    {
        public string SequencePath { get; set; }
        public string SnapshotPath { get; set; }
        public bool SnapshotReadyForVns { get; set; }
    }

    public sealed class AbnormalP4rExporter
    {
        public AbnormalExportResult Export(JObject sourceP4r, ISimWorld simWorld, PipelineRunConfig config)
        {
            JObject snapshot = sourceP4r == null ? new JObject() : (JObject)sourceP4r.DeepClone();
            JArray events = ReadEvents(simWorld);
            JObject metadata = new JObject
            {
                ["RunId"] = config.RunId,
                ["ExportedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ["OperationEvents"] = events,
                ["SnapshotReadyForVns"] = false,
                ["MissingRuntimeExporters"] = new JArray("CNC WIP/failure/remaining time", "buffer WIP", "AMR/Shuttle/Lifter position and load", "ASRS final slot inventory")
            };
            snapshot["_DT_RULE_EXPORT"] = metadata;
            JObject sequence = new JObject
            {
                ["RunId"] = config.RunId,
                ["SourceP4R"] = config.AbnormalSnapshotP4rPath,
                ["OperationEvents"] = events
            };
            Directory.CreateDirectory(config.ReportDirectory);
            File.WriteAllText(config.AbnormalSequenceOutputPath, sequence.ToString(Formatting.Indented));
            File.WriteAllText(config.FinalSnapshotOutputPath, snapshot.ToString(Formatting.Indented));
            return new AbnormalExportResult
            {
                SequencePath = config.AbnormalSequenceOutputPath,
                SnapshotPath = config.FinalSnapshotOutputPath,
                SnapshotReadyForVns = false
            };
        }

        private static JArray ReadEvents(ISimWorld simWorld)
        {
            ISimComponent controller = simWorld.FindComponent("Central Controller");
            var property = controller == null ? null : controller.GetProperty("DispatchInterface::OperationEventLogJSON");
            string raw = property == null ? "[]" : Convert.ToString(property.Value);
            try
            {
                return JArray.Parse(string.IsNullOrWhiteSpace(raw) ? "[]" : raw);
            }
            catch
            {
                return new JArray();
            }
        }
    }
}

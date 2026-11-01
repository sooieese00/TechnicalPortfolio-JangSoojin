using System;

namespace DT_KETI
{
    public enum PipelineExecutionMode
    {
        Initial,
        Abnormal
    }

    public static class PipelineExecutionModeParser
    {
        public static PipelineExecutionMode Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Initial", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "InitialPlan", StringComparison.OrdinalIgnoreCase))
                return PipelineExecutionMode.Initial;
            if (string.Equals(value, "Abnormal", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "AbnormalPlan", StringComparison.OrdinalIgnoreCase))
                return PipelineExecutionMode.Abnormal;
            throw new ArgumentException("ExecutionMode must be either 'Initial' or 'Abnormal'. Actual: " + value);
        }
    }
}

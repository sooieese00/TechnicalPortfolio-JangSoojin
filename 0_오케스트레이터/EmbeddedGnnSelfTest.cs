using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public sealed class EmbeddedGnnSelfTestResult
    {
        public int CandidateCount { get; set; }
        public int ExpectedIndex { get; set; }
        public int ActualIndex { get; set; }
        public double MaximumAbsoluteError { get; set; }
        public bool Passed { get; set; }
    }

    public static class EmbeddedGnnSelfTest
    {
        public static EmbeddedGnnSelfTestResult Run(string manifestPath, string fixturePath)
        {
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Embedded GNN manifest was not found.", manifestPath);
            if (!File.Exists(fixturePath))
                throw new FileNotFoundException("Embedded GNN fixture was not found.", fixturePath);
            JObject fixture = JObject.Parse(File.ReadAllText(fixturePath));
            JObject data = (JObject)fixture["input"];
            var input = new EmbeddedGnnInput
            {
                X = data["x"].ToObject<float[][]>(),
                EdgeSrc = data["edgeSrc"].ToObject<int[]>(),
                EdgeDst = data["edgeDst"].ToObject<int[]>(),
                EdgeType = data["edgeType"].ToObject<int[]>(),
                EdgeWeight = data["edgeWeight"].ToObject<float[]>(),
                CandidateNode = data["candidateNode"].ToObject<int[]>(),
                CandidateResourceClass = data["candidateResourceClass"].ToObject<int[]>(),
                CandidateResourceId = data["candidateResourceId"].ToObject<int[]>(),
                CandidateKind = data["candidateKind"].ToObject<int[]>(),
                CandidateExtra = data["candidateExtra"].ToObject<float[][]>()
            };
            float[] expected = fixture["expectedScores"].ToObject<float[]>();
            float[] actual = new EmbeddedDispatchGnn(EmbeddedGnnWeights.Load(manifestPath)).Forward(input);
            if (actual.Length != expected.Length)
                throw new InvalidDataException("Embedded GNN score count mismatch.");
            double maximumError = 0.0;
            for (int i = 0; i < actual.Length; i++)
                maximumError = Math.Max(maximumError, Math.Abs(actual[i] - expected[i]));
            int expectedIndex = (int)fixture["expectedIndex"];
            int actualIndex = ArgMax(actual);
            return new EmbeddedGnnSelfTestResult
            {
                CandidateCount = actual.Length,
                ExpectedIndex = expectedIndex,
                ActualIndex = actualIndex,
                MaximumAbsoluteError = maximumError,
                Passed = expectedIndex == actualIndex && maximumError <= 1.0e-4
            };
        }

        public static void RunAndLog(string manifestPath, string fixturePath)
        {
            PipelineConsole.Log("[EMBEDDED GNN TEST] Starting pure C# forward verification.");
            EmbeddedGnnSelfTestResult result = Run(manifestPath, fixturePath);
            PipelineConsole.Log("[EMBEDDED GNN TEST] candidates=" + result.CandidateCount);
            PipelineConsole.Log("[EMBEDDED GNN TEST] expectedIndex=" + result.ExpectedIndex + " actualIndex=" + result.ActualIndex);
            PipelineConsole.Log("[EMBEDDED GNN TEST] maxAbsError=" + result.MaximumAbsoluteError.ToString("G9", CultureInfo.InvariantCulture));
            PipelineConsole.Log(result.Passed ? "[EMBEDDED GNN TEST] PASS" : "[EMBEDDED GNN TEST] FAIL");
            if (!result.Passed)
                throw new InvalidOperationException("Embedded GNN forward self-test failed.");
        }

        public static void RunSessionAndLog(string manifestPath, string vcRawPath, string fixturePath, double scaleMakespan)
        {
            JObject fixture = JObject.Parse(File.ReadAllText(fixturePath));
            var candidates = new System.Collections.Generic.List<JObject>();
            foreach (JObject candidate in (JArray)fixture["candidates"])
                candidates.Add(candidate);
            float[] expected = fixture["expectedScores"].ToObject<float[]>();
            int expectedIndex = (int)fixture["expectedIndex"];
            EmbeddedGnnChoice actual = new EmbeddedGnnSession(manifestPath, vcRawPath, scaleMakespan).Choose(candidates);
            double maximumError = 0.0;
            for (int i = 0; i < expected.Length; i++)
                maximumError = Math.Max(maximumError, Math.Abs(expected[i] - actual.Scores[i]));
            bool passed = expectedIndex == actual.Index && maximumError <= 1.0e-4;
            PipelineConsole.Log("[EMBEDDED GNN SESSION TEST] expectedIndex=" + expectedIndex + " actualIndex=" + actual.Index + " maxAbsError=" + maximumError.ToString("G9", CultureInfo.InvariantCulture));
            PipelineConsole.Log(passed ? "[EMBEDDED GNN SESSION TEST] PASS" : "[EMBEDDED GNN SESSION TEST] FAIL");
            if (!passed)
                throw new InvalidOperationException("Embedded GNN state builder/session self-test failed.");
        }

        private static int ArgMax(float[] values)
        {
            int result = 0;
            for (int i = 1; i < values.Length; i++)
                if (values[i] > values[result])
                    result = i;
            return result;
        }
    }
}

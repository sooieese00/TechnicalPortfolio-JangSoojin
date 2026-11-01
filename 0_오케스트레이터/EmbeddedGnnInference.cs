using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public sealed class EmbeddedGnnTensor
    {
        public int[] Shape { get; set; }
        public float[] Values { get; set; }
    }

    public sealed class EmbeddedGnnWeights
    {
        public int Hidden { get; private set; }
        public int Layers { get; private set; }
        public int NodeFeatureDim { get; private set; }
        public int CandidateExtraDim { get; private set; }

        private readonly Dictionary<string, EmbeddedGnnTensor> tensors = new Dictionary<string, EmbeddedGnnTensor>();
        public static EmbeddedGnnWeights Load(string manifestPath)
        {
            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
            if ((string)manifest["format"] != "dt-keti-dispatch-gnn-weights/v1")
                throw new InvalidDataException("Unsupported embedded GNN weight format.");
            JObject model = (JObject)manifest["model"];
            var result = new EmbeddedGnnWeights
            {
                Hidden = (int)model["hidden"],
                Layers = (int)model["layers"],
                NodeFeatureDim = (int)model["nodeFeatureDim"],
                CandidateExtraDim = (int)model["candidateExtraDim"]
            };
            string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
            string binaryPath = Path.Combine(directory, (string)manifest["binaryFile"]);
            byte[] bytes = File.ReadAllBytes(binaryPath);
            foreach (JObject item in (JArray)manifest["tensors"])
            {
                int offset = (int)item["floatOffset"];
                int count = (int)item["floatCount"];
                var values = new float[count];
                for (int i = 0; i < count; i++)
                    values[i] = BitConverter.ToSingle(bytes, (offset + i) * 4);
                result.tensors.Add((string)item["name"], new EmbeddedGnnTensor { Shape = item["shape"].ToObject<int[]>(), Values = values });
            }

            return result;
        }

        public EmbeddedGnnTensor Get(string name)
        {
            EmbeddedGnnTensor tensor;
            if (!tensors.TryGetValue(name, out tensor))
                throw new InvalidDataException("GNN tensor was not found: " + name);
            return tensor;
        }
    }

    public sealed class EmbeddedGnnInput
    {
        public float[][] X { get; set; }
        public int[] EdgeSrc { get; set; }
        public int[] EdgeDst { get; set; }
        public int[] EdgeType { get; set; }
        public float[] EdgeWeight { get; set; }
        public int[] CandidateNode { get; set; }
        public int[] CandidateResourceClass { get; set; }
        public int[] CandidateResourceId { get; set; }
        public int[] CandidateKind { get; set; }
        public float[][] CandidateExtra { get; set; }
    }

    public sealed class EmbeddedDispatchGnn
    {
        private readonly EmbeddedGnnWeights weights;
        public EmbeddedDispatchGnn(EmbeddedGnnWeights weights)
        {
            if (weights == null)
                throw new ArgumentNullException("weights");
            this.weights = weights;
        }

        private float[] Linear(string prefix, float[] input)
        {
            EmbeddedGnnTensor weight = weights.Get(prefix + ".weight");
            EmbeddedGnnTensor bias = weights.Get(prefix + ".bias");
            int rows = weight.Shape[0];
            int columns = weight.Shape[1];
            if (input.Length != columns)
                throw new InvalidDataException(prefix + " input width mismatch.");
            var output = new float[rows];
            for (int row = 0; row < rows; row++)
            {
                float sum = bias.Values[row];
                int baseIndex = row * columns;
                for (int column = 0; column < columns; column++)
                    sum += weight.Values[baseIndex + column] * input[column];
                output[row] = sum;
            }

            return output;
        }

        private static void Relu(float[] values)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i] < 0.0f)
                    values[i] = 0.0f;
        }

        private float[] Mlp2(string prefix, float[] input)
        {
            float[] hidden = Linear(prefix + ".0", input);
            Relu(hidden);
            return Linear(prefix + ".2", hidden);
        }

        private static float Sigmoid(float value)
        {
            if (value >= 0.0f)
                return 1.0f / (1.0f + (float)Math.Exp(-value));
            float exponential = (float)Math.Exp(value);
            return exponential / (1.0f + exponential);
        }

        private float[] Gru(string prefix, float[] input, float[] previous)
        {
            EmbeddedGnnTensor weightInput = weights.Get(prefix + ".weight_ih");
            EmbeddedGnnTensor weightHidden = weights.Get(prefix + ".weight_hh");
            EmbeddedGnnTensor biasInput = weights.Get(prefix + ".bias_ih");
            EmbeddedGnnTensor biasHidden = weights.Get(prefix + ".bias_hh");
            int hidden = previous.Length;
            var gateInput = new float[3 * hidden];
            var gateHidden = new float[3 * hidden];
            for (int row = 0; row < 3 * hidden; row++)
            {
                float inputSum = biasInput.Values[row];
                float hiddenSum = biasHidden.Values[row];
                int inputOffset = row * input.Length;
                int hiddenOffset = row * hidden;
                for (int column = 0; column < input.Length; column++)
                    inputSum += weightInput.Values[inputOffset + column] * input[column];
                for (int column = 0; column < hidden; column++)
                    hiddenSum += weightHidden.Values[hiddenOffset + column] * previous[column];
                gateInput[row] = inputSum;
                gateHidden[row] = hiddenSum;
            }

            var output = new float[hidden];
            for (int i = 0; i < hidden; i++)
            {
                float reset = Sigmoid(gateInput[i] + gateHidden[i]);
                float update = Sigmoid(gateInput[hidden + i] + gateHidden[hidden + i]);
                float candidate = (float)Math.Tanh(gateInput[2 * hidden + i] + reset * gateHidden[2 * hidden + i]);
                output[i] = (1.0f - update) * candidate + update * previous[i];
            }

            return output;
        }

        private static float[] Concat(params float[][] parts)
        {
            int length = 0;
            for (int i = 0; i < parts.Length; i++)
                length += parts[i].Length;
            var result = new float[length];
            int offset = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                Array.Copy(parts[i], 0, result, offset, parts[i].Length);
                offset += parts[i].Length;
            }

            return result;
        }

        private float[] Embedding(string name, int index)
        {
            EmbeddedGnnTensor table = weights.Get(name + ".weight");
            int rows = table.Shape[0];
            int width = table.Shape[1];
            if (index >= rows)
                index = rows - 1;
            if (index < 0)
                throw new InvalidDataException(name + " received a negative index.");
            var result = new float[width];
            Array.Copy(table.Values, index * width, result, 0, width);
            return result;
        }

        public float[] Forward(EmbeddedGnnInput input)
        {
            Validate(input);
            int nodeCount = input.X.Length;
            int hidden = weights.Hidden;
            var nodeState = new float[nodeCount][];
            for (int node = 0; node < nodeCount; node++)
                nodeState[node] = Mlp2("embed", input.X[node]);
            for (int layer = 0; layer < weights.Layers; layer++)
            {
                var aggregate = new float[nodeCount][];
                var degree = new int[nodeCount];
                for (int node = 0; node < nodeCount; node++)
                    aggregate[node] = new float[hidden];
                for (int edge = 0; edge < input.EdgeSrc.Length; edge++)
                {
                    int source = input.EdgeSrc[edge];
                    int destination = input.EdgeDst[edge];
                    float[] messageInput = Concat(nodeState[source], nodeState[destination], Embedding("layers." + layer + ".edge_emb", input.EdgeType[edge]), new[] { input.EdgeWeight[edge] });
                    float[] message = Mlp2("layers." + layer + ".msg", messageInput);
                    for (int j = 0; j < hidden; j++)
                        aggregate[destination][j] += message[j];
                    degree[destination]++;
                }

                var updated = new float[nodeCount][];
                for (int node = 0; node < nodeCount; node++)
                {
                    int divisor = Math.Max(degree[node], 1);
                    for (int j = 0; j < hidden; j++)
                        aggregate[node][j] /= divisor;
                    updated[node] = Gru("layers." + layer + ".upd", aggregate[node], nodeState[node]);
                }

                nodeState = updated;
            }

            var graphContext = new float[hidden];
            for (int node = 0; node < nodeCount; node++)
                for (int j = 0; j < hidden; j++)
                    graphContext[j] += nodeState[node][j];
            for (int j = 0; j < hidden; j++)
                graphContext[j] /= Math.Max(nodeCount, 1);
            int candidateCount = input.CandidateNode.Length;
            var candidate = new float[candidateCount][];
            for (int i = 0; i < candidateCount; i++)
            {
                var extra = new float[weights.CandidateExtraDim];
                Array.Copy(input.CandidateExtra[i], extra, extra.Length);
                candidate[i] = Mlp2("cand_proj", Concat(nodeState[input.CandidateNode[i]], Embedding("res_class_emb", input.CandidateResourceClass[i]), Embedding("res_id_emb", input.CandidateResourceId[i]), Embedding("kind_emb", input.CandidateKind[i]), extra));
            }

            var mean = new float[hidden];
            var maximum = new float[hidden];
            for (int j = 0; j < hidden; j++)
                maximum[j] = -1.0e9f;
            for (int i = 0; i < candidateCount; i++)
                for (int j = 0; j < hidden; j++)
                {
                    mean[j] += candidate[i][j];
                    if (candidate[i][j] > maximum[j])
                        maximum[j] = candidate[i][j];
                }

            for (int j = 0; j < hidden; j++)
                mean[j] /= Math.Max(candidateCount, 1);
            float[] setContext = Mlp2("set_ctx", Concat(mean, maximum));
            var scores = new float[candidateCount];
            for (int i = 0; i < candidateCount; i++)
            {
                float[] interacted = Mlp2("cand_local", Concat(candidate[i], setContext));
                float[] scoreHidden = Linear("scorer.0", Concat(candidate[i], interacted, setContext, graphContext));
                Relu(scoreHidden);
                scoreHidden = Linear("scorer.2", scoreHidden);
                Relu(scoreHidden);
                scores[i] = Linear("scorer.4", scoreHidden)[0];
            }

            return scores;
        }

        private void Validate(EmbeddedGnnInput input)
        {
            if (input == null || input.X == null || input.X.Length == 0)
                throw new ArgumentException("Embedded GNN input has no nodes.", "input");
            for (int i = 0; i < input.X.Length; i++)
                if (input.X[i].Length != weights.NodeFeatureDim)
                    throw new InvalidDataException("GNN node feature width mismatch.");
            int edgeCount = input.EdgeSrc.Length;
            if (input.EdgeDst.Length != edgeCount || input.EdgeType.Length != edgeCount || input.EdgeWeight.Length != edgeCount)
                throw new InvalidDataException("GNN edge arrays have different lengths.");
            int candidateCount = input.CandidateNode.Length;
            if (candidateCount == 0 || input.CandidateResourceClass.Length != candidateCount || input.CandidateResourceId.Length != candidateCount || input.CandidateKind.Length != candidateCount || input.CandidateExtra.Length != candidateCount)
                throw new InvalidDataException("GNN candidate arrays have different lengths.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public sealed class EmbeddedGnnChoice
    {
        public int Index { get; set; }
        public float[] Scores { get; set; }
    }

    public sealed class EmbeddedGnnSession
    {
        private static readonly string[] Kinds =
        {
            "ret",
            "sto",
            "pplus",
            "dplus",
            "pminus",
            "dminus",
            "proc"
        };
        private static readonly string[] Chain =
        {
            "ret",
            "pplus",
            "dplus",
            "proc",
            "pminus",
            "dminus",
            "sto"
        };
        private static readonly string[] EdgeTypes =
        {
            "crane_seq",
            "amr_route",
            "machine_seq",
            "buf_asrs_in",
            "buf_asrs_out",
            "buf_ws_in",
            "buf_ws_out",
            "buf_ws_block",
            "conj",
            "conj_open"
        };
        private sealed class Operation
        {
            public string Kind;
            public int Job;
            public int NodeId;
            public double Due;
            public double Duration;
            public readonly List<string> Resources = new List<string>();
        }

        private sealed class Edge
        {
            public Operation U;
            public Operation V;
            public string Type;
            public double Weight;
        }

        private sealed class Candidate
        {
            public string Kind;
            public int Job;
            public string Resource;
            public Operation Operation;
        }

        private readonly EmbeddedDispatchGnn model;
        private readonly List<Operation> operations = new List<Operation>();
        private readonly Dictionary<string, Operation> operationByKey = new Dictionary<string, Operation>();
        private readonly HashSet<string> committed = new HashSet<string>();
        private readonly Dictionary<string, List<Operation>> sequences = new Dictionary<string, List<Operation>>();
        private readonly List<string> pinnedCrane = new List<string>();
        private readonly int machineCount;
        private readonly int amrCount;
        private readonly int jobCount;
        private readonly int[] inputCapacity;
        private readonly int[] outputCapacity;
        private readonly int asrsOutCapacity;
        private readonly int asrsInCapacity;
        private readonly double scale;
        private int pinIndex;
        public EmbeddedGnnSession(string manifestPath, string vcRawPath, double scaleMakespan)
        {
            model = new EmbeddedDispatchGnn(EmbeddedGnnWeights.Load(manifestPath));
            JObject raw = JObject.Parse(File.ReadAllText(vcRawPath));
            jobCount = (int)raw["m"];
            machineCount = (int)raw["n"];
            amrCount = (int)raw["r"];
            inputCapacity = (raw["b_in"] ?? new JArray()).ToObject<int[]>();
            outputCapacity = (raw["b_out"] ?? new JArray()).ToObject<int[]>();
            asrsOutCapacity = (int? )raw["asrs_out_cap"] ?? -1;
            asrsInCapacity = (int? )raw["asrs_in_cap"] ?? -1;
            scale = Math.Max(scaleMakespan > 0.0 ? scaleMakespan : ((double? )raw["scale_makespan"] ?? 1.0), 1.0);
            foreach (JArray pin in raw["pinned_crane"] ?? new JArray())
                pinnedCrane.Add(Key((string)pin[0], (int)pin[1]));
            int nextNode = 1;
            foreach (JObject job in (JArray)raw["jobs"])
            {
                int jobId = (int)job["job"];
                double due = (double)job["due"];
                foreach (string kind in job["chain"].ToObject<string[]>())
                {
                    var operation = new Operation
                    {
                        Kind = kind,
                        Job = jobId,
                        NodeId = nextNode++,
                        Due = due
                    };
                    ConfigureOperation(operation, job);
                    operations.Add(operation);
                    operationByKey.Add(Key(kind, jobId), operation);
                }
            }
        }

        private void ConfigureOperation(Operation operation, JObject job)
        {
            string kind = operation.Kind;
            if (kind == "ret" || kind == "sto")
            {
                operation.Resources.Add("crane");
                operation.Duration = Value(job["crane_sec"], kind);
                return;
            }

            if (kind == "proc")
            {
                double best = double.PositiveInfinity;
                foreach (int station in job["eligible_ws"].ToObject<int[]>())
                {
                    operation.Resources.Add("ws" + station);
                    double duration = Value(job["setup_in_sec"], station.ToString()) + Value(job["proc_sec"], station.ToString()) + Value(job["setup_out_sec"], station.ToString());
                    best = Math.Min(best, duration);
                }

                operation.Duration = double.IsInfinity(best) ? 0.0 : best;
                return;
            }

            JToken allowedToken = job["amr"] == null ? null : job["amr"][kind];
            IEnumerable<int> allowed = allowedToken == null ? Enumerable.Range(0, amrCount) : allowedToken.ToObject<int[]>();
            foreach (int amr in allowed.OrderBy(x => x))
                operation.Resources.Add("amr" + amr);
            operation.Duration = Value(job["svc_sec"], kind) + Value(job["travel_sec"], kind);
        }

        private static double Value(JToken owner, string name)
        {
            return owner == null || owner[name] == null ? 0.0 : (double)owner[name];
        }

        private static string Key(string kind, int job)
        {
            return kind + ":" + job;
        }

        public void Commit(string kind, int job, string resource)
        {
            string key = Key(kind, job);
            Operation operation;
            if (!operationByKey.TryGetValue(key, out operation))
                return;
            if (!committed.Add(key))
                return;
            List<Operation> sequence;
            if (!sequences.TryGetValue(resource, out sequence))
            {
                sequence = new List<Operation>();
                sequences.Add(resource, sequence);
            }

            sequence.Add(operation);
            if ((kind == "ret" || kind == "sto") && pinIndex < pinnedCrane.Count && pinnedCrane[pinIndex] == key)
                pinIndex++;
        }

        public EmbeddedGnnChoice Choose(IList<JObject> rawCandidates)
        {
            var candidates = new List<Candidate>();
            foreach (JObject raw in rawCandidates)
            {
                string kind = (string)raw["kind"];
                int job = (int? )raw["job"] ?? -1;
                string resource = (string)raw["res"];
                Operation operation;
                if (job >= 0 && !string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(resource) && operationByKey.TryGetValue(Key(kind, job), out operation))
                {
                    candidates.Add(new Candidate { Kind = kind, Job = job, Resource = resource, Operation = operation });
                }
            }

            if (candidates.Count != rawCandidates.Count)
                throw new InvalidDataException("One or more VC candidates cannot be mapped to the GNN graph.");
            if (candidates.Count == 0)
                throw new ArgumentException("No GNN candidates.");
            if (candidates.Count == 1)
                return new EmbeddedGnnChoice
                {
                    Index = 0,
                    Scores = new[]
                    {
                        0.0f
                    }
                };
            float[] scores = model.Forward(BuildInput(candidates));
            int selected = 0;
            for (int i = 1; i < scores.Length; i++)
                if (scores[i] > scores[selected])
                    selected = i;
            return new EmbeddedGnnChoice
            {
                Index = selected,
                Scores = scores
            };
        }

        private EmbeddedGnnInput BuildInput(List<Candidate> candidates)
        {
            List<Edge> edges = BuildEdges();
            Dictionary<Operation, double> times = BuildTimes(edges);
            var undecided = new List<Tuple<Operation, Operation>>();
            foreach (IGrouping<string, Candidate> group in candidates.GroupBy(candidateItem => candidateItem.Resource))
            {
                Operation[] members = group.Select(candidateItem => candidateItem.Operation).Distinct().OrderBy(operationItem => operationItem.NodeId).ToArray();
                for (int a = 0; a < members.Length; a++)
                    for (int b = a + 1; b < members.Length; b++)
                        undecided.Add(Tuple.Create(members[a], members[b]));
            }

            var index = new Dictionary<Operation, int>();
            for (int i = 0; i < operations.Count; i++)
                index[operations[i]] = i;
            var indegree = new double[operations.Count];
            var outdegree = new double[operations.Count];
            var undegree = new double[operations.Count];
            foreach (Edge edge in edges)
            {
                outdegree[index[edge.U]]++;
                indegree[index[edge.V]]++;
            }

            foreach (Tuple<Operation, Operation> pair in undecided)
            {
                undegree[index[pair.Item1]]++;
                undegree[index[pair.Item2]]++;
            }

            var remaining = new Dictionary<int, double>();
            var flexibility = new Dictionary<int, int>();
            foreach (Operation operation in operations)
            {
                if (!committed.Contains(Key(operation.Kind, operation.Job)))
                    remaining[operation.Job] = Get(remaining, operation.Job) + operation.Duration;
                if (operation.Kind == "proc")
                    flexibility[operation.Job] = committed.Contains(Key(operation.Kind, operation.Job)) ? 0 : operation.Resources.Count;
            }

            var x = new float[operations.Count][];
            for (int i = 0; i < operations.Count; i++)
            {
                Operation op = operations[i];
                bool done = committed.Contains(Key(op.Kind, op.Job));
                double start = done ? Get(times, op) : 0.0;
                double finish = done ? start + op.Duration : 0.0;
                var row = new List<float>();
                for (int k = 0; k < Kinds.Length; k++)
                    row.Add(Kinds[k] == op.Kind ? 1.0f : 0.0f);
                row.Add(done ? 1.0f : 0.0f);
                row.Add(done ? 1.0f : 0.0f);
                row.Add((float)(start / scale));
                row.Add((float)(finish / scale));
                row.Add((float)(op.Due / scale));
                row.Add(done ? (float)((op.Due / scale) - (finish / scale)) : 0.0f);
                row.Add((float)(op.Duration / scale));
                row.Add((float)(Math.Log(1.0 + indegree[i]) / 2.0));
                row.Add((float)(Math.Log(1.0 + outdegree[i]) / 2.0));
                row.Add((float)(Math.Log(1.0 + undegree[i]) / 2.0));
                row.Add((float)(Get(remaining, op.Job) / scale));
                row.Add((float)(Math.Min(Get(flexibility, op.Job), 4) / 4.0));
                x[i] = row.ToArray();
            }

            var src = new List<int>();
            var dst = new List<int>();
            var types = new List<int>();
            var weights = new List<float>();
            foreach (Edge edge in edges)
            {
                int type = Array.IndexOf(EdgeTypes, edge.Type);
                AddEdge(src, dst, types, weights, index[edge.U], index[edge.V], type, (float)(edge.Weight / scale));
                AddEdge(src, dst, types, weights, index[edge.V], index[edge.U], type + EdgeTypes.Length, (float)(edge.Weight / scale));
            }

            foreach (Tuple<Operation, Operation> pair in undecided)
            {
                AddEdge(src, dst, types, weights, index[pair.Item1], index[pair.Item2], 20, 0.0f);
                AddEdge(src, dst, types, weights, index[pair.Item2], index[pair.Item1], 20, 0.0f);
            }

            var result = new EmbeddedGnnInput
            {
                X = x,
                EdgeSrc = src.ToArray(),
                EdgeDst = dst.ToArray(),
                EdgeType = types.ToArray(),
                EdgeWeight = weights.ToArray(),
                CandidateNode = new int[candidates.Count],
                CandidateResourceClass = new int[candidates.Count],
                CandidateResourceId = new int[candidates.Count],
                CandidateKind = new int[candidates.Count],
                CandidateExtra = new float[candidates.Count][]
            };
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                result.CandidateNode[i] = index[candidate.Operation];
                result.CandidateKind[i] = Array.IndexOf(Kinds, candidate.Kind);
                ParseResource(candidate.Resource, out result.CandidateResourceClass[i], out result.CandidateResourceId[i]);
                List<Operation> sequence;
                sequences.TryGetValue(candidate.Resource, out sequence);
                double free = sequence == null || sequence.Count == 0 ? 0.0 : Get(times, sequence[sequence.Count - 1]) + sequence[sequence.Count - 1].Duration;
                int load = sequence == null ? 0 : sequence.Count;
                result.CandidateExtra[i] = new[]
                {
                    (float)(free / scale),
                    (float)(Math.Log(1.0 + load) / 2.0)
                };
            }

            return result;
        }

        private List<Edge> BuildEdges()
        {
            var result = new List<Edge>();
            List<Operation> crane = Sequence("crane");
            var retrievals = new List<Operation>();
            var storages = new List<Operation>();
            for (int i = 0; i < crane.Count; i++)
            {
                Operation current = crane[i];
                if (i > 0)
                    Add(result, crane[i - 1], current, crane[i - 1].Duration, "crane_seq");
                if (current.Kind == "ret")
                {
                    if (asrsOutCapacity > 0 && retrievals.Count >= asrsOutCapacity)
                    {
                        Operation gate = retrievals[retrievals.Count - asrsOutCapacity];
                        Operation plus;
                        if (operationByKey.TryGetValue(Key("pplus", gate.Job), out plus) && IsCommitted(plus))
                            Add(result, plus, current, 0.0, "buf_asrs_out");
                    }

                    retrievals.Add(current);
                }
                else
                {
                    if (asrsInCapacity > 0 && storages.Count >= asrsInCapacity)
                    {
                        Operation gate = storages[storages.Count - asrsInCapacity];
                        Operation minus;
                        if (operationByKey.TryGetValue(Key("dminus", current.Job), out minus) && IsCommitted(minus))
                            Add(result, gate, minus, 0.0, "buf_asrs_in");
                    }

                    storages.Add(current);
                }
            }

            for (int amr = 0; amr < amrCount; amr++)
                AddSequence(result, Sequence("amr" + amr), "amr_route");
            for (int station = 0; station < machineCount; station++)
            {
                List<Operation> sequence = Sequence("ws" + station);
                int input = station < inputCapacity.Length ? inputCapacity[station] : 1;
                int output = station < outputCapacity.Length ? outputCapacity[station] : 1;
                for (int q = 0; q < sequence.Count; q++)
                {
                    Operation current = sequence[q];
                    if (q > 0)
                        Add(result, sequence[q - 1], current, sequence[q - 1].Duration, "machine_seq");
                    Operation op;
                    if (q >= input && operationByKey.TryGetValue(Key("dplus", current.Job), out op) && IsCommitted(op))
                        Add(result, sequence[q - input], op, 0.0, "buf_ws_in");
                    if (q >= output && operationByKey.TryGetValue(Key("pminus", current.Job), out op) && IsCommitted(op))
                    {
                        Operation previousMinus;
                        if (operationByKey.TryGetValue(Key("pminus", sequence[q - output].Job), out previousMinus) && IsCommitted(previousMinus))
                            Add(result, previousMinus, op, 0.0, "buf_ws_out");
                    }

                    if (q >= output + 1 && operationByKey.TryGetValue(Key("pminus", sequence[q - 1 - output].Job), out op) && IsCommitted(op))
                        Add(result, op, current, current.Duration, "buf_ws_block");
                }
            }

            for (int job = 0; job < jobCount; job++)
            {
                var chain = new List<Operation>();
                foreach (string kind in Chain)
                {
                    Operation operation;
                    if (operationByKey.TryGetValue(Key(kind, job), out operation))
                        chain.Add(operation);
                }

                for (int i = 0; i + 1 < chain.Count; i++)
                    Add(result, chain[i], chain[i + 1], 0.0, IsCommitted(chain[i]) && IsCommitted(chain[i + 1]) ? "conj" : "conj_open");
            }

            return result;
        }

        private Dictionary<Operation, double> BuildTimes(List<Edge> edges)
        {
            var times = new Dictionary<Operation, double>();
            var indegree = new Dictionary<Operation, int>();
            var outgoing = new Dictionary<Operation, List<Tuple<Operation, double>>>();
            foreach (Operation operation in operations)
                if (IsCommitted(operation))
                {
                    times[operation] = 0.0;
                    indegree[operation] = 0;
                    outgoing[operation] = new List<Tuple<Operation, double>>();
                }

            foreach (Edge edge in edges)
            {
                if (!IsCommitted(edge.U) || !IsCommitted(edge.V))
                    continue;
                double length = edge.Type == "crane_seq" || edge.Type == "amr_route" || edge.Type == "machine_seq" || edge.Type == "buf_ws_block" ? edge.Weight : edge.Type == "conj" || edge.Type == "buf_asrs_in" || edge.Type == "buf_asrs_out" ? edge.U.Duration : 0.0;
                outgoing[edge.U].Add(Tuple.Create(edge.V, length));
                indegree[edge.V]++;
            }

            var queue = new Stack<Operation>(indegree.Where(x => x.Value == 0).Select(x => x.Key));
            while (queue.Count > 0)
            {
                Operation source = queue.Pop();
                foreach (Tuple<Operation, double> next in outgoing[source])
                {
                    times[next.Item1] = Math.Max(times[next.Item1], times[source] + next.Item2);
                    indegree[next.Item1]--;
                    if (indegree[next.Item1] == 0)
                        queue.Push(next.Item1);
                }
            }

            return times;
        }

        private bool IsCommitted(Operation operation)
        {
            return committed.Contains(Key(operation.Kind, operation.Job));
        }

        private List<Operation> Sequence(string resource)
        {
            List<Operation> value;
            return sequences.TryGetValue(resource, out value) ? value : new List<Operation>();
        }

        private static void AddSequence(List<Edge> edges, List<Operation> sequence, string type)
        {
            for (int i = 1; i < sequence.Count; i++)
                Add(edges, sequence[i - 1], sequence[i], sequence[i - 1].Duration, type);
        }

        private static void Add(List<Edge> edges, Operation u, Operation v, double weight, string type)
        {
            edges.Add(new Edge { U = u, V = v, Weight = weight, Type = type });
        }

        private static void AddEdge(List<int> src, List<int> dst, List<int> type, List<float> weight, int u, int v, int t, float w)
        {
            src.Add(u);
            dst.Add(v);
            type.Add(t);
            weight.Add(w);
        }

        private static double Get(Dictionary<int, double> map, int key)
        {
            double value;
            return map.TryGetValue(key, out value) ? value : 0.0;
        }

        private static int Get(Dictionary<int, int> map, int key)
        {
            int value;
            return map.TryGetValue(key, out value) ? value : 0;
        }

        private static double Get(Dictionary<Operation, double> map, Operation key)
        {
            double value;
            return map.TryGetValue(key, out value) ? value : 0.0;
        }

        private static void ParseResource(string resource, out int resourceClass, out int resourceId)
        {
            if (resource == "crane")
            {
                resourceClass = 0;
                resourceId = 0;
            }
            else if (resource.StartsWith("amr"))
            {
                resourceClass = 1;
                resourceId = int.Parse(resource.Substring(3));
            }
            else
            {
                resourceClass = 2;
                resourceId = int.Parse(resource.Substring(2));
            }
        }
    }
}

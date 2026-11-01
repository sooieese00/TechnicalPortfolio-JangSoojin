using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace DT_KETI
{
    public sealed class GnnDecision
    {
        public int Index { get; set; }
        public double[] Scores { get; set; }
    }

    public sealed class GnnProcessAdapter : IDisposable
    {
        private const string ProtocolPrefix = "@@";
        private readonly object _gate = new object ();
        private readonly PipelineRunConfig _config;
        private Process _process;
        public GnnProcessAdapter(PipelineRunConfig config)
        {
            _config = config ?? throw new ArgumentNullException("config");
        }

        public bool IsRunning
        {
            get
            {
                return _process != null && !_process.HasExited;
            }
        }

        public event Action<string> ErrorOutput;
        public void Start()
        {
            lock (_gate)
            {
                if (IsRunning)
                    return;
                ValidateConfiguration();
                var startInfo = new ProcessStartInfo
                {
                    FileName = _config.PythonExecutablePath,
                    Arguments = BuildArguments(),
                    WorkingDirectory = Path.GetDirectoryName(_config.GnnServicePath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                _process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                _process.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs args)
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        Action<string> handler = ErrorOutput;
                        if (handler != null)
                            handler(args.Data);
                    }
                };
                _process.Start();
                _process.BeginErrorReadLine();
                JObject ready = ReadResponse();
                EnsureSuccess(ready);
                if (!string.Equals((string)ready["type"], "ready", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("GNN service did not return a ready response.");
            }
        }

        public bool Health()
        {
            JObject response = Send(new JObject { ["type"] = "health" });
            return (bool? )response["ok"] == true && (string)response["status"] == "ready";
        }

        public GnnDecision Choose(IEnumerable<JObject> candidates)
        {
            var candidateArray = new JArray(candidates);
            if (candidateArray.Count == 0)
                throw new ArgumentException("At least one GNN candidate is required.", "candidates");
            JObject response = Send(new JObject { ["type"] = "choose", ["candidates"] = candidateArray });
            int index = (int)response["index"];
            double[] scores = response["scores"].ToObject<double[]>();
            if (index < 0 || index >= candidateArray.Count || scores.Length != candidateArray.Count || Array.Exists(scores, value => double.IsNaN(value) || double.IsInfinity(value)))
                throw new InvalidDataException("GNN returned invalid candidate index or scores.");
            return new GnnDecision
            {
                Index = index,
                Scores = scores
            };
        }

        public void Commit(string kind, int job, string resource)
        {
            Send(new JObject { ["type"] = "commit", ["kind"] = kind, ["job"] = job, ["res"] = resource });
        }

        public void Reset()
        {
            Send(new JObject { ["type"] = "reset" });
        }

        private JObject Send(JObject request)
        {
            lock (_gate)
            {
                if (!IsRunning)
                    throw new InvalidOperationException("GNN service is not running.");
                _process.StandardInput.WriteLine(request.ToString(Newtonsoft.Json.Formatting.None));
                _process.StandardInput.Flush();
                JObject response = ReadResponse();
                EnsureSuccess(response);
                return response;
            }
        }

        private JObject ReadResponse()
        {
            double configuredTimeout = _config.GnnTimeoutSeconds > 0 ? _config.GnnTimeoutSeconds : 2.0;
            int timeoutMilliseconds = (int)Math.Min(configuredTimeout * 1000.0, int.MaxValue);
            Task<string> readTask = Task.Factory.StartNew(delegate
            {
                while (true)
                {
                    string line = _process.StandardOutput.ReadLine();
                    if (line == null)
                        throw new EndOfStreamException("GNN service closed its output stream.");
                    if (line.StartsWith(ProtocolPrefix, StringComparison.Ordinal))
                        return line.Substring(ProtocolPrefix.Length);
                }
            });
            if (!readTask.Wait(timeoutMilliseconds))
            {
                if (IsRunning)
                    _process.Kill();
                throw new TimeoutException("GNN response exceeded " + configuredTimeout + " seconds.");
            }

            return JObject.Parse(readTask.Result);
        }

        private static void EnsureSuccess(JObject response)
        {
            if ((bool? )response["ok"] != true)
                throw new InvalidOperationException("GNN service error: " + ((string)response["message"] ?? "unknown error"));
        }

        private string BuildArguments()
        {
            return Quote(_config.GnnServicePath) + " --gnn-dir " + Quote(_config.GnnDirectory) + " --checkpoint " + Quote(_config.GnnCheckpointPath) + " --instance " + Quote(_config.GnnInstancePath);
        }

        private void ValidateConfiguration()
        {
            RequireFile(_config.PythonExecutablePath, "Python executable");
            RequireFile(_config.GnnServicePath, "GNN service");
            RequireDirectory(_config.GnnDirectory, "GNN directory");
            RequireFile(_config.GnnCheckpointPath, "GNN checkpoint");
            RequireFile(_config.GnnInstancePath, "GNN instance");
        }

        private static void RequireFile(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException(label + " was not found.", path);
        }

        private static void RequireDirectory(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                throw new DirectoryNotFoundException(label + " was not found: " + path);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_process == null)
                    return;
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.StandardInput.WriteLine("{\"type\":\"close\"}");
                        _process.StandardInput.Flush();
                        _process.WaitForExit(1000);
                    }

                    if (!_process.HasExited)
                        _process.Kill();
                }
                catch
                {
                }
                finally
                {
                    _process.Dispose();
                    _process = null;
                }
            }
        }
    }
}

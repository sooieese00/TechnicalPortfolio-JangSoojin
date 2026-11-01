using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Caliburn.Micro;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public sealed class EmbeddedGnnDispatchBridge : IDisposable
    {
        private readonly object gate = new object ();
        private readonly Queue<JObject> commits = new Queue<JObject>();
        private EmbeddedGnnSession session;
        private ISimComponent controller;
        private Timer timer;
        private Thread worker;
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        private volatile bool stopping;
        private int pollPending;
        private int lastEventId;
        private int lastRequestId;
        private JObject pendingRequest;
        public void Start(ISimWorld simWorld, string manifestPath, string vcRawPath, double scaleMakespan)
        {
            controller = simWorld.FindComponent("Central Controller");
            if (controller == null)
                throw new InvalidOperationException("Central Controller was not found.");
            Require("DispatchInterface::GnnRequestId");
            Require("DispatchInterface::GnnRequestJSON");
            Require("DispatchInterface::GnnResponseId");
            Require("DispatchInterface::GnnResponseJSON");
            session = new EmbeddedGnnSession(manifestPath, vcRawPath, scaleMakespan);
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "DT_KETI Embedded GNN Dispatch"
            };
            worker.Start();
            timer = new Timer(Poll, null, 0, 50);
            PipelineConsole.Log("[EMBEDDED GNN DISPATCH] Started. Rule VC candidate requests now control execution.");
        }

        private void Require(string name)
        {
            if (controller.GetProperty(name) == null)
                throw new InvalidOperationException("Central Controller property was not found: " + name);
        }

        private void Poll(object state)
        {
            if (stopping || Interlocked.Exchange(ref pollPending, 1) != 0)
                return;
            try
            {
                Execute.OnUIThread(PollOnUiThread);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref pollPending, 0);
                PipelineConsole.Log("[EMBEDDED GNN DISPATCH] poll FAIL - " + ex.Message);
            }
        }

        private void PollOnUiThread()
        {
            try
            {
                string logText = Convert.ToString(controller.GetProperty("DispatchInterface::OperationEventLogJSON").Value);
                JArray events = string.IsNullOrWhiteSpace(logText) ? new JArray() : JArray.Parse(logText);
                lock (gate)
                {
                    foreach (JObject item in events.OfType<JObject>().Where(x => ((int? )x["eventId"] ?? -1) > lastEventId).OrderBy(x => (int? )x["eventId"] ?? -1))
                    {
                        lastEventId = (int)item["eventId"];
                        if (string.Equals((string)item["phase"], "COMPLETED", StringComparison.OrdinalIgnoreCase))
                            commits.Enqueue(item);
                    }

                    int requestId = Convert.ToInt32(controller.GetProperty("DispatchInterface::GnnRequestId").Value);
                    if (requestId > lastRequestId && pendingRequest == null)
                    {
                        pendingRequest = JObject.Parse(Convert.ToString(controller.GetProperty("DispatchInterface::GnnRequestJSON").Value));
                        lastRequestId = requestId;
                    }
                }

                signal.Set();
            }
            catch (Exception ex)
            {
                PipelineConsole.Log("[EMBEDDED GNN DISPATCH] read FAIL - " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref pollPending, 0);
            }
        }

        private void WorkerLoop()
        {
            while (!stopping)
            {
                signal.WaitOne(500);
                try
                {
                    JObject commit;
                    while (true)
                    {
                        lock (gate)
                        {
                            commit = commits.Count == 0 ? null : commits.Dequeue();
                        }

                        if (commit == null)
                            break;
                        session.Commit((string)commit["gnnKind"], (int? )commit["gnnJob"] ?? -1, (string)commit["resource"]);
                        PipelineConsole.Log("[EMBEDDED GNN COMMIT] eventId=" + commit["eventId"] + " kind=" + commit["gnnKind"] + " job=" + commit["gnnJob"] + " res=" + commit["resource"]);
                    }

                    JObject request;
                    lock (gate)
                    {
                        request = pendingRequest;
                        pendingRequest = null;
                    }

                    if (request != null)
                        ProcessRequest(request);
                }
                catch (Exception ex)
                {
                    PipelineConsole.Log("[EMBEDDED GNN DISPATCH] worker FAIL - " + ex);
                }
            }
        }

        private void ProcessRequest(JObject request)
        {
            int requestId = (int)request["requestId"];
            JArray requested = (JArray)request["candidates"];
            var modelCandidates = requested.OfType<JObject>().Select(x => new JObject { ["kind"] = x["kind"], ["job"] = x["job"], ["res"] = x["res"] }).ToList();
            Stopwatch watch = Stopwatch.StartNew();
            EmbeddedGnnChoice choice = session.Choose(modelCandidates);
            watch.Stop();
            JObject selected = (JObject)requested[choice.Index];
            var response = new JObject
            {
                ["requestId"] = requestId,
                ["candidateIndex"] = (int)selected["candidateIndex"],
                ["gnnIndex"] = choice.Index,
                ["scores"] = new JArray(choice.Scores),
                ["elapsedMilliseconds"] = watch.Elapsed.TotalMilliseconds
            };
            Execute.OnUIThread(delegate
            {
                controller.GetProperty("DispatchInterface::GnnResponseJSON").Value = response.ToString(Formatting.None);
                controller.GetProperty("DispatchInterface::GnnResponseId").Value = requestId;
                controller.GetProperty("DispatchInterface::EventName").Value = "GNN_DECISION_READY";
                IProperty token = controller.GetProperty("DispatchInterface::EventToken");
                token.Value = Convert.ToInt32(token.Value) + 1;
            });
            PipelineConsole.Log("[EMBEDDED GNN CHOOSE] requestId=" + requestId + " candidates=" + requested.Count + " gnnIndex=" + choice.Index + " candidateIndex=" + selected["candidateIndex"] + " elapsed=" + watch.Elapsed.TotalMilliseconds.ToString("F2") + "ms");
        }

        public void Dispose()
        {
            stopping = true;
            if (timer != null)
                timer.Dispose();
            signal.Set();
            if (worker != null && worker.IsAlive)
                worker.Join(1000);
            signal.Dispose();
            controller = null;
            session = null;
        }
    }
}

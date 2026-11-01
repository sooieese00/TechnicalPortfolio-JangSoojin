using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Caliburn.Micro;
using Newtonsoft.Json.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public sealed class GnnLiveCommunicationMonitor : IDisposable
    {
        private readonly object syncRoot = new object ();
        private readonly Queue<QueuedEvent> queue = new Queue<QueuedEvent>();
        private readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        private GnnProcessAdapter gnn;
        private ISimComponent controller;
        private Thread worker;
        private Timer pollTimer;
        private volatile bool stopping;
        private int pollPending;
        private int lastEventId;
        private int completedCount;
        private double totalHealthMilliseconds;
        private double totalChooseMilliseconds;
        private int chooseCount;
        private sealed class QueuedEvent
        {
            public int EventId;
            public string Json;
            public long EnqueuedTimestamp;
        }

        public void Start(ISimWorld simWorld, GnnProcessAdapter adapter)
        {
            if (simWorld == null)
                throw new ArgumentNullException("simWorld");
            if (adapter == null)
                throw new ArgumentNullException("adapter");
            controller = simWorld.FindComponent("Central Controller");
            if (controller == null)
                throw new InvalidOperationException("Central Controller was not found.");
            if (controller.GetProperty("DispatchInterface::OperationEventId") == null || controller.GetProperty("DispatchInterface::OperationEventJSON") == null || controller.GetProperty("DispatchInterface::OperationEventLogJSON") == null)
                throw new InvalidOperationException("Central Controller operation-event properties were not found.");
            gnn = adapter;
            worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "DT_KETI GNN Live Communication Monitor"
            };
            worker.Start();
            pollTimer = new Timer(PollTimerElapsed, null, 0, 100);
            PipelineConsole.Log("[GNN LIVE] Polling monitor started (100ms). Every VC operation event will perform a health round trip; " + "valid STARTED events will also perform choose (no commit).");
        }

        private void PollTimerElapsed(object state)
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
                PipelineConsole.Log("[GNN LIVE] poll dispatch FAIL - " + ex.Message);
            }
        }

        private void PollOnUiThread()
        {
            try
            {
                if (stopping || controller == null)
                    return;
                IProperty logProperty = controller.GetProperty("DispatchInterface::OperationEventLogJSON");
                string logJson = logProperty == null ? "[]" : Convert.ToString(logProperty.Value);
                JArray events = string.IsNullOrWhiteSpace(logJson) ? new JArray() : JArray.Parse(logJson);
                foreach (JObject operationEvent in events.OfType<JObject>().Where(x => ((int? )x["eventId"] ?? -1) > lastEventId).OrderBy(x => (int? )x["eventId"] ?? -1))
                {
                    int eventId = (int? )operationEvent["eventId"] ?? -1;
                    if (eventId <= lastEventId)
                        continue;
                    Enqueue(eventId, operationEvent.ToString(Newtonsoft.Json.Formatting.None));
                    lastEventId = eventId;
                }
            }
            catch (Exception ex)
            {
                PipelineConsole.Log("[GNN LIVE] poll read FAIL - " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref pollPending, 0);
            }
        }

        private void Enqueue(int eventId, string json)
        {
            lock (syncRoot)
            {
                queue.Enqueue(new QueuedEvent { EventId = eventId, Json = json, EnqueuedTimestamp = Stopwatch.GetTimestamp() });
            }

            queueSignal.Set();
        }

        private void WorkerLoop()
        {
            while (!stopping)
            {
                queueSignal.WaitOne(500);
                while (!stopping)
                {
                    QueuedEvent item;
                    lock (syncRoot)
                    {
                        if (queue.Count == 0)
                            break;
                        item = queue.Dequeue();
                    }

                    Process(item);
                }
            }
        }

        private void Process(QueuedEvent item)
        {
            JObject operationEvent;
            try
            {
                operationEvent = JObject.Parse(item.Json);
            }
            catch (Exception ex)
            {
                PipelineConsole.Log("[GNN LIVE] eventId=" + item.EventId + " invalid VC event JSON: " + ex.Message);
                return;
            }

            string kind = (string)operationEvent["gnnKind"] ?? "";
            int job = (int? )operationEvent["gnnJob"] ?? -1;
            string resource = (string)operationEvent["resource"] ?? "";
            string phase = (string)operationEvent["phase"] ?? "";
            double queueMilliseconds = ElapsedMilliseconds(item.EnqueuedTimestamp);
            try
            {
                Stopwatch healthWatch = Stopwatch.StartNew();
                bool healthy = gnn.Health();
                healthWatch.Stop();
                completedCount++;
                totalHealthMilliseconds += healthWatch.Elapsed.TotalMilliseconds;
                PipelineConsole.Log("[GNN LIVE] eventId=" + item.EventId + " kind=" + kind + " job=" + job + " res=" + resource + " phase=" + phase + " health=" + healthy + " queue=" + queueMilliseconds.ToString("F2") + "ms" + " healthRtt=" + healthWatch.Elapsed.TotalMilliseconds.ToString("F2") + "ms" + " healthAvg=" + (totalHealthMilliseconds / completedCount).ToString("F2") + "ms");
                if (!healthy || !string.Equals(phase, "STARTED", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(kind) || job < 0 || string.IsNullOrWhiteSpace(resource))
                    return;
                var candidate = new JObject
                {
                    ["kind"] = kind,
                    ["job"] = job,
                    ["res"] = resource
                };
                Stopwatch chooseWatch = Stopwatch.StartNew();
                GnnDecision decision = gnn.Choose(new[] { candidate });
                chooseWatch.Stop();
                chooseCount++;
                totalChooseMilliseconds += chooseWatch.Elapsed.TotalMilliseconds;
                PipelineConsole.Log("[GNN LIVE] eventId=" + item.EventId + " choose index=" + decision.Index + " chooseRtt=" + chooseWatch.Elapsed.TotalMilliseconds.ToString("F2") + "ms" + " chooseAvg=" + (totalChooseMilliseconds / chooseCount).ToString("F2") + "ms");
            }
            catch (Exception ex)
            {
                PipelineConsole.Log("[GNN LIVE] eventId=" + item.EventId + " FAIL - " + ex.Message);
            }
        }

        private static double ElapsedMilliseconds(long started)
        {
            return (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        }

        public void Dispose()
        {
            stopping = true;
            if (pollTimer != null)
            {
                pollTimer.Dispose();
                pollTimer = null;
            }

            queueSignal.Set();
            if (worker != null && worker.IsAlive)
                worker.Join(1000);
            queueSignal.Dispose();
            controller = null;
            gnn = null;
        }
    }
}

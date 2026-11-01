using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    public class SimulationReporting
    {
        public void CreateReport(ISimWorld simWorld, string[] cncList, string[] mheList, string[] shuttleConveyors, string scenario, double horizon, double speedFactor, string outputDirectory, JArray completionHistory = null, JObject recovery = null)
        {
            JArray combinedCompletionHistory = completionHistory == null ? new JArray() : (JArray)completionHistory.DeepClone();
            NormalizeCompletionHistory(combinedCompletionHistory);
            HashSet<string> completionHistoryKeys = new HashSet<string>(combinedCompletionHistory.OfType<JObject>().Select(CompletionHistoryKey), StringComparer.Ordinal);
            foreach (string id in cncList)
                AppendComponentCompletionHistory(simWorld.FindComponent(id), combinedCompletionHistory, completionHistoryKeys);
            foreach (string id in shuttleConveyors.Distinct(StringComparer.OrdinalIgnoreCase))
                AppendComponentCompletionHistory(simWorld.FindComponent(id), combinedCompletionHistory, completionHistoryKeys);
            foreach (string id in mheList.Concat(new[] { "ASRS In" }).Concat(Enumerable.Range(1, 8).Select(i => "Terminal Conveyor #" + i)).Distinct())
                AppendComponentCompletionHistory(simWorld.FindComponent(id), combinedCompletionHistory, completionHistoryKeys);
            NormalizeCompletionHistory(combinedCompletionHistory);
            combinedCompletionHistory = new JArray(combinedCompletionHistory.OfType<JObject>().OrderBy(item => item.Value<double?>("CompletionTimeSeconds") ?? 0));
            JObject completionByResource = BuildCompletionByResource(combinedCompletionHistory);
            JArray cncResults = new JArray();
            JArray mheResults = new JArray();
            List<double> cncBusy = new List<double>();
            List<double> mheBusy = new List<double>();
            List<double> completionTimes = new List<double>();
            string highestResource = "";
            string lowestResource = "";
            string mostBlockedResource = "";
            double highestBusy = double.MinValue;
            double lowestBusy = double.MaxValue;
            double mostBlocked = double.MinValue;
            foreach (string id in cncList)
            {
                ISimComponent component = simWorld.FindComponent(id);
                double idle = StatisticsValue(component, "IdlePercentage");
                double busy = StatisticsValue(component, "BusyPercentage");
                double blocked = StatisticsValue(component, "BlockedPercentage");
                double completion = ComponentValue(component, "CompletionTime");
                double other = Math.Max(0, 100 - idle - busy - blocked);
                cncBusy.Add(busy);
                AddCompletion(completionTimes, completion);
                UpdateUtilization(id, busy, ref highestResource, ref highestBusy, ref lowestResource, ref lowestBusy);
                if (blocked > mostBlocked)
                {
                    mostBlocked = blocked;
                    mostBlockedResource = id;
                }

                cncResults.Add(new JObject { ["CNC_Instance_ID"] = id, ["Throughput"] = component.Statistics.PartsExited, ["PartMaxTimeSeconds"] = component.Statistics.PartsMaxTime, ["PartMaxTimeFormatted"] = FormatDuration(component.Statistics.PartsMaxTime), ["PartMinTimeSeconds"] = component.Statistics.PartsMinTime, ["PartMinTimeFormatted"] = FormatDuration(component.Statistics.PartsMinTime), ["PartsAverageCount"] = component.Statistics.PartsAverageCount, ["IdlePercentage"] = Round(idle), ["BusyPercentage"] = Round(busy), ["BlockedPercentage"] = Round(blocked), ["OtherPercentage"] = Round(other), ["CompletionTimeSeconds"] = completion, ["CompletionTimeFormatted"] = FormatDuration(completion) });
            }

            int shuttleIndex = 0;
            foreach (string id in mheList)
            {
                ISimComponent component = simWorld.FindComponent(id);
                bool isShuttle = id.StartsWith("SHUTTLE", StringComparison.OrdinalIgnoreCase);
                double idle = StatisticsValue(component, "IdlePercentage");
                double busy = StatisticsValue(component, "BusyPercentage");
                double completion = 0;
                bool hasCompletion = false;
                if (isShuttle && shuttleIndex * 2 + 1 < shuttleConveyors.Length)
                {
                    completion = getCompletionTimePropertyShuttle(simWorld, null, shuttleConveyors[shuttleIndex * 2], shuttleConveyors[shuttleIndex * 2 + 1]);
                    shuttleIndex++;
                    hasCompletion = true;
                    AddCompletion(completionTimes, completion);
                }
                else if (id.StartsWith("AMR", StringComparison.OrdinalIgnoreCase))
                {
                    hasCompletion = TryGetAmrChangeTypeCompletion(combinedCompletionHistory, id, out completion);
                    if (hasCompletion)
                        AddCompletion(completionTimes, completion);
                }
                else if (string.Equals(id, "LIFTER_A_001", StringComparison.OrdinalIgnoreCase))
                {
                    hasCompletion = TryGetLatestComponentCompletion(simWorld, new[] { "Terminal Conveyor #1", "Terminal Conveyor #2", "Terminal Conveyor #3", "Terminal Conveyor #4" }, out completion);
                    if (hasCompletion)
                        AddCompletion(completionTimes, completion);
                }
                else if (string.Equals(id, "LIFTER_B_001", StringComparison.OrdinalIgnoreCase))
                {
                    hasCompletion = TryGetLatestComponentCompletion(simWorld, new[] { "Terminal Conveyor #5", "Terminal Conveyor #6", "Terminal Conveyor #7", "Terminal Conveyor #8" }, out completion);
                    if (hasCompletion)
                        AddCompletion(completionTimes, completion);
                }

                mheBusy.Add(busy);
                UpdateUtilization(id, busy, ref highestResource, ref highestBusy, ref lowestResource, ref lowestBusy);
                JObject item = new JObject
                {
                    ["MHE_Instance_ID"] = id,
                    ["MHE_Type"] = MheType(id),
                    ["Throughput"] = isShuttle ? component.Statistics.PartsEntered : component.Statistics.PartsExited,
                    ["IdlePercentage"] = Round(idle),
                    ["BusyPercentage"] = Round(busy),
                    ["CompletionTimeAvailable"] = hasCompletion
                };
                if (hasCompletion)
                {
                    item["CompletionTimeSeconds"] = completion;
                    item["CompletionTimeFormatted"] = FormatDuration(completion);
                }

                mheResults.Add(item);
            }

            ISimComponent asrsIn = simWorld.FindComponent("ASRS In");
            double totalProduction = asrsIn.Statistics.PartsExited;
            double asrsCompletion = ComponentValue(asrsIn, "CompletionTime");
            AddCompletion(completionTimes, asrsCompletion);
            double makespan = completionTimes.Count == 0 ? 0 : completionTimes.Max();
            double productionRate = makespan == 0 ? 0 : totalProduction * 3600 / makespan;
            JObject report = new JObject
            {
                ["Metadata"] = new JObject
                {
                    ["Scenario"] = scenario,
                    ["ReportTime"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["SimulationHorizonSeconds"] = horizon,
                    ["SpeedFactor"] = speedFactor,
                    ["Status"] = "SimulationStopped"
                },
                ["Summary"] = new JObject
                {
                    ["MakespanSeconds"] = makespan,
                    ["MakespanFormatted"] = FormatDuration(makespan),
                    ["TotalProduction"] = totalProduction,
                    ["ProductionRatePerHour"] = Round(productionRate),
                    ["AverageCNCUtilization"] = Round(Average(cncBusy)),
                    ["AverageMHEUtilization"] = Round(Average(mheBusy)),
                    ["OverallAverageUtilization"] = Round(Average(cncBusy.Concat(mheBusy))),
                    ["HighestUtilizationResource"] = highestResource,
                    ["HighestUtilizationPercentage"] = Round(highestBusy),
                    ["LowestUtilizationResource"] = lowestResource,
                    ["LowestUtilizationPercentage"] = Round(lowestBusy),
                    ["BottleneckCandidate"] = highestResource,
                    ["MostBlockedResource"] = mostBlockedResource,
                    ["MostBlockedPercentage"] = Round(mostBlocked),
                    ["ASRSInCompletionTimeSeconds"] = asrsCompletion,
                    ["ASRSInCompletionTimeFormatted"] = FormatDuration(asrsCompletion)
                },
                ["CNC"] = cncResults,
                ["MHE"] = mheResults,
                ["CompletionHistory"] = combinedCompletionHistory,
                ["CompletionTimeline"] = combinedCompletionHistory.DeepClone(),
                ["CompletionByResource"] = completionByResource
            };
            if (recovery != null)
                report["Recovery"] = recovery;
            WriteReportFiles(report, outputDirectory);
        }

        public void WriteReportFiles(JObject report, string outputDirectory)
        {
            var combinedCompletionHistory = report["CompletionTimeline"] as JArray ?? new JArray();
            NormalizeCompletionHistory(combinedCompletionHistory);
            report["CompletionTimeline"] = combinedCompletionHistory;
            report["CompletionHistory"] = combinedCompletionHistory.DeepClone();
            report["CompletionByResource"] = BuildCompletionByResource(combinedCompletionHistory);
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllLines(Path.Combine(outputDirectory, "CompletionTimeLog.jsonl"), combinedCompletionHistory.OfType<JObject>().Select(item => item.ToString(Formatting.None)), Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "SimulationReport.json"), report.ToString(Formatting.Indented));
            File.WriteAllText(Path.Combine(outputDirectory, "SimulationReport.html"), BuildHtml(report), Encoding.UTF8);
        }

        private bool TryGetAmrChangeTypeCompletion(JArray completionHistory, string resourceId, out double completion)
        {
            completion = completionHistory.OfType<JObject>().Where(item => string.Equals(Convert.ToString(item["Resource"]), resourceId, StringComparison.OrdinalIgnoreCase)).Where(item => string.Equals(Convert.ToString(item["Action"]), "unloading", StringComparison.OrdinalIgnoreCase)).Where(item => string.Equals(Convert.ToString(item["Location"]), "Change Type", StringComparison.OrdinalIgnoreCase)).Select(item => item.Value<double?>("CompletionTimeSeconds") ?? 0).DefaultIfEmpty(0).Max();
            return completion > 0;
        }

        private bool TryGetLatestComponentCompletion(ISimWorld simWorld, IEnumerable<string> componentNames, out double completion)
        {
            completion = 0;
            foreach (string componentName in componentNames)
            {
                ISimComponent component = simWorld.FindComponent(componentName);
                if (component == null)
                    continue;
                IProperty property = component.GetProperty("CompletionTime");
                if (property == null)
                    continue;
                double value;
                try
                {
                    value = Convert.ToDouble(property.Value);
                }
                catch
                {
                    continue;
                }

                if (value > completion)
                    completion = value;
            }

            return completion > 0;
        }

        private double StatisticsValue(ISimComponent component, string propertyName)
        {
            return Convert.ToDouble(component.Statistics.GetProperty(propertyName).Value);
        }

        private double ComponentValue(ISimComponent component, string propertyName)
        {
            return Convert.ToDouble(component.GetProperty(propertyName).Value);
        }

        private void AddCompletion(List<double> values, double completion)
        {
            if (completion > 0)
                values.Add(completion);
        }

        private void UpdateUtilization(string id, double busy, ref string highestResource, ref double highest, ref string lowestResource, ref double lowest)
        {
            if (busy > highest)
            {
                highest = busy;
                highestResource = id;
            }

            if (busy < lowest)
            {
                lowest = busy;
                lowestResource = id;
            }
        }

        private double Average(IEnumerable<double> values)
        {
            double[] items = values.ToArray();
            return items.Length == 0 ? 0 : items.Average();
        }

        private double Round(double value)
        {
            return Math.Round(value, 2);
        }

        private static string FormatDuration(double seconds)
        {
            TimeSpan value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)value.TotalHours, value.Minutes, value.Seconds);
        }

        private string MheType(string id)
        {
            int separator = id.IndexOf('_');
            return separator < 0 ? id : id.Substring(0, separator);
        }

        private string Html(object value)
        {
            return WebUtility.HtmlEncode(Convert.ToString(value));
        }

        private JObject BuildCompletionByResource(JArray timeline)
        {
            JObject result = new JObject();
            foreach (IGrouping<string, JObject> group in timeline.OfType<JObject>().GroupBy(item => Convert.ToString(item["Resource"]), StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                JArray events = new JArray();
                int sequence = 1;
                foreach (JObject source in group.OrderBy(item => item.Value<double?>("CompletionTimeSeconds") ?? 0))
                {
                    JObject item = (JObject)source.DeepClone();
                    item["Sequence"] = sequence++;
                    events.Add(item);
                }

                result[group.Key] = events;
            }

            return result;
        }

        public string BuildHtml(JObject report)
        {
            JObject metadata = (JObject)report["Metadata"];
            JObject summary = (JObject)report["Summary"];
            JArray cncResults = (JArray)report["CNC"];
            JArray mheResults = (JArray)report["MHE"];
            JArray completionTimeline = (JArray)report["CompletionTimeline"];
            NormalizeCompletionHistory(completionTimeline);
            JObject completionByResource = BuildCompletionByResource(completionTimeline);
            JObject recovery = report["Recovery"] as JObject;
            string title = recovery == null ? "Simulation Report" : "Recovery Simulation Report";
            StringBuilder html = new StringBuilder();
            html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
            html.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            html.Append("<title>").Append(title).Append("</title><style>");
            html.Append("body{margin:0;background:#f4f7fb;color:#172033;font-family:Segoe UI,Arial,sans-serif}");
            html.Append("main{max-width:1200px;margin:auto;padding:32px}h1{margin:0 0 6px}h2{margin-top:30px}");
            html.Append(".sub{color:#64748b;margin-bottom:24px}.cards{display:grid;grid-template-columns:repeat(4,1fr);gap:14px}");
            html.Append(".card,.panel{background:#fff;border:1px solid #e2e8f0;border-radius:12px;box-shadow:0 4px 14px #0f172a0d}");
            html.Append(".card{padding:18px}.label{font-size:13px;color:#64748b}.value{font-size:23px;font-weight:700;margin-top:6px}");
            html.Append(".panel{overflow:hidden}table{width:100%;border-collapse:collapse}th,td{padding:11px 13px;text-align:right;border-bottom:1px solid #e2e8f0}");
            html.Append("th{background:#f8fafc;color:#475569;font-size:12px}th:first-child,td:first-child{text-align:left}");
            html.Append(".bar{height:7px;background:#e2e8f0;border-radius:5px;overflow:hidden;min-width:85px}.fill{height:100%;background:#2563eb}");
            html.Append(".note{margin-top:20px;padding:14px 16px;background:#fff7ed;border-left:4px solid #f97316;color:#9a3412}");
            html.Append("@media(max-width:800px){.cards{grid-template-columns:repeat(2,1fr)}main{padding:18px}.panel{overflow:auto}}");
            html.Append("</style></head><body><main><h1>").Append(title).Append("</h1><div class=\"sub\">");
            html.Append(Html(metadata["Scenario"])).Append(" · ").Append(Html(metadata["ReportTime"])).Append("</div>");
            if (recovery != null)
                AppendRecoverySummary(html, recovery, completionTimeline);
            html.Append("<section class=\"cards\">");
            Card(html, "Makespan", summary["MakespanFormatted"] + "");
            Card(html, "Total Production", summary["TotalProduction"] + " units");
            Card(html, "Production Rate", summary["ProductionRatePerHour"] + " units/hour");
            Card(html, "Overall Utilization", summary["OverallAverageUtilization"] + "%");
            Card(html, "Average CNC Utilization", summary["AverageCNCUtilization"] + "%");
            Card(html, "Average MHE Utilization", summary["AverageMHEUtilization"] + "%");
            Card(html, "Bottleneck Candidate", summary["BottleneckCandidate"] + " (" + summary["HighestUtilizationPercentage"] + "%)");
            Card(html, "Most Blocked", summary["MostBlockedResource"] + " (" + summary["MostBlockedPercentage"] + "%)");
            html.Append("</section>");
            html.Append("<h2>CNC Performance</h2><div class=\"panel\"><table><thead><tr>");
            html.Append("<th>CNC</th><th>Throughput</th><th>Busy</th><th>Idle</th><th>Blocked</th><th>Other</th><th>Min Time</th><th>Max Time</th><th>Completion</th></tr></thead><tbody>");
            foreach (JObject cnc in cncResults)
            {
                html.Append("<tr><td>").Append(Html(cnc["CNC_Instance_ID"])).Append("</td><td>").Append(cnc["Throughput"]).Append("</td>");
                PercentCell(html, cnc["BusyPercentage"]);
                html.Append("<td>").Append(cnc["IdlePercentage"]).Append("%</td><td>").Append(cnc["BlockedPercentage"]);
                html.Append("%</td><td>").Append(cnc["OtherPercentage"]).Append("%</td><td>").Append(cnc["PartMinTimeFormatted"]);
                html.Append("</td><td>").Append(cnc["PartMaxTimeFormatted"]).Append("</td><td>").Append(cnc["CompletionTimeFormatted"]).Append("</td></tr>");
            }

            html.Append("</tbody></table></div><h2>Material Handling Equipment</h2><div class=\"panel\"><table><thead><tr>");
            html.Append("<th>MHE</th><th>Type</th><th>Throughput</th><th>Busy</th><th>Idle</th><th>Completion</th></tr></thead><tbody>");
            foreach (JObject mhe in mheResults)
            {
                html.Append("<tr><td>").Append(Html(mhe["MHE_Instance_ID"])).Append("</td><td>").Append(Html(mhe["MHE_Type"]));
                html.Append("</td><td>").Append(mhe["Throughput"]).Append("</td>");
                PercentCell(html, mhe["BusyPercentage"]);
                html.Append("<td>").Append(mhe["IdlePercentage"]).Append("%</td><td>");
                html.Append(mhe["CompletionTimeAvailable"].Value<bool>() ? mhe["CompletionTimeFormatted"] + "" : "Not measured");
                html.Append("</td></tr>");
            }

            html.Append("</tbody></table></div>");
            if (recovery != null)
                AppendRecoveryGantt(html, recovery, completionTimeline);
            AppendGantt(html, completionTimeline, recovery == null ? "Resource Gantt Chart" : "Rescheduled Execution Gantt Chart");
            html.Append("<h2>Completion Timeline</h2>");
            AppendCompletionTable(html, completionTimeline, true);
            html.Append("<h2>Completion By Resource</h2>");
            foreach (JProperty resource in completionByResource.Properties())
            {
                html.Append("<h3>").Append(Html(resource.Name)).Append(" <span class=\"sub\">(");
                html.Append(((JArray)resource.Value).Count).Append(" events)</span></h3>");
                AppendCompletionTable(html, (JArray)resource.Value, false);
            }

            if (!completionByResource.Properties().Any())
                html.Append("<div class=\"panel\">No completion events recorded</div>");
            html.Append("<div class=\"note\">CNC and conveyor bars show processing time; time blocked while handing the product to the next buffer is excluded (BlockedSeconds in the JSON). AMR bars span dispatch to loading, then loading to unloading. Diamonds show completion events without a recorded start. Unknown means the material was not recorded.</div>");
            html.Append("</main></body></html>");
            return html.ToString();
        }

        private void AppendCompletionTable(StringBuilder html, JArray events, bool showResource)
        {
            html.Append("<div class=\"panel\"><table><thead><tr>");
            if (showResource)
                html.Append("<th>Resource</th>");
            else
                html.Append("<th>#</th>");
            html.Append("<th>Type</th><th>Material</th><th>Action</th><th>Location</th><th>Source</th><th>Start</th><th>Completion</th><th>Recorded At</th></tr></thead><tbody>");
            int sequence = 1;
            foreach (JObject completion in events.OfType<JObject>())
            {
                html.Append("<tr><td>").Append(showResource ? Html(completion["Resource"]) : Html(completion["Sequence"] ?? sequence)).Append("</td><td>");
                html.Append(Html(completion["ResourceType"])).Append("</td><td>");
                html.Append(Html(completion["Material"] ?? "")).Append("</td><td>");
                html.Append(Html(completion["Action"] ?? "")).Append("</td><td>");
                html.Append(Html(completion["Location"] ?? "")).Append("</td><td>");
                html.Append(Html(completion["SourceComponent"])).Append("</td><td>");
                html.Append(Html(completion["StartTimeFormatted"] ?? "Not recorded")).Append("</td><td>");
                html.Append(Html(completion["CompletionTimeFormatted"])).Append("</td><td>");
                html.Append(Html(completion["RecordedAt"])).Append("</td></tr>");
                sequence++;
            }

            if (events.Count == 0)
                html.Append("<tr><td colspan=\"9\">No completion events recorded</td></tr>");
            html.Append("</tbody></table></div>");
        }

        public static void NormalizeCompletionHistory(JArray events)
        {
            foreach (JObject item in events.OfType<JObject>())
            {
                string raw = (string)item["ProductType"] ?? (string)item["Material"] ?? "";
                if (string.IsNullOrWhiteSpace(raw))
                    raw = (string)item["Material"] ?? "";
                item["ProductType"] = raw;
                Match match = Regex.Match(raw, @"^(?:AMR_WIP_)?MT_([A-Z])(?:_[A-Z])?$", RegexOptions.IgnoreCase);
                Match asrs = Regex.Match(raw, @"^ASRS_([A-Z])$", RegexOptions.IgnoreCase);
                item["Material"] = match.Success ? "MT_" + match.Groups[1].Value.ToUpperInvariant() : asrs.Success ? "MT_" + asrs.Groups[1].Value.ToUpperInvariant() : string.IsNullOrWhiteSpace(raw) ? "Unknown" : raw;
                double end = item.Value<double?>("CompletionTimeSeconds") ?? 0;
                double? start = item.Value<double?>("StartTimeSeconds");
                if (start.HasValue && !double.IsNaN(start.Value) && !double.IsInfinity(start.Value) && start.Value >= 0 && start.Value <= end)
                {
                    item["StartTimeFormatted"] = FormatDuration(start.Value);
                    item["DurationSeconds"] = end - start.Value;
                }
                else
                {
                    item["StartTimeSeconds"] = JValue.CreateNull();
                    item["StartTimeFormatted"] = "Not recorded";
                    item["DurationSeconds"] = JValue.CreateNull();
                    item["TimingSource"] = "CompletionOnly";
                }
            }
        }

        private void AppendGantt(StringBuilder html, JArray events, string heading = "Resource Gantt Chart")
        {
            html.Append("<h2>").Append(Html(heading)).Append("</h2><p class=\"sub\">Elapsed simulation time · color by material · hover for details. Bars: measured intervals. Diamonds: completion only.</p>");
            var items = events.OfType<JObject>().Where(e => e.Value<double?>("CompletionTimeSeconds") >= 0 && !double.IsInfinity(e.Value<double>("CompletionTimeSeconds"))).ToList();
            if (items.Count == 0)
            {
                html.Append("<div class=\"panel\" style=\"padding:20px\">No completion events recorded.</div>");
                return;
            }

            string[] palette =
            {
                "#2563eb",
                "#059669",
                "#d97706",
                "#9333ea",
                "#65a30d",
                "#0891b2",
                "#be185d"
            };
            var materials = items.Select(e => (string)e["Material"] ?? "Unknown").Distinct().OrderBy(x => x).ToList();
            var colors = materials.Select((m, i) => new { m, color = m == "Unknown" ? "#64748b" : palette[i % palette.Length] }).ToDictionary(x => x.m, x => x.color);
            html.Append("<div style=\"display:flex;gap:16px;flex-wrap:wrap;margin-bottom:12px\">");
            foreach (var material in materials)
                html.Append("<span><b style=\"color:").Append(colors[material]).Append("\">&#9632;</b> ").Append(Html(material)).Append("</span>");
            html.Append("</div>");
            const double left = 175, plot = 1050;
            double max = Math.Max(1, items.Max(e => e.Value<double>("CompletionTimeSeconds")));
            var body = new StringBuilder();
            double y = 55;
            foreach (var group in items.GroupBy(e => (string)e["Resource"] ?? "Unknown").OrderBy(g => g.Key))
            {
                var laneEnds = new List<double>();
                var marks = new StringBuilder();
                foreach (var item in group.OrderBy(e => e.Value<double?>("StartTimeSeconds") ?? e.Value<double>("CompletionTimeSeconds")))
                {
                    double end = item.Value<double>("CompletionTimeSeconds");
                    double? start = item.Value<double?>("StartTimeSeconds");
                    double begin = start ?? end;
                    int lane = laneEnds.FindIndex(t => t <= begin);
                    if (lane < 0)
                    {
                        lane = laneEnds.Count;
                        laneEnds.Add(end);
                    }
                    else
                        laneEnds[lane] = end;
                    string material = (string)item["Material"] ?? "Unknown";
                    double x = left + plot * begin / max, cy = y + lane * 27 + 16;
                    string title = Html(group.Key + " | " + material + " | " + (string)item["Action"] + " | " + (start.HasValue ? FormatDuration(start.Value) : "Start not recorded") + " → " + FormatDuration(end) + " | " + (string)item["SourceComponent"] + " | " + (string)item["TimingSource"]);
                    if (start.HasValue && end > begin)
                    {
                        double width = Math.Max(2, plot * (end - begin) / max);
                        marks.Append("<rect x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(cy - 9)).Append("\" width=\"").Append(Number(width)).Append("\" height=\"19\" rx=\"3\" fill=\"").Append(colors[material]).Append("\"><title>").Append(title).Append("</title></rect>");
                        if (width > 48)
                            marks.Append("<text x=\"").Append(Number(x + 5)).Append("\" y=\"").Append(Number(cy + 5)).Append("\" fill=\"white\" font-size=\"11\" pointer-events=\"none\">").Append(Html(material)).Append("</text>");
                    }
                    else
                        marks.Append("<path d=\"M ").Append(Number(x)).Append(" ").Append(Number(cy - 6)).Append(" l 6 6 -6 6 -6 -6 Z\" fill=\"").Append(colors[material]).Append("\"><title>").Append(title).Append("</title></path>");
                }

                double height = Math.Max(1, laneEnds.Count) * 27 + 12;
                body.Append("<rect x=\"0\" y=\"").Append(Number(y)).Append("\" width=\"1250\" height=\"").Append(Number(height)).Append("\" fill=\"#f8fafc\" stroke=\"#e2e8f0\"/><text x=\"12\" y=\"").Append(Number(y + 21)).Append("\" font-size=\"12\" fill=\"#334155\">").Append(Html(group.Key)).Append("</text>").Append(marks);
                y += height;
            }

            html.Append("<div class=\"panel\" style=\"overflow-x:auto\"><svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"Resource Gantt chart\" viewBox=\"0 0 1250 ").Append(Number(y + 15)).Append("\" style=\"display:block;width:100%;min-width:1050px\"><title>Measured operations by resource and material</title>").Append(body);
            for (int i = 0; i <= 5; i++)
            {
                double x = left + plot * i / 5;
                html.Append("<line x1=\"").Append(Number(x)).Append("\" x2=\"").Append(Number(x)).Append("\" y1=\"45\" y2=\"").Append(Number(y)).Append("\" stroke=\"#cbd5e1\" stroke-dasharray=\"3 5\" pointer-events=\"none\"/>").Append("<text x=\"").Append(Number(x)).Append("\" y=\"29\" text-anchor=\"middle\" font-size=\"12\" fill=\"#475569\">").Append(FormatDuration(max * i / 5)).Append("</text>");
            }

            html.Append("</svg></div>");
        }

        private sealed class GanttMark
        {
            public double Begin, End;
            public bool HasStart, Faded, Failure;
            public string Material, Title;
        }

        private static double LatestEnd(JArray events)
        {
            return events == null ? 0 : events.OfType<JObject>().Select(e => e.Value<double?>("CompletionTimeSeconds") ?? 0).Where(t => !double.IsInfinity(t) && !double.IsNaN(t)).DefaultIfEmpty(0).Max();
        }

        private void AppendRecoverySummary(StringBuilder html, JObject recovery, JArray rescheduled)
        {
            double failure = recovery.Value<double?>("FailureTimeSeconds") ?? 0;
            double window = recovery.Value<double?>("RecoveryWindowSeconds") ?? 0;
            double initialEnd = LatestEnd(recovery["InitialTimeline"] as JArray);
            double recoveredEnd = failure + window + LatestEnd(rescheduled);
            string failed = string.Join(", ", (recovery["FailedResources"] as JArray ?? new JArray()).Select(t => (string)t));
            html.Append("<section class=\"cards\" style=\"margin-bottom:14px\">");
            Card(html, "Failure", (failed.Length == 0 ? "Machine" : failed) + " @ " + FormatDuration(failure));
            Card(html, "Recovery Window", FormatDuration(window) + " (GNN rule dispatch)");
            Card(html, "Initial Plan End", initialEnd > 0 ? FormatDuration(initialEnd) : "Not available");
            Card(html, "Recovered Plan End", FormatDuration(recoveredEnd));
            html.Append("</section>");
        }

        private void AppendRecoveryGantt(StringBuilder html, JObject recovery, JArray rescheduled)
        {
            double failure = recovery.Value<double?>("FailureTimeSeconds") ?? 0;
            double window = recovery.Value<double?>("RecoveryWindowSeconds") ?? 0;
            double resume = failure + window;
            JArray initial = recovery["InitialTimeline"] as JArray ?? new JArray();
            JArray abnormal = recovery["RecoveryTimeline"] as JArray ?? new JArray();
            var failed = new HashSet<string>((recovery["FailedResources"] as JArray ?? new JArray()).Select(t => (string)t), StringComparer.OrdinalIgnoreCase);
            var plan = new Dictionary<string, List<GanttMark>>(StringComparer.OrdinalIgnoreCase);
            var recoveryRows = new Dictionary<string, List<GanttMark>>(StringComparer.OrdinalIgnoreCase);
            Action<Dictionary<string, List<GanttMark>>, JObject, double, double, string, bool> add = (rows, e, offset, clip, phase, faded) =>
            {
                double end = e.Value<double?>("CompletionTimeSeconds") ?? -1;
                if (end < 0 || double.IsInfinity(end) || double.IsNaN(end))
                    return;
                double? start = e.Value<double?>("StartTimeSeconds");
                double begin = (start ?? end) + offset;
                end += offset;
                if (begin >= clip)
                    return;
                if (end > clip)
                    end = clip;
                string resource = (string)e["Resource"] ?? "Unknown";
                string action = (string)e["Action"] ?? "";
                var mark = new GanttMark
                {
                    Begin = begin,
                    End = end,
                    HasStart = start.HasValue,
                    Faded = faded,
                    Failure = action.IndexOf("Failure", StringComparison.OrdinalIgnoreCase) >= 0,
                    Material = (string)e["Material"] ?? "Unknown"
                };
                mark.Title = Html(phase + " | " + resource + " | " + mark.Material + (action.Length == 0 ? "" : " | " + action) + " | " + (start.HasValue ? FormatDuration(begin) : "Start not recorded") + " → " + FormatDuration(end) + (e["TimingSource"] == null ? "" : " | " + (string)e["TimingSource"]));
                if (!rows.ContainsKey(resource))
                    rows[resource] = new List<GanttMark>();
                rows[resource].Add(mark);
            };
            foreach (JObject e in initial.OfType<JObject>())
                add(plan, e, 0, double.MaxValue, "Initial plan", (e.Value<double?>("CompletionTimeSeconds") ?? 0) > failure);
            foreach (JObject e in abnormal.OfType<JObject>())
                add(recoveryRows, e, failure, resume, "Abnormal window", false);
            foreach (JObject e in rescheduled.OfType<JObject>())
                add(recoveryRows, e, resume, double.MaxValue, "Rescheduled plan", false);
            foreach (string resource in failed)
                if (!plan.ContainsKey(resource))
                    plan[resource] = new List<GanttMark>();
            var all = plan.Values.Concat(recoveryRows.Values).SelectMany(m => m).ToList();
            string[] palette =
            {
                "#2563eb",
                "#059669",
                "#d97706",
                "#9333ea",
                "#65a30d",
                "#0891b2",
                "#be185d"
            };
            var materials = all.Where(m => !m.Failure).Select(m => m.Material).Distinct().OrderBy(x => x).ToList();
            var colors = materials.Select((m, i) => new { m, color = m == "Unknown" ? "#64748b" : palette[i % palette.Length] }).ToDictionary(x => x.m, x => x.color);
            var resources = plan.Keys.Concat(recoveryRows.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
            double max = Math.Max(Math.Max(1, resume), all.Select(m => m.End).DefaultIfEmpty(0).Max());
            html.Append("<h2>Initial Plan vs. Recovery Gantt Chart</h2><p class=\"sub\">Time from the initial plan start · ").Append("color by material · hover for details. Diamonds: completion recorded without a start time.</p>");
            html.Append("<div style=\"display:flex;gap:16px;flex-wrap:wrap;margin-bottom:12px;align-items:center\">");
            foreach (var material in materials)
                html.Append("<span><b style=\"color:").Append(colors[material]).Append("\">&#9632;</b> ").Append(Html(material)).Append("</span>");
            html.Append("<span><b style=\"color:#2563eb;opacity:.35\">&#9632;</b> Initial plan after the failure (not executed)</span>").Append("<span><b style=\"color:#b91c1c\">&#9636;</b> Failure / repair</span>").Append("<span><b style=\"color:#dc2626\">&#9679;</b> Failure point</span></div>");
            html.Append("<h3>1. Initial Plan</h3>");
            if (initial.Count == 0)
                html.Append("<div class=\"note\" style=\"margin:0 0 12px\">Initial plan report not found.</div>");
            else if (!initial.OfType<JObject>().Any(e => e.Value<double?>("StartTimeSeconds").HasValue))
                html.Append("<div class=\"note\" style=\"margin:0 0 12px\">This initial report has no start times; run Initial again to draw plan bars.</div>");
            AppendGanttChart(html, "Initial plan with failure point", resources, plan, max, colors, failed, failure, -1, -1);
            html.Append("<h3>2. Recovery &amp; Rescheduled Plan</h3>");
            AppendGanttChart(html, "Abnormal window and rescheduled plan", resources, recoveryRows, max, colors, new HashSet<string>(), -1, window > 0 ? failure : -1, window > 0 ? resume : -1);
        }

        private void AppendGanttChart(StringBuilder html, string label, List<string> resources, Dictionary<string, List<GanttMark>> rows, double max, Dictionary<string, string> colors, HashSet<string> failedResources, double failureAt, double windowFrom, double windowTo)
        {
            const double left = 175, plot = 1050, top = 62;
            Func<double, double> X = t => left + plot * Math.Max(0, t) / max;
            var body = new StringBuilder();
            var dots = new StringBuilder();
            double y = top;
            foreach (string resource in resources)
            {
                List<GanttMark> marks;
                rows.TryGetValue(resource, out marks);
                var svg = new StringBuilder();
                int lanes = DrawTrack(svg, marks, y + 6, 24, 18, X, colors);
                double height = Math.Max(1, lanes) * 24 + 12;
                body.Append("<rect x=\"0\" y=\"").Append(Number(y)).Append("\" width=\"1250\" height=\"").Append(Number(height)).Append("\" fill=\"#f8fafc\" stroke=\"#e2e8f0\"/><text x=\"12\" y=\"").Append(Number(y + 21)).Append("\" font-size=\"12\" fill=\"#334155\">").Append(Html(resource)).Append("</text>").Append(svg);
                if (failureAt >= 0 && failedResources.Contains(resource))
                {
                    double cy = y + 15;
                    string tip = Html("Failure: " + resource + " at " + FormatDuration(failureAt));
                    dots.Append("<circle cx=\"").Append(Number(X(failureAt))).Append("\" cy=\"").Append(Number(cy)).Append("\" r=\"7\" fill=\"#dc2626\" fill-opacity=\"0.35\"><animate attributeName=\"r\" values=\"7;13;7\" dur=\"1.6s\" repeatCount=\"indefinite\"/></circle>").Append("<circle cx=\"").Append(Number(X(failureAt))).Append("\" cy=\"").Append(Number(cy)).Append("\" r=\"7\" fill=\"#dc2626\" stroke=\"#fff\" stroke-width=\"2\"><title>").Append(tip).Append("</title></circle>");
                }

                y += height;
            }

            html.Append("<div class=\"panel\" style=\"overflow-x:auto;margin-bottom:18px\"><svg xmlns=\"http://www.w3.org/2000/svg\" role=\"img\" aria-label=\"").Append(Html(label)).Append("\" viewBox=\"0 0 1250 ").Append(Number(y + 15)).Append("\" style=\"display:block;width:100%;min-width:1050px\"><title>").Append(Html(label)).Append("</title>").Append("<defs><pattern id=\"failureHatch\" width=\"6\" height=\"6\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">").Append("<rect width=\"6\" height=\"6\" fill=\"#fecaca\"/><line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"6\" stroke=\"#b91c1c\" stroke-width=\"2.5\"/></pattern></defs>");
            if (windowFrom >= 0 && windowTo > windowFrom)
                html.Append("<rect x=\"").Append(Number(X(windowFrom))).Append("\" y=\"").Append(Number(top - 14)).Append("\" width=\"").Append(Number(X(windowTo) - X(windowFrom))).Append("\" height=\"").Append(Number(y - top + 14)).Append("\" fill=\"#fee2e2\" fill-opacity=\"0.55\" pointer-events=\"none\"/>").Append("<text x=\"").Append(Number(X(windowFrom) + 4)).Append("\" y=\"").Append(Number(top - 20)).Append("\" font-size=\"12\" font-weight=\"700\" fill=\"#b91c1c\">Abnormal window (GNN rule)</text>");
            html.Append(body);
            for (int i = 0; i <= 5; i++)
            {
                double x = X(max * i / 5);
                html.Append("<line x1=\"").Append(Number(x)).Append("\" x2=\"").Append(Number(x)).Append("\" y1=\"").Append(Number(top - 14)).Append("\" y2=\"").Append(Number(y)).Append("\" stroke=\"#cbd5e1\" stroke-dasharray=\"3 5\" pointer-events=\"none\"/>").Append("<text x=\"").Append(Number(x)).Append("\" y=\"18\" text-anchor=\"middle\" font-size=\"12\" fill=\"#475569\">").Append(FormatDuration(max * i / 5)).Append("</text>");
            }

            if (failureAt >= 0)
                html.Append("<line x1=\"").Append(Number(X(failureAt))).Append("\" x2=\"").Append(Number(X(failureAt))).Append("\" y1=\"").Append(Number(top - 14)).Append("\" y2=\"").Append(Number(y)).Append("\" stroke=\"#dc2626\" stroke-width=\"2\" pointer-events=\"none\"/>").Append("<text x=\"").Append(Number(X(failureAt) + 4)).Append("\" y=\"").Append(Number(top - 20)).Append("\" font-size=\"12\" font-weight=\"700\" fill=\"#dc2626\">Failure ").Append(FormatDuration(failureAt)).Append("</text>");
            if (windowTo >= 0)
                html.Append("<line x1=\"").Append(Number(X(windowTo))).Append("\" x2=\"").Append(Number(X(windowTo))).Append("\" y1=\"").Append(Number(top - 14)).Append("\" y2=\"").Append(Number(y)).Append("\" stroke=\"#0f172a\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\" pointer-events=\"none\"/>").Append("<text x=\"").Append(Number(X(windowTo) + 4)).Append("\" y=\"").Append(Number(top - 20)).Append("\" font-size=\"12\" font-weight=\"700\" fill=\"#0f172a\">Rescheduled ").Append(FormatDuration(windowTo)).Append("</text>");
            html.Append(dots).Append("</svg></div>");
        }

        private int DrawTrack(StringBuilder svg, List<GanttMark> marks, double y, double laneHeight, double barHeight, Func<double, double> X, Dictionary<string, string> colors)
        {
            if (marks == null || marks.Count == 0)
                return 0;
            var laneEnds = new List<double>();
            foreach (var mark in marks.OrderBy(m => m.Begin))
            {
                int lane = laneEnds.FindIndex(t => t <= mark.Begin);
                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(mark.End);
                }
                else
                    laneEnds[lane] = mark.End;
                double cy = y + lane * laneHeight + barHeight / 2;
                string fill = mark.Failure ? "url(#failureHatch)" : colors.ContainsKey(mark.Material) ? colors[mark.Material] : "#64748b";
                string opacity = mark.Faded ? "0.35" : "1";
                double x = X(mark.Begin);
                if (mark.HasStart && mark.End > mark.Begin)
                {
                    double width = Math.Max(2, X(mark.End) - x);
                    svg.Append("<rect x=\"").Append(Number(x)).Append("\" y=\"").Append(Number(cy - barHeight / 2)).Append("\" width=\"").Append(Number(width)).Append("\" height=\"").Append(Number(barHeight)).Append("\" rx=\"3\" fill=\"").Append(fill).Append("\" fill-opacity=\"").Append(opacity).Append("\"").Append(mark.Failure ? " stroke=\"#b91c1c\"" : "").Append("><title>").Append(mark.Title).Append("</title></rect>");
                    if (!mark.Faded && !mark.Failure && barHeight >= 16 && width > 48)
                        svg.Append("<text x=\"").Append(Number(x + 5)).Append("\" y=\"").Append(Number(cy + 4)).Append("\" fill=\"white\" font-size=\"11\" pointer-events=\"none\">").Append(Html(mark.Material)).Append("</text>");
                }
                else
                {
                    double r = Math.Min(6, barHeight / 2);
                    svg.Append("<path d=\"M ").Append(Number(x)).Append(" ").Append(Number(cy - r)).Append(" l ").Append(Number(r)).Append(" ").Append(Number(r)).Append(" ").Append(Number(-r)).Append(" ").Append(Number(r)).Append(" ").Append(Number(-r)).Append(" ").Append(Number(-r)).Append(" Z\" fill=\"").Append(fill).Append("\" fill-opacity=\"").Append(opacity).Append("\"><title>").Append(mark.Title).Append("</title></path>");
                }
            }

            return laneEnds.Count;
        }

        private static string Number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void Card(StringBuilder html, string label, string value)
        {
            html.Append("<div class=\"card\"><div class=\"label\">").Append(Html(label));
            html.Append("</div><div class=\"value\">").Append(Html(value)).Append("</div></div>");
        }

        private void PercentCell(StringBuilder html, JToken token)
        {
            double value = token.Value<double>();
            double width = Math.Max(0, Math.Min(100, value));
            html.Append("<td>").Append(value.ToString("0.##", CultureInfo.InvariantCulture));
            html.Append("%<div class=\"bar\"><div class=\"fill\" style=\"width:");
            html.Append(width.ToString("0.##", CultureInfo.InvariantCulture));
            html.Append("%\"></div></div></td>");
        }

        public double getThroughputForeachMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            double vcResult = targetComponent.Statistics.PartsExited;
            result = vcResult;
            return result;
        }

        public double getPartMaxtimeMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            double vcResult = targetComponent.Statistics.PartsMaxTime;
            result = vcResult;
            return result;
        }

        public double getPartMintimeMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            double vcResult = targetComponent.Statistics.PartsMinTime;
            result = vcResult;
            return result;
        }

        public double getPartsAverageCountMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            double vcResult = targetComponent.Statistics.PartsAverageCount;
            result = vcResult;
            return result;
        }

        public double getIdlePercentageMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            var value = targetComponent.Statistics.GetProperty("IdlePercentage").Value;
            double vcResult = Convert.ToDouble(value);
            result = vcResult;
            return result;
        }

        public double getCompletionTimePropertyMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double CompletionTime = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            var value = targetComponent.GetProperty("CompletionTime").Value;
            double vcResult2 = Convert.ToDouble(value);
            CompletionTime = vcResult2;
            return CompletionTime;
        }

        public double getBusyPercentageMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            var value = targetComponent.Statistics.GetProperty("BusyPercentage").Value;
            double vcResult = Convert.ToDouble(value);
            result = vcResult;
            return result;
        }

        public double getBlockedPercentageMachine(ISimWorld simWorld, IApplication app, string machineList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == machineList)
                {
                    targetComponent = searchComponent;
                }
            }

            var value = targetComponent.Statistics.GetProperty("BlockedPercentage").Value;
            double vcResult = Convert.ToDouble(value);
            result = vcResult;
            return result;
        }

        public double getThroughputForEachMHE(ISimWorld simWorld, string MheID)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            if (MheID.StartsWith("SHUTTLE"))
            {
                foreach (ISimComponent searchComponent in simWorld.Components)
                {
                    if (searchComponent.Name == MheID)
                    {
                        targetComponent = searchComponent;
                    }
                }

                result = targetComponent.Statistics.PartsEntered;
                return result;
            }
            else
            {
                foreach (ISimComponent searchComponent in simWorld.Components)
                {
                    if (searchComponent.Name == MheID)
                    {
                        targetComponent = searchComponent;
                    }
                }

                result = targetComponent.Statistics.PartsExited;
                return result;
            }
        }

        public double getIdlePercentageMHE(ISimWorld simWorld, IApplication app, string MHEList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == MHEList)
                {
                    targetComponent = searchComponent;
                }
            }

            var value = targetComponent.Statistics.GetProperty("IdlePercentage").Value;
            double vcResult = Convert.ToDouble(value);
            result = vcResult;
            return result;
        }

        public double getBusyPercentageMHE(ISimWorld simWorld, IApplication app, string MHEList)
        {
            double result = 0;
            ISimComponent targetComponent = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == MHEList)
                {
                    targetComponent = searchComponent;
                }
            }

            var value = targetComponent.Statistics.GetProperty("BusyPercentage").Value;
            double vcResult = Convert.ToDouble(value);
            result = vcResult;
            return result;
        }

        public double getCompletionTimePropertyShuttle(ISimWorld simWorld, IApplication app, string ShuttleConveyorOut, string ShuttleConveyorIn)
        {
            double CompletionTime = 0;
            ISimComponent targetComponent1 = null;
            ISimComponent targetComponent2 = null;
            foreach (ISimComponent searchComponent in simWorld.Components)
            {
                if (searchComponent.Name == ShuttleConveyorOut)
                {
                    targetComponent1 = searchComponent;
                }

                if (searchComponent.Name == ShuttleConveyorIn)
                {
                    targetComponent2 = searchComponent;
                }
            }

            var value1 = targetComponent1.GetProperty("CompletionTime").Value;
            var value2 = targetComponent2.GetProperty("CompletionTime").Value;
            double vcResult = Math.Max(Convert.ToDouble(value1), Convert.ToDouble(value2));
            CompletionTime = vcResult;
            return CompletionTime;
        }

        private void AppendComponentCompletionHistory(ISimComponent component, JArray destination, HashSet<string> keys)
        {
            if (component == null)
                return;
            IProperty property = component.GetProperty("CompletionHistoryJson");
            if (property == null || string.IsNullOrWhiteSpace(Convert.ToString(property.Value)))
                return;
            JArray items;
            try
            {
                items = JArray.Parse(Convert.ToString(property.Value));
            }
            catch
            {
                return;
            }

            foreach (JObject source in items.OfType<JObject>())
            {
                JObject item = (JObject)source.DeepClone();
                if (item["Resource"] == null)
                    item["Resource"] = component.Name;
                bool isLifterATerminal = component.Name == "Terminal Conveyor #1" || component.Name == "Terminal Conveyor #2" || component.Name == "Terminal Conveyor #3" || component.Name == "Terminal Conveyor #4";
                bool isLifterBTerminal = component.Name == "Terminal Conveyor #5" || component.Name == "Terminal Conveyor #6" || component.Name == "Terminal Conveyor #7" || component.Name == "Terminal Conveyor #8";
                if (isLifterATerminal)
                {
                    item["Resource"] = "LIFTER_A_001";
                    item["ResourceType"] = "LIFTER";
                }
                else if (isLifterBTerminal)
                {
                    item["Resource"] = "LIFTER_B_001";
                    item["ResourceType"] = "LIFTER";
                }
                else if (item["ResourceType"] == null)
                    item["ResourceType"] = component.Name.StartsWith("CNC_", StringComparison.OrdinalIgnoreCase) ? "CNC" : "MHE";
                if (item["SourceComponent"] == null)
                    item["SourceComponent"] = component.Name;
                double completion = item.Value<double?>("CompletionTimeSeconds") ?? 0;
                if (item["CompletionTimeFormatted"] == null)
                    item["CompletionTimeFormatted"] = FormatDuration(completion);
                if (item["RecordedAt"] == null)
                    item["RecordedAt"] = "";
                NormalizeCompletionHistory(new JArray(item));
                string key = CompletionHistoryKey(item);
                if (keys.Add(key))
                    destination.Add(item);
                else
                {
                    var existing = destination.OfType<JObject>().FirstOrDefault(e => CompletionHistoryKey(e) == key);
                    if (existing != null && existing.Value<double?>("StartTimeSeconds") == null && item.Value<double?>("StartTimeSeconds") != null)
                    {
                        existing["StartTimeSeconds"] = item["StartTimeSeconds"].DeepClone();
                        existing["TimingSource"] = item["TimingSource"].DeepClone();
                    }
                }
            }
        }

        private string CompletionHistoryKey(JObject item)
        {
            return string.Join("|", new string[] { Convert.ToString(item["Resource"]), Convert.ToString(item["Material"]), Convert.ToString(item["Action"]), Convert.ToString(item["Location"]), Convert.ToString(item["SourceComponent"]), Convert.ToString(item["CompletionTimeSeconds"], CultureInfo.InvariantCulture) });
        }
    }

    public sealed class CompletionTimeLogger : IDisposable
    {
        private readonly object syncRoot = new object ();
        private readonly List<Subscription> subscriptions = new List<Subscription>();
        private readonly Dictionary<string, double> lastValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> completedEventKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> lastAmrEventKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly JArray history = new JArray();
        private string logPath;
        private sealed class Subscription
        {
            public IPropertyContainer Container;
            public EventHandler<PropertyContainerChangedEventArgs> Handler;
        }

        public void Start(ISimWorld simWorld, string[] cncList, string[] mheList, string[] shuttleConveyors, string outputDirectory, bool install = true)
        {
            Directory.CreateDirectory(outputDirectory);
            logPath = Path.Combine(outputDirectory, "CompletionTimeLog.jsonl");
            File.WriteAllText(logPath, string.Empty, Encoding.UTF8);
            if (install)
                ReportInstrumentation.InstallAmr(simWorld, mheList);
            foreach (string id in cncList)
            {
                if (install)
                    ReportInstrumentation.Install(simWorld.FindComponent(id));
                SubscribeComponent(simWorld.FindComponent(id), id, "CNC", id);
            }

            foreach (string id in shuttleConveyors.Concat(new[] { "ASRS In" }).Concat(Enumerable.Range(1, 8).Select(i => "Terminal Conveyor #" + i)).Distinct())
                if (install)
                    ReportInstrumentation.Install(simWorld.FindComponent(id));
            int shuttleIndex = 0;
            foreach (string id in mheList)
            {
                if (id.StartsWith("AMR", StringComparison.OrdinalIgnoreCase))
                {
                    SubscribeAmrComponent(simWorld.FindComponent(id), id);
                }
                else if (id.StartsWith("SHUTTLE", StringComparison.OrdinalIgnoreCase) && shuttleIndex * 2 + 1 < shuttleConveyors.Length)
                {
                    SubscribeComponent(simWorld.FindComponent(shuttleConveyors[shuttleIndex * 2]), id, "MHE", shuttleConveyors[shuttleIndex * 2]);
                    SubscribeComponent(simWorld.FindComponent(shuttleConveyors[shuttleIndex * 2 + 1]), id, "MHE", shuttleConveyors[shuttleIndex * 2 + 1]);
                    shuttleIndex++;
                }
                else
                {
                    SubscribeComponent(simWorld.FindComponent(id), id, "MHE", id);
                }
            }

            SubscribeComponent(simWorld.FindComponent("ASRS In"), "ASRS In", "ASRS", "ASRS In");
        }

        private void SubscribeAmrComponent(ISimComponent component, string resource)
        {
            if (component == null || component.GetProperty("LastCompletionTime") == null)
                return;
            EventHandler<PropertyContainerChangedEventArgs> handler = delegate (object sender, PropertyContainerChangedEventArgs args)
            {
                if (args == null || args.Property == null || !string.Equals(args.Property.Name, "LastCompletionTime", StringComparison.OrdinalIgnoreCase))
                    return;
                double completion;
                try
                {
                    completion = Convert.ToDouble(args.Property.Value);
                }
                catch
                {
                    return;
                }

                if (completion < 0)
                    return;
                RecordAmr(resource, PropertyText(component, "LastCompletedMaterial"), PropertyText(component, "LastCompletedAction"), PropertyText(component, "LastCompletedLocation"), completion);
            };
            component.PropertyContainerChanged += handler;
            subscriptions.Add(new Subscription { Container = component, Handler = handler });
        }

        private string PropertyText(ISimComponent component, string name)
        {
            IProperty property = component.GetProperty(name);
            return property == null ? "" : Convert.ToString(property.Value, CultureInfo.InvariantCulture);
        }

        public JArray Snapshot()
        {
            lock (syncRoot)
                return (JArray)history.DeepClone();
        }

        private void SubscribeComponent(ISimComponent component, string resource, string resourceType, string sourceComponent)
        {
            if (component == null || component.GetProperty("CompletionTime") == null)
                return;
            EventHandler<PropertyContainerChangedEventArgs> handler = delegate (object sender, PropertyContainerChangedEventArgs args)
            {
                if (args == null || args.Property == null || !string.Equals(args.Property.Name, "CompletionTime", StringComparison.OrdinalIgnoreCase))
                    return;
                double completion;
                try
                {
                    completion = Convert.ToDouble(args.Property.Value);
                }
                catch
                {
                    return;
                }

                Record(resource, resourceType, sourceComponent, completion, PropertyText(component, "LastCompletedMaterial"), PropertyText(component, "ReportStartTime"), PropertyText(component, "ReportEndTime"));
            };
            component.PropertyContainerChanged += handler;
            subscriptions.Add(new Subscription { Container = component, Handler = handler });
        }

        private void Record(string resource, string resourceType, string sourceComponent, double completion, string material, string startText, string endText = null)
        {
            if (completion <= 0)
                return;
            if (sourceComponent == "Terminal Conveyor #1" || sourceComponent == "Terminal Conveyor #2" || sourceComponent == "Terminal Conveyor #3" || sourceComponent == "Terminal Conveyor #4")
            {
                resource = "LIFTER_A_001";
                resourceType = "LIFTER";
            }
            else if (sourceComponent == "Terminal Conveyor #5" || sourceComponent == "Terminal Conveyor #6" || sourceComponent == "Terminal Conveyor #7" || sourceComponent == "Terminal Conveyor #8")
            {
                resource = "LIFTER_B_001";
                resourceType = "LIFTER";
            }

            lock (syncRoot)
            {
                string sourceKey = resource + "|" + sourceComponent;
                double previous;
                if (lastValues.TryGetValue(sourceKey, out previous) && previous == completion)
                    return;
                lastValues[sourceKey] = completion;
                double start, end;
                bool hasStart = double.TryParse(startText, NumberStyles.Float, CultureInfo.InvariantCulture, out start) && start >= 0 && start <= completion;
                if (!(hasStart && double.TryParse(endText, NumberStyles.Float, CultureInfo.InvariantCulture, out end) && end >= start && end <= completion))
                    end = completion;
                JObject item = new JObject
                {
                    ["Resource"] = resource,
                    ["ResourceType"] = resourceType,
                    ["Material"] = material,
                    ["ProductType"] = material,
                    ["SourceComponent"] = sourceComponent,
                    ["CompletionTimeSeconds"] = end,
                    ["CompletionTimeFormatted"] = FormatCompletion(end),
                    ["HandoffTimeSeconds"] = completion,
                    ["BlockedSeconds"] = completion - end,
                    ["RecordedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                };
                if (hasStart)
                {
                    item["StartTimeSeconds"] = start;
                    item["TimingSource"] = "Processing";
                }

                history.Add(item);
                File.AppendAllText(logPath, item.ToString(Formatting.None) + Environment.NewLine, Encoding.UTF8);
            }
        }

        private void RecordAmr(string resource, string material, string action, string location, double completion)
        {
            if (completion < 0)
                return;
            string eventKey = string.Join("|", new string[] { resource ?? "", material ?? "", action ?? "", location ?? "", completion.ToString("R", CultureInfo.InvariantCulture) });
            lock (syncRoot)
            {
                string previousEventKey;
                if (lastAmrEventKeys.TryGetValue(resource, out previousEventKey) && previousEventKey == eventKey)
                    return;
                if (!completedEventKeys.Add(eventKey))
                    return;
                lastAmrEventKeys[resource] = eventKey;
                JObject item = new JObject
                {
                    ["Resource"] = resource,
                    ["ResourceType"] = "AMR",
                    ["SourceComponent"] = resource,
                    ["Material"] = material,
                    ["Action"] = action,
                    ["Location"] = location,
                    ["CompletionTimeSeconds"] = completion,
                    ["CompletionTimeFormatted"] = FormatCompletion(completion),
                    ["RecordedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                };
                history.Add(item);
                File.AppendAllText(logPath, item.ToString(Formatting.None) + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static string FormatCompletion(double seconds)
        {
            TimeSpan value = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (int)value.TotalHours, value.Minutes, value.Seconds);
        }

        public void Dispose()
        {
            foreach (Subscription subscription in subscriptions)
                subscription.Container.PropertyContainerChanged -= subscription.Handler;
            subscriptions.Clear();
        }
    }
}

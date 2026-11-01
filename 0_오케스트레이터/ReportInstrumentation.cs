using System;
using System.Linq;
using VisualComponents.Create3D;

namespace DT_KETI
{
    internal static class ReportInstrumentation
    {
        private const string Marker = "Report: capture product";
        private const string EndMarker = "Report: capture end";
        public static void Install(ISimComponent component)
        {
            if (component == null || component.GetProperty("CompletionTime") == null)
                return;
            var executor = component.FindBehavior("ProcessExecutor") as IProcessExecutor;
            if (executor == null || executor.ActiveProgram == null)
                return;
            Ensure(component, typeof(string), "LastCompletedMaterial", "");
            Ensure(component, typeof(double), "ReportStartTime", -1.0);
            Ensure(component, typeof(double), "ReportEndTime", -1.0);
            foreach (var routine in executor.ActiveProgram.Routines)
                Capture(routine);
            var logger = component.FindBehavior("CompletionHistoryLogger") as IPythonScript;
            if (logger != null)
                logger.Script = HistoryScript;
            component.Rebuild();
        }

        private static void Ensure(IPropertyContainer component, Type type, string name, object value)
        {
            if (component.GetProperty(name) == null)
                component.CreateProperty(type, PropertyConstraintType.NotSpecified, name).Value = value;
        }

        public static void InstallAmr(ISimWorld world, string[] resources)
        {
            foreach (string id in resources.Where(id => id.StartsWith("AMR", StringComparison.OrdinalIgnoreCase)))
            {
                var amr = world.FindComponent(id);
                if (amr == null)
                    continue;
                Ensure(amr, typeof(string), "CompletionHistoryJson", "[]");
                amr.GetProperty("CompletionHistoryJson").Value = "[]";
            }

            var controller = world.FindComponent("AMR_Transport_Controller");
            var logic = controller == null ? null : controller.FindBehavior("TaskLogic") as IPythonScript;
            if (logic == null || string.IsNullOrEmpty(logic.Script) || logic.Script.Contains("# REPORT AMR HISTORY"))
                return;
            const string claim = "  PENDING_SEQUENCE_CLAIMS[plan_idx] = resource.component.Name";
            const string complete = "  publish_amr_completion(resource, item, action)";
            if (!logic.Script.Contains(claim) || !logic.Script.Contains(complete))
            {
                PipelineConsole.Log("AMR report interval hooks unavailable; recording completion events only.");
                return;
            }

            logic.Script = AmrHistoryScript + logic.Script.Replace(claim, claim + "\n  REPORT_AMR_STARTS[plan_idx] = sim.SimTime").Replace(complete, "  report_amr_event(resource, item, action, completed_idx)\n" + complete).Replace("def OnStart():", "def OnStart():\n  REPORT_AMR_STARTS.clear()");
            controller.Rebuild();
        }

        private const string AmrHistoryScript = @"# REPORT AMR HISTORY
REPORT_AMR_STARTS = {}
def report_amr_event(resource, item, action, index):
  import json
  component = resource.component
  history_p = component.getProperty('CompletionHistoryJson')
  if not history_p:
    history_p = component.createProperty(VC_STRING, 'CompletionHistoryJson')
    history_p.Value = '[]'
  start = REPORT_AMR_STARTS.pop(index, None)
  if action == 'loading':
    pair = find_unloading_pair_index(get_plan(), index)
    if pair >= 0: REPORT_AMR_STARTS[pair] = sim.SimTime
  history = json.loads(history_p.Value or '[]')
  history.append({'Resource': component.Name, 'ResourceType': 'AMR',
    'SourceComponent': component.Name, 'Material': str(item.get('material', '')),
    'Action': str(action), 'Location': str(item.get('location', '')),
    'StartTimeSeconds': start, 'CompletionTimeSeconds': sim.SimTime,
    'TimingSource': ('DispatchToLoading' if action == 'loading' else 'LoadingToUnloading') if start is not None else 'CompletionOnly'})
  history_p.Value = json.dumps(history)

";
        private static void Capture(IStatementScope scope)
        {
            foreach (var statement in scope.Statements.ToArray())
            {
                var branch = statement as IProcessIfStatement;
                if (branch != null)
                {
                    Capture(branch.ThenScope);
                    Capture(branch.ElseScope);
                }

                var loop = statement as IProcessWhileStatement;
                if (loop != null)
                    Capture(loop.Scope);
                if (statement is ITransportOutStatement)
                {
                    int at = scope.Statements.IndexOf(statement);
                    if (at > 0 && scope.Statements[at - 1].Name == EndMarker)
                        continue;
                    var end = scope.AddStatement<ISetPropertyStatement>(at);
                    end.Name = EndMarker;
                    end.TargetPropertyName = "ReportEndTime";
                    end.TargetValueExpression = "SimTime()";
                    continue;
                }

                var incoming = statement as ITransportInStatement;
                var created = statement as ICreateProductStatement;
                string variable = incoming != null ? incoming.ProductVariableName : created != null ? created.ProductVariableName : null;
                if (string.IsNullOrWhiteSpace(variable))
                    continue;
                int index = scope.Statements.IndexOf(statement) + 1;
                if (index < scope.StatementCount && scope.Statements[index].Name == Marker)
                    continue;
                var material = scope.AddStatement<ISetPropertyStatement>(index);
                material.Name = Marker;
                material.TargetPropertyName = "LastCompletedMaterial";
                material.TargetValueExpression = variable + ".ProductType.Name";
                var start = scope.AddStatement<ISetPropertyStatement>(index + 1);
                start.Name = "Report: capture start";
                start.TargetPropertyName = "ReportStartTime";
                start.TargetValueExpression = "SimTime()";
            }
        }

        private const string HistoryScript = @"from vcScript import *
import json
comp = getComponent()
completion_p = comp.getProperty('CompletionTime')
history_p = comp.getProperty('CompletionHistoryJson')
if not history_p:
  history_p = comp.createProperty(VC_STRING, 'CompletionHistoryJson')
def completion_time_changed(prop):
  completion = float(prop.Value)
  if completion <= 0: return
  material = str(comp.getProperty('LastCompletedMaterial').Value or '')
  start = float(comp.getProperty('ReportStartTime').Value)
  end_p = comp.getProperty('ReportEndTime')
  end = float(end_p.Value) if end_p else -1.0
  has_start = 0 <= start <= completion
  # Bar ends when processing ends; time blocked in TransportOut is kept separately.
  if not (has_start and start <= end <= completion): end = completion
  item = {'Resource': comp.Name, 'ProductType': material, 'Material': material,
          'CompletionTimeSeconds': end, 'HandoffTimeSeconds': completion,
          'BlockedSeconds': completion - end,
          'StartTimeSeconds': start if has_start else None,
          'TimingSource': 'Processing' if has_start else 'CompletionOnly'}
  history = json.loads(history_p.Value or '[]')
  history.append(item)
  history_p.Value = json.dumps(history)
def OnStart():
  history_p.Value = '[]'
  comp.getProperty('LastCompletedMaterial').Value = ''
  comp.getProperty('ReportStartTime').Value = -1.0
  if comp.getProperty('ReportEndTime'): comp.getProperty('ReportEndTime').Value = -1.0
  completion_p.OnChanged = completion_time_changed
def OnReset():
  history_p.Value = '[]'
";
    }
}

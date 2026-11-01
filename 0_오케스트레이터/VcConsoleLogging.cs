using System.IO;
using System.Linq;
using Newtonsoft.Json;
using VisualComponents.Create3D;

namespace DT_KETI
{
    internal static class VcConsoleLogging
    {
        public static void Install(ISimWorld world)
        {
            var central = world.FindComponent("Central Controller");
            if (central == null)
                return;
            var script = central.Behaviors.OfType<IPythonInstance>().FirstOrDefault(s => s.Script.Contains("def choose_and_execute(state,event_name):"));
            if (script == null || script.Script.Contains("# VC ESSENTIAL CONSOLE"))
                return;
            string runtime;
            using (var stream = typeof(VcConsoleLogging).Assembly.GetManifestResourceStream("DT_KETI.VcConsoleLogging.py"))
            using (var reader = new StreamReader(stream))
                runtime = reader.ReadToEnd().Replace("\r\n", "\n").Replace("\r", "\n");
            script.Script = "# VC ESSENTIAL CONSOLE\nexec(" + JsonConvert.SerializeObject(runtime) + ")\n" + script.Script;
        }
    }
}

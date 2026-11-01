using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PDP_ASRS_WIP
{
    internal partial class Program
    {
        static int RunExportLayout(string scenarioPath, string? configPath)
        {
            ScenarioInstance inst = ScenarioLoader.Load(scenarioPath, configPath);
            var layout = inst.Layout;
            var names = new Dictionary<string, int>
            {
                ["asrs_out"] = layout.AsrsOut,
                ["asrs_in"] = layout.AsrsIn
            };
            for (int k = 0; k < inst.n; k++)
            {
                names["in" + k] = layout.In(k);
                names["out" + k] = layout.Out(k);
            }

            var travel = new Dictionary<string, Dictionary<string, double[]>>();
            foreach (var a in names)
            {
                var row = new Dictionary<string, double[]>();
                foreach (var b in names)
                {
                    var per = new double[inst.r];
                    for (int h = 0; h < inst.r; h++)
                    {
                        per[h] = Math.Round(inst.t_klh[a.Value][b.Value][h], 3);
                    }

                    row[b.Key] = per;
                }

                travel[a.Key] = row;
            }

            string dir = Path.GetDirectoryName(Path.GetFullPath(scenarioPath)) ?? ".";
            string outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(scenarioPath) + "_LAYOUT.json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(new { n = inst.n, r = inst.r, travel }));
            Console.WriteLine("LAYOUT written: " + outPath);
            return 0;
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DT_KETI
{
    public static class PipelineConsole
    {
        private static readonly object Gate = new object ();
        private static string _logPath;
        private static bool _opened;
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetConsoleTitle(string title);
        public static void Open(string reportDirectory)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(reportDirectory);
                _logPath = Path.Combine(reportDirectory, "pipeline.log");
                if (!_opened)
                {
                    bool allocated = AllocConsole();
                    SetConsoleTitle("DT_KETI Pipeline Progress");
                    if (allocated)
                    {
                        Stream standardOutput = Console.OpenStandardOutput();
                        var writer = new StreamWriter(standardOutput, new UTF8Encoding(false))
                        {
                            AutoFlush = true
                        };
                        Console.SetOut(TextWriter.Synchronized(writer));
                        Stream standardError = Console.OpenStandardError();
                        var errorWriter = new StreamWriter(standardError, new UTF8Encoding(false))
                        {
                            AutoFlush = true
                        };
                        Console.SetError(TextWriter.Synchronized(errorWriter));
                    }

                    _opened = true;
                }
            }

            Progress("Pipeline started.");
            Log("Progress log: " + _logPath);
        }

        public static void Progress(string message)
        {
            lock (Gate)
            {
                try
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
                }
                catch
                {
                }

                Log(message);
            }
        }

        public static void ReportStage(string stage)
        {
            switch (stage)
            {
                case "InitialPlanning":
                    Progress("Optimizing the initial schedule...");
                    break;
                case "TrainingEmbeddedGnn":
                    Progress("Training the scheduling model...");
                    break;
                case "PreparingAbnormal":
                    Progress("Preparing recovery simulation...");
                    break;
                case "Synchronizing":
                    Progress("Loading the simulation model...");
                    break;
                case "GnnReady":
                    Progress("Scheduling model ready.");
                    break;
                case "SimulationRunning":
                    Progress("Running simulation...");
                    break;
                case "GeneratingReport":
                    Progress("Generating results...");
                    break;
                case "ExportingAbnormalP4R":
                    Progress("Exporting the recovery state...");
                    break;
                case "Rescheduling":
                    Progress("Optimizing the recovery schedule...");
                    break;
                case "AwaitingRuleSnapshotExporter":
                    Progress("Paused: recovery state is incomplete.");
                    break;
                case "Completed":
                    Progress("Pipeline completed.");
                    break;
                case "Failed":
                    Progress("Pipeline failed. See pipeline.log for details.");
                    break;
            }
        }

        public static void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            lock (Gate)
            {
                Debug.WriteLine(line);
                if (!string.IsNullOrWhiteSpace(_logPath))
                {
                    try
                    {
                        File.AppendAllText(_logPath, line + Environment.NewLine);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}

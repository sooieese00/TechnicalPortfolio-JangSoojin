using System;
using System.Collections.Generic;
using System.IO;

namespace PDP_ASRS_WIP
{
    public static class RunSettings
    {
        public static RunTarget Target = RunTarget.Scenario;
        public static string ScenarioFile = "scenario_Y01_P4R_INPUT.json";
        public static string Mode = "vns";
        public static string InitialPolicy = "edd";
        public static int Seed = 1;
        public static int NoImprovementLimit = 200;
        public static int Evaluations = 0;
        public static int MaxParallelism = 0;
        public static string LocalSearch = "vnd";
        public static string? ConfigFile = null;
        public static int VerificationInstance = 8;
        public static string? ReplayExportDir = null;
        public enum RunTarget
        {
            Scenario,
            Verification
        }

        public static int EffectiveParallelism
        {
            get
            {
                int configured = EnvInt("PDP_PARALLEL", MaxParallelism);
                return configured > 0 ? configured : Environment.ProcessorCount;
            }
        }

        public static int EffectiveNoImprovementLimit => EnvInt("PDP_NO_IMPROVE", NoImprovementLimit);

        public static string EffectiveLocalSearch
        {
            get
            {
                string? text = Environment.GetEnvironmentVariable("PDP_LOCAL_SEARCH");
                return string.IsNullOrWhiteSpace(text) ? LocalSearch : text.Trim().ToLowerInvariant();
            }
        }

        static int EnvInt(string name, int fallback)
        {
            string? text = Environment.GetEnvironmentVariable(name);
            return int.TryParse(text, out int value) ? value : fallback;
        }

        public static string[] BuildArguments()
        {
            Console.WriteLine("실행 설정: RunSettings.cs (명령줄 인자 없음)");
            if (!string.IsNullOrWhiteSpace(ReplayExportDir))
            {
                Directory.CreateDirectory(ReplayExportDir);
                Environment.SetEnvironmentVariable("PDP_REPLAY_DIR", ReplayExportDir);
                Console.WriteLine("  리플레이 내보내기: " + ReplayExportDir);
            }

            if (Target == RunTarget.Verification)
            {
                Console.WriteLine("  검증 인스턴스 " + VerificationInstance);
                return new[]
                {
                    Convert.ToString(VerificationInstance)
                };
            }

            string scenarioPath = ResolveInputPath(ScenarioFile);
            Console.WriteLine("  시나리오: " + scenarioPath);
            Console.WriteLine("  모드: " + Mode + ", 시드: " + Seed + ", 평가 횟수: " + Evaluations);
            var args = new List<string>
            {
                scenarioPath,
                Mode,
                Convert.ToString(Seed),
                Convert.ToString(Evaluations)
            };
            if (!string.IsNullOrWhiteSpace(ConfigFile))
            {
                args.Add(ResolveInputPath(ConfigFile));
            }

            return args.ToArray();
        }

        public static string ResolveInputPath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            var searched = new List<string>();
            string fromCurrent = Path.GetFullPath(path);
            searched.Add(fromCurrent);
            if (File.Exists(fromCurrent))
            {
                return fromCurrent;
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int depth = 0; depth < 10 && directory != null; depth++)
            {
                string candidate = Path.Combine(directory.FullName, path);
                searched.Add(candidate);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("'" + path + "' 을(를) 찾지 못했습니다. RunSettings.ScenarioFile 에 전체 경로를 적거나, " + "파일을 프로젝트 폴더에 두세요. 찾아본 위치:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", searched));
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PDP_ASRS_WIP
{
    partial class Program
    {
        static readonly int[] Seeds =
        {
            1,
            2,
            3
        };
        static string? FindPackageRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "2_규칙모델")) && Directory.Exists(Path.Combine(dir.FullName, "3_시나리오")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }

        static string? FindScenario(string pkg)
        {
            string dir = Path.Combine(pkg, "3_시나리오");
            string want = (RunSettings.ScenarioFile ?? string.Empty).Trim();
            if (want.Length > 0)
            {
                if (Path.IsPathRooted(want) && File.Exists(want))
                {
                    return Path.GetFullPath(want);
                }

                foreach (string baseDir in new[]
                {
                    dir,
                    pkg,
                    AppContext.BaseDirectory
                }

                )
                {
                    string cand = Path.Combine(baseDir, want);
                    if (File.Exists(cand))
                    {
                        return Path.GetFullPath(cand);
                    }
                }

                if (Directory.Exists(dir))
                {
                    string? hit = Directory.GetFiles(dir, Path.GetFileName(want), SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null)
                    {
                        return Path.GetFullPath(hit);
                    }
                }

                Console.WriteLine($"RunSettings.ScenarioFile 을 못 찾았다: {want}");
                Console.WriteLine($"  찾아본 곳: {dir}");
                return null;
            }

            if (!Directory.Exists(dir))
            {
                return null;
            }

            return Directory.GetFiles(dir, "*.json").Where(f => !f.Contains("_OUTPUT_") && !f.Contains("VCRAW") && !f.Contains("LAYOUT")).OrderBy(f => f).FirstOrDefault();
        }

        static string? FindPython()
        {
            foreach (string cand in new[]
            {
                "python",
                "python3",
                "py"
            }

            )
            {
                try
                {
                    var psi = new ProcessStartInfo(cand, "--version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    using var p = Process.Start(psi);
                    if (p == null)
                    {
                        continue;
                    }

                    p.WaitForExit(15000);
                    if (p.ExitCode == 0)
                    {
                        return cand;
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        static int RunAllPipeline()
        {
            string? pkg = FindPackageRoot();
            if (pkg == null)
            {
                Console.WriteLine("패키지 구조(2_규칙모델 / 3_시나리오)를 못 찾았다. " + "시나리오 경로를 인자로 주고 실행하라: PDP_ASRS_WIP.exe <시나리오.json> hyb 1 0");
                return 1;
            }

            string? scn = FindScenario(pkg);
            if (scn == null)
            {
                Console.WriteLine("3_시나리오 에 P4R 입력이 없다.");
                return 1;
            }

            Console.WriteLine("=== 전 과정 자동 실행 ===");
            Console.WriteLine("시나리오: " + Path.GetFileName(scn));
            Console.WriteLine();
            foreach (int seed in Seeds)
            {
                Console.WriteLine($"--- HYB seed {seed} ---");
                var sw = Stopwatch.StartNew();
                int rc;
                try
                {
                    rc = RunScenario(scn, "hyb", seed, 0, null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  실패: " + ex.Message);
                    rc = 1;
                }

                sw.Stop();
                Console.WriteLine($"--- seed {seed} 종료 (rc={rc}, {sw.Elapsed.TotalSeconds:F0}s) ---");
                Console.WriteLine();
            }

            if (Environment.GetEnvironmentVariable("PDP_TRAIN") == "0")
            {
                Console.WriteLine("PDP_TRAIN=0 이라 학습은 건너뛴다 (메타휴리스틱까지만).");
                return 0;
            }

            string script = Path.Combine(pkg, "2_규칙모델", "run_pipeline.py");
            if (!File.Exists(script))
            {
                Console.WriteLine("run_pipeline.py 가 없다: " + script);
                return 1;
            }

            string? py = FindPython();
            if (py == null)
            {
                Console.WriteLine("파이썬을 찾지 못했다. 메타휴리스틱과 학습데이터는 만들어졌으니,");
                Console.WriteLine("파이썬을 설치한 뒤 아래를 직접 실행하면 모델이 나온다:");
                Console.WriteLine("  cd \"" + Path.Combine(pkg, "2_규칙모델") + "\"");
                Console.WriteLine("  pip install torch");
                Console.WriteLine("  python run_pipeline.py --skip-meta");
                return 0;
            }

            Console.WriteLine("--- 규칙모델 학습 (5겹 교차검증) ---");
            var psi2 = new ProcessStartInfo(py)
            {
                WorkingDirectory = Path.Combine(pkg, "2_규칙모델"),
                UseShellExecute = false,
            };
            psi2.ArgumentList.Add(script);
            psi2.ArgumentList.Add("--skip-meta");
            psi2.ArgumentList.Add("--scenario");
            psi2.ArgumentList.Add(scn);
            psi2.Environment["PYTHONIOENCODING"] = "utf-8";
            psi2.Environment["PYTHONUNBUFFERED"] = "1";
            int pyRc;
            try
            {
                using var proc = Process.Start(psi2);
                if (proc == null)
                {
                    Console.WriteLine("학습 프로세스를 띄우지 못했다.");
                    return 1;
                }

                proc.WaitForExit();
                pyRc = proc.ExitCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine("학습 실행 실패: " + ex.Message);
                Console.WriteLine("torch 가 없으면 `pip install torch` 후 다시 실행하라.");
                return 1;
            }

            if (pyRc != 0)
            {
                Console.WriteLine($"학습이 실패했다 (exit {pyRc}). torch 설치 여부를 확인하라.");
                return pyRc;
            }

            string made = Path.Combine(pkg, "2_규칙모델", "model.pt");
            if (File.Exists(made))
            {
                string dst = Path.Combine(AppContext.BaseDirectory, "model.pt");
                File.Copy(made, dst, overwrite: true);
                Console.WriteLine();
                Console.WriteLine("=== 완료 ===");
                Console.WriteLine("모델: " + dst);
                Console.WriteLine("      " + made + " (원본)");
                string res = Path.Combine(pkg, "2_규칙모델", "pipeline_result.json");
                if (File.Exists(res))
                {
                    Console.WriteLine("요약: " + res);
                }
            }
            else
            {
                Console.WriteLine("model.pt 가 만들어지지 않았다: " + made);
                return 1;
            }

            return 0;
        }
    }
}

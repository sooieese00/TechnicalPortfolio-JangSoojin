
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent

def main():
    env = dict(os.environ, PYTHONIOENCODING="utf-8", OMP_NUM_THREADS="1", MKL_NUM_THREADS="1")
    (ROOT / "results").mkdir(exist_ok=True)
    commands = [
        ["2_규칙모델/train.py", "--data", "data", "--out", "results/model.pt", "--cpu",
         "--seed", "0", "--epochs", "20", "--select", "1", "--metrics", "results/metrics.json"],
        ["2_규칙모델/dispatch.py", "--ckpt", "results/model.pt", "--jsonl",
         "data/replay_scenario04_s1_hyb.jsonl", "--limit", "3"],
        ["src/visualize.py"],
    ]
    for args, log in zip(commands, ["training.log", "predictions.txt", "visualization.log"]):
        result = subprocess.run([sys.executable, *args], cwd=ROOT, env=env,
                                capture_output=True, text=True, encoding="utf-8", errors="replace")
        (ROOT / "results" / log).write_text(result.stdout + result.stderr, encoding="utf-8")
        print(result.stdout, end="")
        if result.returncode:
            print(result.stderr, file=sys.stderr)
            raise SystemExit(result.returncode)

if __name__ == "__main__":
    main()

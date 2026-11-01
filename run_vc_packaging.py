
from pathlib import Path
import os
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
TOOLS = "0_오케스트레이터/tools/"

def main():
    out = ROOT / "results/vc-package"
    out.mkdir(parents=True, exist_ok=True)
    prefix = "results/vc-package/weights"
    commands = [
        ["export_lk_gnn_weights.py", "results/model.pt", prefix],
        ["build_vc_lk_runtime.py", "--gnn-dir", "2_규칙모델", "--kernel", TOOLS + "vc_lk_gnn_runtime.py",
         "--output", "results/vc-package/runtime.py"],
        ["modify_vcmx_python_embedded_gnn.py", "4_VC모델/rule/trained-rule-optimized.vcmx",
         "results/vc-package/embedded.vcmx", prefix + ".manifest.json", prefix + ".bin",
         "data/scenario04_INSTANCE.json", "results/vc-package/runtime.py"],
        ["modify_vcmx_pending_storage_recovery.py", "results/vc-package/embedded.vcmx", "results/vc-package/trained-rule.vcmx"],
        ["validate_vcmx_python_embedded_gnn.py", "results/vc-package/trained-rule.vcmx"],
        ["create_lk_forward_fixture.py", "--gnn-dir", "2_규칙모델", "--checkpoint", "results/model.pt",
         "--instance", "data/scenario04_INSTANCE.json", "--output", "results/vc-package/fixtures.json"],
        ["verify_lk_embedded_forward.py", prefix + ".manifest.json", "results/vc-package/fixtures.json",
         "--runtime-dir", "0_오케스트레이터/tools"],
    ]
    log = []
    for script, *args in commands:
        result = subprocess.run([sys.executable, TOOLS + script, *args], cwd=ROOT,
                                env=dict(os.environ, PYTHONIOENCODING="utf-8", OMP_NUM_THREADS="1"),
                                capture_output=True, text=True, encoding="utf-8", errors="replace")
        output = result.stdout + result.stderr
        log.append(script + "\n" + output.replace(str(ROOT), "<PORTFOLIO_ROOT>"))
        print(output, end="")
        (ROOT / "results/vc-packaging.log").write_text("\n".join(log), encoding="utf-8")
        if result.returncode:
            raise SystemExit(result.returncode)

if __name__ == "__main__":
    main()

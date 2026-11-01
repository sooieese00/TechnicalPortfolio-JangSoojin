
from collections import Counter
import json
from pathlib import Path
import os
os.environ.setdefault("MPLCONFIGDIR", str(Path(__file__).resolve().parents[1] / ".cache/matplotlib"))
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

ROOT = Path(__file__).resolve().parents[1]

def main():
    metrics = json.loads((ROOT / "results/metrics.json").read_text(encoding="utf-8"))
    records = [json.loads(line) for line in
               (ROOT / "data/replay_scenario04_s1_hyb.jsonl").read_text(encoding="utf-8").splitlines()]
    decisions = [r for r in records if r.get("t") == "decision"]
    history = metrics["history"]
    plt.rcParams.update({"font.size": 10, "axes.spines.top": False, "axes.spines.right": False})
    fig, axes = plt.subplots(1, 2, figsize=(11, 4), layout="constrained")
    epochs = [h["epoch"] for h in history]
    axes[0].plot(epochs, [h["loss"] for h in history], color="#2563eb", marker=".")
    axes[0].set(xlabel="Epoch", ylabel="Bradley-Terry loss", title="Training loss")
    for key, label in [("paa", "Pairwise accuracy"), ("top1", "Top-1 accuracy"), ("mrr", "MRR")]:
        axes[1].plot(epochs, [h[key] for h in history], label=label)
    axes[1].set(xlabel="Epoch", ylabel="Validation score", ylim=(0, 1.05), title="Within-trajectory validation")
    axes[1].legend()
    fig.suptitle("GNN dispatch imitation learning | seed 0")
    fig.savefig(ROOT / "results/training.png", dpi=160)
    plt.close(fig)
    counts = Counter(d["label"]["kind"] for d in decisions)
    fig, axes = plt.subplots(1, 2, figsize=(11, 4), layout="constrained")
    axes[0].bar(list(counts), list(counts.values()), color="#0d9488")
    axes[0].set(xlabel="Selected operation type", ylabel="Decision count", title="Teacher operation distribution")
    axes[1].plot([d["step"] for d in decisions], [len(d["candidates"]) for d in decisions], color="#7c3aed")
    axes[1].set(xlabel="Decision step", ylabel="Available candidate count", title="Dispatch choice complexity")
    fig.suptitle(f"Scenario 04 simulation replay | {len(decisions)} decisions")
    fig.savefig(ROOT / "results/replay.png", dpi=160)
    plt.close(fig)
    print("Saved results/training.png and results/replay.png")

if __name__ == "__main__":
    main()

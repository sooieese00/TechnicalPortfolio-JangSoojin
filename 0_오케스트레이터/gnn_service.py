# -*- coding: utf-8 -*-

from __future__ import print_function

import argparse
import json
import os
import sys
import traceback


class Session(object):
    def __init__(self, instance_path, checkpoint):
        from dispatch import Dispatcher
        from rollout_builder import Builder, Instance
        self.instance_path = os.path.abspath(instance_path)
        self.dispatcher = Dispatcher(os.path.abspath(checkpoint), device="cpu")
        self.builder = Builder(Instance.from_json(self.instance_path))
        self.step = 0

    @staticmethod
    def key(candidate):
        return (str(candidate.get("kind", "")), int(candidate.get("job", -1)),
                str(candidate.get("res", "")))

    def decide(self, requested, state=None):
        if not requested:
            raise ValueError("choose requires at least one candidate")
        record = self.builder.emit_state(self.step)
        feasible = {self.key(c): c for c in record["candidates"]}
        mapped = []
        for candidate in requested:
            key = self.key(candidate)
            if key not in feasible:
                raise ValueError("candidate is not feasible in current graph state: {!r}".format(key))
            mapped.append(feasible[key])
        record["candidates"] = mapped
        if state:
            for name in ("now", "res_state", "caps", "ws_choice"):
                if name in state:
                    record[name] = state[name]
            for item in state.get("completed") or []:
                key = (str(item["kind"]), int(item["job"]))
                if item.get("done") is not None:
                    self.builder.exact_done[key] = float(item["done"])
        pick = self.dispatcher.decide(record)
        return pick["index"], pick["scores"]

    def commit(self, kind, job, resource, done=None):
        key = (str(kind), int(job), str(resource))
        if key not in set(self.builder.candidates()):
            raise ValueError("commit is not feasible in current graph state: {!r}".format(key))
        self.builder.commit(key[0], key[1], key[2])
        if done is not None:
            self.builder.exact_done[(key[0], key[1])] = float(done)
        self.step += 1


def emit(payload):
    sys.stdout.write("@@" + json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser()
    parser.add_argument("--gnn-dir", required=True)
    parser.add_argument("--checkpoint", required=True)
    parser.add_argument("--instance", required=True)
    args = parser.parse_args()

    gnn_dir = os.path.abspath(args.gnn_dir)
    if gnn_dir not in sys.path:
        sys.path.insert(0, gnn_dir)

    def new_session():
        return Session(args.instance, args.checkpoint)

    session = new_session()
    emit({"ok": True, "type": "ready", "model": "LkGnn", "protocol": 2})

    for raw_line in sys.stdin:
        raw_line = raw_line.strip()
        if not raw_line:
            continue
        try:
            request = json.loads(raw_line)
            request_type = request.get("type")

            if request_type == "health":
                emit({"ok": True, "type": "health", "status": "ready",
                      "step": session.step, "done": session.builder.done()})
            elif request_type == "commit":
                session.commit(request["kind"], int(request["job"]), request["res"], request.get("done"))
                emit({"ok": True, "type": "commit", "step": session.step})
            elif request_type == "choose":
                index, scores = session.decide(request.get("candidates") or [], request.get("state"))
                emit({"ok": True, "type": "choose", "index": index, "scores": scores})
            elif request_type == "reset":
                session = new_session()
                emit({"ok": True, "type": "reset", "step": 0})
            elif request_type == "close":
                emit({"ok": True, "type": "close"})
                break
            else:
                raise ValueError("Unsupported request type: {!r}".format(request_type))
        except Exception as error:
            emit({
                "ok": False,
                "type": "error",
                "message": str(error),
                "trace": traceback.format_exc(),
            })


if __name__ == "__main__":
    main()

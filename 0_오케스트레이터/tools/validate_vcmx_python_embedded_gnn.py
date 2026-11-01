import json
import re
import sys
import zipfile


path = sys.argv[1]
with zipfile.ZipFile(path, "r") as archive:
    bad = archive.testzip()
    layout = archive.read("layout.rsc").decode("utf-8")
start = layout.find('Name "Central Controller"')
match = re.search(r'Script "((?:\\.|[^"\\])*)"', layout[start:], re.S)
controller = json.loads('"' + match.group(1) + '"')
compile(controller, "Central Controller", "exec")

required = [
    "class VcLkGnn", "class Builder", "class VcLkEmbeddedSession",
    "EMBEDDED_GNN_WEIGHTS_B64=", "def choose_vcmx_embedded_gnn",
    "DispatchRule::EmbeddedGnnInstanceJSON", "dropCode", "context_features",
    "[CENTRAL VCMX GNN CHOOSE]", "[CENTRAL VCMX GNN COMMAND]",
    "VC_EMBEDDED_GNN_SESSION.commit(kind,job_id,resolved_resource)",
    "VC_EMBEDDED_GNN_SESSION.commit(kind,job_id,resolved_resource,float(sim.SimTime))",
    "# [DT RULE LOGICAL PENDING STORAGE RECOVERY V1]",
    "getattr(executor,'Processes',[])",
    "[CENTRAL PENDING STORAGE CANDIDATES]",
    "[CENTRAL PENDING STORAGE EXECUTION ERROR]",
    "'gnn_job':int(product.get('gnn_job',-1))",
    "product_states.append({'name':identity['runtime_product']",
]
forbidden = [
    "DispatchInterface::GnnRequestJSON", "consume_embedded_gnn_response",
    "import subprocess", "import socket", "import numpy", "import pandas", "import torch",
]
missing = [value for value in required if value not in controller]
present_forbidden = [value for value in forbidden if value in controller]
print("zip_test={}".format("OK" if bad is None else bad))
print("central_controller_python=OK")
print("central_controller_chars={}".format(len(controller)))
print("missing={}".format(missing))
print("forbidden={}".format(present_forbidden))
if bad is not None or missing or present_forbidden:
    raise SystemExit(1)

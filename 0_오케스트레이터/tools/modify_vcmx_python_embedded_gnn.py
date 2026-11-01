import base64
import json
import re
import sys
import zipfile


SOURCE, TARGET, MANIFEST, WEIGHTS, INSTANCE, RUNTIME = sys.argv[1:7]


def once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError("{} expected once, found {}".format(label, count))
    return text.replace(old, new, 1)


def replace_line(script, prefix, value, label):
    pattern = re.compile("^" + re.escape(prefix) + ".*$", re.M)
    if len(pattern.findall(script)) != 1:
        raise RuntimeError("{} expected once".format(label))
    return pattern.sub(lambda _: prefix + value, script, count=1)


def reembed(script, manifest, weights, instance, runtime):

    script = replace_line(script, "EMBEDDED_GNN_MANIFEST_JSON=", repr(manifest), "manifest")
    script = replace_line(script, "EMBEDDED_GNN_WEIGHTS_B64=", repr(weights), "weights")
    script = replace_line(script,
        "embedded_gnn_instance_p=prop(comp,VC_STRING,'DispatchRule::EmbeddedGnnInstanceJSON',",
        repr(instance) + ")", "default instance")
    weights_end = script.index("\n", script.index("EMBEDDED_GNN_WEIGHTS_B64=")) + 1
    session = script.index("\n\nVC_EMBEDDED_GNN_SESSION=None\n\ndef embedded_gnn_candidate(", weights_end)
    return script[:weights_end] + "\n" + runtime + script[session:]


def edit(script):
    manifest = open(MANIFEST, encoding="utf-8").read()
    weights = base64.b64encode(open(WEIGHTS, "rb").read()).decode("ascii")
    instance = open(INSTANCE, encoding="utf-8").read()
    runtime = open(RUNTIME, encoding="utf-8").read()
    if "EMBEDDED_GNN_WEIGHTS_B64=" in script:
        return reembed(script, manifest, weights, instance, runtime)
    constants = ("\n# Embedded in this Python Behavior by modify_vcmx_python_embedded_gnn.py.\n" +
                 "EMBEDDED_GNN_MANIFEST_JSON=" + repr(manifest) + "\n" +
                 "EMBEDDED_GNN_WEIGHTS_B64=" + repr(weights) + "\n")

    script = once(script,
        "operation_event_log_p=prop(comp,VC_STRING,'DispatchInterface::OperationEventLogJSON','[]')",
        "operation_event_log_p=prop(comp,VC_STRING,'DispatchInterface::OperationEventLogJSON','[]')\n\n"
        "embedded_gnn_instance_p=prop(comp,VC_STRING,'DispatchRule::EmbeddedGnnInstanceJSON'," + repr(instance) + ")\n\n"
        "embedded_gnn_ready_p=prop(comp,VC_BOOLEAN,'DispatchRule::EmbeddedGnnReady',False)\n\n"
        "embedded_gnn_elapsed_p=prop(comp,VC_REAL,'DispatchRule::EmbeddedGnnLastForwardMs',0.0)\n\n"
        "embedded_gnn_error_p=prop(comp,VC_STRING,'DispatchRule::EmbeddedGnnError','')",
        "Python embedded GNN properties")

    helper = constants + "\n" + runtime + '''

VC_EMBEDDED_GNN_SESSION=None

def embedded_gnn_candidate(candidate,index):
  if candidate.get('type','')=='A7_IDLE' or candidate.get('payload')=='REQUEST_TASK':
      return None
  kind=operation_kind(candidate)
  job=operation_job(candidate)
  resource=operation_resource(kind,candidate)
  if not kind or job<0 or not resource:
      return None
  return {'candidateIndex':int(index),'kind':kind,'job':int(job),'res':resource,
          'destination':str(candidate.get('destination','')),
          'location':candidate.get('location',candidate.get('payload')),
          'travelTime':float(candidate.get('travel_time',0.0))}

def initialize_vcmx_embedded_gnn():
  global VC_EMBEDDED_GNN_SESSION
  embedded_gnn_ready_p.Value=False
  embedded_gnn_error_p.Value=''
  try:
      VC_EMBEDDED_GNN_SESSION=vc_embedded_gnn_create(
        str(embedded_gnn_instance_p.Value or ''))
      embedded_gnn_ready_p.Value=True
      print('[CENTRAL VCMX GNN READY] runtime={} weights={} nodes={} loadMs={:.2f} time={}'.format(
        GNN_RUNTIME_VERSION,
        len(VC_EMBEDDED_GNN_SESSION.weights.tensors),
        len(VC_EMBEDDED_GNN_SESSION.builder.inst.active),
        VC_EMBEDDED_GNN_SESSION.load_seconds*1000.0,sim.SimTime))
      return True
  except Exception as exception:
      VC_EMBEDDED_GNN_SESSION=None
      embedded_gnn_error_p.Value=str(exception)
      print('[CENTRAL VCMX GNN INIT ERROR] error={} time={}'.format(exception,sim.SimTime))
      return False

def choose_vcmx_embedded_gnn(mapped,event_name):
  if VC_EMBEDDED_GNN_SESSION is None:
      raise RuntimeError('VCMX embedded GNN session is not initialized')
  choice,scores,elapsed=VC_EMBEDDED_GNN_SESSION.choose(mapped,float(sim.SimTime))
  embedded_gnn_elapsed_p.Value=elapsed*1000.0
  print('[CENTRAL VCMX GNN CHOOSE] candidates={} index={} score={:.7g} elapsedMs={:.2f} event={} time={}'.format(
    len(mapped),choice,scores[choice],elapsed*1000.0,event_name,sim.SimTime))
  return choice

'''
    script = once(script, "def choose_and_execute(state,event_name):", helper +
                  "def choose_and_execute(state,event_name):", "embed Python GNN runtime")

    marker = "  print('[CENTRAL CANDIDATES PUBLISHED] count={} event={}'.format(len(candidates),event_name))\n"
    direct = marker + '''  mapped=[]
  for candidate_index,candidate in enumerate(candidates):
      gnn_candidate=embedded_gnn_candidate(candidate,candidate_index)
      if gnn_candidate:
            mapped.append(gnn_candidate)
  if mapped:
      try:
            mapped_index=choose_vcmx_embedded_gnn(mapped,event_name)
            raw_index=int(mapped[mapped_index]['candidateIndex'])
            selected=candidates[raw_index]
            print('[CENTRAL VCMX GNN COMMAND] candidate_index={} type={} product={} source={} destination={} time={}'.format(
              raw_index,selected.get('type',''),selected.get('product_id',selected.get('product','')),
              selected.get('source',''),selected.get('destination',''),sim.SimTime))
            global CURRENT_ACTION
            CURRENT_ACTION=selected
            execute(selected)
            return
      except Exception as exception:
            embedded_gnn_error_p.Value=str(exception)
            print('[CENTRAL VCMX GNN ERROR] error={} event={} time={}'.format(exception,event_name,sim.SimTime))
            raise
  print('[CENTRAL VCMX GNN FALLBACK] no mappable operation candidate; using Rule Model event={} time={}'.format(
    event_name,sim.SimTime))
'''
    script = once(script, marker, direct, "direct Python GNN choice")

    script = once(script,
        "  if phase=='STARTED':\n      ACTIVE_GNN_OPERATIONS[key]=dict(candidate)\n  elif phase=='COMPLETED':\n      ACTIVE_GNN_OPERATIONS.pop(key,None)\n  return True",
        "  if phase=='STARTED':\n"
        "      ACTIVE_GNN_OPERATIONS[key]=dict(candidate)\n"
        "      if VC_EMBEDDED_GNN_SESSION is not None:\n"
        "            committed=VC_EMBEDDED_GNN_SESSION.commit(kind,job_id,resolved_resource)\n"
        "            print('[CENTRAL VCMX GNN COMMIT] phase=STARTED job={} kind={} resource={} committed={} time={}'.format(\n"
        "              job_id,kind,resolved_resource,committed,sim.SimTime))\n"
        "  elif phase=='COMPLETED':\n"
        "      ACTIVE_GNN_OPERATIONS.pop(key,None)\n"
        "      if VC_EMBEDDED_GNN_SESSION is not None:\n"
        "            committed=VC_EMBEDDED_GNN_SESSION.commit(kind,job_id,resolved_resource,float(sim.SimTime))\n"
        "            print('[CENTRAL VCMX GNN COMMIT] phase=COMPLETED job={} kind={} resource={} committed={} time={}'.format(\n"
        "              job_id,kind,resolved_resource,committed,sim.SimTime))\n"
        "  return True",
        "commit started and complete operation in Python session")

    script = once(script,
        "  load_gnn_job_master()\n\n  GNN_JOB_ASSIGNMENTS.clear()",
        "  load_gnn_job_master()\n\n"
        "  if not initialize_vcmx_embedded_gnn():\n"
        "      raise RuntimeError('VCMX embedded GNN initialization failed: '+str(embedded_gnn_error_p.Value))\n\n"
        "  GNN_JOB_ASSIGNMENTS.clear()",
        "initialize embedded GNN on run")

    script = once(script,
        "  global INITIAL_WIP_INITIALIZED\n\n  global RULE_RESPONSE_CONSUMED\n",
        "  global INITIAL_WIP_INITIALIZED\n\n  global RULE_RESPONSE_CONSUMED\n\n"
        "  global VC_EMBEDDED_GNN_SESSION\n",
        "reset Python GNN global")
    script = once(script,
        "  OCCUPIED=set()\n",
        "  VC_EMBEDDED_GNN_SESSION=None\n\n"
        "  embedded_gnn_ready_p.Value=False\n\n"
        "  embedded_gnn_elapsed_p.Value=0.0\n\n"
        "  embedded_gnn_error_p.Value=''\n\n"
        "  OCCUPIED=set()\n",
        "reset Python GNN state")
    return script


with zipfile.ZipFile(SOURCE, "r") as archive:
    infos = archive.infolist()
    payloads = {item.filename: archive.read(item.filename) for item in infos}
layout = payloads["layout.rsc"].decode("utf-8")
start = layout.find('Name "Central Controller"')
match = re.search(r'Script "((?:\\.|[^"\\])*)"', layout[start:], re.S)
left, right = start + match.start(1), start + match.end(1)
controller = json.loads('"' + match.group(1) + '"')
modified = edit(controller)
compile(modified, "Central Controller", "exec")
encoded = json.dumps(modified, ensure_ascii=False)[1:-1]
payloads["layout.rsc"] = (layout[:left] + encoded + layout[right:]).encode("utf-8")
with zipfile.ZipFile(TARGET, "w") as output:
    for item in infos:
        output.writestr(item, payloads[item.filename])
print("created", TARGET)
print("controller bytes", len(controller), "->", len(modified))

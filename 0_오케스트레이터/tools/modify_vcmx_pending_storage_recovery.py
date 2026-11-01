
import json
import os
import re
import sys
import zipfile


SOURCE, TARGET = sys.argv[1:3]
MARKER = "# [DT RULE LOGICAL PENDING STORAGE RECOVERY V1]"


def replace_function(script, name, next_name, replacement):
    start = script.find("def {}(".format(name))
    end = script.find("def {}(".format(next_name), start)
    if start < 0 or end < 0:
        raise RuntimeError("function boundary not found: {} -> {}".format(name, next_name))
    return script[:start] + replacement.rstrip() + "\n\n" + script[end:]


PENDING_CANDIDATES = r'''# [DT RULE LOGICAL PENDING STORAGE RECOVERY V1]
def pending_storage_candidates(state):
  if state.get('storage_pending',False):
      return []
  pending_values=pending_storage_wips()
  if len(pending_values)>1:
      pending_storage_error_p.Value='MULTIPLE_PENDING_STORAGE_WIP {}'.format(len(pending_values))
      print('[CENTRAL PENDING STORAGE ERROR] multiple pending WIPs count={} time={}'.format(
        len(pending_values),sim.SimTime))
      return []
  result=[]
  for pending in pending_values:
      if pending.get('state')!='PENDING': continue
      tier_filter=None
      if pending.get('type')=='SHUTTLE_STORAGE':
            tier_filter=int(pending.get('currentTier',-1))
            if tier_filter<0 or tier_filter>=len(SHELVES):
                  pending_storage_error_p.Value='INVALID_SHUTTLE_TIER {}'.format(tier_filter)
                  continue
      product_type=str(pending.get('productType',''))
      product_id=str(pending.get('productId',''))
      source=str(pending.get('sourceComponent',''))
      job_id=int(pending.get('jobId',-1))
      if job_id<0: job_id=gnn_job_id('',product_id,product_type,None)
      before=len(result)
      for location in vc_empty_storage_locations(tier_filter):
            row,column,tier=location
            lifter_delay,shuttle_delay,total_time=pending_storage_delays(pending,location)
            payload={'mode':'PENDING_STORAGE','storageType':pending.get('type',''),
                     'sourceComponent':source,'productId':product_id,
                     'location':[row,column,tier],
                     'lifterDelay':lifter_delay,'shuttleDelay':shuttle_delay}
            candidate=action('A6_STORAGE',1.0e30,source,SHELVES[tier],
                             'PENDING_STORAGE_'+product_id,product_type,payload,'',total_time)
            candidate['product_id']=product_id
            candidate['product_type']=product_type
            candidate['runtime_product']='PENDING_STORAGE_'+product_id
            candidate['location']=[row,column,tier]
            candidate['target_row'],candidate['target_column'],candidate['target_tier']=location
            candidate['lifter_delay']=lifter_delay
            candidate['shuttle_delay']=shuttle_delay
            if job_id>=0: candidate['gnn_job']=job_id
            result.append(candidate)
      print('[CENTRAL PENDING STORAGE CANDIDATES] type={} source={} product_id={} count={} tier_filter={} time={}'.format(
        pending.get('type',''),source,product_id,len(result)-before,tier_filter,sim.SimTime))
  return result
'''


RUN_PENDING = r'''def run_pending_storage(candidate,location):
  global STORAGE_PENDING
  product_id=str(candidate.get('product_id',''))
  pending=pending_storage_wip(product_id)
  if not pending or pending.get('state')!='MOVING':
      storage_state_p.Value='ERROR PENDING_STATE'
      return False
  row,column,tier=location
  lifter_delay=float(candidate.get('lifter_delay',0.0))
  shuttle_delay=float(candidate.get('shuttle_delay',0.0))
  shelf=app.findComponent(SHELVES[tier])
  storage_position=row*4+column+1
  try:
      if pending.get('type')=='LIFTER_STORAGE':
            terminal=app.findComponent('Lifter_WIP #{}'.format(tier+5))
            signal_storage_motion(terminal,(storage_position,lifter_delay,lifter_delay))
            delay(max(lifter_delay,0.0))
            configure_storage_creator(terminal,pending.get('productType',''),(storage_position,0.0,0.0))
            configure_storage_creator(shelf,pending.get('productType',''),(shuttle_delay,))
            pending['createdInVc']=True
            pending['state']='CREATED'
            update_pending_storage(pending)
            lifter_wip=terminal.getProperty('LifterStorageWorkInProcess') if terminal else None
            if not lifter_wip: raise RuntimeError('LifterStorageWorkInProcess missing')
            lifter_wip.Value=True
      else:
            signal_storage_motion(shelf,(shuttle_delay,))
            delay(max(shuttle_delay,0.0))
            configure_storage_creator(shelf,pending.get('productType',''),(0.0,))
            pending['createdInVc']=True
            pending['state']='CREATED'
            update_pending_storage(pending)
            shuttle_wip=shelf.getProperty('ShuttleStorageWorkInProcess') if shelf else None
            if not shuttle_wip: raise RuntimeError('ShuttleStorageWorkInProcess missing')
            shuttle_wip.Value=True
      storage_state_p.Value='REQUESTED'
      if not trigger(SHELVES[tier]):
            raise RuntimeError('Shelf trigger failed')
      print('[CENTRAL PENDING STORAGE CREATED] type={} product_id={} location={} lifter_delay={} shuttle_delay={} time={}'.format(
        pending.get('type',''),pending.get('productId',''),location,lifter_delay,shuttle_delay,sim.SimTime))
      return True
  except Exception as exception:
      pending_storage_error_p.Value=str(exception)
      # OnChanged observes STORAGE_PENDING before it is cleared and queues the
      # STORAGE_ERROR event.  That event releases CURRENT_ACTION and dispatches
      # the next GNN decision, as in loaded-model-3.
      storage_state_p.Value='ERROR PENDING_EXECUTION {}'.format(exception)
      STORAGE_RESERVED.discard(location)
      STORAGE_PENDING=None
      print('[CENTRAL PENDING STORAGE EXECUTION ERROR] location={} error={} time={}'.format(
        location,exception,sim.SimTime))
      return False
'''


def edit(script):
    script = replace_function(
        script, "pending_storage_candidates", "storage_program_statements",
        PENDING_CANDIDATES)
    script = replace_function(
        script, "run_pending_storage", "vc_empty_storage_locations",
        RUN_PENDING)
    compile(script, "Central Controller", "exec")
    return script


with zipfile.ZipFile(SOURCE, "r") as archive:
    infos = archive.infolist()
    payloads = {item.filename: archive.read(item.filename) for item in infos}

layout = payloads["layout.rsc"].decode("utf-8")
component = layout.find('Name "Central Controller"')
match = re.search(r'Script "((?:\\.|[^"\\])*)"', layout[component:], re.S)
if component < 0 or match is None:
    raise RuntimeError("Central Controller TaskLogic script was not found")
left, right = component + match.start(1), component + match.end(1)
controller = json.loads('"' + match.group(1) + '"')
modified = edit(controller)
payloads["layout.rsc"] = (layout[:left] + json.dumps(modified, ensure_ascii=False)[1:-1] + layout[right:]).encode("utf-8")

temporary = TARGET + ".building"
if os.path.exists(temporary):
    os.remove(temporary)
with zipfile.ZipFile(temporary, "w") as output:
    for item in infos:
        output.writestr(item, payloads[item.filename])
with zipfile.ZipFile(temporary, "r") as check:
    if check.testzip() is not None:
        raise RuntimeError("VCMX ZIP validation failed")
os.replace(temporary, TARGET)


PARALLEL_TYPES = ('A1_RETRIEVAL', 'A2_LOADING')


def least_used_amr_loading(candidates):

    usage = {}
    for c in candidates:
        if c.get('type') == 'A2_LOADING':
            key = (c.get('source', ''), str(c.get('product', '')))
            usage[key] = min(usage.get(key, 1 << 30), int(c.get('amr_usage', 0)))
    return [c for c in candidates if c.get('type') != 'A2_LOADING' or
            int(c.get('amr_usage', 0)) == usage[(c.get('source', ''), str(c.get('product', '')))]]


def abnormal_priority_candidates(state, candidates):

    candidates = least_used_amr_loading(candidates)
    unload = [c for c in candidates if c.get('type') == 'A3_UNLOADING']
    storage = [c for c in candidates if c.get('type') == 'A6_STORAGE']
    carrying = any(int(r.get('load_count', 0)) > 0 or r.get('loaded_products') or r.get('unload_ready')
                   for r in state.get('amr_controller', {}).get('resources', []))
    if carrying:

        if unload or storage:
            return unload or storage


        return [c for c in candidates if c.get('type') in PARALLEL_TYPES]

    return storage or candidates


def abnormal_initialization_ready():
    owners = json.loads(comp.getProperty('Abnormal::InstanceOwners').Value)
    for name in owners:
        owner = app.findComponent(str(name))
        error = owner.getProperty('Abnormal::InstanceError').Value
        if error:
            raise RuntimeError('Abnormal initialization: ' + str(name) + ': ' + str(error))
        rows = json.loads(owner.getProperty('Abnormal::InstanceQueueJSON').Value)
        if int(owner.getProperty('Abnormal::ReleasedCount').Value) < len(rows):
            status = 'WAITING: ' + str(name)
            prop = comp.getProperty('Abnormal::InitializationStatus')
            if prop.Value != status:
                prop.Value = status
                print('[INITIALIZATION WAIT] component=' + str(name))
            return False
    return True

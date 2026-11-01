

from vcScript import *
import json
import vcMatrix
import vcVector

QUEUE_PROPERTY = 'Abnormal::InstanceQueueJSON'
STAGING = 'AbnormalInstanceStaging'


def native_text(value):

    try:
        unicode_type = unicode
    except NameError:
        return value
    return value.encode('utf-8') if isinstance(value, unicode_type) else value


def set_value(owner, name, value):
    prop = owner.getProperty(name)
    if prop is None:
        raise RuntimeError('Missing instance property: ' + name)
    prop.Value = native_text(value)


def world_matrix(values):
    matrix = vcMatrix.new()
    matrix.N = vcVector.new(*values[0])
    matrix.O = vcVector.new(*values[1])
    matrix.A = vcVector.new(*values[2])
    matrix.P = vcVector.new(*values[3])
    return matrix


def stamp_instance(row, product):
    try:
        product.Component.Name = native_text(row['instanceId'])
    except Exception as error:
        raise RuntimeError('Cannot name abnormal instance: material=%s id=%s error=%s' %
                           (row['material'], row['instanceId'], error))
    values = {
        'P4RInstanceId': row['instanceId'], 'P4RSourceId': row['sourceId'],
        'ProductID': row['instanceId'], 'LotID': row['instanceId'],
        'GnnJobId': row['job'], 'MaterialId': row['material'],
        'P4RInitialComponent': row['logicalComponent'],
        'P4RInitialRow': row.get('row', -1),
        'P4RInitialColumn': row.get('column', -1),
        'P4RInitialTier': row.get('tier', -1),
        'P4RInitialSlot': row.get('slot', -1), 'DueDate': row['due']}
    values['P4RInitialPlaced'] = False

    for name, value in values.items():
        set_value(product, name, value)
        set_value(product.Component, name, value)
    product.WorldPositionMatrix = world_matrix(row['worldPosition'])


def prepare_instances(owner, simulation):
    rows = json.loads(owner.getProperty(QUEUE_PROPERTY).Value or '[]')
    if rows and 'creationStatement' in rows[0]:

        set_value(owner, 'Abnormal::PreparedCount', 0)
        set_value(owner, 'Abnormal::ReleasedCount', 0)
        set_value(owner, 'Abnormal::InstanceError', '')
        return []
    stage = owner.findBehaviour(STAGING)
    types = dict((t.Name, t) for t in simulation.ProcessController.ProductTypeManager.ProductTypes)
    ids = [r['instanceId'] for r in rows]
    if len(ids) != len(set(ids)):
        raise RuntimeError('Duplicate product instance ID in ' + owner.Name)
    existing = {}
    for component in list(stage.Components):
        prop = component.getProperty('P4RInstanceId')
        if prop and prop.Value:
            if prop.Value in existing:
                raise RuntimeError('Duplicate staged product: ' + str(prop.Value))
            existing[str(prop.Value)] = component.Product
        else:
            raise RuntimeError('Unidentified product in abnormal staging container')
    if set(existing) - set(ids):
        raise RuntimeError('Stale products in abnormal staging container')
    products = []
    for row in rows:
        product_type = types.get(row['material'])
        if product_type is None:
            raise RuntimeError('Unknown product type: ' + row['material'])
        product = existing.get(row['instanceId'])
        if product is None:
            product = product_type.createProduct(stage)
        if product is None or product.ProductType.Name != row['material']:
            raise RuntimeError('Failed to create instance: ' + row['instanceId'])
        stamp_instance(row, product)
        products.append((row, product))
    set_value(owner, 'Abnormal::PreparedCount', len(products))
    set_value(owner, 'Abnormal::ReleasedCount', 0)
    set_value(owner, 'Abnormal::InstanceError', '')
    return products


def release_container(owner):
    executor = owner.findBehaviour('ProcessExecutor')
    node = executor.TransportNode if executor is not None else None
    destination = node.ComponentContainer if node is not None else None
    if destination is None:
        raise RuntimeError('Missing process transport container: ' + owner.Name)
    return destination


def release_instance(owner, row, product):
    destination = release_container(owner)
    if list(destination.Components):
        raise RuntimeError('Cannot release into occupied feeder: ' + owner.Name)
    product.WorldPositionMatrix = world_matrix(row['worldPosition'])
    destination.grab(product.Component)
    product.WorldPositionMatrix = world_matrix(row['worldPosition'])


def wait_until(predicate):


    while not predicate():
        delay(0.01)


def OnStart():
    global prepared
    try:
        prepared = prepare_instances(getComponent(), getSimulation())
    except Exception as error:
        set_value(getComponent(), 'Abnormal::InstanceError', str(error))
        raise


def cnc_product_created(statement, isImmediate):
    owner = getComponent()
    rows = json.loads(owner.getProperty(QUEUE_PROPERTY).Value or '[]')
    for row in rows:
        if statement.Name != row.get('creationStatement'):
            continue
        try:
            container = owner.findBehaviour(native_text(row['creationContainer']))
            products = [c.Product for c in container.Components
                        if c.Product is not None and c.Product.ProductType.Name == row['material']
                        and not c.getProperty('P4RInstanceId').Value]
            if len(products) != 1:
                raise RuntimeError('Expected one new CNC WIP instance, found ' + str(len(products)))
            stamp_instance(row, products[0])
            set_value(owner, 'Abnormal::PreparedCount', 1)
            set_value(owner, 'Abnormal::ReleasedCount', 1)
        except Exception as error:
            set_value(owner, 'Abnormal::InstanceError', str(error))
            raise


def OnRun():
    owner = getComponent()
    rows = json.loads(owner.getProperty(QUEUE_PROPERTY).Value or '[]')
    if rows and 'creationStatement' in rows[0]:


        try:
            for row in rows:
                container = owner.findBehaviour(native_text(row['creationContainer']))
                if container is None:
                    raise RuntimeError('Missing CNC creation container: ' + str(row['creationContainer']))
                deadline = getSimulation().SimTime + 5.0
                while not [c for c in container.Components if c.Product is not None
                           and c.Product.ProductType.Name == row['material']
                           and not c.getProperty('P4RInstanceId').Value]:
                    if getSimulation().SimTime >= deadline:
                        raise RuntimeError('CNC WIP was not created: ' + str(row['instanceId']))
                    delay(0.001)

                class CreatedStatement:
                    Name = row['creationStatement']
                cnc_product_created(CreatedStatement(), True)
        except Exception as error:
            set_value(owner, 'Abnormal::InstanceError', str(error))
            raise
        return
    if not prepared:
        return
    try:
        destination = release_container(owner)
        for row, product in prepared:
            wait_until(lambda: not list(destination.Components))
            release_instance(owner, row, product)
            count = owner.getProperty('Abnormal::ReleasedCount')
            count.Value = int(count.Value) + 1

            if row.get('placeholder', False):


                wait_until(lambda: getApplication().findComponent(native_text(row['instanceId'])) is None)
            else:
                wait_until(lambda: product.Component not in list(destination.Components))
            if row['logicalComponent'] == 'ASRS':
                if not row.get('placeholder', False):
                    wait_until(lambda: bool(product.Component.getProperty('P4RInitialPlaced').Value))
            delay(0.001)
    except Exception as error:
        set_value(owner, 'Abnormal::InstanceError', str(error))
        raise


def OnReset():
    global prepared
    prepared = []


prepared = []

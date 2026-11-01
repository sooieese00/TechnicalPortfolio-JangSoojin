from vcScript import *
import vcVector
import vcMatrix
import math

PREFIX = 'AbnormalFailure_'


def clear_display():
    geo = getComponent().UserGeometry
    for item in list(geo.GeometrySets):
        if item.Name.startswith(PREFIX):
            geo.deleteGeometrySet(item)


def OnRun():
    owner = getComponent()
    sim = getSimulation()
    app = getApplication()
    clear_display()
    duration = float(owner.getProperty('Abnormal::RepairSeconds').Value)
    if duration <= 0:
        return
    deadline = sim.SimTime + duration
    center, size = owner.BoundCenter, owner.BoundDiagonal
    material = app.findMaterial('AbnormalFailureRed')
    if material is None:
        material = app.createMaterial('AbnormalFailureRed')
        material.Diffuse = vcVector.new(1, 0.05, 0.05)
        material.Ambient = vcVector.new(1, 0.05, 0.05)
        app.addMaterial(material)
    geo = owner.UserGeometry
    outline = geo.createGeometrySet(VC_COMPACTLINESET)
    outline.Name = PREFIX + 'Outline'
    outline.Material = material
    outline.HasMaterialInheritance = False
    outline.LineWidth = 8.0
    x, y = max(size.X, 600) + 200, max(size.Y, 600) + 200
    outline.addLine([vcVector.new(center.X+a*x, center.Y+b*y, 30)
                     for a,b in [(-1,-1),(1,-1),(1,1),(-1,1),(-1,-1)]])
    label = geo.createGeometrySet(VC_TEXT2DSET)
    label.Name = PREFIX + 'Label'
    label.Material = material
    label.HasMaterialInheritance = False
    label.BillBoard = True
    label.FixedSize = True
    label.DepthBuffered = False
    label.Position = vcVector.new(center.X, center.Y, center.Z + size.Z + 500)
    label.OffsetY = 35
    title = geo.createGeometrySet(VC_TEXT3DSET)
    title.Name = PREFIX + 'Title'
    title.Material = material
    title.HasMaterialInheritance = False
    title.Text = 'DOWN'
    matrix = vcMatrix.new()
    matrix.scaleAbs(30, 30, 30)
    matrix.P = vcVector.new(center.X - x, center.Y, center.Z + size.Z + 350)
    title.Matrix = matrix
    title.update()
    while bool(owner.getProperty('MachineFailure').Value):
        remaining = max(0, int(math.ceil(deadline - sim.SimTime)))
        text = ('DOWN - Repair in %d s' % remaining) if remaining else 'DOWN - Awaiting recovery'
        owner.getProperty('Abnormal::FailureStatus').Value = text
        label.Text = owner.Name + ' | ' + text
        label.update()
        delay(0.25)
    owner.getProperty('Abnormal::FailureStatus').Value = 'RUNNING'
    clear_display()


def OnReset():
    clear_display()

# Builds the car from nothing and exports assets/car/car.glb. Run on GitHub machines: blender -b -P tools/build_car.py
# Blender axes: +Y is the nose, +X is the car's right, Z is up. The road is z = 0 at static ride height.
import bpy, bmesh, math, sys, os
from mathutils import Matrix, Vector

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "car", "car.glb")
R = 0.34           # tyre radius, matches Car.WheelRadius
HALF_TRACK = 0.83  # matches Car.TrackWidth / 2
FRONT_Y, REAR_Y = 1.30, -1.36
MATS = ["paint", "stripe", "black", "carbon", "glass", "headlight", "taillight", "tyre", "rim", "disc", "caliper"]
M = {n: i for i, n in enumerate(MATS)}

bpy.ops.wm.read_factory_settings(use_empty=True)
for n in MATS:
    m = bpy.data.materials.new(n)
    m.use_nodes = True

def with_mats(me):
    for n in MATS:
        me.materials.append(bpy.data.materials[n])

def obj(name, me, parent=None, loc=(0, 0, 0)):
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.location = loc
    if parent:
        o.parent = parent
    return o

# Half cross-section, normalised: (0..1 sideways, 0..1 up), bottom centre round to top centre.
SECTION = [(0, 0), (.55, 0), (.88, .06), (1, .30), (1, .55), (.93, .82), (.72, .96), (.20, 1), (0, 1)]

def ring(y, w, z0, z1, section=SECTION):
    half = [(u * w, y, z0 + v * (z1 - z0)) for u, v in section]
    other = [(-x, yy, z) for x, yy, z in reversed(half[1:-1])]
    return half + other

def loft(name, stations, section, face_mat, sub=3):
    me = bpy.data.meshes.new(name)
    with_mats(me)
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in ring(*s, section)] for s in stations]
    n = len(rings[0])
    for a in range(len(rings) - 1):
        for i in range(n):
            f = bm.faces.new((rings[a][i], rings[a][(i + 1) % n], rings[a + 1][(i + 1) % n], rings[a + 1][i]))
            f.material_index = face_mat(a, i, n)
    bm.faces.new(rings[0]).material_index = face_mat(0, 0, n)
    bm.faces.new(list(reversed(rings[-1]))).material_index = face_mat(len(rings) - 2, 0, n)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = obj(name, me)
    o.modifiers.new("sub", "SUBSURF").levels = sub
    return o

# y, half width, floor height, top height. Nose first.
BODY = [
    (2.30, .50, .22, .34), (2.18, .78, .16, .43), (1.86, .95, .13, .61), (1.30, .99, .13, .70), (.80, .93, .12, .72),
    (.20, .92, .12, .74), (-.50, .95, .12, .78), (-1.00, .99, .12, .85), (-1.36, 1.0, .13, .89), (-1.85, .98, .16, .91),
    (-2.14, .92, .25, .89), (-2.24, .78, .36, .80),
]
def body_mat(a, i, n):
    m = n // 2  # first half of the ring is the car's right side, seen from bottom to top
    if i in (0, 1, n - 1, n - 2, 2, n - 3):
        return M["black"]
    if i in (m - 1, m):
        return M["stripe"]
    return M["paint"]
body = loft("shell", BODY, SECTION, body_mat)

for y in (FRONT_Y, REAR_Y):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=64, radius1=.405, radius2=.405, depth=2.6,
                          matrix=Matrix.Translation((0, y, R)) @ Matrix.Rotation(math.pi / 2, 4, "Y"))
    cm = bpy.data.meshes.new("arch")
    bm.to_mesh(cm); bm.free()
    c = obj("arch", cm); c.hide_render = True
    mod = body.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"; mod.object = c; mod.solver = "EXACT"

CABIN = [
    (.98, .64, .66, .73), (.56, .64, .66, .91), (.22, .60, .66, 1.08), (-.10, .57, .66, 1.14), (-.46, .56, .66, 1.14),
    (-.82, .57, .66, 1.05), (-1.20, .60, .66, .92), (-1.60, .55, .66, .90),
]
CABIN_SECTION = [(0, 0), (.6, 0), (1, .1), (1, .45), (.84, .85), (.45, .98), (0, 1)]
def cabin_mat(a, i, n):
    m = n // 2
    top = i in (m - 1, m)
    upper = i in (m - 2, m + 1)
    if top:
        return M["glass"] if a <= 1 else M["paint"]
    if upper and a <= 1:
        return M["glass"]
    if upper and 2 <= a <= 4:
        return M["glass"]
    if i in (m - 3, m + 2) and a in (2, 3, 4):
        return M["glass"]
    if a >= 5 and upper:
        return M["carbon"]
    return M["paint"]
cabin = loft("cabin", CABIN, CABIN_SECTION, cabin_mat, sub=2)

def bake(o):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    return bpy.data.meshes.new_from_object(o.evaluated_get(dg), depsgraph=dg)

parts = bmesh.new()
for o in (body, cabin):
    me = bake(o)
    parts.from_mesh(me)
    bpy.data.meshes.remove(me)

def box(c, s, mat, rot=None):
    mx = Matrix.Translation(c) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((s[0], s[1], s[2], 1))
    r = bmesh.ops.create_cube(parts, size=1, matrix=mx)
    faces = {f for v in r["verts"] for f in v.link_faces}
    for f in faces:
        f.material_index = M[mat]

def blob(c, s, mat, rot=None):
    mx = Matrix.Translation(c) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((s[0], s[1], s[2], 1))
    r = bmesh.ops.create_uvsphere(parts, u_segments=24, v_segments=14, radius=1, matrix=mx)
    faces = {f for v in r["verts"] for f in v.link_faces}
    for f in faces:
        f.material_index = M[mat]
        f.smooth = True

def rod(a, b, rad, mat):
    a, b = Vector(a), Vector(b)
    d = b - a
    mx = Matrix.Translation((a + b) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    r = bmesh.ops.create_cone(parts, cap_ends=True, segments=12, radius1=rad, radius2=rad, depth=d.length, matrix=mx)
    faces = {f for v in r["verts"] for f in v.link_faces}
    for f in faces:
        f.material_index = M[mat]

box((0, 2.12, .105), (1.72, .46, .025), "carbon")                     # splitter
box((0, 2.27, .25), (.92, .10, .11), "black")                         # main intake
box((0, 2.0, .47), (.46, .28, .05), "black", Matrix.Rotation(-.38, 4, "X"))  # bonnet vent
for s in (-1, 1):
    blob((s * .66, 1.91, .475), (.21, .27, .045), "headlight", Matrix.Rotation(-.34, 4, "X") @ Matrix.Rotation(s * .55, 4, "Z"))
    box((s * .985, -.70, .46), (.05, .50, .20), "black")               # side intake
    box((s * .985, 0, .13), (.05, 1.5, .05), "carbon")                 # sill
    blob((s * .99, .62, .84), (.095, .055, .055), "paint")             # mirror
    rod((s * .66, .70, .78), (s * .94, .63, .83), .012, "carbon")
    box((s * .50, -2.215, .72), (.72, .03, .045), "taillight")
    box((s * .87, -2.02, 1.02), (.018, .46, .25), "carbon")            # wing end plate
    box((s * .36, -2.02, .95), (.022, .18, .30), "carbon")             # wing support
    rod((s * .22, -2.1, .30), (s * .22, -2.3, .30), .045, "disc")      # exhaust
box((0, -2.02, 1.10), (1.72, .32, .022), "carbon")                     # rear wing
box((0, -2.2, .20), (1.5, .12, .10), "carbon")                         # diffuser
for i in range(-3, 4):
    box((i * .2, -2.22, .16), (.012, .16, .10), "black")
for y in (-1.3, -1.45, -1.6):
    box((0, y, .96), (.70, .05, .012), "black")                        # engine louvres

me = bpy.data.meshes.new("body_mesh")
with_mats(me)
bmesh.ops.remove_doubles(parts, verts=parts.verts, dist=.0002)
for f in parts.faces:
    f.smooth = True
for e in parts.edges:
    if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(38):
        e.smooth = False
# Inside of the wheel arches: black, so no bright paint shows behind the tyres.
for f in parts.faces:
    c = f.calc_center_median()
    for y in (FRONT_Y, REAR_Y):
        if abs(math.hypot(c.y - y, c.z - R) - .405) < .012 and abs(c.x) < 1.2:
            f.material_index = M["black"]
parts.to_mesh(me)
parts.free()
car = obj("body", me)

for name in ("shell", "cabin", "arch"):
    for o in [x for x in bpy.data.objects if x.name.startswith(name)]:
        bpy.data.objects.remove(o)

def add_faces(bm, verts, mat):
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        f.material_index = M[mat]
        f.smooth = True

def lathe(bm, profile, mat, seg=56):
    # profile: (radius, offset along the axle). Axle runs along X.
    first = [bm.verts.new((a, r, 0)) for r, a in profile]
    edges = [bm.edges.new((first[i], first[i + 1])) for i in range(len(first) - 1)]
    res = bmesh.ops.spin(bm, geom=first + edges, cent=(0, 0, 0), axis=(1, 0, 0), dvec=(0, 0, 0), angle=math.tau, steps=seg, use_merge=True)
    vs = first + [g for g in res["geom_last"] if isinstance(g, bmesh.types.BMVert)]
    faces = {f for v in bm.verts for f in v.link_faces if f.material_index == 0 or not f.tag}
    return vs

def wheel(tag, x, y):
    out = 1 if x > 0 else -1                        # outer face of the wheel
    pivot = bpy.data.objects.new("wheel_" + tag, None)
    bpy.context.collection.objects.link(pivot)
    pivot.location = (x, y, R)

    bm = bmesh.new()
    W = .150
    # Tyre: bead, sidewall, shoulder, four-groove tread, and back again.
    tyre = [(.232, -W + .018), (.262, -W), (.312, -W - .004), (.334, -W + .014), (.340, -W + .036)]
    for c in (-.075, -.022, .022, .075):
        tyre += [(.340, c - .007), (.3335, c - .007), (.3335, c + .007), (.340, c + .007)]
    tyre += [(.340, W - .036), (.334, W - .014), (.312, W + .004), (.262, W), (.232, W - .018)]
    before = set(bm.faces)
    lathe(bm, tyre, "tyre")
    for f in set(bm.faces) - before:
        f.material_index = M["tyre"]; f.smooth = True
    # Rim barrel: inner surface visible through the spokes.
    before = set(bm.faces)
    rim = [(.232, W - .018), (.228, W - .006), (.215, W - .012), (.205, W - .05), (.205, -W + .03), (.228, -W + .006)]
    lathe(bm, [(r, a * out) for r, a in rim], "rim")
    for f in set(bm.faces) - before:
        f.material_index = M["rim"]; f.smooth = True
    # Five split spokes and a centre hub, set near the outer face.
    face_x = out * .085
    for k in range(5):
        ang = k * math.tau / 5
        for spread in (-.13, .13):
            rot = Matrix.Rotation(ang + spread, 4, "X")
            mx = rot @ Matrix.Translation((face_x, 0, .125)) @ Matrix.Diagonal((.022, .022, .185, 1))
            r = bmesh.ops.create_cube(bm, size=1, matrix=mx)
            for f in {f for v in r["verts"] for f in v.link_faces}:
                f.material_index = M["rim"]
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=.058, radius2=.05, depth=.05,
                              matrix=Matrix.Translation((face_x + out * .008, 0, 0)) @ Matrix.Rotation(out * math.pi / 2, 4, "Y"))
    for f in {f for v in r["verts"] for f in v.link_faces}:
        f.material_index = M["black"]
    # Brake disc spins with the wheel.
    r = bmesh.ops.create_cone(bm, cap_ends=True, segments=40, radius1=.175, radius2=.175, depth=.03,
                              matrix=Matrix.Translation((-out * .005, 0, 0)) @ Matrix.Rotation(math.pi / 2, 4, "Y"))
    for f in {f for v in r["verts"] for f in v.link_faces}:
        f.material_index = M["disc"]; f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("spin_mesh_" + tag)
    with_mats(mesh)
    bm.to_mesh(mesh); bm.free()
    obj("spin_" + tag, mesh, pivot)

    bm = bmesh.new()
    mx = Matrix.Rotation(-.7, 4, "X") @ Matrix.Translation((-out * .005, 0, .165)) @ Matrix.Diagonal((.075, .13, .06, 1))
    r = bmesh.ops.create_cube(bm, size=1, matrix=mx)
    for f in bm.faces:
        f.material_index = M["caliper"]
    mesh = bpy.data.meshes.new("caliper_mesh_" + tag)
    with_mats(mesh)
    bm.to_mesh(mesh); bm.free()
    obj("caliper_" + tag, mesh, pivot)

wheel("FL", -HALF_TRACK, FRONT_Y)
wheel("FR", HALF_TRACK, FRONT_Y)
wheel("RL", -HALF_TRACK, REAR_Y)
wheel("RR", HALF_TRACK, REAR_Y)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", export_yup=True, export_apply=True, export_materials="EXPORT", export_cameras=False, export_lights=False)
print("WROTE", OUT, "triangles", sum(len(o.data.polygons) for o in bpy.data.objects if o.type == "MESH"))

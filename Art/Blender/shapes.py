# Blender modelleri icin ortak yardimcilar (quarry.py, minecart.py).
# Her parca bir gruba eklenir; finish() gruplari tek mesh'e birlestirir, .blend kaydeder, istenirse FBX cikarir.
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix, Euler
from common import palette_mat

MAT = None
OUT = {}  # grup adi -> nesneler
rng = random.Random(0)

def start(seed):
    global MAT
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MAT = palette_mat()
    OUT.clear()
    rng.seed(seed)

# Palet sutunlari: 0 tas grisi (v: alt koyu -> ust acik), 1 gri->siyah, 2 ahsap, 3 sari, 4 kirmizi, 5 beyaz
STONE, DARK, WOOD, YELLOW, RED, WHITE = range(6)

def obj_from_bm(bm, name, group):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
    me.materials.append(MAT); OUT.setdefault(group, []).append(o)
    return o

def paint(o, col, lo, hi, vlo, vhi, axis=2):
    me = o.data
    uv = me.uv_layers.get("UV") or me.uv_layers.new(name="UV")
    u = (col + 0.5) / 64
    for loop in me.loops:
        c = me.vertices[loop.vertex_index].co[axis]
        t = 0 if hi == lo else min(1, max(0, (c - lo) / (hi - lo)))
        uv.data[loop.index].uv = (u, vlo + t * (vhi - vlo))

def xform(bm, loc, rot):
    m = Matrix.Translation(loc) @ Euler([math.radians(a) for a in rot]).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)

def box(name, group, size, loc, rot=(0, 0, 0), bevel=0.06, seg=2):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if bevel: bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=seg, profile=0.5, affect='EDGES')
    xform(bm, loc, rot)
    return obj_from_bm(bm, name, group)

def cyl(name, group, r, h, loc, rot=(0, 0, 0), n=8, bevel=0.04, r2=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=n, radius1=r, radius2=r if r2 is None else r2, depth=h)
    if bevel: bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=1, affect='EDGES')
    xform(bm, loc, rot)
    return obj_from_bm(bm, name, group)

def hull(name, group, pts):
    bm = bmesh.new()
    for p in pts: bm.verts.new(p)
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    junk = list({v for v in res["geom_interior"] + res["geom_unused"] if isinstance(v, bmesh.types.BMVert)})
    if junk: bmesh.ops.delete(bm, geom=junk, context='VERTS')
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(3), verts=bm.verts[:], edges=bm.edges[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return obj_from_bm(bm, name, group)

def rock(name, group, r, loc, scl=(1, 1, 1), jitter=0.22, pts=18, flat=0.6, floor=-0.3):
    # rastgele noktalarin dis kabugu: koseli kaya; ust kismi duzlenir, alti zemine gomulur
    golden = math.pi * (3 - math.sqrt(5))
    rot = Matrix.Rotation(rng.uniform(0, 6.28), 3, 'Z')
    P = []
    for i in range(pts):
        zz = 1 - 2 * (i + 0.5) / pts
        rr = math.sqrt(1 - zz * zz); th = golden * i + rng.uniform(-0.35, 0.35)
        p = Vector((math.cos(th) * rr, math.sin(th) * rr, zz)) * r * (1 + rng.uniform(-jitter, jitter) * 1.6)
        p.z = min(p.z, r * flat * rng.uniform(0.9, 1.05))
        p = rot @ Vector((p.x * scl[0], p.y * scl[1], p.z * scl[2])) + Vector(loc)
        p.z = max(p.z, floor)
        P.append(p)
    return hull(name, group, P)

def prism(name, group, profile, y0, y1, cap_front=True, cap_back=True):
    # profile: (x, z) listesi, y0..y1 boyunca uzatilir
    bm = bmesh.new()
    a = [bm.verts.new((x, y0, z)) for x, z in profile]
    b = [bm.verts.new((x, y1, z)) for x, z in profile]
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap_front: bm.faces.new(list(reversed(a)))
    if cap_back: bm.faces.new(b)
    bm.normal_update()
    if cap_front and cap_back: bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return obj_from_bm(bm, name, group)

def boolean(o, cutter, op='DIFFERENCE'):
    mod = o.modifiers.new("b", 'BOOLEAN'); mod.operation = op; mod.object = cutter; mod.solver = 'EXACT'
    with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]):
        bpy.ops.object.modifier_apply(modifier="b")

def arch_profile(hw, h, n=12, grow=0.0):
    hw += grow
    pts = [(hw, -grow - 0.4), (hw, h)]
    for i in range(1, n):
        t = math.pi * i / n
        pts.append((hw * math.cos(t), h + hw * math.sin(t)))
    pts += [(-hw, h), (-hw, -grow - 0.4)]
    return pts


def finish(root_name, blend_path, fbx_path=None, pivots={}, flat=set()):
    # pivots: grup -> nokta (sallanan/donen parcalarin pivotu); flat: duz golgelenecek gruplar
    root = bpy.data.objects.new(root_name, None); bpy.context.scene.collection.objects.link(root)
    for g, objs in OUT.items():
        with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
            bpy.ops.object.join()
        o = objs[0]; o.name = g; o.data.name = g
        with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o], selected_editable_objects=[o]):
            if g in flat: bpy.ops.object.shade_flat()
            else: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
        if g in pivots:
            o.data.transform(Matrix.Translation(-pivots[g])); o.location = pivots[g]
        o.parent = root
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print("TRIS", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects if o.type == 'MESH'))
    # Unity icin FBX: Blender'da -Y'ye bakan on yuz Unity'de +Z'ye bakar
    if fbx_path:
        bpy.ops.export_scene.fbx(filepath=fbx_path, object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True,
            mesh_smooth_type='FACE', apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
            bake_space_transform=True, use_custom_props=False, add_leaf_bones=False, path_mode='STRIP')
        print("FBX", fbx_path)

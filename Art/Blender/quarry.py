# Maden ocagi modeli. Blender 4.2: blender -b --python quarry.py
# QUARRY_FBX ortam degiskeni verilirse Unity icin FBX de cikarir. Giris -Y'ye bakar (Unity'de +Z).
import bpy, bmesh, math, random, sys, os
from mathutils import Vector, Matrix, Euler
S = os.path.dirname(os.path.abspath(__file__)); sys.path.append(S)
from common import *
bpy.ops.wm.read_factory_settings(use_empty=True)
MAT = palette_mat()
rng = random.Random(11)
OUT = {}  # grup adi -> nesneler

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

# ================= Olculer =================
HW, AH = 1.7, 2.5        # tunel yari genisligi, kemerin basladigi yukseklik (tepe = AH + HW)
FRONT = -2.7             # tas kemerin on yuzu
RING = 0.75              # kemer taslarinin kalinligi

# ================= Kaya kutlesi =================
SPOTS = [
    # (r, konum, olcek, duzlik) -- alt katman: genis yassi tabakalar
    (4.6, (0.0, 3.2, 0.6), (1.5, 1.0, 0.45), 0.55),
    (3.4, (-4.2, 1.2, 0.4), (1.1, 1.0, 0.5), 0.55),
    (3.6, (4.4, 1.5, 0.5), (1.1, 1.0, 0.5), 0.55),
    # orta katman
    (3.2, (-2.6, 2.4, 2.4), (1.1, 1.0, 0.7), 0.6),
    (3.3, (2.8, 2.6, 2.6), (1.0, 1.0, 0.7), 0.6),
    (2.6, (0.0, 1.8, 4.2), (1.3, 0.9, 0.7), 0.55),
    (2.2, (-5.6, 0.3, 1.3), (1.0, 1.0, 0.8), 0.6),
    (2.3, (5.8, 0.6, 1.4), (1.0, 1.0, 0.8), 0.6),
    # ust: tepeler
    (2.3, (-1.6, 3.6, 5.6), (1.0, 1.0, 0.85), 0.7),
    (2.0, (1.9, 3.4, 6.0), (1.0, 1.0, 0.9), 0.75),
    (1.5, (0.2, 4.4, 7.0), (1.0, 1.0, 1.0), 0.8),
    (1.6, (-4.0, 3.0, 3.9), (1.0, 1.0, 0.8), 0.65),
    (1.7, (4.4, 3.1, 4.3), (1.0, 1.0, 0.8), 0.65),
    # tuneli saran dolgu kayalari
    (1.9, (2.9, -0.9, 1.2), (1.0, 1.2, 0.9), 0.65),
    (1.9, (-2.9, -0.9, 1.2), (1.0, 1.2, 0.9), 0.65),
    (1.8, (0.0, -0.6, 4.6), (1.4, 1.1, 0.7), 0.6),
]
rock_objs = [rock(f"Rock{i}", "Rocks", r, l, s, flat=f) for i, (r, l, s, f) in enumerate(SPOTS)]
# kemerin onundeki kaya parcalari kesilir, kemer yuzu temiz kalir; tunel deligi acilir
face_cut = prism("cut", "tmp", arch_profile(HW + RING - 0.08, AH, n=16), FRONT - 10, FRONT + 0.3)
tun_cut = prism("cut2", "tmp", arch_profile(HW, AH, grow=0.05), FRONT - 1, FRONT + 9)
for o in rock_objs:
    boolean(o, face_cut); boolean(o, tun_cut)
# eteklerdeki kucuk kayalar
for i in range(16):
    side = rng.choice((-1, 1))
    x = side * rng.uniform(3.0, 7.5); y = rng.uniform(-1.8, 2.5)
    if abs(x) < HW + RING + 0.6 and y < FRONT + 1: continue
    rock("Boulder", "Rocks", rng.uniform(0.35, 0.9), (x, y, 0.0), (1, 1, 0.75), pts=12, flat=0.7)
for o in (face_cut, tun_cut): bpy.data.objects.remove(o)
OUT.pop("tmp")

# ================= Tunel (kemerli, derine dogru kararir) =================
t = prism("Tunnel", "Tunnel", arch_profile(HW, AH), FRONT + 0.2, FRONT + 8, cap_front=False, cap_back=True)
bm = bmesh.new(); bm.from_mesh(t.data); bm.normal_update()
c = Vector((0, FRONT + 4, AH))
for f in bm.faces:  # normaller tunelin icine baksin
    if (f.calc_center_median() - c).dot(f.normal) > 0: f.normal_flip()
bm.to_mesh(t.data); bm.free()

# tunelin dis kabugu: kaya bosluklarindan bakilinca kara delik yerine tas gorunsun
shell = prism("Shell", "Rocks", arch_profile(HW + 0.55, AH), FRONT + 0.8, FRONT + 8.2, cap_front=False, cap_back=False)
bm = bmesh.new(); bm.from_mesh(shell.data); bm.normal_update()
for f in bm.faces:  # normaller disari
    if (f.calc_center_median() - c).dot(f.normal) < 0: f.normal_flip()
bm.to_mesh(shell.data); bm.free()

# ================= Tas kemer (kuyunun tuglalariyla ayni dil) =================
def wedge(name, group, r1, r2, a0, a1, y0, y1, cz, bevel=0.05):
    bm = bmesh.new()
    vs = []
    for y in (y0, y1):
        for r, a in ((r1, a0), (r2, a0), (r2, a1), (r1, a1)):
            vs.append(bm.verts.new((r * math.cos(a), y, cz + r * math.sin(a))))
    q = lambda *ix: bm.faces.new([vs[i] for i in ix])
    q(0, 1, 2, 3); q(7, 6, 5, 4); q(0, 4, 5, 1); q(1, 5, 6, 2); q(2, 6, 7, 3); q(3, 7, 4, 0)
    bm.normal_update(); bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=2, profile=0.5, affect='EDGES')
    return obj_from_bm(bm, name, group)

N = 9
for i in range(N):
    a0 = math.pi * i / N + 0.012; a1 = math.pi * (i + 1) / N - 0.012
    key = i == N // 2
    out = RING + (0.25 if key else rng.uniform(-0.05, 0.08))
    yo = FRONT - (0.18 if key else rng.uniform(0, 0.08))
    wedge("Arch", "Portal", HW + 0.02, HW + out, a0, a1, yo, FRONT + 0.9, AH)
z = 0.0; layer = 0
while z < AH - 0.05:
    h = min(0.5, AH - z)
    for sx in (-1, 1):
        w = RING + (0.12 if layer % 2 else -0.04)
        box("Pillar", "Portal", (w, 0.9 + rng.uniform(-0.05, 0.1), h - 0.03),
            (sx * (HW + w / 2 + 0.02), FRONT + 0.45 - rng.uniform(0, 0.08), z + h / 2), rot=(0, 0, rng.uniform(-2, 2)), bevel=0.06)
    z += h; layer += 1
for sx in (-1, 1):  # kemerin tabaninda kaide taslari
    box("Plinth", "Portal", (RING + 0.35, 1.1, 0.35), (sx * (HW + RING / 2 + 0.1), FRONT + 0.4, 0.12), bevel=0.06)

# ================= Ahsap ic destekler =================
for k, y in enumerate((FRONT + 0.6, FRONT + 2.6, FRONT + 4.6)):
    dark = k > 0
    for x in (-HW + 0.2, HW - 0.2):
        box("InPost" if dark else "Post", "Frame", (0.32, 0.32, AH + 0.4), (x, y, (AH + 0.4) / 2), bevel=0.04)
    box("InBeam" if dark else "Beam", "Frame", (2 * HW - 0.1, 0.34, 0.34), (0, y, AH + 0.5), bevel=0.04)
    if not dark:
        for sx in (-1, 1):
            box("Brace", "Frame", (0.18, 0.2, 0.9), (sx * (HW - 0.55), y - 0.02, AH + 0.12), rot=(0, sx * 50, 0), bevel=0.03)

# ================= Raylar =================
RAIL_END = FRONT - 10.0
for x in (-0.55, 0.55):
    box("Rail", "Rails", (0.12, FRONT + 6 - RAIL_END, 0.14), (x, (FRONT + 6 + RAIL_END) / 2, 0.2), bevel=0.02, seg=1)
y = FRONT + 5.6
while y > RAIL_END + 0.3:
    box("Sleeper", "Rails", (1.7, 0.32, 0.14), (rng.uniform(-0.05, 0.05), y, 0.07), rot=(0, 0, rng.uniform(-5, 5)), bevel=0.03, seg=1)
    for x in (-0.55, 0.55):  # civiler
        box("Spike", "Rails", (0.06, 0.06, 0.06), (x + 0.12, y, 0.17), bevel=0)
    y -= 0.8
for i in range(40):  # rayin kenarinda balast taslari
    yy = rng.uniform(RAIL_END, FRONT + 0.4)
    rock("Ballast", "Rails", rng.uniform(0.07, 0.13), (rng.choice((-1, 1)) * rng.uniform(0.9, 1.15), yy, 0.0), pts=8, jitter=0.3)

# ================= Kesme tas istifi (palet uzerinde) =================
def pallet(cx, cy, rz):
    R = Matrix.Rotation(math.radians(rz), 3, 'Z')
    for dx in (-0.55, 0, 0.55):
        p = R @ Vector((dx, 0, 0))
        box("PalletSlat", "StonePile", (0.4, 1.9, 0.08), (cx + p.x, cy + p.y, 0.18), rot=(0, 0, rz), bevel=0.02, seg=1)
    for dy in (-0.75, 0.75):
        p = R @ Vector((0, dy, 0))
        box("PalletRun", "StonePile", (1.6, 0.18, 0.14), (cx + p.x, cy + p.y, 0.07), rot=(0, 0, rz), bevel=0.02, seg=1)
def stack(cx, cy, z0, rz, layers):
    R = Matrix.Rotation(math.radians(rz), 3, 'Z')
    z = z0
    for nx, ny in layers:
        for i in range(nx):
            for j in range(ny):
                off = R @ Vector(((i - (nx - 1) / 2) * 0.5, (j - (ny - 1) / 2) * 0.9, 0))
                box("Block", "StonePile", (0.46, 0.86, 0.36),
                    (cx + off.x + rng.uniform(-0.03, 0.03), cy + off.y + rng.uniform(-0.03, 0.03), z + 0.18),
                    rot=(0, 0, rz + rng.uniform(-5, 5)), bevel=0.06)
        z += 0.37
pallet(3.9, FRONT - 2.2, 8); stack(3.9, FRONT - 2.2, 0.22, 8, [(3, 2), (3, 2), (2, 2), (1, 1)])
pallet(6.2, FRONT - 3.5, -14); stack(6.2, FRONT - 3.5, 0.22, -14, [(3, 2), (2, 1)])
for i in range(45):  # daginik moloz
    a = rng.uniform(0, 6.28); d = rng.uniform(0.5, 1.0)
    cx, cy = rng.choice(((2.4, FRONT - 1.0), (-2.6, FRONT - 0.8), (1.5, FRONT - 4.0), (5.0, FRONT - 3.0)))
    rock("Rubble", "StonePile", rng.uniform(0.08, 0.28), (cx + math.cos(a) * d * 1.4, cy + math.sin(a) * d, 0.0), pts=9, jitter=0.3)

# ================= Vinc (makara + sallanan kova) =================
CX, CY = 6.2, FRONT - 1.0
for dx in (-0.7, 0.7):
    box("CraneLeg", "Crane", (0.22, 0.22, 4.0), (CX + dx * 0.55, CY, 1.95), rot=(0, dx * 10, 0), bevel=0.03)
box("CraneLegBack", "Crane", (0.2, 0.2, 4.1), (CX, CY + 0.5, 1.9), rot=(-14, 0, 0), bevel=0.03)
box("CraneTop", "Crane", (0.9, 0.3, 0.3), (CX, CY, 3.95), bevel=0.04)
box("CraneArm", "Crane", (0.22, 2.8, 0.22), (CX, CY - 1.1, 3.85), rot=(-6, 0, 0), bevel=0.03)
box("CraneStrut", "Crane", (0.14, 0.14, 1.6), (CX, CY - 0.6, 3.25), rot=(-48, 0, 0), bevel=0.02, seg=1)
cyl("Pulley", "Crane", 0.22, 0.12, (CX, CY - 2.4, 3.7), rot=(0, 90, 0), n=10, bevel=0.02)
cyl("Winch", "Crane", 0.2, 0.9, (CX, CY + 0.15, 1.1), rot=(0, 90, 0), n=10, bevel=0.02)
for dx in (-0.5, 0.5):
    box("WinchCrank", "Crane", (0.06, 0.06, 0.4), (CX + dx, CY + 0.15, 0.95), bevel=0)
BUCKET_HOOK = Vector((CX, CY - 2.4 - 0.22, 3.7))
box("Rope", "Bucket", (0.04, 0.04, 1.6), BUCKET_HOOK + Vector((0, 0, -0.8)), bevel=0)
cyl("BucketBody", "Bucket", 0.36, 0.5, BUCKET_HOOK + Vector((0, 0, -1.85)), n=10, r2=0.42, bevel=0.03)
cyl("BucketBand", "Bucket", 0.43, 0.06, BUCKET_HOOK + Vector((0, 0, -1.68)), n=10, bevel=0)
for i in range(3):
    rock("BucketStone", "Bucket", 0.17, BUCKET_HOOK + Vector((rng.uniform(-0.12, 0.12), rng.uniform(-0.12, 0.12), -1.55)), pts=8, floor=-99)
box("Rope2", "Crane", (0.04, 0.04, 3.2), (CX, CY - 1.1, 2.45), rot=(-38, 0, 0), bevel=0)  # makaradan vince

# ================= Alet sundurmasi =================
SX, SY = -4.7, FRONT - 1.6
for dx in (-1.2, 1.2):
    for dy, h in ((-1.1, 2.0), (1.1, 2.6)):
        box("ShedPost", "Shed", (0.2, 0.2, h), (SX + dx, SY + dy, h / 2), bevel=0.03)
    box("ShedRail", "Shed", (0.16, 2.5, 0.16), (SX + dx, SY, 2.3), rot=(15, 0, 0), bevel=0.02, seg=1)
for i in range(7):
    x = SX - 1.5 + i * 0.5
    box("Plank", "Shed", (0.46, 2.9, 0.08), (x, SY - 0.05, 2.45 + rng.uniform(-0.02, 0.03)), rot=(15 + rng.uniform(-2, 2), 0, rng.uniform(-2, 2)), bevel=0.02, seg=1)
for bx, by in ((SX - 0.6, SY + 0.5), (SX + 0.2, SY + 0.7)):
    cyl("Barrel", "Props", 0.36, 0.85, (bx, by, 0.43), n=10)
    for zz in (0.15, 0.7):
        cyl("Hoop", "Props", 0.385, 0.07, (bx, by, zz), n=10, bevel=0)
box("Crate", "Props", (0.7, 0.7, 0.6), (SX + 0.8, SY - 0.3, 0.3), rot=(0, 0, 10), bevel=0.05)
box("Crate", "Props", (0.6, 0.6, 0.5), (SX + 0.75, SY - 0.25, 0.85), rot=(0, 0, -8), bevel=0.05)
for i in range(3):  # cuvallar
    box("Sack", "Sacks", (0.55, 0.4, 0.35), (SX - 0.9 + i * 0.45, SY - 0.6, 0.18 + (0.3 if i == 1 else 0)), rot=(0, 0, rng.uniform(-20, 20)), bevel=0.14, seg=3)
box("PickHandle", "Props", (0.08, 0.08, 1.4), (SX + 1.45, SY - 1.25, 0.66), rot=(-14, 10, 0), bevel=0.02, seg=1)
box("PickHead", "Props", (0.85, 0.11, 0.11), (SX + 1.57, SY - 1.4, 1.32), rot=(-14, 10, 0), bevel=0.03, seg=1)
box("ShovelHandle", "Props", (0.07, 0.07, 1.3), (SX - 1.4, SY - 1.25, 0.8), rot=(-12, -8, 0), bevel=0.02, seg=1)
box("ShovelHead", "Props", (0.32, 0.06, 0.4), (SX - 1.48, SY - 1.4, 0.2), rot=(-12, -8, 0), bevel=0.03, seg=1)
bpy.ops.mesh.primitive_torus_add(major_radius=0.3, minor_radius=0.08, major_segments=12, minor_segments=6, location=(SX + 1.7, SY + 0.6, 0.08))
tor = bpy.context.active_object; tor.name = "Rope"; tor.data.materials.append(MAT); OUT.setdefault("Props", []).append(tor)

# ================= El arabasi =================
WX, WY, WR = 1.9, FRONT - 2.6, -30
RW = Matrix.Rotation(math.radians(WR), 3, 'Z')
def at(dx, dy, z): p = RW @ Vector((dx, dy, 0)); return (WX + p.x, WY + p.y, z)
cyl("Tray", "Barrow", 0.45, 0.4, at(0, 0, 0.58), rot=(0, 0, WR + 45), n=4, r2=0.62, bevel=0.03)
cyl("Wheel", "Barrow", 0.24, 0.1, at(0, -0.7, 0.24), rot=(0, 90, WR), n=10, bevel=0.02)
for dx in (-0.3, 0.3):
    box("Handle", "Barrow", (0.07, 1.6, 0.07), at(dx, 0.1, 0.45), rot=(-8, 0, WR), bevel=0.02, seg=1)
    box("Leg", "Barrow", (0.07, 0.07, 0.35), at(dx, 0.4, 0.17), rot=(0, 0, WR), bevel=0)
for i in range(5):
    rock("BarrowStone", "Barrow", rng.uniform(0.14, 0.2), at(rng.uniform(-0.22, 0.22), rng.uniform(-0.22, 0.22), 0.8), pts=8, floor=-99)

# ================= Fenerler =================
def lantern(group, hook, s=1.0):
    box("Chain", group, (0.05, 0.05, 0.3 * s), hook + Vector((0, 0, -0.15 * s)), bevel=0)
    cyl("LampTop", group, 0.14 * s, 0.12 * s, hook + Vector((0, 0, -0.33 * s)), n=6, r2=0.02, bevel=0)
    box("LampCap", group, (0.42 * s, 0.42 * s, 0.1 * s), hook + Vector((0, 0, -0.42 * s)), bevel=0.03, seg=1)
    box("LampGlow", group, (0.3 * s, 0.3 * s, 0.38 * s), hook + Vector((0, 0, -0.66 * s)), bevel=0.04, seg=1)
    for dx in (-1, 1):
        for dy in (-1, 1):
            box("LampBar", group, (0.05 * s, 0.05 * s, 0.4 * s), hook + Vector((dx * 0.16 * s, dy * 0.16 * s, -0.66 * s)), bevel=0)
    box("LampBase", group, (0.4 * s, 0.4 * s, 0.09 * s), hook + Vector((0, 0, -0.89 * s)), bevel=0.03, seg=1)
LANTERN_HOOK = Vector((-0.85, FRONT + 0.42, AH + 0.33))
lantern("Lantern", LANTERN_HOOK, 1.15)
POST_HOOKS = []
for y in (FRONT - 3.2, FRONT - 7.4):
    px = -1.35
    box("LampPost", "Posts", (0.18, 0.18, 2.5), (px, y, 1.25), bevel=0.03)
    box("LampArm", "Posts", (0.6, 0.12, 0.12), (px + 0.25, y, 2.4), bevel=0.02, seg=1)
    box("LampArmBrace", "Posts", (0.08, 0.08, 0.5), (px + 0.12, y, 2.2), rot=(0, 45, 0), bevel=0)
    POST_HOOKS.append(Vector((px + 0.48, y, 2.34)))
for i, h in enumerate(POST_HOOKS):
    lantern(f"PostLantern{i}", h, 0.9)

# ================= Tabela =================
box("SignPost", "Sign", (0.16, 0.16, 2.0), (-2.4, FRONT - 5.4, 1.0), bevel=0.03)
box("SignBoard", "Sign", (1.3, 0.1, 0.5), (-2.4, FRONT - 5.5, 1.65), rot=(0, 3, 0), bevel=0.04)
box("SignArrow", "Sign", (1.0, 0.1, 0.32), (-2.3, FRONT - 5.5, 1.1), rot=(0, -4, 0), bevel=0.04)
cyl("SignArrowTip", "Sign", 0.24, 0.1, (-1.75, FRONT - 5.5, 1.1), rot=(90, 0, 0), n=3, bevel=0.02)
box("SignIcon", "Sign", (0.5, 0.04, 0.07), (-2.4, FRONT - 5.57, 1.67), rot=(0, 35, 0), bevel=0)  # kazma isareti
box("SignIcon", "Sign", (0.5, 0.04, 0.07), (-2.4, FRONT - 5.57, 1.67), rot=(0, -35, 0), bevel=0)

# ================= Kristaller ve bayrak =================
def crystals(cx, cy, cz, n, tilt=25):
    for i in range(n):
        h = rng.uniform(0.5, 1.15)
        rx, ry = rng.uniform(-tilt, tilt), rng.uniform(-tilt, tilt)
        d = Euler((math.radians(rx), math.radians(ry), 0)).to_matrix() @ Vector((0, 0, h / 2))
        cyl("Crystal", "Crystals", rng.uniform(0.12, 0.2), h, (cx + rng.uniform(-0.2, 0.2) + d.x, cy + rng.uniform(-0.2, 0.2) + d.y, cz + d.z),
            rot=(rx, ry, rng.uniform(0, 60)), n=6, r2=0.01, bevel=0)
crystals(2.85, FRONT + 0.2, 0.0, 6)
crystals(4.6, FRONT + 0.9, 0.3, 4)
crystals(-6.6, FRONT + 0.6, 0.0, 4)
crystals(-2.85, FRONT + 0.2, 0.0, 4)
crystals(-1.2, 1.0, 5.2, 3, tilt=35)
box("FlagPole", "Props", (0.08, 0.08, 2.0), (0.2, 4.4, 8.2), bevel=0.02, seg=1)
bm = bmesh.new()
A = [bm.verts.new(p) for p in ((0.24, 4.4, 9.15), (0.24, 4.4, 8.6), (1.05, 4.4, 8.88))]
B = [bm.verts.new(p) for p in ((0.24, 4.4, 9.15), (0.24, 4.4, 8.6), (1.05, 4.4, 8.88))]
bm.faces.new(A); bm.faces.new(list(reversed(B)))
obj_from_bm(bm, "Flag", "Props")

# ================= Boyama =================
def is_(o, *names): return o.name.startswith(names)
for o in OUT["Rocks"]: paint(o, STONE, -0.3, 9.0, 0.02, 0.98)
for o in OUT["Tunnel"]: paint(o, DARK, FRONT + 3.0, FRONT + 0.2, 0.0, 0.38, axis=1)
for o in OUT["Portal"]: paint(o, STONE, 0, AH + HW + RING, 0.35, 0.98)
for o in OUT["Frame"]:
    if is_(o, "In"): paint(o, WOOD, 0, AH + 0.7, 0.0, 0.25)
    else: paint(o, WOOD, 0, AH + 0.7, 0.15, 0.98)
for o in OUT["Rails"]:
    if is_(o, "Rail", "Spike"): paint(o, DARK, 0, 1, 0.62, 0.62)
    elif is_(o, "Ballast"): paint(o, STONE, 0, 0.3, 0.05, 0.4)
    else: paint(o, WOOD, 0, 0.14, 0.05, 0.55)
for o in OUT["StonePile"]:
    if is_(o, "Pallet"): paint(o, WOOD, 0, 0.25, 0.2, 0.8)
    else: paint(o, STONE, 0, 1.8, 0.2, 0.98)
for g in ("Crane", "Shed", "Sign", "Posts"):
    for o in OUT[g]:
        if is_(o, "Pulley", "WinchCrank", "SignIcon"): paint(o, DARK, 0, 1, 0.5, 0.5)
        elif is_(o, "Rope"): paint(o, WOOD, 0, 1, 0.98, 0.98)
        else: paint(o, WOOD, 0, 4.5, 0.1, 0.98)
for o in OUT["Bucket"]:
    if is_(o, "BucketStone"): paint(o, STONE, 0, 1, 0.9, 0.9)
    elif is_(o, "BucketBand"): paint(o, DARK, 0, 1, 0.55, 0.55)
    elif is_(o, "Rope"): paint(o, WOOD, 0, 1, 0.98, 0.98)
    else: paint(o, WOOD, 0, 4, 0.3, 0.7)
for o in OUT["Barrow"]:
    if is_(o, "BarrowStone"): paint(o, STONE, 0, 1, 0.9, 0.9)
    elif is_(o, "Tray"): paint(o, RED, 0.3, 0.8, 0.0, 1.0)
    elif is_(o, "Wheel"): paint(o, DARK, 0, 1, 0.5, 0.5)
    else: paint(o, WOOD, 0, 1, 0.3, 0.9)
for g in [g for g in OUT if "Lantern" in g]:
    for o in OUT[g]:
        if is_(o, "LampGlow"): paint(o, YELLOW, 0, 1, 0.5, 0.5)
        else: paint(o, DARK, 0, 1, 0.45, 0.45)
for o in OUT["Sacks"]: paint(o, STONE, 0, 0.6, 0.75, 0.98)
for o in OUT["Crystals"]: paint(o, YELLOW, 0, 1, 0.5, 0.5)
for o in OUT["Props"]:
    if is_(o, "PickHead", "Hoop", "ShovelHead"): paint(o, DARK, 0, 1, 0.6, 0.6)
    elif is_(o, "Flag") and not is_(o, "FlagPole"): paint(o, RED, 0, 1, 0.5, 0.5)
    elif is_(o, "Rope"): paint(o, WOOD, 0, 1, 0.98, 0.98)
    else: paint(o, WOOD, 0, 1.5, 0.2, 0.95)

# ================= Gruplari birlestir =================
PIVOTS = {"Lantern": LANTERN_HOOK, "Bucket": BUCKET_HOOK}
PIVOTS.update({f"PostLantern{i}": h for i, h in enumerate(POST_HOOKS)})
FLAT = {"Rocks", "Tunnel", "Crystals", "StonePile", "Rails"}
root = bpy.data.objects.new("Quarry", None); bpy.context.scene.collection.objects.link(root)
for g, objs in OUT.items():
    with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs):
        bpy.ops.object.join()
    o = objs[0]; o.name = g; o.data.name = g
    with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o], selected_editable_objects=[o]):
        if g in FLAT: bpy.ops.object.shade_flat()
        else: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    if g in PIVOTS:  # sallanabilsin diye pivot kancada
        o.data.transform(Matrix.Translation(-PIVOTS[g])); o.location = PIVOTS[g]
    o.parent = root

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(S, "quarry.blend"))
print("TRIS", sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bpy.data.objects if o.type == 'MESH'))

# ================= Unity icin FBX (giris Unity'de +Z'ye bakar) =================
FBX = os.environ.get("QUARRY_FBX")
if FBX:
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'EMPTY', 'MESH'}, use_mesh_modifiers=True,
        mesh_smooth_type='FACE', apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_custom_props=False, add_leaf_bones=False, path_mode='STRIP')
    print("FBX", FBX)

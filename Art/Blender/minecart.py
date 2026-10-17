# Maden vagonu modeli. Blender 4.2: blender -b --python minecart.py
# MINECART_FBX ortam degiskeni verilirse Unity icin FBX de cikarir. On yuz -Y (Unity'de +Z).
# Tekerlekler ocaktaki raylara oturur (ray araligi 1.1 m, ray ustu 0.27 m).
import bpy, bmesh, math, sys, os
from mathutils import Vector, Matrix, Euler
S = os.path.dirname(os.path.abspath(__file__)); sys.path.append(S)
import shapes
from shapes import *
start(23)

R = 0.24                 # tekerlek yaricapi
WZ = 0.22 + R            # tekerlek merkezi: alti ray ustune yakin
GAUGE = 0.55             # tekerleklerin x'i (ray araligi / 2)
AXLES = (-0.5, 0.5)      # on ve arka aks (y)
Z0, Z1 = 0.62, 1.3       # kasanin alt ve ust yuksekligi
BOT = (0.52, 0.7)        # kasanin altta yari genislik / yari boy
TOP = (0.68, 0.88)       # ustte (kasa yukari dogru genisler)

def slant(a, b, z):  # kasa yuzeyinin z yuksekligindeki konumu
    return a + (b - a) * (z - Z0) / (Z1 - Z0)

# ================= Kasa =================
bm = bmesh.new()
bmesh.ops.create_cube(bm, size=1)
for v in bm.verts:
    top = v.co.z > 0
    hx, hy = TOP if top else BOT
    v.co = Vector((math.copysign(hx, v.co.x), math.copysign(hy, v.co.y), Z1 if top else Z0))
bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.05, segments=2, profile=0.5, affect='EDGES')
obj_from_bm(bm, "Body", "Body")
# ici: kenarin hemen altinda koyu bosluk
box("Inside", "Inside", (2 * TOP[0] - 0.2, 2 * TOP[1] - 0.2, 0.02), (0, 0, Z1 + 0.012), bevel=0)

# tahta araliklari (yatay koyu seritler) ve metal kusaklar
ax = math.degrees(math.atan2(TOP[0] - BOT[0], Z1 - Z0))
ay = math.degrees(math.atan2(TOP[1] - BOT[1], Z1 - Z0))
for z in (0.85, 1.08):
    for sx in (-1, 1):
        box("Groove", "Grooves", (0.02, 2 * slant(BOT[1], TOP[1], z) - 0.1, 0.035), (sx * (slant(BOT[0], TOP[0], z) + 0.004), 0, z), rot=(0, sx * ax, 0), bevel=0)
    for sy in (-1, 1):
        box("Groove", "Grooves", (2 * slant(BOT[0], TOP[0], z) - 0.1, 0.02, 0.035), (0, sy * (slant(BOT[1], TOP[1], z) + 0.004), z), rot=(-sy * ay, 0, 0), bevel=0)
zm = (Z0 + Z1) / 2
for sx in (-1, 1):
    for y in (-0.42, 0.42):
        box("Band", "Metal", (0.04, 0.12, Z1 - Z0 + 0.06), (sx * (slant(BOT[0], TOP[0], zm) + 0.02), y, zm), rot=(0, sx * ax, 0), bevel=0.01, seg=1)
for sy in (-1, 1):
    box("Band", "Metal", (0.12, 0.04, Z1 - Z0 + 0.06), (0, sy * (slant(BOT[1], TOP[1], zm) + 0.02), zm), rot=(-sy * ay, 0, 0), bevel=0.01, seg=1)
# ust kenar: metal cerceve
for sx in (-1, 1):
    box("Rim", "Metal", (0.08, 2 * TOP[1] + 0.08, 0.08), (sx * TOP[0], 0, Z1), bevel=0.02, seg=1)
for sy in (-1, 1):
    box("Rim", "Metal", (2 * TOP[0] + 0.08, 0.08, 0.08), (0, sy * TOP[1], Z1), bevel=0.02, seg=1)
# koselerde percin
for sx in (-1, 1):
    for sy in (-1, 1):
        box("Rivet", "Metal", (0.1, 0.1, 0.06), (sx * TOP[0], sy * TOP[1], Z1 + 0.05), bevel=0.02, seg=1)

# ================= Sasi, tamponlar, kanca =================
for x in (-0.4, 0.4):
    box("Beam", "Chassis", (0.14, 1.75, 0.14), (x, 0, Z0 - 0.06), bevel=0.03)
for sy in (-1, 1):
    box("Bumper", "Chassis", (0.95, 0.16, 0.2), (0, sy * 0.9, Z0 - 0.02), bevel=0.04)
    box("BumperPlate", "Metal", (0.5, 0.04, 0.1), (0, sy * 0.99, Z0 - 0.02), bevel=0.01, seg=1)
bpy.ops.mesh.primitive_torus_add(major_radius=0.08, minor_radius=0.025, major_segments=10, minor_segments=5,
                                 location=(0, -1.04, Z0 - 0.02), rotation=(0, math.radians(90), 0))
ring = bpy.context.active_object; ring.name = "Hitch"; ring.data.materials.append(shapes.MAT)
OUT.setdefault("Metal", []).append(ring)
# yan plaka (kucuk kirmizi isaret)
box("Plate", "Plate", (0.02, 0.36, 0.18), (slant(BOT[0], TOP[0], 0.97) + 0.03, 0, 0.97), rot=(0, ax, 0), bevel=0.01, seg=1)

# ================= Tekerlekler (her aks ayri: donsun diye pivot aksta) =================
WHEEL_PIVOTS = {}
for name, y in (("WheelsFront", AXLES[0]), ("WheelsBack", AXLES[1])):
    box("Axle", name, (2 * GAUGE + 0.1, 0.07, 0.07), (0, y, WZ), bevel=0)
    for sx in (-1, 1):
        x = sx * GAUGE
        cyl("Wheel", name, R, 0.1, (x, y, WZ), rot=(0, 90, 0), n=12, bevel=0.02)
        cyl("Flange", name, R + 0.04, 0.03, (x - sx * 0.065, y, WZ), rot=(0, 90, 0), n=12, bevel=0)
        cyl("Hub", name, 0.08, 0.16, (x, y, WZ), rot=(0, 90, 0), n=8, bevel=0.015)
        for k in range(3):  # tekerlek yuzundeki parmaklar: donunce gozuksun
            box("Spoke", name, (0.02, 0.05, 2 * R - 0.08), (x + sx * 0.052, y, WZ), rot=(k * 60, 0, 0), bevel=0)
    WHEEL_PIVOTS[name] = Vector((0, y, WZ))
# aks yataklari
for y in AXLES:
    for x in (-0.4, 0.4):
        box("Bearing", "Metal", (0.16, 0.16, 0.12), (x, y, WZ + 0.08), bevel=0.02, seg=1)

# ================= Fener (arka kosede direk) =================
box("LampPole", "Chassis", (0.06, 0.06, 0.62), (-TOP[0] + 0.02, TOP[1] + 0.06, Z1 + 0.18), bevel=0.01, seg=1)
box("LampArm", "Chassis", (0.06, 0.24, 0.05), (-TOP[0] + 0.02, TOP[1] + 0.16, Z1 + 0.47), bevel=0.01, seg=1)
LAMP = Vector((-TOP[0] + 0.02, TOP[1] + 0.26, Z1 + 0.44))
box("Chain", "CartLantern", (0.03, 0.03, 0.08), LAMP + Vector((0, 0, -0.04)), bevel=0)
box("LampCap", "CartLantern", (0.2, 0.2, 0.05), LAMP + Vector((0, 0, -0.1)), bevel=0.015, seg=1)
box("LampGlow", "CartLantern", (0.14, 0.14, 0.18), LAMP + Vector((0, 0, -0.22)), bevel=0.02, seg=1)
box("LampBase", "CartLantern", (0.19, 0.19, 0.04), LAMP + Vector((0, 0, -0.33)), bevel=0.01, seg=1)

# ================= Yuk: kesme taslar ve kaba kayalar (tasirken gorunur) =================
for i, (x, y, z, rz) in enumerate(((-0.3, -0.42, 0, 8), (0.28, -0.4, 0, -6), (-0.28, 0.38, 0, -10), (0.3, 0.42, 0, 5),
                                   (0.0, -0.05, 0.3, 80), (-0.05, 0.5, 0.25, 70))):
    box("Block", "Load", (0.5, 0.36, 0.3), (x, y, Z1 + 0.1 + z), rot=(rng.uniform(-6, 6), rng.uniform(-6, 6), rz), bevel=0.05)
for i in range(7):
    rock("LoadRock", "Load", rng.uniform(0.16, 0.24), (rng.uniform(-0.45, 0.45), rng.uniform(-0.6, 0.6), Z1 + 0.18 + rng.uniform(0, 0.15)), pts=10, jitter=0.25, floor=-99)

# ================= Boyama =================
for o in OUT["Body"]: paint(o, WOOD, Z0, Z1, 0.3, 0.98)
for o in OUT["Inside"]: paint(o, DARK, 0, 1, 0.12, 0.12)
for o in OUT["Grooves"]: paint(o, WOOD, 0, 1, 0.02, 0.02)
for o in OUT["Metal"]: paint(o, DARK, 0, 1, 0.58, 0.58)
for o in OUT["Plate"]: paint(o, RED, 0, 1, 0.5, 0.5)
for o in OUT["Chassis"]: paint(o, WOOD, 0, 1.8, 0.1, 0.8)
for g in WHEEL_PIVOTS:
    for o in OUT[g]:
        if o.name.startswith("Hub"): paint(o, STONE, 0, 1, 0.95, 0.95)
        elif o.name.startswith("Spoke"): paint(o, DARK, 0, 1, 0.3, 0.3)
        else: paint(o, DARK, 0, 1, 0.55, 0.55)
for o in OUT["CartLantern"]:
    if o.name.startswith("LampGlow"): paint(o, YELLOW, 0, 1, 0.5, 0.5)
    else: paint(o, DARK, 0, 1, 0.45, 0.45)
for o in OUT["Load"]: paint(o, STONE, Z1, Z1 + 0.7, 0.4, 0.98)

PIVOTS = dict(WHEEL_PIVOTS); PIVOTS["CartLantern"] = LAMP
finish("MineCart", os.path.join(S, "minecart.blend"), os.environ.get("MINECART_FBX"), PIVOTS, {"Load"})

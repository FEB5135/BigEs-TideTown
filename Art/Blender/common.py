import bpy, math, mathutils
import os
PALETTE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Textures", "palette.png")
def palette_mat():
    m = bpy.data.materials.get("Flat") or bpy.data.materials.new("Flat")
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bs.inputs["Roughness"].default_value = 0.9
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(PALETTE, check_existing=True)
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs[0], bs.inputs["Base Color"])
    nt.links.new(bs.outputs[0], out.inputs[0])
    return m
def setup_render(path, target=(0,0,1), dist=20, res=900, elev=35, azim=45):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x = res; sc.render.resolution_y = res
    sc.render.filepath = path
    sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55,0.7,0.85,1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.8
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun","SUN"))
    sun.data.energy = 3.5; sun.rotation_euler = (math.radians(50),0,math.radians(30))
    sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"; cam.data.ortho_scale = dist
    sc.collection.objects.link(cam); sc.camera = cam
    e, a = math.radians(elev), math.radians(azim)
    t = mathutils.Vector(target)
    cam.location = t + mathutils.Vector((math.cos(e)*math.sin(a), -math.cos(e)*math.cos(a), math.sin(e)))*60
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z","Y").to_euler()

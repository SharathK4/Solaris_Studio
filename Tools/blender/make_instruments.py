"""Procedural instrument product shots for the Jamming Session screen.

    blender -b --factory-startup -P Tools/blender/make_instruments.py -- <out_dir> [id ...] [--preview]

Every instrument is modelled from parametric outlines + primitives, lit with a warm studio rig
(amber key, ember rim, soft fill) and rendered with Cycles/OptiX on a transparent background.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = ARGS[0] if ARGS else os.path.abspath("instruments")
PREVIEW = "--preview" in ARGS
ONLY = [a for a in ARGS[1:] if not a.startswith("--")]


# ----------------------------------------------------------------------------- helpers
def lin(hexstr):
    h = hexstr.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((v + 0.055) / 1.055) ** 2.4 if v > 0.04045 else v / 12.92 for v in c) + (1.0,)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    try:
        prefs.compute_device_type = "OPTIX"
        prefs.get_devices()
        for d in prefs.devices:
            d.use = d.type == "OPTIX"
        sc.cycles.device = "GPU"
    except Exception as e:  # noqa: BLE001
        print("GPU unavailable, CPU render:", e)
    sc.cycles.samples = 24 if PREVIEW else 160
    sc.cycles.use_denoising = True
    sc.cycles.max_bounces = 8
    sc.render.film_transparent = True
    sc.render.resolution_x = 760
    sc.render.resolution_y = 880
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "AgX"
    try:
        sc.view_settings.look = "AgX - Punchy"
    except Exception:  # noqa: BLE001
        pass
    world = bpy.data.worlds.new("World")
    sc.world = world
    if not world.use_nodes:
        world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = lin("2A140A")
    bg.inputs["Strength"].default_value = 0.22


def material(name, color, rough=0.4, metal=0.0, coat=0.0, spec=0.5, aniso=0.0):
    m = bpy.data.materials.new(name)
    if not m.use_nodes:
        m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = lin(color) if isinstance(color, str) else color
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["Coat Weight"].default_value = coat
    b.inputs["Coat Roughness"].default_value = 0.04
    b.inputs["Specular IOR Level"].default_value = spec
    if aniso:
        b.inputs["Anisotropic"].default_value = aniso
    return m


def wood(name, light, dark, scale=24.0, rough=0.35, coat=0.7, direction="X", distortion=5.0):
    m = material(name, light, rough, 0, coat)
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type = "BANDS"
    wave.bands_direction = direction
    wave.inputs["Scale"].default_value = scale
    wave.inputs["Distortion"].default_value = distortion
    wave.inputs["Detail"].default_value = 3.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = lin(dark)
    ramp.color_ramp.elements[1].color = lin(light)
    ramp.color_ramp.elements[0].position = 0.38
    ramp.color_ramp.elements[1].position = 0.62
    nt.links.new(tc.outputs["Object"], wave.inputs["Vector"])
    nt.links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    return m


def burst(name, centre, mid, edge, radius=(0.17, 0.23), coat=1.0):
    """Sunburst lacquer: colour by elliptical distance from the body centre (object space)."""
    m = material(name, centre, 0.25, 0, coat)
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (1 / radius[0], 1 / radius[1], 0)
    ln = nt.nodes.new("ShaderNodeVectorMath")
    ln.operation = "LENGTH"
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    e = ramp.color_ramp.elements
    e[0].position, e[0].color = 0.35, lin(centre)
    e[1].position, e[1].color = 0.98, lin(edge)
    mid_el = e.new(0.72)
    mid_el.color = lin(mid)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], ln.inputs[0])
    nt.links.new(ln.outputs["Value"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    return m


def emissive(name, color, strength=3.0):
    m = material(name, color, 0.5)
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Emission Color"].default_value = lin(color)
    b.inputs["Emission Strength"].default_value = strength
    return m


ROOT = None


def own(obj):
    obj.parent = ROOT
    return obj


def box(name, size, loc, mat, bevel=0.002, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel > 0:
        md = o.modifiers.new("Bevel", "BEVEL")
        md.width = bevel
        md.segments = 3
        md.limit_method = "ANGLE"
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return own(o)


def cyl(name, r, depth, loc, mat, rot=(0, 0, 0), verts=48, r2=None, bevel=0.0):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    if bevel > 0:
        md = o.modifiers.new("Bevel", "BEVEL")
        md.width = bevel
        md.segments = 3
        md.limit_method = "ANGLE"
    bpy.ops.object.shade_smooth()
    return own(o)


def torus(name, R, r, loc, mat, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, location=loc, rotation=rot,
                                     major_segments=64, minor_segments=12)
    o = bpy.context.active_object
    o.name = name
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return own(o)


def sphere(name, r, loc, mat, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=r, location=loc, segments=32, ring_count=16)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    o.data.materials.append(mat)
    bpy.ops.object.shade_smooth()
    return own(o)


def catmull(points, n_per=10, closed=True):
    """Smooth closed/open Catmull-Rom through 2D points."""
    pts = [Vector(p) for p in points]
    out = []
    count = len(pts) if closed else len(pts) - 1
    for i in range(count):
        p0 = pts[(i - 1) % len(pts)] if closed or i > 0 else pts[0]
        p1 = pts[i]
        p2 = pts[(i + 1) % len(pts)]
        p3 = pts[(i + 2) % len(pts)] if closed or i + 2 < len(pts) else pts[-1]
        for s in range(n_per):
            t = s / n_per
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    return out


def symmetric_outline(profile, n=14):
    """Profile = [(y, halfwidth)] from bottom to top -> closed outline symmetric in x."""
    right = catmull([(w, y) for y, w in profile], n, closed=False)
    right = [Vector((max(p.x, 0.0), p.y)) for p in right] + [Vector((0.0, profile[-1][0]))]
    left = [Vector((-p.x, p.y)) for p in reversed(right[1:-1])]
    return [Vector((0.0, profile[0][0]))] + right[1:] + left


def slab(name, outline, z0, depth, mat, bevel=0.004, inset=0.0):
    """Extrude a closed 2D outline between z0 and z0 + depth, with rounded edges."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "2D"
    cu.fill_mode = "BOTH"
    cu.extrude = max(depth / 2 - bevel, 0.0001)
    cu.bevel_depth = bevel
    cu.bevel_resolution = 4
    cu.offset = -inset
    sp = cu.splines.new("POLY")
    sp.points.add(len(outline) - 1)
    for i, p in enumerate(outline):
        sp.points[i].co = (p.x, p.y, 0, 1)
    sp.use_cyclic_u = True
    o = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(o)
    o.location.z = z0 + depth / 2
    cu.materials.append(mat)
    return own(o)


def line(name, pts3, radius, mat):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 3
    sp = cu.splines.new("POLY")
    sp.points.add(len(pts3) - 1)
    for i, p in enumerate(pts3):
        sp.points[i].co = (p[0], p[1], p[2], 1)
    o = bpy.data.objects.new(name, cu)
    bpy.context.collection.objects.link(o)
    cu.materials.append(mat)
    return own(o)


def start(name):
    global ROOT
    reset()
    ROOT = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(ROOT)
    return ROOT


def lights(center, size):
    def area(nm, loc, energy, color, sz, target):
        ld = bpy.data.lights.new(nm, "AREA")
        ld.energy = energy
        ld.color = lin(color)[:3]
        ld.shape = "RECTANGLE"
        ld.size = sz
        ld.size_y = sz * 0.6
        o = bpy.data.objects.new(nm, ld)
        bpy.context.collection.objects.link(o)
        o.location = loc
        d = Vector(target) - Vector(loc)
        o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()

    c = Vector(center)
    s = size
    area("Key", c + Vector((-1.4 * s, -1.8 * s, 1.3 * s)), 300 * s * s, "FFE2BE", 1.6 * s, c)
    area("Rim", c + Vector((1.6 * s, 1.4 * s, 0.9 * s)), 420 * s * s, "FF7A36", 1.2 * s, c)
    area("Rim2", c + Vector((-1.5 * s, 1.2 * s, 0.4 * s)), 200 * s * s, "FF9A4A", 1.0 * s, c)
    area("Fill", c + Vector((1.6 * s, -1.6 * s, -0.2 * s)), 70 * s * s, "FFE8D2", 2.0 * s, c)
    area("Top", c + Vector((0, -0.3 * s, 2.4 * s)), 130 * s * s, "FFF0DC", 2.2 * s, c)


def frame(azimuth=0.0, elevation=6.0, lens=70, margin=1.12, lift=0.0):
    bpy.context.view_layer.update()
    mins = Vector((1e9, 1e9, 1e9))
    maxs = Vector((-1e9, -1e9, -1e9))
    for o in bpy.context.scene.objects:
        if o.type not in {"MESH", "CURVE"}:
            continue
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            mins = Vector(map(min, mins, w))
            maxs = Vector(map(max, maxs, w))
    center = (mins + maxs) / 2
    center.z += lift
    radius = (maxs - mins).length / 2
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = lens
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    fov = 2 * math.atan(cam_data.sensor_width / 2 / lens)
    sc = bpy.context.scene
    aspect = sc.render.resolution_x / sc.render.resolution_y
    fov_eff = fov if aspect >= 1 else 2 * math.atan(math.tan(fov / 2) * aspect)
    dist = radius * margin / math.sin(fov_eff / 2)
    az, el = math.radians(azimuth), math.radians(elevation)
    cam.location = center + Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el))) * dist
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    lights(center, radius * 1.6)


def render(name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".png")
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("RENDERED", path)


# ----------------------------------------------------------------------------- instruments
def fretted(name, body_profile, depth, neck_len, head_len, strings, top_mat, side_mat, neck_mat,
            board_mat, hole=0.045, frets=19, board_w=(0.043, 0.056)):
    """Acoustic-style instrument in hand space: top faces +Z, neck toward +Y. Call start() first."""
    root = ROOT
    outline = symmetric_outline(body_profile)
    y0, y1 = body_profile[0][0], body_profile[-1][0]
    ivory = material("Ivory", "EEE6D2", 0.35)
    black = material("Black", "0B0807", 0.25, coat=0.6)
    chrome = material("Chrome", "E9E6E1", 0.12, metal=1.0)
    bronze = material("Bronze", "C9955A", 0.25, metal=1.0)
    slab("Sides", outline, -depth / 2, depth, side_mat, bevel=0.006)
    slab("Binding", outline, depth / 2 - 0.004, 0.006, ivory, bevel=0.002)
    slab("Top", outline, depth / 2 - 0.002, 0.005, top_mat, bevel=0.0015, inset=0.004)

    hole_y = y0 + (y1 - y0) * 0.62
    cyl("Hole", hole, 0.012, (0, hole_y, depth / 2 - 0.002), black, verts=64)
    torus("Rosette", hole * 1.22, 0.0035, (0, hole_y, depth / 2 + 0.0025), ivory)
    torus("Rosette2", hole * 1.36, 0.002, (0, hole_y, depth / 2 + 0.0025), black)

    bridge_y = y0 + (y1 - y0) * 0.24
    box("Bridge", (hole * 3.4, 0.03, 0.009), (0, bridge_y, depth / 2 + 0.005), board_mat, 0.003)
    box("Saddle", (hole * 2.2, 0.003, 0.004), (0, bridge_y + 0.006, depth / 2 + 0.011), ivory, 0.001)

    neck_y0 = y1 - 0.01
    neck_y1 = neck_y0 + neck_len
    box("Neck", (board_w[0] * 0.95, neck_len, 0.022), (0, (neck_y0 + neck_y1) / 2, depth / 2 - 0.012), neck_mat, 0.008)
    box("Board", (board_w[0], neck_len + 0.08, 0.006), (0, (neck_y0 + neck_y1) / 2 - 0.04, depth / 2 + 0.004), board_mat, 0.001)
    scale_len = (neck_y1 - bridge_y)
    for f in range(1, frets):
        fy = neck_y1 - scale_len * (1 - 2 ** (-f / 12))
        if fy < y1 - 0.08:
            break
        box(f"Fret{f}", (board_w[0], 0.0016, 0.002), (0, fy, depth / 2 + 0.0075), chrome, 0.0005)
        if f in (3, 5, 7, 9, 15, 17):
            cyl(f"Dot{f}", 0.0028, 0.001, (0, fy - 0.012, depth / 2 + 0.0072), ivory, verts=20)
        if f == 12:
            for dx in (-0.009, 0.009):
                cyl(f"Dot12{dx}", 0.0028, 0.001, (dx, fy - 0.012, depth / 2 + 0.0072), ivory, verts=20)
    box("Nut", (board_w[0], 0.004, 0.006), (0, neck_y1, depth / 2 + 0.006), ivory, 0.001)
    head = box("Head", (board_w[1], head_len, 0.014), (0, neck_y1 + head_len / 2 + 0.004, depth / 2 - 0.006), board_mat, 0.006,
               rot=(math.radians(-12), 0, 0))
    per_side = (strings + 1) // 2
    for i in range(strings):
        side = -1 if i < per_side else 1
        k = i if i < per_side else i - per_side
        ty = neck_y1 + head_len * (0.25 + 0.6 * k / max(1, per_side - 1)) + 0.004
        tz = depth / 2 - 0.006 - (ty - neck_y1) * math.tan(math.radians(12))
        cyl(f"Peg{i}", 0.0045, 0.03, (side * (board_w[1] / 2 + 0.012), ty, tz), chrome, rot=(0, math.radians(90), 0), verts=24)
        cyl(f"PegKnob{i}", 0.009, 0.004, (side * (board_w[1] / 2 + 0.03), ty, tz), chrome, rot=(0, math.radians(90), 0), verts=24,
            bevel=0.0015)

    for i in range(strings):
        x_b = (i - (strings - 1) / 2) * (hole * 1.9 / max(1, strings - 1))
        x_n = (i - (strings - 1) / 2) * (board_w[0] * 0.8 / max(1, strings - 1))
        r = 0.0011 - i * 0.00012 if strings == 6 else 0.0009
        line(f"String{i}", [(x_b, bridge_y + 0.006, depth / 2 + 0.013), (x_n, neck_y1, depth / 2 + 0.0095)], max(r, 0.00045),
             bronze if strings == 6 and i < 4 else chrome)
    return root


def acoustic_guitar():
    start("acoustic_guitar")
    top = wood("Spruce", "D9A45E", "B98446", scale=70, rough=0.3, coat=0.8)
    sides = wood("Mahogany", "5A2410", "34130A", scale=40, rough=0.3, coat=0.8)
    neck = wood("Neck", "7A3A18", "4A200C", scale=20)
    board = material("Rosewood", "2A140C", 0.4, coat=0.3)
    prof = [(-0.25, 0.0), (-0.243, 0.1), (-0.215, 0.172), (-0.15, 0.202), (-0.07, 0.19), (0.0, 0.142), (0.045, 0.124),
            (0.11, 0.146), (0.17, 0.148), (0.215, 0.12), (0.245, 0.065), (0.252, 0.0)]
    root = fretted("acoustic_guitar", prof, 0.105, 0.33, 0.19, 6, top, sides, neck, board)
    pick = material("Pickguard", "1A0D08", 0.2, coat=0.8)
    pg = [(0.07, 0.03), (0.105, 0.0), (0.118, 0.06), (0.1, 0.115), (0.065, 0.12), (0.06, 0.07)]
    slab("Pickguard", [Vector(p) for p in catmull(pg, 8)], 0.0525 - 0.0005, 0.0012, pick, bevel=0.0004)
    root.rotation_euler = (math.radians(90 - 8), 0, math.radians(14))
    frame(azimuth=8, elevation=4, lens=70, margin=1.06)
    render("acoustic_guitar")


def ukulele():
    start("ukulele")
    top = wood("Koa", "A8561F", "7E3A12", scale=55, rough=0.28, coat=0.9, distortion=8)
    sides = wood("KoaSide", "7A3A14", "52220A", scale=45, rough=0.3, coat=0.9)
    board = material("Rosewood", "2A140C", 0.4, coat=0.3)
    prof = [(-0.14, 0.0), (-0.136, 0.055), (-0.118, 0.089), (-0.08, 0.098), (-0.035, 0.088), (0.0, 0.068), (0.03, 0.064),
            (0.07, 0.078), (0.105, 0.074), (0.13, 0.05), (0.142, 0.0)]
    root = fretted("ukulele", prof, 0.065, 0.19, 0.085, 4, top, sides, sides, board, hole=0.026, frets=16,
                   board_w=(0.034, 0.042))
    root.rotation_euler = (math.radians(90 - 8), 0, math.radians(-12))
    frame(azimuth=-8, elevation=4, lens=70, margin=1.08)
    render("ukulele")


def electric_guitar():
    root = start("electric_guitar")
    body_pts = [(0, -0.228), (0.09, -0.215), (0.155, -0.17), (0.172, -0.09), (0.15, -0.02), (0.118, 0.03), (0.13, 0.09),
                (0.136, 0.15), (0.12, 0.186), (0.094, 0.166), (0.072, 0.108), (0.032, 0.088), (-0.032, 0.088), (-0.078, 0.114),
                (-0.102, 0.19), (-0.122, 0.218), (-0.146, 0.196), (-0.152, 0.124), (-0.136, 0.052), (-0.127, 0.0),
                (-0.158, -0.06), (-0.17, -0.13), (-0.132, -0.2), (-0.07, -0.224)]
    outline = catmull(body_pts, 8)
    lacquer = burst("Sunburst", "E09A2A", "8A2A0C", "120605")
    slab("Body", outline, -0.022, 0.044, lacquer, bevel=0.009)
    pearl = material("Pearl", "EFE9DD", 0.3, coat=0.5)
    pg_pts = [(0.03, 0.08), (0.075, 0.1), (0.1, 0.05), (0.12, -0.03), (0.13, -0.1), (0.09, -0.15), (0.03, -0.16), (-0.05, -0.12),
              (-0.1, -0.05), (-0.105, 0.03), (-0.07, 0.085)]
    slab("Pickguard", catmull(pg_pts, 8), 0.0205, 0.0022, pearl, bevel=0.0008)
    cream = material("Cream", "F3EAD6", 0.35)
    chrome = material("Chrome", "EDEAE4", 0.1, metal=1.0)
    black = material("Knob", "151111", 0.3, coat=0.5)
    for i, y in enumerate((0.045, -0.0, -0.05)):
        box(f"Pickup{i}", (0.07, 0.018, 0.006), (-0.004 + i * 0.004, y, 0.025), cream, 0.004, rot=(0, 0, math.radians(-8 if i == 2 else 0)))
        for s in range(6):
            cyl(f"Pole{i}{s}", 0.0022, 0.001, (-0.03 + s * 0.0104 + i * 0.004, y, 0.0284), chrome, verts=16)
    box("Bridge", (0.075, 0.035, 0.005), (0.004, -0.1, 0.0245), chrome, 0.002)
    for i, (x, y) in enumerate(((0.07, -0.06), (0.085, -0.095), (0.095, -0.13))):
        cyl(f"Knob{i}", 0.009, 0.012, (x, y, 0.028), cream, verts=32, bevel=0.002)
    cyl("Switch", 0.004, 0.012, (0.09, -0.02, 0.028), cream, rot=(0.4, 0.3, 0), verts=16)
    maple = wood("Maple", "E0B279", "C8965A", scale=40, rough=0.25, coat=0.9, direction="Y")
    box("Neck", (0.044, 0.44, 0.02), (0, 0.3, 0.012), maple, 0.007)
    ivory = material("Dots", "20140E", 0.4)
    for f in range(1, 22):
        fy = 0.518 - 0.648 * (1 - 2 ** (-f / 12))
        box(f"Fret{f}", (0.043, 0.0016, 0.002), (0, fy, 0.0228), chrome, 0.0005)
        if f in (3, 5, 7, 9, 15, 17, 19, 21):
            cyl(f"Dot{f}", 0.0028, 0.001, (0, fy + 0.012, 0.0224), ivory, verts=20)
    head_pts = [(-0.022, 0.515), (0.024, 0.515), (0.034, 0.56), (0.045, 0.64), (0.04, 0.7), (0.02, 0.705), (0.005, 0.68),
                (-0.02, 0.6), (-0.028, 0.55)]
    slab("Head", catmull(head_pts, 8), 0.004, 0.014, maple, bevel=0.004)
    for i in range(6):
        y = 0.55 + i * 0.026
        x = 0.03 + i * 0.002
        cyl(f"Tuner{i}", 0.006, 0.016, (x, y, 0.022), chrome, verts=24)
        cyl(f"TunerKey{i}", 0.008, 0.004, (x + 0.02, y, 0.004), chrome, rot=(0, math.radians(90), 0), verts=24, bevel=0.001)
    for s in range(6):
        x = -0.026 + s * 0.0104
        line(f"String{s}", [(x + 0.004, -0.095, 0.03), (x * 0.8, 0.518, 0.0245), (0.02 + s * 0.002, 0.55 + s * 0.026, 0.026)],
             0.0005 + (5 - s) * 0.00009, chrome)
    root.rotation_euler = (math.radians(90 - 6), 0, math.radians(12))
    frame(azimuth=10, elevation=4, lens=70, margin=1.06)
    render("electric_guitar")


def violin():
    root = start("violin")
    varnish = wood("Varnish", "9E4217", "6A260A", scale=34, rough=0.22, coat=1.0, direction="Y", distortion=12)
    ebony = material("Ebony", "0E0A09", 0.3, coat=0.4)
    maple = material("BridgeMaple", "E7C79A", 0.5)
    chrome = material("Steel", "D8D4CE", 0.15, metal=1.0)
    prof = [(0.0, 0.0), (0.012, 0.055), (0.04, 0.088), (0.08, 0.1), (0.115, 0.103), (0.15, 0.098), (0.168, 0.088),
            (0.176, 0.07), (0.19, 0.056), (0.205, 0.058), (0.216, 0.072), (0.226, 0.082), (0.262, 0.083), (0.3, 0.074),
            (0.33, 0.05), (0.35, 0.02), (0.356, 0.0)]
    outline = symmetric_outline(prof, 10)
    slab("Body", outline, -0.02, 0.04, varnish, bevel=0.009)
    sphere("Arch", 1.0, (0, 0.18, 0.012), varnish, scale=(0.075, 0.15, 0.012))
    for side in (-1, 1):
        pts = [(side * 0.03, 0.15, 0.021), (side * 0.036, 0.17, 0.022), (side * 0.03, 0.19, 0.022), (side * 0.036, 0.21, 0.021)]
        line(f"FHole{side}", pts, 0.0016, ebony)
    box("Board", (0.03, 0.27, 0.006), (0, 0.33, 0.03), ebony, 0.002)
    box("Neck", (0.026, 0.13, 0.018), (0, 0.42, 0.016), varnish, 0.006)
    box("Pegbox", (0.022, 0.07, 0.022), (0, 0.515, 0.012), varnish, 0.006)
    torus("Scroll", 0.013, 0.0065, (0, 0.555, 0.018), varnish, rot=(0, math.radians(90), 0))
    sphere("ScrollEye", 0.008, (0, 0.555, 0.018), varnish)
    for i, (y, side) in enumerate(((0.495, -1), (0.51, 1), (0.525, -1), (0.54, 1))):
        cyl(f"Peg{i}", 0.0035, 0.045, (side * 0.02, y, 0.014), ebony, rot=(0, math.radians(90), 0), verts=16)
        sphere(f"PegHead{i}", 0.008, (side * 0.044, y, 0.014), ebony, scale=(0.5, 1, 1))
    tail = [(-0.012, 0.012), (0.012, 0.012), (0.022, 0.1), (-0.022, 0.1)]
    slab("Tailpiece", [Vector(p) for p in tail], 0.022, 0.006, ebony, bevel=0.002)
    box("Bridge", (0.042, 0.004, 0.022), (0, 0.155, 0.033), maple, 0.001)
    sphere("ChinRest", 1.0, (0.035, 0.03, 0.026), ebony, scale=(0.035, 0.03, 0.006))
    for s in range(4):
        x = -0.009 + s * 0.006
        line(f"String{s}", [(x * 0.9, 0.1, 0.028), (x, 0.155, 0.045), (x * 0.6, 0.47, 0.034)], 0.0005, chrome)
    wood_bow = material("Pernambuco", "5A2410", 0.3, coat=0.6)
    hair = material("Hair", "F2EDE2", 0.6)
    line("Bow", [(0.1, -0.12, 0.05), (-0.13, 0.56, 0.05)], 0.0035, wood_bow)
    line("BowHair", [(0.093, -0.1, 0.035), (-0.126, 0.535, 0.043)], 0.002, hair)
    box("Frog", (0.012, 0.045, 0.02), (0.09, -0.095, 0.04), ebony, 0.003, rot=(0, 0, math.radians(-18)))
    root.rotation_euler = (math.radians(90 - 6), 0, math.radians(-16))
    frame(azimuth=-6, elevation=4, lens=70, margin=1.06)
    render("violin")


def piano():
    root = start("piano")
    gloss = material("PianoBlack", "050404", 0.08, coat=1.0, spec=0.7)
    white = material("Keys", "F4F0E8", 0.3, coat=0.3)
    ebony = material("Sharps", "0A0808", 0.2, coat=0.5)
    brass = material("Brass", "C8A060", 0.2, metal=1.0)
    felt = material("Felt", "7A1E14", 0.9)
    case_pts = [(-0.74, 0.0), (0.74, 0.0), (0.745, 0.45), (0.66, 0.82), (0.42, 1.12), (0.2, 1.42), (0.02, 1.72), (-0.2, 1.92),
                (-0.48, 1.98), (-0.68, 1.9), (-0.74, 1.62)]
    outline = catmull(case_pts, 10)
    slab("Case", outline, 0.62, 0.3, gloss, bevel=0.012)
    slab("Soundboard", outline, 0.9, 0.012, felt, bevel=0.002, inset=0.03)
    lid = slab("Lid", outline, 0.0, 0.018, gloss, bevel=0.006)
    lid.location = (0, 0, 0)
    # Hinge along the straight (left) side: rotate the lid about the x = -0.74 line.
    hinge = bpy.data.objects.new("Hinge", None)
    bpy.context.collection.objects.link(hinge)
    hinge.parent = root
    hinge.location = (-0.74, 0, 0.935)
    lid.parent = hinge
    lid.location = (0.74, 0, 0.0)
    hinge.rotation_euler = (0, math.radians(-38), 0)
    line("Prop", [(0.55, 0.9, 0.93), (0.72, 0.85, 1.72)], 0.012, gloss)
    box("KeyBed", (1.46, 0.3, 0.06), (0, -0.14, 0.7), gloss, 0.008)
    box("Cheek", (0.07, 0.34, 0.14), (-0.735, -0.12, 0.74), gloss, 0.01)
    box("Cheek2", (0.07, 0.34, 0.14), (0.735, -0.12, 0.74), gloss, 0.01)
    box("Fallboard", (1.4, 0.04, 0.08), (0, 0.02, 0.8), gloss, 0.008)
    n_white = 52
    kw = 1.36 / n_white
    for i in range(n_white):
        box(f"W{i}", (kw * 0.94, 0.15, 0.022), (-0.68 + kw * (i + 0.5), -0.19, 0.742), white, 0.002)
    pattern = [1, 1, 0, 1, 1, 1, 0]   # sharps after C D (none after E), F G A (none after B)
    for i in range(n_white - 1):
        if pattern[(i + 5) % 7]:
            box(f"B{i}", (kw * 0.55, 0.095, 0.03), (-0.68 + kw * (i + 1), -0.16, 0.762), ebony, 0.002)
    box("Desk", (0.72, 0.012, 0.26), (0, 0.12, 1.02), gloss, 0.004, rot=(math.radians(-14), 0, 0))
    for i, (x, y) in enumerate(((-0.62, 0.12), (0.62, 0.12), (-0.25, 1.62))):
        cyl(f"Leg{i}", 0.05, 0.62, (x, y, 0.31), gloss, verts=32, r2=0.035)
        cyl(f"Caster{i}", 0.03, 0.03, (x, y, 0.015), brass, verts=24)
    box("Lyre", (0.2, 0.05, 0.4), (0, 0.35, 0.28), gloss, 0.01)
    for i in range(3):
        box(f"Pedal{i}", (0.035, 0.1, 0.012), (-0.06 + i * 0.06, 0.29, 0.06), brass, 0.004)
    frame(azimuth=-38, elevation=16, lens=60, margin=1.02)
    render("piano")


def drums():
    root = start("drums")
    shell = material("Shell", "7A1810", 0.28, metal=0.5, coat=1.0)
    head = material("Head", "EDE6D8", 0.55)
    chrome = material("Chrome", "EAE6E0", 0.12, metal=1.0)
    bronze = material("Cymbal", "C99A55", 0.22, metal=1.0, aniso=0.6)
    black = material("Black", "0E0A09", 0.4)

    def drum(name, r, depth, loc, rot):
        cyl(name, r, depth, loc, shell, rot=rot, verts=64)
        for side in (-1, 1):
            axis = rotate_axis(rot)
            c = Vector(loc) + axis * side * depth / 2
            torus(name + f"Hoop{side}", r + 0.004, 0.006, tuple(c), chrome, rot=rot)
            cyl(name + f"Head{side}", r, 0.002, tuple(c), head, rot=rot, verts=64)

    def rotate_axis(rot):
        from mathutils import Euler
        return Euler(rot).to_matrix() @ Vector((0, 0, 1))

    drum("Kick", 0.28, 0.4, (0, 0.1, 0.3), (math.radians(90), 0, 0))
    cyl("KickLogo", 0.1, 0.004, (0, -0.103, 0.3), black, rot=(math.radians(90), 0, 0), verts=48)
    drum("Tom1", 0.12, 0.17, (-0.15, 0.08, 0.72), (math.radians(70), 0, math.radians(8)))
    drum("Tom2", 0.13, 0.18, (0.16, 0.08, 0.72), (math.radians(70), 0, math.radians(-8)))
    drum("Floor", 0.16, 0.3, (0.52, -0.02, 0.5), (math.radians(8), 0, 0))
    drum("Snare", 0.17, 0.13, (-0.5, -0.08, 0.62), (math.radians(14), 0, 0))
    for x, y in ((0.4, -0.12), (0.64, -0.12), (0.52, 0.12)):
        line("FloorLeg", [(x, y, 0.45), (x + (x - 0.52) * 0.4, y * 1.3, 0.0)], 0.006, chrome)
    line("SnareStand", [(-0.5, -0.08, 0.56), (-0.5, -0.08, 0.1)], 0.008, chrome)
    for a in (0, 120, 240):
        t = math.radians(a)
        line("SnareLeg", [(-0.5, -0.08, 0.12), (-0.5 + 0.18 * math.cos(t), -0.08 + 0.18 * math.sin(t), 0.0)], 0.006, chrome)

    def cymbal(name, r, loc, tilt):
        cyl(name, r, 0.004, loc, bronze, rot=tilt, verts=96, r2=r * 0.12)
        sphere(name + "Bell", r * 0.16, loc, bronze, scale=(1, 1, 0.45))
        line(name + "Stand", [(loc[0], loc[1], loc[2] - 0.01), (loc[0], loc[1], 0.02)], 0.007, chrome)
        for a in (0, 120, 240):
            t = math.radians(a + 30)
            line(name + "Leg", [(loc[0], loc[1], 0.3), (loc[0] + 0.22 * math.cos(t), loc[1] + 0.22 * math.sin(t), 0.0)], 0.006, chrome)

    cymbal("HiHat", 0.17, (-0.82, 0.02, 0.92), (math.radians(4), 0, 0))
    cyl("HiHatTop", 0.17, 0.004, (-0.82, 0.02, 0.945), bronze, rot=(math.radians(4), 0, 0), verts=96, r2=0.02)
    cymbal("Crash", 0.23, (-0.42, 0.25, 1.28), (math.radians(-24), math.radians(14), 0))
    cymbal("Ride", 0.26, (0.66, 0.28, 1.16), (math.radians(-18), math.radians(-12), 0))
    line("TomMount", [(0, 0.1, 0.58), (0, 0.1, 0.66)], 0.01, chrome)
    frame(azimuth=0, elevation=14, lens=60, margin=1.02, lift=-0.03)
    render("drums")


def keyboard():
    root = start("keyboard")
    body = material("Body", "1A1614", 0.35, coat=0.4)
    panel = material("Panel", "2A2420", 0.45)
    white = material("Keys", "F2EEE6", 0.3, coat=0.2)
    black = material("Sharps", "0B0908", 0.25, coat=0.4)
    knob = material("Knob", "C9A06A", 0.25, metal=1.0)
    chrome = material("Chrome", "D8D4CE", 0.2, metal=1.0)
    screen = emissive("Screen", "F2A546", 2.5)
    box("Body", (1.02, 0.34, 0.085), (0, 0, 0.9), body, 0.02)
    box("Panel", (0.98, 0.12, 0.01), (0, 0.1, 0.945), panel, 0.004)
    n_white = 29
    kw = 0.86 / n_white
    for i in range(n_white):
        box(f"W{i}", (kw * 0.93, 0.15, 0.02), (-0.4 + kw * (i + 0.5), -0.075, 0.942), white, 0.002)
    pattern = [1, 1, 0, 1, 1, 1, 0]
    for i in range(n_white - 1):
        if pattern[i % 7]:
            box(f"B{i}", (kw * 0.55, 0.09, 0.022), (-0.4 + kw * (i + 1), -0.045, 0.958), black, 0.002)
    for i in range(10):
        cyl(f"Knob{i}", 0.011, 0.018, (-0.34 + i * 0.05, 0.11, 0.958), knob, verts=32, bevel=0.002)
    box("Screen", (0.14, 0.05, 0.004), (0.34, 0.1, 0.951), screen, 0.002)
    for i, x in enumerate((-0.47, -0.44)):
        cyl(f"Wheel{i}", 0.022, 0.012, (x, -0.06, 0.945), panel, rot=(0, math.radians(90), 0), verts=32)
    for side in (-1, 1):
        line(f"StandA{side}", [(side * 0.32, -0.25, 0.0), (side * 0.32, 0.25, 0.84)], 0.014, chrome)
        line(f"StandB{side}", [(side * 0.32, 0.25, 0.0), (side * 0.32, -0.25, 0.84)], 0.014, chrome)
        cyl(f"Arm{side}", 0.012, 0.3, (side * 0.32, 0, 0.845), chrome, rot=(math.radians(90), 0, 0), verts=24)
    cyl("Bolt", 0.018, 0.66, (0, 0, 0.42), chrome, rot=(0, math.radians(90), 0), verts=24)
    frame(azimuth=-22, elevation=22, lens=60, margin=1.02)
    render("keyboard")


BUILDERS = {
    "acoustic_guitar": acoustic_guitar,
    "electric_guitar": electric_guitar,
    "piano": piano,
    "drums": drums,
    "ukulele": ukulele,
    "keyboard": keyboard,
    "violin": violin,
}

for key, fn in BUILDERS.items():
    if ONLY and key not in ONLY:
        continue
    fn()

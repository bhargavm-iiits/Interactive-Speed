import bpy
import math
import os

# 1. Configuration Constants
FBX_INPUT_PATH = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped\Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx"
FBX_OUTPUT_PATH = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped\Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx"
VIDEO_OUTPUT_PATH = r"U:\Summer\Speed\Recordings\mouth_animation.mp4"

# Clear existing sample elements in headless scene
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()

# 2. Import the Mesh & Armature
print("Loading Meshy.ai FBX Asset...")
bpy.ops.import_scene.fbx(filepath=FBX_INPUT_PATH)

# Identify the target mesh and armature
mesh_obj = None
armature_obj = None
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH':
        mesh_obj = obj
    elif obj.type == 'ARMATURE':
        armature_obj = obj

if not mesh_obj:
    raise RuntimeError("Could not isolate a valid 3D Mesh in the provided asset.")

# Ensure target mesh is active
bpy.context.view_layer.objects.active = mesh_obj
mesh_obj.select_set(True)

# 3. Create procedural teeth textures inside Blender
def generate_teeth_image(is_upper):
    width = 128
    height = 64
    name = "TeethTex_Upper" if is_upper else "TeethTex_Lower"
    
    if name in bpy.data.images:
        return bpy.data.images[name]
        
    image = bpy.data.images.new(name, width=width, height=height)
    
    gum_color = (0.72, 0.28, 0.32, 1.0)
    tooth_color = (0.96, 0.96, 0.94, 1.0)
    transparent = (0.0, 0.0, 0.0, 0.0)
    gap_color = (0.12, 0.05, 0.08, 1.0)
    
    pixels = []
    for y in range(height):
        yn = y / (height - 1)
        for x in range(width):
            xn = x / (width - 1)
            
            is_gum = (yn > 0.48) if is_upper else (yn < 0.52)
            edge_dist = yn if is_upper else (1.0 - yn)
            
            if is_gum:
                pixels.extend(gum_color)
            elif edge_dist < 0.08:
                tooth_phase = xn * 10.0
                tooth_frac = tooth_phase - math.floor(tooth_phase)
                corner_dist = min(tooth_frac, 1.0 - tooth_frac)
                
                if corner_dist < 0.12 and edge_dist < (0.08 - corner_dist):
                    pixels.extend(transparent)
                else:
                    pixels.extend(tooth_color)
            else:
                tooth_phase = xn * 10.0
                tooth_frac = tooth_phase - math.floor(tooth_phase)
                
                if tooth_frac < 0.06 or x == 0 or x == width - 1:
                    pixels.extend(gap_color)
                else:
                    t1 = min(max(tooth_frac / 0.15, 0.0), 1.0)
                    t2 = min(max((1.0 - tooth_frac) / 0.15, 0.0), 1.0)
                    edge_shade = (t1 * t1 * (3 - 2 * t1)) * (t2 * t2 * (3 - 2 * t2))
                    
                    c = [
                        gap_color[i] + (tooth_color[i] - gap_color[i]) * (0.25 + edge_shade * 0.75)
                        for i in range(3)
                    ] + [1.0]
                    pixels.extend(c)
                    
    image.pixels = pixels
    image.pack()
    return image

print("Generating procedural teeth textures...")
upper_teeth_img = generate_teeth_image(True)
lower_teeth_img = generate_teeth_image(False)

def create_teeth_material(image):
    mat_name = image.name + "_Mat"
    if mat_name in bpy.data.materials:
        return bpy.data.materials[mat_name]
        
    mat = bpy.data.materials.new(name=mat_name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    
    nodes.clear()
    
    output_node = nodes.new(type='ShaderNodeOutputMaterial')
    principled_node = nodes.new(type='ShaderNodeBsdfPrincipled')
    texture_node = nodes.new(type='ShaderNodeTexImage')
    
    texture_node.image = image
    
    links.new(texture_node.outputs['Color'], principled_node.inputs['Base Color'])
    links.new(texture_node.outputs['Alpha'], principled_node.inputs['Alpha'])
    links.new(principled_node.outputs['BSDF'], output_node.inputs['Surface'])
    
    mat.blend_method = 'CLIP'
    mat.shadow_method = 'CLIP'
    
    return mat

upper_teeth_mat = create_teeth_material(upper_teeth_img)
lower_teeth_mat = create_teeth_material(lower_teeth_img)

# 4. Find the Head bone to parent the teeth properly
head_bone_name = None
if armature_obj:
    for bone in armature_obj.pose.bones:
        if "head" in bone.name.lower() and "top" not in bone.name.lower():
            head_bone_name = bone.name
            break
print(f"Targeting parent head bone: {head_bone_name}")

# Create teeth plane meshes inside Blender in Centimeter Scale
# Mouth center coordinates: X=0.0, Y=142.0 (Height), Z=-15.0 (Depth)
bpy.ops.mesh.primitive_plane_add(size=1.0)
upper_teeth_obj = bpy.context.active_object
upper_teeth_obj.name = "ProceduralTeeth_Upper"
upper_teeth_obj.scale = (8.0, 2.0, 1.0) # 8 cm wide, 2 cm high
bpy.ops.object.transform_apply(scale=True)
upper_teeth_obj.location = (0.0, 143.0, -14.2)
upper_teeth_obj.data.materials.append(upper_teeth_mat)

bpy.ops.mesh.primitive_plane_add(size=1.0)
lower_teeth_obj = bpy.context.active_object
lower_teeth_obj.name = "ProceduralTeeth_Lower"
lower_teeth_obj.scale = (8.0, 2.0, 1.0)
bpy.ops.object.transform_apply(scale=True)
lower_teeth_obj.location = (0.0, 140.0, -14.2)
lower_teeth_obj.data.materials.append(lower_teeth_mat)

# Parent the teeth to the head bone so they move with the head
def parent_to_head_bone(obj):
    if armature_obj and head_bone_name:
        world_matrix = obj.matrix_world.copy()
        obj.parent = armature_obj
        obj.parent_type = 'BONE'
        obj.parent_bone = head_bone_name
        obj.matrix_world = world_matrix

parent_to_head_bone(upper_teeth_obj)
parent_to_head_bone(lower_teeth_obj)

# 5. Create a Mouth-Opening Shape Key with depth cavity (Centimeter Scale coordinates)
print("Generating shape keys and deforming mouth vertices...")
if mesh_obj.data.shape_keys:
    mesh_obj.shape_key_add(name="Basis")
else:
    mesh_obj.shape_key_add(name="Basis")

mouth_shape = mesh_obj.shape_key_add(name="Mouth_Open")

# Deform jaw/lip vertices down and push back to create 3D mouth cavity
# Y is height, Z is depth, X is width
mouth_center_y = 142.0
deformed_verts_count = 0
for vertex in mouth_shape.data:
    co = vertex.co
    # Target vertices around the mouth region (low-mid centered coordinates)
    if -20.0 < co.x < 20.0 and 130.0 < co.y < 147.0 and -16.5 < co.z < -10.0:
        # Calculate horizontal and vertical factors
        x_factor = (20.0 - abs(co.x)) / 20.0  # 1 at center, 0 at edges
        y_factor = (147.0 - co.y) / 17.0      # 1 at bottom, 0 at top
        factor = x_factor * y_factor
        
        if factor > 0:
            # Shift vertices vertically downwards cleanly (Blender Y is height)
            # Up to 5.5 cm jaw drop
            vertex.co.y -= 5.5 * factor
            
            # Push center of mouth inward to create a 3D mouth cavity/hole (increasing Blender Z pushes inward)
            # Up to 4.5 cm depth push
            y_dist = abs(co.y - mouth_center_y)
            z_push_factor = 1.0 - (y_dist / 8.0)
            z_push_factor = max(0.0, z_push_factor)
            vertex.co.z += 4.5 * factor * z_push_factor
            deformed_verts_count += 1

print(f"Procedurally deformed {deformed_verts_count} vertices for shape key 'Mouth_Open'.")

# 6. Automate Procedural Keyframing Loop
print("Keyframing talking cadence and teeth motion...")
scene = bpy.context.scene
scene.frame_start = 1
scene.frame_end = 120  # 4 seconds at 30 fps

if not mesh_obj.data.shape_keys.animation_data:
    mesh_obj.data.shape_keys.animation_data_create()

default_lower_teeth_y = lower_teeth_obj.location.y

for frame in range(scene.frame_start, scene.frame_end + 1):
    scene.frame_set(frame)
    
    # Mathematical synthesis of dialogue mouth fluctuations
    primary_wave = math.sin(frame * 0.25) * 0.5 + 0.5
    secondary_noise = math.sin(frame * 0.65) * 0.15
    
    raw_intensity = primary_wave + secondary_noise
    mouth_intensity = max(0.0, min(1.0, raw_intensity))
    
    # Inject value directly to the shape slider
    mouth_shape.value = mouth_intensity
    mouth_shape.keyframe_insert(data_path="value", frame=frame)
    
    # Move lower teeth down (Y axis) in sync with jaw
    lower_teeth_obj.location.y = default_lower_teeth_y - 3.5 * mouth_intensity
    lower_teeth_obj.keyframe_insert(data_path="location", index=1, frame=frame)

# 7. Render Setup
print("Setting up camera and lights for rendering...")
# Create target empty at mouth center
bpy.ops.object.empty_add(location=(0.0, 142.0, -15.0))
target_empty = bpy.context.active_object
target_empty.name = "Camera_Target"

# Create a camera pointing at the empty target
bpy.ops.object.camera_add(location=(0.0, 142.0, -65.0))
camera_obj = bpy.context.active_object
camera_obj.name = "Render_Camera"
scene.camera = camera_obj

camera_obj.data.lens = 35.0

# Add Track To constraint to align camera perfectly to mouth
constraint = camera_obj.constraints.new(type='TRACK_TO')
constraint.target = target_empty
constraint.track_axis = 'TRACK_NEGATIVE_Z'
constraint.up_axis = 'UP_Y'

# Create lighting
bpy.ops.object.light_add(type='SUN', location=(0.0, 250.0, -150.0))
sun_obj = bpy.context.active_object
sun_obj.name = "Render_Sun"
sun_obj.data.energy = 5.0

# Add a fill light from side
bpy.ops.object.light_add(type='POINT', location=(-50.0, 142.0, -80.0))
fill_light = bpy.context.active_object
fill_light.name = "Render_Fill"
fill_light.data.energy = 50000.0

# Set ambient background color to grey instead of black
scene.world.use_nodes = True
bg_node = scene.world.node_tree.nodes.get("Background")
if bg_node:
    bg_node.inputs['Color'].default_value = (0.2, 0.2, 0.2, 1.0)

# Configure Render Settings
scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x = 1080
scene.render.resolution_y = 1080
scene.render.filepath = VIDEO_OUTPUT_PATH
scene.render.image_settings.file_format = 'FFMPEG'
scene.render.ffmpeg.format = 'MPEG4'
scene.render.ffmpeg.codec = 'H264'
scene.render.ffmpeg.constant_rate_factor = 'MEDIUM'
scene.render.ffmpeg.audio_codec = 'NONE'

os.makedirs(os.path.dirname(VIDEO_OUTPUT_PATH), exist_ok=True)

# 8. Render the Animation Video
print(f"Rendering mouth movement animation to {VIDEO_OUTPUT_PATH}...")
bpy.ops.render.render(animation=True)
print("Rendering complete!")

# 9. Clean up render helper objects from the scene before FBX export
bpy.data.objects.remove(camera_obj, do_unlink=True)
bpy.data.objects.remove(target_empty, do_unlink=True)
bpy.data.objects.remove(sun_obj, do_unlink=True)
bpy.data.objects.remove(fill_light, do_unlink=True)

# 10. Native FBX Compilation and Export
print("Writing compiled rig, teeth geometry, and shape keys back to file...")
bpy.ops.export_scene.fbx(
    filepath=FBX_OUTPUT_PATH,
    use_selection=False,
    add_leaf_bones=False,       # Prevents adding unneeded bone extensions
    bake_anim=True,             # Compiles the newly injected blendshapes natively inside the FBX file
    bake_anim_step=1,
    bake_anim_simplify_factor=1.0,
    path_mode='COPY',
    embed_textures=True         # Prevents damaging standard texture linking configurations
)

print(f"Workflow Complete! File updated at: {FBX_OUTPUT_PATH}")

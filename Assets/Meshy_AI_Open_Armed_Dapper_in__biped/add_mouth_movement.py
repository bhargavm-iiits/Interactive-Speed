import bpy
import math

# 1. Configuration Constants
FBX_INPUT_PATH = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped\Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx"
FBX_OUTPUT_PATH = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped\Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx"

# Clear existing sample elements in headless scene
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()

# 2. Import the Mesh & Armature
print("Loading Meshy.ai FBX Asset...")
bpy.ops.import_scene.fbx(filepath=FBX_INPUT_PATH)

# Identify the target mesh (Meshy.ai exports standard structures as "Mesh" or "Character")
mesh_obj = None
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH':
        mesh_obj = obj
        break

if not mesh_obj:
    raise RuntimeError("Could not isolate a valid 3D Mesh in the provided asset.")

# Ensure target mesh is actively targeted
bpy.context.view_layer.objects.active = mesh_obj
mesh_obj.select_set(True)

# 3. Create a Mouth-Opening Procedural Shape Key (Blendshape)
print("Generating procedural mouth morph dynamics...")
if not mesh_obj.data.shape_keys:
    mesh_obj.shape_key_add(name="Basis")

# Add the specific shape morph channel to simulate mouth tracking
mouth_shape = mesh_obj.shape_key_add(name="Mouth_Open")

# Deform lower jaw vertices down on the Z-axis to cleanly simulate speech space
# For Meshy models, target vertices positioned in lower face boundaries
for vertex in mouth_shape.data:
    co = vertex.co
    # Target vertices around the mouth region (low-mid centered coordinates)
    if -0.2 < co.x < 0.2 and 0.05 < co.y < 0.25 and 1.3 < co.z < 1.45:
        # Calculate distance gradient to smoothly decay edge distortion
        factor = (0.2 - abs(co.x)) * (1.45 - co.z)
        if factor > 0:
            # Shift vertices vertically downwards cleanly based on facial height mapping
            vertex.co.z -= 0.045 * (factor * 15.0)

# 4. Automate Procedural Keyframing Loop (The AI Talking Cadence)
print("Injecting mathematical chattering parameters across target animation layers...")
scene = bpy.context.scene
scene.frame_start = 1
scene.frame_end = 250  # Matches typical hand-wavy talking loops

# Ensure animation path nodes exist on the object
if not mesh_obj.data.shape_keys.animation_data:
    mesh_obj.data.shape_keys.animation_data_create()

# Procedurally generate talking pacing using overlaid sine wave patterns
for frame in range(scene.frame_start, scene.frame_end + 1):
    scene.frame_set(frame)
    
    # Mathematical synthesis of standard dialogue mouth fluctuations (Ambient talking loops)
    primary_wave = math.sin(frame * 0.45) * 0.5 + 0.5
    secondary_noise = math.sin(frame * 0.95) * 0.2
    
    # Combine wave variables and clamp bounds between 0.0 (Closed) and 1.0 (Fully Open)
    raw_intensity = primary_wave + secondary_noise
    mouth_intensity = max(0.0, min(1.0, raw_intensity))
    
    # Inject programmatic value directly to the shape slider array block
    mouth_shape.value = mouth_intensity
    mouth_shape.keyframe_insert(data_path="value", frame=frame)

# 5. Native FBX Compilation and Export
print("Writing compiled rig and data to file...")
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

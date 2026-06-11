import bpy
import math

# Configuration Constants
FBX_INPUT_PATH = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped\Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx"
FBX_OUTPUT_PATH = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped\Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx"

# Clear scene
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()

# Import FBX
print("Loading Meshy.ai FBX Asset...")
bpy.ops.import_scene.fbx(filepath=FBX_INPUT_PATH)

# Identify the target mesh, armature, and duplicate teeth
mesh_obj = None
armature_obj = None
objs_to_delete = []

for obj in bpy.context.scene.objects:
    if obj.type == 'MESH':
        if "teeth" in obj.name.lower() or "procedural" in obj.name.lower():
            objs_to_delete.append(obj)
        else:
            mesh_obj = obj
    elif obj.type == 'ARMATURE':
        armature_obj = obj

for obj in objs_to_delete:
    print(f"Removing old duplicate teeth mesh: {obj.name}")
    bpy.data.objects.remove(obj, do_unlink=True)

if not mesh_obj:
    raise RuntimeError("Could not isolate a valid 3D Mesh in the provided asset.")

# Ensure target mesh is active
bpy.context.view_layer.objects.active = mesh_obj
mesh_obj.select_set(True)

# Clear all shape keys to start fresh
if mesh_obj.data.shape_keys:
    print("Clearing existing shape keys...")
    mesh_obj.shape_key_clear()

# Add Basis shape key
basis_key = mesh_obj.shape_key_add(name="Basis")

# Add Mouth_Open shape key
mouth_shape = mesh_obj.shape_key_add(name="Mouth_Open")

# Deform jaw/lip vertices down and push back to create 3D mouth cavity (Centimeter Scale)
mouth_center_y = 142.0
deformed_verts_count = 0
for vertex in mouth_shape.data:
    co = vertex.co
    # Target vertices around the mouth region (low-mid centered coordinates)
    if -15.0 < co.x < 15.0 and 130.0 < co.y < 147.0 and -16.5 < co.z < -10.0:
        # Horizontal factor (1 at center, 0 at cheeks)
        x_factor = (15.0 - abs(co.x)) / 15.0
        x_factor = max(0.0, min(1.0, x_factor))
        
        # Vertical factors relative to mouth center (142.0)
        y_dist = co.y - mouth_center_y
        
        if y_dist < 0:
            # Below mouth center (lower lip & jaw): pull down
            # pulls most at the lip (y_dist near 0) and decays towards chin
            y_factor = (12.0 - abs(y_dist)) / 12.0
            y_factor = max(0.0, min(1.0, y_factor))
            
            factor = x_factor * y_factor
            
            # Deform DOWN (Y decreases in Blender)
            vertex.co.y -= 14.0 * factor # Up to 14 cm jaw drop
            
            # Push inward (Z increases in Blender)
            z_push = 9.0 * factor * (1.0 - abs(y_dist) / 6.0)
            vertex.co.z += max(0.0, z_push)
            
        else:
            # Above mouth center (upper lip & nose): pull up
            # pulls most at the lip (y_dist near 0) and decays towards nose
            y_factor = (5.0 - y_dist) / 5.0
            y_factor = max(0.0, min(1.0, y_factor))
            
            factor = x_factor * y_factor
            
            # Deform UP (Y increases in Blender)
            vertex.co.y += 3.0 * factor # Pull up upper lip by 3 cm
            
            # Push inward (Z increases in Blender)
            z_push = 6.0 * factor
            vertex.co.z += z_push
            
        deformed_verts_count += 1

print(f"Procedurally deformed {deformed_verts_count} vertices for shape key 'Mouth_Open'.")

# Export clean FBX
print("Writing compiled rig and clean shape keys back to file...")
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

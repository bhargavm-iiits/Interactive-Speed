import bpy

FBX_INPUT_PATH = r"U:\Summer\Speed\Assets\Meshy_AI_Open_Armed_Dapper_in__biped\Meshy_AI_Open_Armed_Dapper_in__biped_Animation_Talk_with_Right_Hand_Open_withSkin.fbx"

# Clear scene
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()

# Import FBX
bpy.ops.import_scene.fbx(filepath=FBX_INPUT_PATH)

mesh_obj = None
for obj in bpy.context.scene.objects:
    if obj.type == 'MESH' and "Procedural" not in obj.name:
        mesh_obj = obj
        break

if mesh_obj:
    # Print vertices near the face center
    verts = []
    for v in mesh_obj.data.vertices:
        co = v.co
        # Look in center column (-10 < X < 10) and height (130 < Y < 155)
        if -10.0 < co.x < 10.0 and 130.0 < co.y < 155.0 and co.z < -10.0:
            verts.append((v.index, co.x, co.y, co.z))
            
    print(f"Found {len(verts)} vertices in front center column.")
    # Sort by Y (height)
    verts.sort(key=lambda x: x[2])
    print("Lowest 20 vertices (Chin area):")
    for v in verts[:20]:
        print(f"Index {v[0]}: X={v[1]:.2f}, Y={v[2]:.2f}, Z={v[3]:.2f}")
        
    print("\nHighest 20 vertices (Nose/Upper lip area):")
    for v in verts[-20:]:
        print(f"Index {v[0]}: X={v[1]:.2f}, Y={v[2]:.2f}, Z={v[3]:.2f}")

# Exports Gorilla_IK_Rig.blend to Assets/Resources/Rig/GorillaRig.fbx (the game loads it automatically).
# Run ONCE on a PC that has Blender (3.6+):
#   blender -b _Originals/Gorilla_IK_Rig.blend -P Tools/export_rig_fbx.py
# Then commit Assets/Resources/Rig/GorillaRig.fbx. Unity Cloud has no Blender, so it needs the FBX, not the .blend.
import bpy, os

root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
out = os.path.join(root, "Assets", "Resources", "Rig", "GorillaRig.fbx")
os.makedirs(os.path.dirname(out), exist_ok=True)

bpy.ops.object.select_all(action='DESELECT')
for o in bpy.context.scene.objects:
    if o.type in {'ARMATURE', 'MESH'} and not o.hide_viewport:
        o.select_set(True)

bpy.ops.export_scene.fbx(
    filepath=out,
    use_selection=True,
    object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False,
    bake_anim=False,
    apply_scale_options='FBX_SCALE_ALL',
    mesh_smooth_type='FACE',
    use_mesh_modifiers=True,
    path_mode='COPY',
    embed_textures=False,
)
print("Exported", out, os.path.getsize(out) // 1024, "KB")

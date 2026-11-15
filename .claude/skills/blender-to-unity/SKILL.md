---
name: blender-to-unity
description: Makes or edits a 3D asset in Blender (through the blender MCP server) and brings it into this Unity project at the right scale, axis, pivot, naming and folder, with URP materials and colliders. Use when the user asks for a model, prop, character or environment piece from Blender, or an imported model is rotated -90°, tiny or huge, has a bad pivot, or pink materials.
---

# Blender → Unity

Blender is driven through the `blender` MCP server (`.mcp.json`, add-on in `Tools/blender_mcp_addon.py`).
If its tools are missing, tell the user to open Blender, press **Connect to Claude** in the
N-panel BlenderMCP tab, and restart Claude Code. Don't guess at a model you can't see.
Use the viewport screenshot tool after every big change.

## Where files go

| What | Where |
|------|-------|
| `.blend` source files | `BlenderSource/<AssetName>/<AssetName>.blend`, at the repo root, **outside `Assets/`** |
| Rebuild scripts (procedural models) | `Tools/Blender/build_<asset>.py`, run headless: `blender --background --factory-startup --python Tools/Blender/build_<asset>.py` |
| Exported models | `Assets/_Project/Art/Models/<AssetName>/<Prefix>_<AssetName>.fbx` |
| Textures | `Assets/_Project/Art/Models/<AssetName>/Textures/` |
| Materials | `Assets/_Project/Art/Materials/` (shared) or next to the model |
| Prefabs | `Assets/_Project/Prefabs/` |

The `.blend` stays out of `Assets/` because Unity would then import it by launching Blender,
which every teammate and the build machine would need to have installed. LFS already tracks `*.blend`, `*.fbx` and `*.glb`.

Prefixes: `SM_` static mesh, `SK_` skinned mesh, `MAT_` material,
`T_<Name>_BaseColor` / `_Normal` / `_MaskMap` textures, `<Name>_Collider` collision mesh.

## Before export (in Blender)

1. **Units are meters.** 1 Blender unit = 1 Unity unit = 1 m. A door is about 2 m and a player about 1.8 m.
   Check the size with `obj.dimensions`.
2. **Origin is the pivot.** Props sit at the bottom center, doors at the hinge, and characters
   between the feet at world origin.
3. **Apply rotation and scale** (`bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)`).
   Scale should be (1,1,1) and rotation (0,0,0) on every exported object.
4. **Clean the mesh:** recalculate normals outside, remove doubles, make sure there are no
   loose verts and that UVs exist (a second UV channel is only needed for baked lightmaps).
5. **Materials:** one Principled BSDF per material slot, named `MAT_...`. Prefer few slots,
   because each slot is one draw call.

## Export

Static or prop (FBX):

```python
import bpy
bpy.ops.export_scene.fbx(
    filepath=path, use_selection=True,
    object_types={'MESH', 'EMPTY'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    mesh_smooth_type='FACE', use_mesh_modifiers=True,
    add_leaf_bones=False, bake_anim=False,
    path_mode='COPY', embed_textures=False)
```

Rigged or animated: same call but `object_types={'MESH','ARMATURE'}`, `bake_anim=True`,
**`bake_space_transform=False`** (it can break armatures). Then fix the axis in Unity with
`ModelImporter.bakeAxisConversion = true` instead.

GLB (`bpy.ops.export_scene.gltf`) is fine for static props, but Unity needs the glTFast
package for it. Check `Packages/manifest.json` and ask before adding packages.

## Import settings (Unity, via an editor script or the Unity CLI)

- `ModelImporter.globalScale = 1`, `useFileScale = true`.
- Static props: `importAnimation = false`, `animationType = None`. Characters: Generic, or
  Humanoid only when the rig is humanoid. Ask before changing a rig, because it rebuilds the Avatar.
- `materialImportMode = None` and assign URP materials made in Unity, **or** extract the
  materials and switch them to `Universal Render Pipeline/Lit`. Pink means the shader is not URP.
- URP Lit wants **smoothness**, not roughness: smoothness = 1 − roughness, stored in the alpha of
  the metallic map (or the MaskMap).
- Colliders: use primitive colliders on the prefab where they fit. Otherwise use a separate
  low-poly `<Name>_Collider` mesh on a convex `MeshCollider`, never the render mesh.
  In multiplayer, the server's physics uses these too, so keep them simple and identical for all peers.

## Verify (do it, don't hand it over)

With the Unity CLI (`unity-cli` skill), after importing:
- The root transform has rotation (0,0,0) and scale (1,1,1), with no -90° X.
- `Renderer.bounds.size` matches the size measured in Blender (±1%).
- The pivot sits where step 2 says.
- No material uses a non-URP shader.
- Take a screenshot of the prefab next to a 1 m reference cube and look at it.

Report the size, triangle count and file paths in your summary.

## Gotchas

- **Rotated -90° on X in Unity:** the export forgot `bake_space_transform` (static), or
  `bakeAxisConversion` is off (rigged).
- **100× too small or big:** wrong `apply_scale_options`, or the Blender scene unit scale is not 1.0.
- **Faceted or black shading:** the normals are flipped, or smoothing was not exported (`mesh_smooth_type`).
- **Textures missing:** they were exported with absolute paths. Use `path_mode='COPY'` and put
  them in the `Textures/` folder.
- **Never** edit `.meta` files to change import settings. Use `ModelImporter` from an editor script.

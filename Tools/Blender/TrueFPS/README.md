# ENFILADE animation authoring workspace

User entry point: `ArtSource/TrueFPS/START_ANIMATING.cmd`.
User guide: `ArtSource/TrueFPS/START_HERE.md`.

Current deliverable: `ArtSource/TrueFPS/ENFILADE_TrueFPS_FullRig.blend`.
It exposes all 54 humanoid joints, including 30 finger joints and the upper/lower
arm and leg bones. Four keyable IK/FK switches support direct FK joint rotation.
`upgrade_full_rig.py` upgrades a saved workspace to a new output without replacing
the input; all original bind matrices and hierarchy are retained.

The workspace is authored for Blender 4.5 LTS and KazusaBand's existing FBX.
It adds properly aligned mechanism chains instead of changing imported bone
rolls or lengths. Driver bones transfer solved transforms to the original
371-bone hierarchy. Control bones are never inserted into that hierarchy.

Build from the project root (the builder refuses to overwrite existing files):

```powershell
& 'F:\SteamLibrary\steamapps\common\Blender\blender.exe' --background --factory-startup --python-exit-code 1 --python .\Tools\Blender\TrueFPS\build_workspace.py -- 'F:\GameProject\ENFILADE\ArtSource\TrueFPS\NewWorkspace.blend'
```

To build a different output path, append `-- 'absolute/path/new.blend'`.
The source FBX, its Unity metadata and existing game files are never edited.
The launcher registers the local tools for this Blender session without
changing Blender's automatic script execution preferences.

The export helper samples evaluated original bone matrices per frame onto
a temporary original-only skeleton, writes one animation take and restores
the authoring scene. The original rest matrices must remain unchanged so
Unity can use the existing KazusaBandAvatar via Copy From Other Avatar.

Head preview: source eye midpoint + 0.02 source metres forward, 90 degree
horizontal FOV, 16:9. A local view excludes head meshes only in the camera
viewport. This is authored head motion, not a simulation of Unity's mouse
look, head stabilization, retargeting or runtime IK.

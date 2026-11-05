# Playback appearance and portable Inspector

Select a baked prefab or a scene instance, then open **RVFX Piece Transform Playback > Debris**.
Each existing row contains a **Mesh** and **Material**. Replace either asset by dragging it into
the field or using the object picker. Changes can be previewed without recording or baking again.

Recorded positions, rotations, spawn times and lifetimes stay unchanged. Replacing a visual mesh
does not recalculate physics, Collision Playback or Adaptive landing. Choose a similar size and
pivot; a much larger mesh can intersect its surroundings. One material is used for all submeshes
of that entry. GPU indirect rendering transfers supported surface properties into the RVFX
indirect shader; arbitrary custom shader behavior is not transferred.

Slots remain in their baked order. This Inspector does not add, remove or reorder them. Clearing
a Mesh uses a fallback box for that slot without remapping the other slots. Rebuild from the Rig
when changing the slot layout or recorded motion.

Edit outside Play Mode to save changes. Editing a scene instance creates prefab overrides;
apply those overrides deliberately to update the shared prefab. Rebuilding the playback prefab
from its Rig replaces prefab-level appearance settings with the authoring setup.

## Transfer to a project without the authoring tool

**Package for Another Project > Include Playback Runtime** now includes the playback Inspector
as well as runtime DLLs. Preview, playback settings, Effects and Debris pairs remain editable.
The portable Inspector is Editor-only and does not include authoring or baking windows. If the
full tool is installed, its Inspector takes precedence.

Both Inspectors include the same [Transform validation and particle scaling warnings](PlaybackTransforms.md).
Exact allows XYZ rotation; Adaptive Landing allows only world-up-aligned Y rotation.
Both require positive uniform scale including parents, with no shear. An invalid placement
blocks Play and scrubbing. At world scale other than 1, enabled effect prefabs using Local/Shape
particle scaling show a Warning directing the user to **Main > Scaling Mode = Hierarchy**.
The warning permits playback. Content Only does not install these updated runtime/Inspector controls.

Older transfers that show only the default Unity Script Inspector need a new transfer with
**Include Playback Runtime** enabled. Import its matching runtime and Inspector together.
Later **Content Only** packages keep the already-installed runtime and Inspector. They do not
install or update either. Review Unity's import selection before overwriting locally edited assets.

The destination must use the matching render pipeline. Packaging does not convert materials.
Use **Tools > RVFX > Playback > Validate Imported Packages**, then test actual playback in the
destination project.

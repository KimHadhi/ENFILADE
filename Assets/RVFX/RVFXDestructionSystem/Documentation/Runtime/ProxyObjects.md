# Proxy Objects

In **Destruction Rig > Base Simulation > Optional Features > Proxy Objects**, use **+ Add** to add a Box, Capsule, or existing low-resolution simulation object. Connect a **Playback Target** to show a detailed mesh or prefab using the Proxy's recorded motion.

- Each enabled mapping uses one Rigidbody at its Proxy root and one or more simple Colliders. Moving Mesh Colliders must be convex. Configure mass, gravity and Anchored in the tab; edit collider shape and Rigidbody constraints on the Proxy.
- Disabled mappings exclude both the Proxy and its Target from simulation and output. Closing simulation restores their original state.
- Scene Proxies and connected scene Targets belong under the Rig, outside Originals and Fracture. Deleting a Proxy node removes its simulation object through Undo. Deleting a Target node disconnects it without deleting the visual object. Shared or protected source references must be disconnected/reassigned before a Proxy can be deleted.
- Preview Display selects **Proxy**, **Target**, or **Both**. Display choice does not change simulated physics. The original Target hierarchy is suspended during simulation; a rendering-only copy follows the Proxy when selected.
- Target Alignment stores relative position, rotation and scale. Connecting a scene Target aligns its position and rotation to the Proxy while preserving its world scale; prefab assets start at the Proxy origin. To retain a custom scene arrangement, position the Target and use **Keep Scene Alignment**. One Proxy records rigid root motion. Articulated movement requires separately configured body tracks; assigning one character does not automatically record its skeleton.

**GPU Playback > Base Recording** records VAT Pieces and CPU Proxy tracks in the same fixed-timestep simulation. FPS changes sampling density. Debris recording reuses this Base and does not replace Proxy tracks. Changing Proxy physics requires a new Base recording; changing its rendering Target or display mode does not.

**Build** creates the normal Playback prefab plus a CPU Transform data asset and mapped Target children. Proxy meshes never enter the combined VAT mesh. Rendering copies preserve mesh renderers, skinned renderers/bones and materials; physics and behaviours, including Animator and user scripts, are stripped. Use the normal playback controls (`Play`, pause, frame scrubbing, reset): the parent player supplies one shared frame for VAT, Debris and CPU tracks.

Runtime performs recorded Transform playback. Small track sets use managed CPU interpolation; sets of at least 32 tracks use parallel Transform jobs when worker threads are available, with Burst compilation where supported. This path does not require compute shaders. Native buffers are released on disable, destruction and editor reload.

The owning playback's placement rules also apply to these CPU tracks: positive uniform scale,
XYZ rotation for Exact, and world-up-aligned Y rotation for Adaptive Landing. Invalid root or
parent transforms pause playback and disable the tracks until corrected. See [Playback transforms](PlaybackTransforms.md).

The current Rig workflow requires at least one built Fracture target alongside Proxy Objects. Proxy-only packages and automatic ragdoll bone remapping are not part of this workflow.

# Collision Playback

Keep all visible fracture geometry in VAT, and add recorded, renderer-free Colliders only where gameplay needs contact.

## Authoring

1. Record **GPU Playback > 2. Base Recording** as usual.
2. Open **Optional Features > Collision**. Select large Base Pieces in the Scene or Hierarchy and click **Add Scene Selection**. Use the list to select the added tracks. Nothing is included by default.
3. Choose **Simulation Collider** (recorded Box/Sphere/Capsule/convex Mesh colliders), **Box** (recorded piece bounds), or **Convex Hull** (the recorded piece mesh cooked as a convex Collider). Simulation Collider falls back to a Box if no supported Collider was recorded. Box is the cheapest and most predictable option for bridge decks and large blocks.
4. Inherit the recorded layers, or override the collision layer for a Piece. Verify the project's collision layer matrix against your units. Recorded physics materials are copied into the package.
5. Scrub or play the preview with **Show Colliders** enabled. Preview draws wireframes, not physical Colliders, so it cannot change the Base simulation. Configure any other optional features you need, then use **3. Build**.

Selection, shape, and layer changes reuse the approved Base recording. They do not re-simulate Base or regenerate Debris. Re-fracturing/removing a selected source invalidates its mapping: remove the stale selection and record/select the new Pieces. Existing in-memory recordings made before this feature need to be recorded once again.

## Output and playback

The normal VAT renderer is unchanged. The output prefab additionally contains a **Collision Playback** hierarchy with Kinematic Rigidbodies and Collider components only: no MeshRenderer, SkinnedMeshRenderer or MeshFilter. A separate Collision Playback data asset stores only the selected tracks; convex meshes and physics materials are saved as subassets.

The existing playback controls drive everything together. No separate collision API is necessary:

```csharp
using RVFX.DestructionSystem;

var playback = RVFXRigAPI.GetBakedPlayback(bakedObject);
playback.Play();
// playback.PlayDelayed(2f);
// playback.Pause();
// playback.Stop();     // Return to the starting pose.
// playback.Restart();
```

- Collision-enabled prefabs advance their shared VAT/Proxy/Debris timeline on Unity's fixed physics clock. The Collider uses kinematic MovePosition/MoveRotation; it does not re-simulate the fracture. Rendering therefore follows the physics step cadence too. No global project timestep setting is changed.
- Start delay holds the starting pose with collision available. Pause holds the current pose. Reset, Stop, reactivation, explicit seeking and loop wrap use a teleport instead of sweeping a Collider through the entire rewind path.
- Colliders stop when their original Piece is replaced by Secondary Fracturing or removed. Secondary child Pieces and particle/mesh Debris are not automatically assigned Colliders in this version.
- Otherwise, the Collider remains at the last pose when playback completes. Disabling the playback component/GameObject disables collisions and restores the starting Transform. Editor playback keeps physical collision detection off, preventing baked prefabs from affecting subsequent authoring bakes.

## Gameplay boundaries

Collision can support or push dynamic bodies, but those bodies cannot change the baked trajectory. A collapsing bridge can remove physical support beneath a unit. Character controllers must implement their own gravity/grounding; NavMesh Agents need game-side transition to falling/ragdoll and navigation updates. This feature does not create dynamic NavMeshes or automatic character behaviour.

Use positive **uniform** scale on the playback root and its parents. Exact supports XYZ rotation; with **Adaptive Landing** enabled, only world-up-aligned Y rotation is supported. The composed Transform is checked, including parents and shear. Non-uniform, zero/negative scale or Adaptive tilt produces an **Error that blocks the whole playback**, not just a collision warning. If it becomes invalid while playing, playback pauses and collision tracks are disabled. Correct the Transform and press Play to resume. See [Playback transforms](PlaybackTransforms.md).

Use a small number of large contact surfaces. Runtime collision detection/solving still costs CPU; no compute shader or DOTS Physics package is required. Continuous Speculative mode helps moving kinematic bodies, but low recording FPS, thin surfaces, fast playback and abrupt time-remap curves still require gameplay-specific testing. Looping or seeking into an occupied space can cause overlap; only rewind movement itself is excluded from sweeping.

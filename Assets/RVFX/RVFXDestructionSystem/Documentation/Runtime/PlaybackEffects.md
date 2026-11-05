# Effects on baked playback

Select a baked prefab or a scene instance and open **RVFX Piece Transform Playback > Effects**.
Each baked filter shows its name, recorded trigger type and connected effect slots.

## Replace an effect without rebaking motion

1. Drag a particle prefab from the Project into the slot's **Prefab** field.
2. Adjust **Scale**. Open **Lifetime & Placement** for emission, offset and alignment settings.
3. Scrub or play the preview. The effect schedule and particle pool are rebuilt at the current time; the recorded body motion and trigger data are preserved.
4. Save outside Play Mode. A scene instance stores prefab overrides; apply them to the source prefab only when that change should affect its other instances.

Disable an individual slot or its filter to mute it. **Add Effect** attaches another prefab to the same recorded filter. **Remove** disconnects a slot without deleting its asset or recorded events. A slot with no prefab emits nothing. Filters and new trigger locations/times are authored in the Rig, not in this panel. If no filters were baked, configure Rig > GPU Playback > Effects and build the playback prefab first.

**Refresh Effects** recreates the current particles after changes inside an effect prefab or its material. To change dust color, opacity or texture, edit the material assigned to that prefab's ParticleSystem Renderer. Duplicate the effect prefab and material first if only this playback should have a different appearance. Play Mode edits are temporary. Rebuilding the baked prefab from its Rig replaces local playback effect edits with the Rig's settings.

## Lifetime and placement

| Control | Behavior |
| --- | --- |
| Lifetime: Auto | Lets non-looping particle hierarchies finish naturally. Looping systems use the emission limit. |
| Lifetime: Timed | Stops new emission at the limit, then waits for remaining particles and supported particle trails. |
| Emission Limit (s) | Maximum emission time for loops in Auto mode, or for all systems in Timed mode. |
| World Offset | Moves the recorded spawn position along world X, Y and Z. |
| Align Impact | Aligns local +Y to the recorded surface normal; not shown for manual-point filters. |
| Follow Playback | Parents the effect to the playback root, not an individual fragment. Off keeps it in world space during Play Mode. |

The stored event timeline is unchanged. Replacing a prefab with a longer-lived effect or adding slots can change which events pass the existing active-effect caps and distance reservations. Review the preview after a swap.

Legacy exports without modern filters expose existing prefab slots and their legacy total **Lifetime (s)**. Rebuild from the Rig to use Auto/Timed emission with natural particle tails.

## Supported particle hierarchy

The prefab root does not need a ParticleSystem. RVFX gathers ParticleSystems through the entire child hierarchy. Multiple active children, nested transforms and sub emitters are supported. Authored inactive children stay inactive; they are not automatically enabled. Sub emitters are driven by their parent particle system rather than simulated a second time. Fixed random seeds make ordinary particle scrubbing repeatable with unchanged settings and scene conditions.

This is ParticleSystem timeline control, not a general prefab animation player. Animator, VFX Graph, audio and custom scripts that spawn or enable objects are not automatically synchronized. A prefab with no ParticleSystem produces a warning. Use an effect compatible with the project's render pipeline.

## Scaling the playback instance

When the playback's world scale differs from 1, set **Main > Scaling Mode = Hierarchy** on every ParticleSystem in each enabled effect prefab. The Inspector checks nested children, including authored inactive children, and lists Local/Shape systems in a **Warning** with a **Select Particle System** button. Disabled filters and slots are ignored. Inactive children are not enabled by this check. Playback remains allowed; independent Local/Shape sizing can be intentional.

A scale change rebuilds live effects at the current sampled time, preserving the body clock and particle tail. Hierarchy controls scale inheritance, not world-space gravity/velocity settings. **Adaptive Landing does not retime particle events to corrected contacts:** events still use their recorded times/positions. Verify effects at each intended height and scale. See [Playback transforms](PlaybackTransforms.md) for Exact XYZ rotation, Adaptive Y-only rotation and positive uniform scale requirements.

## Playback time and cleanup

Particles follow the sampled playback time. Forward playback advances by the elapsed amount; seeking backward rebuilds the effect state. Speed and Pause also affect particles. Normal Play Mode uses scaled game time.

When non-looping body playback ends, already emitted particles keep advancing until they finish. Pause freezes this tail too. Loop clears the previous cycle's effects when restarting. Stop clears effects; refreshing a stopped or disabled owner does not restart them. Disabling or destroying the owner disposes its effect instances and pool. A prefab swap also refreshes lifetime estimates and removes the old instances.

When transferring a baked prefab, leave **Include Connected Effects** enabled to package the assigned effect dependencies. Authoring Inspector changes require the updated editor package; a runtime-only content package is not an authoring upgrade.

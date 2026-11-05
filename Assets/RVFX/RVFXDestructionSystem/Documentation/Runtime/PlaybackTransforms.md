# Playback placement: scale, rotation and effects

Exact baked playback supports arbitrary XYZ rotation and positive uniform scale.
Adaptive Landing supports Y-axis rotation and positive uniform scale. Its up axis
must remain aligned with world up. Turn Adaptive Landing off to use the original
recording with arbitrary rotation. Rotating Exact playback also rotates the
recorded falling direction; it does not simulate new gravity.

Use equal positive X, Y and Z scale values, for example (0.5, 0.5, 0.5) or
(2, 2, 2). Parent transforms are included. Zero/negative scale, non-uniform world
scale, and shear introduced by a rotated child under a stretched parent are not
supported. Small numerical differences are tolerated.

The Playback Inspector displays an Error for unsupported placement and disables
preview controls. Runtime Play returns false and logs the reason once for the
current error. If placement becomes invalid during playback, playback pauses,
effect instances are cleared, debris draw submission stops and collision tracks
are disabled. Correct the Transform and press Play to resume. RVFX does not
automatically change the user's Transform. Manual frame application is guarded too.

Uniform scale changes preserve recorded duration. Instanced debris uses the same
root transform as the body, and Adaptive caches are rebuilt for a changed placement.
Active effect instances are rebuilt at the current sampled time when scale changes.
Prefer setting placement before Play; continuous Transform animation is not a
replacement for authoring physical motion.

## Particle scaling warning

When world scale differs from 1, enabled effect prefabs are checked recursively,
including nested/inactive child Particle Systems and prefabs without a Particle
System at the root. Set **Main > Scaling Mode = Hierarchy** on each Particle System
to scale the effect with the playback object. The Inspector identifies Local/Shape
systems and offers Select Particle System. This is a Warning, not a playback error:
independent effect sizing can be intentional. Disabled effect rules are ignored.

Hierarchy controls scale inheritance, not world-space gravity or velocity settings
inside the effect. Adjust those settings for the intended orientation. Particle
effects still use recorded event times/positions: Adaptive Landing does not retime
particle events to a newly corrected landing. Hierarchy does not solve that timing
limitation.

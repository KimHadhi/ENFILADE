# Group activation during authoring

Open **Fracture > Fracture Groups** and select a **group name**. Its Group Settings control Initial State. Selecting an object or fracture operation still opens Fracture Steps and the existing Rebuild controls.

- **Active** is the default and preserves existing scene behavior.
- **Inactive** keeps generated pieces visible and collidable but holds their motion until activation. It does not disable their GameObjects.

For an inactive group, enable any of these independent conditions:

| Condition | Test |
|---|---|
| Force Impulse | Incoming authored force impulse reaches the threshold. |
| Collision Impact | Reported contact impact, after collision multipliers, reaches the threshold. |
| Damage | Incoming damage reaches the threshold, either in a Single Hit or Accumulated per waiting piece. |

Any enabled condition is sufficient. A zero input never activates a zero threshold. Turning all conditions off leaves explicit Force activation available. Activation damage is separate from constraint durability and Secondary Fracture damage; it is measured before constraints consume an incoming hit.

## Activate Together

- **Hit Pieces:** release directly affected pieces.
- **Connected Chunk:** follow live constraints within the same group.
- **Entire Group:** release that group's pieces.

Interaction target filters still apply. Scope expansion does not extend a Force's radius or add impulse to unhit pieces. Activation removes only the initial waiting condition: Anchors and Constraints remain effective. A connected inactive group or Anchor may hold a shared rigid chunk stationary even after some pieces activate.

Secondary Fracture waits while a piece is inactive; it cannot bypass the activation condition by replacing that piece with moving children. After activation, its own damage and depth rules apply normally.

## Force controls

In **Interactions**, select a Force and choose its **Activation > Mode**:

- **Follow Group Rules:** use the target group's enabled thresholds.
- **Activate Affected:** bypass thresholds, using the group's Activate Together scope.
- **Do Not Activate:** leave waiting pieces held. This event does not contribute to their activation-damage total; normal constraint damage and forces on already active pieces still apply.

The Force's time, target connections, radius, direction, impulse and damage retain their existing meanings. Damage alone can activate without pushing; impulse alone can activate without adding explicit Damage. Collision events from moving colliders or other pieces use Collision Impact rather than the Force Impulse condition. Collision reporting still uses its configured arming/cooldown and multipliers.

## Save, preview and bake

Group and Force settings are included in the Rig's Save/Load setup. Each new preview or recording begins with fresh initial states and activation-damage totals. Existing setups default to Active and Follow Group Rules.

After changing activation settings, record Base again, review the transition, then rebuild the GPU playback prefab. The resulting playback contains recorded motion; it does not evaluate these thresholds at runtime. Existing Fracture, Anchors, Constraints, Proxy, Collision Playback and Secondary Debris retain their separate roles.

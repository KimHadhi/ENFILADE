using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// A one-off clip's own travel moves the zombie: a swing's lunge, a stagger's steps back, the step a getting up ends on.
/// The user, 2026-11-14, on everything made in Blender that was lost on its way into the game ("근본적으로"): a zombie
/// clip's travel came in as root motion (inPlace 1) and the body, moved by its agent, dropped it, so a lunge made in the
/// zombie workspace (or a Mixamo download's step) played on the spot with the feet sliding under it.
/// The clips in the slot folders still come in with their travel out of the pose (ModelImportRules), so the pose stays
/// over the zombie's feet and nothing snaps back as a clip eases out. ZombieClipFolders measures each one-off clip's
/// travel as it comes in (ZombieLooks.travels); here, while such a clip plays, its share of the body (its weight on the
/// top mixer) of the travel since the last frame moves the zombie. Only on the PC that moves the enemy (the host, or a
/// lone PC), by its NavMeshAgent and stopped short of the walk mesh's edge, so it never goes through a wall or off a
/// ledge. The other PCs follow the host's place as they follow its walk (EnemyNetwork): every PC shows the zombie in the
/// same place. They reckon the same travel only so that it doesn't count as walking (Move). Loops (walk, idle) keep
/// theirs dropped, as the agent walks them, and the packs' clips have none measured: they play as before.
/// Tables/ZombieClips clipTravel: the share of it that moves the zombie (0 puts the old way back).
/// </summary>
public sealed partial class ZombieBody
{
    // How far short of the walk mesh's edge a clip's travel stops (m), as a hit's push does (EnemyHitReaction): the agent
    // is never left standing on the very edge.
    private const float TravelEdge = .02f;

    [SerializeField, Range(0f, 1f), Tooltip("How much of a one-off clip's own travel (a swing's lunge, a stagger's steps back) moves " +
        "the zombie (Tables/ZombieClips clipTravel). 1: as the clip was made; 0: none, the pose plays where it stands as it used to.")]
    private float clipTravel = 1f;

    // A clip on the top mixer that travels: its input, its playable, its travel, and its time and whether it showed as
    // last seen.
    private struct Traveller
    {
        public int input;
        public AnimationClipPlayable playable;
        public ZombieLooks.ClipTravel travel;
        public float time;
        public bool shown;
    }

    private readonly List<Traveller> travellers = new List<Traveller>();
    // What the last frame's clips travelled, in the world: moved on the PC that moves the enemy, only reckoned on a copy
    // (whose place is the host's). The next Move leaves it out of the walking pace.
    private Vector3 travelled;

    // Clip made a playable on the top mixer: if its clip travels, it is followed from now on, in place of whatever was on
    // that input before (a lost arm's swing takes the last one's place).
    private void NoteTraveller(AnimationClipPlayable playable, AnimationClip clip, AnimationMixerPlayable mixer, int input)
    {
        if (!top.IsValid() || mixer.GetHandle() != top.GetHandle()) return;
        travellers.RemoveAll(each => each.input == input);
        var looks = ZombieLooks.Instance;
        if (looks != null && looks.TryGetTravel(clip, out var travel))
            travellers.Add(new Traveller { input = input, playable = playable, travel = travel });
    }

    /// <summary>
    /// From Animate, once this frame's clip times and weights are set (Weigh): each travelling clip's travel since the
    /// last frame, by its share of the body, moves the zombie here, or is only reckoned (a copy).
    /// </summary>
    private void Travel()
    {
        travelled = Vector3.zero;
        if (travellers.Count == 0) return;
        // Down (its doll moves it), held, lying or in a paired execution: what moves it then is not a clip of its own.
        bool still = down || Pinned || IsExecuted || animator == null || !animator.enabled;
        Vector2 moved = Vector2.zero;
        for (int i = 0; i < travellers.Count; i++)
        {
            var each = travellers[i];
            bool valid = each.playable.IsValid();
            float weight = still || !valid ? 0f : top.GetInputWeight(each.input);
            float time = valid ? (float)each.playable.GetTime() : 0f;
            // Only while it goes on: begun again (a new swing over the last one's follow-through) or not shown last frame,
            // it starts from where it is now, so a clip that starts over never pulls the zombie back to where it began.
            if (weight > 0f && each.shown && time > each.time) moved += (each.travel.At(time) - each.travel.At(each.time)) * weight;
            each.time = time;
            each.shown = weight > 0f;
            travellers[i] = each;
        }
        float share = Mathf.Clamp01(clipTravel);
        if (share <= 0f || moved.sqrMagnitude <= 0f) return;
        // From the avatar's own units to metres on this body (a brute's bigger frame goes further), along the way it faces.
        var body = animator.transform;
        Vector3 step = body.rotation * new Vector3(moved.x, 0f, moved.y) * (animator.humanScale * body.lossyScale.y * share);
        step.y = 0f;
        if (!MovesEnemy)
        {
            travelled = step;
            return;
        }
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        // An agent that doesn't carry the body just now (a wave's pause holds it, a drop or a climb moves the body itself:
        // EnemyHealth, ZombieDrop) is left alone: moved under a body that stays, the body would jump to it afterwards.
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || !agent.updatePosition) return;
        Vector3 from = agent.nextPosition;
        if (NavMesh.Raycast(from, from + step, out var edge, agent.areaMask))
            step = step.normalized * Mathf.Max(0f, Vector3.Distance(from, edge.position) - TravelEdge);
        agent.Move(step);
        travelled = step;
    }
}

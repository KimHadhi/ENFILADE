using UnityEngine;

// ZombieMind: Hearing. One part of the class; the rest is in ZombieMind.cs and the files beside it.
public sealed partial class ZombieMind
{
    private static void Hear(NoiseEvent noise)
    {
        if (noise.Lure) { HearLure(noise); return; }
        var source = noise.Source != null ? noise.Source.GetComponentInParent<PlayerMovement>() : null;
        if (source == null || !Alive(source)) return;
        foreach (var mind in minds) if (mind != null && mind.Thinks && !mind.horde) mind.Heard(noise, source);
    }

    // A thrown stone landing (PlayerThrow): every one not awake that hears it turns to watch where it fell, its mind on it
    // (lureGlance, lureFocus), and the nearest then goes over to look. It never says where the thrower is, nor wakes
    // anyone, nor marks anyone on the thrower's stealth HUD: a zombie facing the way past is turned off it, which is what
    // the stone is for (user, 2026-11-11: "좀비가 내 쪽을 바라보고 있으면 접근 조차 불가능 ... 돌로 시선을 그쪽으로
    // 자연스럽게 옮기고 싶은데"). Advanced Stealth: "The nearest guard goes to check the object; others turn to look".
    private static void HearLure(in NoiseEvent noise)
    {
        if (noise.Gather) { HearGather(noise); return; }
        ZombieMind nearest = null;
        float best = float.MaxValue;
        foreach (var mind in minds)
        {
            // Asleep, a stone's clack doesn't stir it; behind bars it couldn't go and look.
            if (mind == null || !mind.Thinks || mind.horde || mind.IsAwake || mind.Asleep || mind.caged) continue;
            if (mind.melee != null && mind.melee.IsStunned) continue;
            if (!mind.Reaches(noise, out float distance, out _)) continue;
            if (distance < best) { best = distance; nearest = mind; }
            mind.distractedUntil = Time.time + mind.lureGlance;
            mind.Glance(noise.Point, null, mind.lureGlance);
        }
        if (nearest != null) nearest.Investigate(noise.Point, null);
    }

    // A lure that goes on (a TV, a radio, an announcement: GimmickLure, which makes it again every second or so): every one
    // not awake that hears it stares a moment and walks over, each to its own place round it so they bunch there rather
    // than stand on one spot, and looks round there a good while (lureLook) before it settles. One already on its way to
    // it isn't sent again. The sleeping sleep on. PAYDAY 3's speakers, Guts & Blackpowder's music box.
    private static void HearGather(in NoiseEvent noise)
    {
        foreach (var mind in minds)
        {
            if (mind == null || !mind.Thinks || mind.horde || mind.IsAwake || mind.Asleep || mind.caged) continue;
            if (mind.melee != null && mind.melee.IsStunned) continue;
            if (mind.lured && (mind.lureAt - noise.GatherPoint).sqrMagnitude < GatherRound * GatherRound * 4f) continue;
            if (!mind.Reaches(noise, out _, out _)) continue;
            // Where the sound calls them: where it is heard from, or the place it pages (NoiseEvent.GatherPoint).
            Vector2 round = Random.insideUnitCircle * GatherRound;
            mind.distractedUntil = Time.time + mind.lureGlance;
            mind.Investigate(noise.GatherPoint + new Vector3(round.x, 0f, round.y), null);
        }
    }

    // How far round a gathering lure each comes to stand (m).
    private const float GatherRound = 2.5f;

    /// <summary>For the throw's aim (StealthView): whether this zombie, not awake, would hear this sound now.</summary>
    internal bool Hears(in NoiseEvent noise) => !IsAwake && !caged && Living && Reaches(noise, out _, out _);

    private void Heard(in NoiseEvent noise, PlayerMovement source)
    {
        // Behind bars it rages at everything already: no sound sets it looking (Cage).
        if (caged) return;
        // A crouched player's small sounds (a sneaking step, crouching, a lean) don't carry to it: crouched steps alerted
        // a zombie a step behind it, which then turned, saw them that close and woke (user, 2026-11-11: "앉아서 가도 금방
        // 눈치챈다"). Advanced Stealth silences crouched footsteps the same way. A dodge made from a crouch is crouched too
        // (IsCrouchStance), and its sound is a crouched one (SoundCues DodgeLow).
        if (source.IsCrouchStance && noise.Radius <= sneakSilent) return;
        // Coughing in blinding gas it takes nothing in (ZombieEffects.Blind).
        if (gassed) return;
        if (!Reaches(noise, out float distance, out float reach)) return;
        float loudness = 1f - distance / Mathf.Max(.01f, reach);
        if (IsAwake)
        {
            // Its quarry gives itself away.
            if (source == focus) { lastSeen = Time.time; lastKnown = source.transform.position; }
            return;
        }
        if (melee != null && melee.IsStunned) return;
        float gained = hearGain * loudness;
        suspicion += gained;
        focus = noticed = source;
        stimulus = noise.Point;
        if (suspicion >= 1f) Wake(source);
        else if (suspicion >= StirAt) { glanceUntil = 0f; Alert(noise.Point); }
        // Too faint to stop for, it still turns its head that way a moment, and may see who made it.
        else if (gained >= glanceAt && !Asleep) Glance(noise.Point, source, glanceSeconds);
    }

    // Whether this sound reaches its ears, how far off it is and how far it carries to it (m): half as far through a wall.
    private bool Reaches(in NoiseEvent noise, out float distance, out float reach)
    {
        // Eating or dozing it hears less, a stone or a knock too (BusyHear): asleep it may sleep through one.
        reach = noise.Radius * (noise.Lure ? lureHearShare : Mood == ZombieMood.Calm ? calmHearShare : hearShare) * BusyHear;
        Vector3 ear = Eye();
        distance = Vector3.Distance(noise.Point, ear);
        if (distance > reach) return false;
        Vector3 path = ear - noise.Point;
        if (distance > .01f && Physics.Raycast(noise.Point, path / distance, distance, Hearing, QueryTriggerInteraction.Ignore))
        {
            reach *= wallShare;
            if (distance > reach) return false;
        }
        return true;
    }

    /// <summary>
    /// For the stealth view (StealthView): how far a sound of the local player's carries to a calm zombie in the open (m),
    /// and whether any zombie that isn't awake hears it now. 0 when it doesn't carry at all (a crouched player's small
    /// sounds), or with no zombie about.
    /// </summary>
    internal static float CalmReach(in NoiseEvent noise, PlayerMovement source, out bool heard)
    {
        heard = false;
        ZombieMind any = null;
        foreach (var mind in minds) if (mind != null) { any = mind; break; }
        if (any == null) return 0f;
        if (!noise.Lure && source != null && source.IsCrouchStance && noise.Radius <= any.sneakSilent) return 0f;
        foreach (var mind in minds)
        {
            if (mind == null || mind.IsAwake || mind.health == null || !mind.health.IsAlive) continue;
            if (mind.Reaches(noise, out _, out _)) { heard = true; break; }
        }
        return noise.Radius * (noise.Lure ? any.lureHearShare : any.calmHearShare);
    }
}

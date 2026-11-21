using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: shots, punches, shoves, evades and executions as the other PCs see them. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    private void OnExecutionStarted(MeleeCombatTarget victim) => ExecutionStartedRpc();

    private void OnHit(DamageHit hit, DamageResult result)
    {
        // A hold taking hold is no wound: friends see no blood on them for it either.
        if (result == DamageResult.Damaged && !hit.TakesHold)
            HurtRpc(hit.Point, hit.Direction, hit.Kind == DamageKind.Fall, receiver.LastHitShielded, hit.EventSeed);
    }

    // Each of a shotgun's pellets comes on its own, numbered, so the copy's gun fires once and each pellet leaves its mark.
    // With the shooter's penetration factor (ShotPierce), so the copy's round gets as far through as theirs did.
    [Rpc(SendTo.NotMe)]
    private void ShotRpc(Vector3 origin, Vector3 direction, byte pellet, ushort pierce, uint seed)
        => weapon.ShowShot(origin, direction, pellet, pierce / 100f, seed);

    // This PC's factor on its guns' penetration (a Ghost's 과관통), in hundredths: only this PC knows how far its rounds get
    // through, and friends' PCs trace each shot again to draw it.
    private static ushort ShotPierce => (ushort)Mathf.Clamp(Mathf.RoundToInt(PlayerPerks.OwnPierce * 100f), 0, ushort.MaxValue);

    // A swing as the other PCs see it (the user's rule: what one sees is what the others see): its wind-up beginning,
    // which their copy's own timeline then carries on into the charge with its tremble (SwingTimeline, MeleeKnife), the
    // swing leaving with how charged it was, which move it is (a combo's strike showed as a plain slash before) and how
    // much further ahead the arm reaches to meet whom it is aimed at (the blade is what hits, so the arm is shown where
    // it was on their screen), and a wind-up or a charge let go of with no swing (the guard, V, a move). Their PC judged
    // what it hit.
    [Rpc(SendTo.NotMe)]
    private void PunchRpc(float charge, byte strike, float reachIn) => melee.ShowPunch(charge, (ComboStrike)strike, reachIn);

    [Rpc(SendTo.NotMe)]
    private void WindRpc() => melee.ShowWind();

    [Rpc(SendTo.NotMe)]
    private void SwingDroppedRpc() => melee.ShowDrop();

    // The counter's back elbow on the attacker's side: the copy's fist pulls back and its arm drives the elbow behind, as
    // their own body does. Its swing and hit come as fist sounds.
    [Rpc(SendTo.NotMe)]
    private void ElbowRpc(bool right) => melee.ShowElbow(right ? 1 : -1);

    [Rpc(SendTo.NotMe)]
    private void FistSoundRpc(FistSound sound) => fists.Play(sound);

    [Rpc(SendTo.NotMe)]
    private void ShoveSoundRpc(ShoveImpactKind impact) => shoves.Play(impact);

    // The copy's legs play the kick (PlayerBody.Kick); the kicker's PC judged what it hit, and its sounds come as above.
    [Rpc(SendTo.NotMe)]
    private void KickRpc()
    {
        if (kicks != null) kicks.ShowKick();
    }

    // The copy's gun swings through the shove's bash (FPSBody.Bash), as the shover sees their own; their PC judged what it
    // hit (the user's rule: what one sees is what the others see).
    [Rpc(SendTo.NotMe)]
    private void BashRpc()
    {
        if (kicks != null) kicks.ShowBash();
    }

    // The dodger's PC judged the evade: every other PC's copy of them turns that dodge's streak red too (DodgeDash), as
    // the dodger sees it (the user's rule: what one sees is what the others see). side: a Q/E slip's lean side, 0 for a step.
    [Rpc(SendTo.NotMe)]
    private void EvadedRpc(sbyte side)
    {
        if (dodgeLook != null) dodgeLook.ShowEvaded(side);
    }

    // The player struck the guard's parry pose (a Knight's cover turning a blow, GuardPose): every other PC's copy of them
    // strikes it too, toward the same blow, as they see it themselves.
    [Rpc(SendTo.NotMe)]
    private void GuardPoseRpc(Vector3 threat)
    {
        if (guardPose != null) guardPose.Show(threat);
    }

    [Rpc(SendTo.NotMe)]
    private void ExecutionStartedRpc() => CombatCueAudio.Play(gameObject, CombatCue.Grab, 1f, true);

    private void OnPairBegan(ExecutionPairPlayer.Shared shared)
        => PairBeganRpc(shared.Pair, shared.Victim, shared.Anchor, shared.Yaw, shared.Clock, shared.Pace, shared.Free);

    private void OnPairStopped() => PairStoppedRpc();

    // The owner's PC began a paired execution (Tables/Executions.csv) on that zombie, laid there: every other PC plays the
    // same pair at the same place on its copies of the player and the zombie, caught up by the time the word took (the
    // user's rule: what one sees is what the others see), as fast as the owner's PC plays it (pace: its rule's killIn, a
    // Ghost's 빠른 칼) and letting the player go at the same moment of the clips (free, s of the clips: ExecutionRules.Timing).
    [Rpc(SendTo.NotMe)]
    private void PairBeganRpc(byte pair, ulong victim, Vector3 anchor, float yaw, double clock, float pace, float free)
    {
        if (executionPairs == null) return;
        GameObject target = null;
        if (victim != 0UL && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victim, out NetworkObject found)) target = found.gameObject;
        float late = clock > 0d ? Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - clock)) : 0f;
        executionPairs.Show(pair, target, anchor, yaw, late, pace, free);
    }

    [Rpc(SendTo.NotMe)]
    private void PairStoppedRpc()
    {
        if (executionPairs != null) executionPairs.ShowStop();
    }

    private void OnExecutionKnife(ExecutionKnife.Phase phase, Vector3 neck, Vector3 head) => ExecutionKnifeRpc((byte)phase, neck, head);

    // The executing player's knife stroke went on a step (drawn, stabbing, put away), aimed at the victim's neck and head
    // as their PC measured them: every other PC's copy of them plays the same stroke there (the user's rule: what one sees
    // is what the others see).
    [Rpc(SendTo.NotMe)]
    private void ExecutionKnifeRpc(byte phase, Vector3 neck, Vector3 head)
    {
        if (executionKnife != null) executionKnife.Show((ExecutionKnife.Phase)phase, neck, head);
    }
}

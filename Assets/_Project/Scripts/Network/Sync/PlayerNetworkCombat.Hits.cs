using Unity.Netcode;
using UnityEngine;

// PlayerNetworkCombat: shots, punches, shoves, evades and executions as the other PCs see them. One part of the class; the rest is in PlayerNetworkCombat.cs and the files beside it.
public sealed partial class PlayerNetworkCombat
{
    private void OnExecutionStarted(MeleeCombatTarget victim) => ExecutionStartedRpc();

    private void OnHit(DamageHit hit, DamageResult result)
    {
        // A hold taking hold is no wound: friends see no blood on them for it either.
        if (result == DamageResult.Damaged && !hit.TakesHold) HurtRpc(hit.Point, hit.Direction, hit.Kind == DamageKind.Fall, receiver.LastHitShielded);
    }

    // Each of a shotgun's pellets comes on its own, numbered, so the copy's gun fires once and each pellet leaves its mark.
    [Rpc(SendTo.NotMe)]
    private void ShotRpc(Vector3 origin, Vector3 direction, byte pellet) => weapon.ShowShot(origin, direction, pellet);

    [Rpc(SendTo.NotMe)]
    private void PunchRpc(float charge) => melee.ShowPunch(charge);

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
        => PairBeganRpc(shared.Pair, shared.Victim, shared.Anchor, shared.Yaw, shared.Clock);

    private void OnPairStopped() => PairStoppedRpc();

    // The owner's PC began a paired execution (Tables/Executions.csv) on that zombie, laid there: every other PC plays the
    // same pair at the same place on its copies of the player and the zombie, caught up by the time the word took (the
    // user's rule: what one sees is what the others see).
    [Rpc(SendTo.NotMe)]
    private void PairBeganRpc(byte pair, ulong victim, Vector3 anchor, float yaw, double clock)
    {
        if (executionPairs == null) return;
        GameObject target = null;
        if (victim != 0UL && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victim, out NetworkObject found)) target = found.gameObject;
        float late = clock > 0d ? Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - clock)) : 0f;
        executionPairs.Show(pair, target, anchor, yaw, late);
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

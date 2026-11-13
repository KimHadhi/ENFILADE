using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using UnityEngine;

// FPSGun: The action worked by hand after every shot (a bolt, a pump, a lever: FPSBodyAssets.Gun.sourceFire). One part of the class; the rest is in FPSGun.cs and the files beside it.
public sealed partial class FPSGun
{
    // The body's clip of it, made for this body (FPSBody, from GunBakes); null for a gun that has none (most guns: a clip that
    // only kicks the gun, as the X18's, is never set as one). The gun-part clip of the shot at the same pace.
    private FPSAnimationAsset cycleClip;

    private KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset partsCycle;

    // Until when it plays (s).
    private float cycleUntil;

    // KINEMATION's sounds of it at the moments the gun-part clip of the shot calls for them, in order, and the next one due.
    private readonly System.Collections.Generic.List<(float at, AudioClip sound)> cycleSounds = new();

    private int nextCycleSound;

    // A sound whose moment passed longer ago than this (s) is dropped rather than played late (a long hitch).
    private const float CycleSoundLate = .3f;

    /// <summary>The gun is worked by hand after every shot: a bolt action's bolt, a pump gun's pump, a lever.</summary>
    internal bool HasCycle => FPSAnimationAsset.IsValid(cycleClip);

    /// <summary>The body is working the gun's action after a shot now: a hand is on the bolt or the pump.</summary>
    internal bool Cycling => Time.time < cycleUntil;

    /// <summary>The pace the action plays at (its asset's rate, 1 as PackGunSetup makes it).</summary>
    internal float CycleRate => HasCycle ? Mathf.Max(.01f, cycleClip.blendTime.rateScale) : 1f;

    /// <summary>How long the action's clip takes to ease back into the hold at its end (s at its own pace): its blend out.</summary>
    internal float CycleSettle => HasCycle ? Mathf.Clamp(cycleClip.blendTime.blendOutTime, 0f, cycleClip.clip.length * .5f) : 0f;

    /// <summary>
    /// The body's clip of the action worked after every shot, made for this body (FPSBody, from GunBakes), and with it the
    /// gun-part clip of the shot at the same pace.
    /// </summary>
    internal void SetCycle(FPSAnimationAsset clip)
    {
        cycleClip = clip;
        partsCycle = HasCycle && Kinemation != null ? RatedParts(Kinemation.fire.weapon, clip) : null;
    }

    /// <summary>
    /// After a shot, a gun worked by hand has the body work its bolt or pump, as FPS Animation Ultimate's own players play the
    /// shot's clip on the arms (useFireClip): the clip made for this body on the body, KINEMATION's own on the camera and,
    /// through it, the gun and both hands where KINEMATION's arms have them on it (FPSBody.Contacts, as through a reload, at
    /// the hip or down the sights), and its sounds as their moments come (TakeCycleSound). Always at its own pace: the rules
    /// hold the next shot until the hands are back (WeaponCycle), so it is never cut short or hurried. Returns how long it
    /// plays (s); 0 for a gun with none.
    /// </summary>
    internal float Cycle(IPlayablesController playables, FPSBodyCamera camera)
    {
        if (!HasCycle || playables == null) return 0f;
        float rate = CycleRate;
        playables.PlayAnimation(cycleClip, 0f);
        if (camera != null && entry.sourceFire != null) camera.PlayGunMotion(entry.sourceFire, rate);
        float length = cycleClip.clip.length / rate;
        cycleUntil = Time.time + length;
        // The hands settle back into the hold at its end, as after a reload.
        FollowClip(entry.sourceFire, cycleClip, 0f, true, false, true);
        QueueCycleSounds(rate);
        return length;
    }

    // The action's sounds: the gun's own recording of the whole action as it starts (WeaponData.CycleSounds), else KINEMATION's
    // sounds at the moments its gun-part clip of the shot calls for them (its PlayWeaponSound events, an index into the gun's
    // weaponEventSounds): the Kar98k's bolt opened at the shot and closed a third of a second on, the L96X's whole bolt, the
    // KXG12's pump. KINEMATION's own gun script would play them from those events, but it stays silent here (FPSBody.AddGun),
    // so these go through WeaponFeedback like every other gun sound. A gun from another pack has no such list (TacticalGunSetup
    // leaves it empty) and takes WeaponData's.
    private void QueueCycleSounds(float rate)
    {
        cycleSounds.Clear();
        nextCycleSound = 0;
        var own = Data != null ? Data.CycleSounds : null;
        if (own != null && own.Length > 0)
        {
            var whole = own[UnityEngine.Random.Range(0, own.Length)];
            if (whole != null) cycleSounds.Add((Time.time, whole));
            return;
        }
        var parts = Kinemation != null ? Kinemation.fire.weapon : null;
        var sounds = Kinemation != null && Kinemation.weaponSettings != null ? Kinemation.weaponSettings.weaponEventSounds : null;
        if (parts == null || parts.clip == null || sounds == null) return;
        float pace = rate * (Mathf.Approximately(parts.playRate, 0f) ? 1f : parts.playRate);
        foreach (var moment in parts.clip.events)
        {
            if (moment.functionName != "PlayWeaponSound" || moment.intParameter < 0 || moment.intParameter >= sounds.Count) continue;
            var sound = sounds[moment.intParameter];
            if (sound != null) cycleSounds.Add((Time.time + moment.time / pace, sound));
        }
        cycleSounds.Sort((a, b) => a.at.CompareTo(b.at));
    }

    /// <summary>The next of KINEMATION's sounds of the action worked after a shot whose moment has come (FPSBody plays it).</summary>
    internal bool TakeCycleSound(out AudioClip sound)
    {
        sound = null;
        while (nextCycleSound < cycleSounds.Count && Time.time >= cycleSounds[nextCycleSound].at)
        {
            var due = cycleSounds[nextCycleSound++];
            if (Time.time - due.at > CycleSoundLate) continue;
            sound = due.sound;
            return true;
        }
        return false;
    }

    // Another of the gun's clips takes the hands (a reload, a draw, a holster, a look over it), or the gun is drawn or put
    // away: the action's clip lets go, and its sounds still to come with it.
    private void EndCycle()
    {
        cycleUntil = 0f;
        cycleSounds.Clear();
        nextCycleSound = 0;
    }

    /// <summary>
    /// KINEMATION's M1911 comes with no rest pose on its gun-part player (its MX16A4 has one, A_W_MX16A4_Pose): once a clip
    /// blended out, nothing put the parts back, so a slide the last round locked back stayed back through the reload
    /// (user, 2026-11-08). A gun without one gets its fire clip held at its first frame, the gun at rest, as the pose under
    /// every clip. Done once, the first time the gun is out (its player is built when the gun first switches on).
    /// </summary>
    private void EnsureRestPose()
    {
        if (restChecked || partsPlayer == null || Kinemation == null) return;
        restChecked = true;
        var field = typeof(SimpleAnimationPlayer).GetField("defaultClipPose",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (field != null && field.GetValue(partsPlayer) is AnimationClip own && own != null) return;
        var rest = Kinemation.fire.weapon != null ? Kinemation.fire.weapon.clip : null;
        if (rest != null) partsPlayer.PlayBasePlayable(rest);
    }

    // The slide is still held back by the last round's clip although the gun has been loaded (its reload clip never
    // played: the reload began while the body wasn't holding the gun up). The parts let go of it and go back to rest.
    private void ReleaseSlide()
    {
        if (!slideHeld || partsPlayer == null || !partsPlayer.enabled) return;
        slideHeld = false;
        partsPlayer.enabled = false;
        partsPlayer.enabled = true;
    }

    /// <summary>The trigger let go (Weapon.OnFireReleased): the recoil settles.</summary>
    internal void StopFire(RecoilAnimation recoil) => recoil.OrNull()?.Stop();

    /// <summary>
    /// Our reload began (Weapon.OnReload): its clip on the body, its gun-part clip on the gun, its camera motion, all from
    /// <paramref name="from"/> of the way through when it goes on from where a cut-short one got to (WeaponRuntime keeps
    /// that). Returns how long the body's whole clip plays (s).
    /// </summary>
    internal float Reload(bool empty, IPlayablesController playables, FPSBodyCamera camera, float from = 0f)
    {
        EndCycle();
        reloadEmptyPlaying = empty;
        reloading = true;
        from = Mathf.Clamp01(from);
        var clip = empty ? reloadEmpty : reloadTactical;
        if (FPSAnimationAsset.IsValid(clip)) playables.PlayAnimation(clip, from * clip.clip.length);
        if (camera != null)
            camera.PlayGunMotion(empty ? entry.sourceReloadEmpty : entry.sourceReloadTactical, clip != null ? clip.blendTime.rateScale : 1f, from);
        float length = clip != null && clip.clip != null ? clip.clip.length / Mathf.Max(.01f, clip.blendTime.rateScale) : 0f;
        // The hands follow KINEMATION's own clip on the gun (FPSBody.Contacts). A shell reload's start hands over to its
        // shells; any other reload settles back into the hold at its end.
        FollowClip(empty ? entry.sourceReloadEmpty : entry.sourceReloadTactical, clip, from, entry.weapon == null || !entry.weapon.ShellReload);
        // The old magazine will fall out as the clip takes it away (GunMagazine); going on past that, it is already out.
        if (magazine != null) magazine.Begin(empty, length, from);
        if (Kinemation == null || partsPlayer == null) return length;
        var pair = empty ? Kinemation.reloadEmpty : Kinemation.reloadTactical;
        // FPS Animation Ultimate's guns loaded a round at a time start every reload with a gun-part clip of their own
        // (FPSBodyAssets.Gun partsReloadTactical, partsReloadEmpty): their CasFPSWeapon's are a stand-in, or the Kar98k's
        // stripper clip, which its start isn't.
        var own = empty ? entry.partsReloadEmpty : entry.partsReloadTactical;
        // The reload's own clip takes the slide from the last round's and lets it go forward.
        if (own != null)
        {
            if (partsPlayer.PlayAnimation(own)) slideHeld = false;
        }
        else if (pair.weapon != null && partsPlayer.PlayAnimation(pair.weapon, PartsTime(pair, from))) slideHeld = false;
        return length;
    }

    // Where in the gun-part clip a reload <paramref name="share"/> of the way through is: the pair plays fitted to the
    // character clip's length with the part clip at its own rate, as ReloadMoments measures it.
    private static float PartsTime(FPSAnimationPair pair, float share)
    {
        if (share <= 0f || pair.weapon == null || pair.weapon.clip == null) return 0f;
        var timing = pair.character != null ? pair.character : pair.weapon;
        float span = timing.GetPlayLength() * (Mathf.Approximately(pair.weapon.playRate, 0f) ? 1f : pair.weapon.playRate);
        return Mathf.Clamp(share * span, 0f, pair.weapon.clip.length);
    }

    /// <summary>
    /// A shell reload moved on (WeaponRuntime.ReloadStep): the shell clip again for the next shell, or the end clip, on the
    /// body, the gun's parts and the camera, as KINEMATION's TacticalShotgun plays its loop and end. Returns how long the
    /// body's clip plays (s).
    /// </summary>
    internal float ReloadStep(WeaponRuntime.ReloadStep step, IPlayablesController playables, FPSBodyCamera camera)
    {
        bool end = step == WeaponRuntime.ReloadStep.End;
        var clip = end ? reloadEnd : reloadShell;
        reloading = true;
        if (FPSAnimationAsset.IsValid(clip)) playables.PlayAnimation(clip, 0f);
        if (camera != null)
            camera.PlayGunMotion(end ? entry.sourceReloadEnd : entry.sourceReloadShell, clip != null ? clip.blendTime.rateScale : 1f);
        FollowClip(end ? entry.sourceReloadEnd : entry.sourceReloadShell, clip, 0f, end);
        var parts = end ? entry.partsReloadEnd : entry.partsReloadShell;
        if (partsPlayer != null && parts != null) partsPlayer.PlayAnimation(parts);
        return clip != null && clip.clip != null ? clip.clip.length / Mathf.Max(.01f, clip.blendTime.rateScale) : 0f;
    }

    /// <summary>Our reload finished: the gun is loaded, so a slide still held back goes forward.</summary>
    internal void ReloadDone()
    {
        reloading = false;
        if (magazine != null) magazine.End();
        ReleaseSlide();
    }

    /// <summary>The reload was cut short (a dodge, a climb, death): its clips stop.</summary>
    internal void StopReload(IPlayablesController playables, float blendOut, FPSBodyCamera camera)
    {
        // The hands let go of the clip's hold with it, whether or not the reload itself had finished.
        contactClip = null;
        if (!reloading) return;
        reloading = false;
        // Cut short with the old magazine leaving, the hand lets it go and the gun stays without one until the next reload
        // brings the new one (WeaponRuntime.MagazineOut); cut short before that, the gun keeps its magazine.
        if (magazine != null) magazine.End(true);
        playables?.StopAnimation(blendOut);
        if (camera != null) camera.StopGunMotion();
        if (Kinemation == null) return;
        var pair = reloadEmptyPlaying ? Kinemation.reloadEmpty : Kinemation.reloadTactical;
        // KINEMATION's gun-part player stops its clip when it is switched off.
        if (partsPlayer != null && (pair.weapon != null || entry.partsReloadShell != null) && partsPlayer.enabled)
        {
            partsPlayer.enabled = false;
            partsPlayer.enabled = true;
        }
    }
}

using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Friend feedback C2: the brute, the boss a song's highlight sends (CampaignMusic). One of the pack's bodies worn half
/// again as big (ZombieType.Scale): its capsule, agent and stride grow with it, its hitboxes ride its bones and grow
/// too, and its corpse gets a doll made for its size (ZombieCorpse.MarkGrown). Its coming is heard on every PC: a roar
/// where it stands, a deep boom for everyone, a shake of the view by how near it is, and an alert that comes in from the
/// side it is on and leaves a mark on it for a while (AlertHUD; it was a line in the feed). The sounds
/// are Resources/Audio/Boss/BruteRoar and BruteSting when those files exist, otherwise made here. Its counter, shooting
/// a leg out and then its head, is ZombieBody.Legs.cs.
/// </summary>
public sealed partial class ZombieBody
{
    private const string RoarPath = "Audio/Boss/BruteRoar", StingPath = "Audio/Boss/BruteSting";
    private const int SampleRate = 44100;
    // The view shakes fully within NearShake of it and not at all past FarShake (m).
    private const float NearShake = 25f, FarShake = 70f;
    // How long the alert's mark stays on it after it comes (real s): long enough to turn and find it.
    private const float MarkSeconds = 6f;
    private static AudioClip roar, sting;
    internal static void WarmSounds() { Roar(); Sting(); } // made behind the loading screen (SceneWarmup), not on the first brute's frame

    // How much bigger than the pack's own size this body stands, and whether it wears a body of its own (never grown).
    private float grown = 1f;
    private bool wearsOwn, arrived;

    private float GrowthWanted => TryGetComponent(out ZombieType type) ? type.Scale : 1f;

    /// <summary>Its kind changed after it was dressed (a late word from the host): the body takes the new size.</summary>
    internal void Regrow()
    {
        if (!Wears || Model == null || wearsOwn) return;
        float wanted = GrowthWanted;
        if (!Mathf.Approximately(wanted, grown))
        {
            float ratio = wanted / grown;
            grown = wanted;
            Model.transform.localScale = Vector3.one * grown;
            if (TryGetComponent(out CapsuleCollider capsule))
            {
                capsule.radius *= ratio;
                capsule.height *= ratio;
                capsule.center = new Vector3(capsule.center.x, feetHeight + capsule.height * .5f, capsule.center.z);
            }
            if (TryGetComponent(out NavMeshAgent agent)) { agent.radius *= ratio; agent.height *= ratio; }
            walkSpeed *= ratio;
            runSpeed *= ratio;
            searchWalkSpeed *= ratio;
            if (!Mathf.Approximately(grown, 1f)) ZombieCorpse.MarkGrown(animator);
        }
        Arrive();
    }

    // Once, as a brute's body first stands in the world.
    private void Arrive()
    {
        if (arrived || !Wears || !TryGetComponent(out ZombieType type) || type.Kind != ZombieKind.Brute) return;
        arrived = true;
        GameAudio.PlayAt(Roar(), transform.position + Vector3.up * 2f, AudioChannel.Combat, 1f, 6f, 120f);
        PlayFlat(Sting(), .8f);
        var health = GetComponent<EnemyHealth>();
        // The band says how to beat it in a line (Texts hud.alert.brute: its knees, then its head), and a moment later
        // the player nearest it shouts for its legs (ZombieBody.Legs.cs).
        AlertHUD.Danger(Texts.Get("hud.alert.brute"), transform, MarkSeconds, () => health == null || !health.IsAlive);
        CallForLegs();
        var mine = PlayerRoster.Local;
        if (mine == null) return;
        float near = 1f - Mathf.InverseLerp(NearShake, FarShake, Vector3.Distance(mine.transform.position, transform.position));
        if (near <= 0f) return;
        var view = mine.GetComponentInChildren<PlayerCameraEffects>();
        if (view != null) view.ApplyCameraImpulse(new Vector3(-7f * near, 0f, (Random.value < .5f ? -4f : 4f) * near), 4f * near);
    }

    // A sound with no place, heard the same by everyone (the boom under the roar).
    private static void PlayFlat(AudioClip clip, float volume)
    {
        if (clip == null) return;
        var source = new GameObject("One Shot (" + clip.name + ")").AddComponent<AudioSource>();
        RuntimeSceneHierarchy.Group(source.gameObject, RuntimeSceneHierarchy.Effects, "Audio");
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        GameAudio.Bind(source, AudioChannel.Combat);
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
        Destroy(source.gameObject, clip.length + .1f);
    }

    // A long low bellow: a falling sawtooth with a rasp of noise through it, swelling and dying away.
    private static AudioClip Roar()
    {
        if (roar != null) return roar;
        roar = Resources.Load<AudioClip>(RoarPath);
        if (roar != null) return roar;
        const float Length = 1.8f, Attack = .18f, Release = .7f;
        int total = Mathf.RoundToInt(Length * SampleRate);
        var samples = new float[total];
        var random = new System.Random(7);
        float phase = 0f, noise = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)SampleRate;
            float pitch = Mathf.Lerp(105f, 62f, t / Length) * (1f + .04f * Mathf.Sin(2f * Mathf.PI * 6f * t));
            phase = Mathf.Repeat(phase + pitch / SampleRate, 1f);
            float saw = 2f * phase - 1f;
            // Rough throat: noise, low-passed, chopped at a growl's rate.
            noise = Mathf.Lerp(noise, (float)random.NextDouble() * 2f - 1f, .18f);
            float rasp = noise * (.6f + .4f * Mathf.Sin(2f * Mathf.PI * 31f * t));
            float level = Mathf.Min(1f, t / Attack, (Length - t) / Release);
            samples[i] = Mathf.Clamp((saw * .55f + rasp * .7f) * level * .8f, -1f, 1f);
        }
        roar = AudioClip.Create("Brute Roar", total, 1, SampleRate, false);
        roar.SetData(samples, 0);
        return roar;
    }

    // A deep hit with a tail: a sub drop and a short burst of noise.
    private static AudioClip Sting()
    {
        if (sting != null) return sting;
        sting = Resources.Load<AudioClip>(StingPath);
        if (sting != null) return sting;
        const float Length = 1.4f;
        int total = Mathf.RoundToInt(Length * SampleRate);
        var samples = new float[total];
        var random = new System.Random(11);
        float phase = 0f, noise = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)SampleRate;
            float pitch = Mathf.Lerp(70f, 34f, Mathf.Sqrt(t / Length));
            phase += 2f * Mathf.PI * pitch / SampleRate;
            noise = Mathf.Lerp(noise, (float)random.NextDouble() * 2f - 1f, .35f);
            float sub = Mathf.Sin(phase) * Mathf.Exp(-t * 2.2f);
            float hit = noise * Mathf.Exp(-t * 14f);
            float level = Mathf.Min(1f, t / .005f);
            samples[i] = Mathf.Clamp((sub * .95f + hit * .5f) * level, -1f, 1f);
        }
        sting = AudioClip.Create("Brute Sting", total, 1, SampleRate, false);
        sting.SetData(samples, 0);
        return sting;
    }
}

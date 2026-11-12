using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A character's face, on its own shape keys (the user, 2026-11-12, after Left 4 Dead 2's KSEP addon, which gives VRChat
/// avatar survivors a face for shooting, melee, shoving, being shoved, jumping, nodding, shaking the head, fidgeting
/// and being down: "콜아웃이나 상황 따라 표정 바뀌었으면 좋겠어. 표정 신기하고 귀여운 거 많던데 마요랑 이치고는").
/// Moods (Tables/Faces.csv) are asked for by whoever knows the reason (PlayerFace for a player, the title menu for its
/// character): a moment of one (Pulse: a hit, a kill, a callout) or one held on (Hold: downed, aiming, low on health).
/// The one with the highest priority takes the face and the rest ease out under it, each at its own speed. On top of
/// the mood the eyes blink and glance now and then, and the mouth makes the vowels of a voice while one plays (Speak,
/// FaceVoice), as much as the mood lets them (Faces.csv blink, talk).
/// The keys are the character's own (FaceKeys.csv, mostly the creator's own expressions from FacePresets.csv), else the
/// common VRChat/MMD names, so any humanoid a modder brings gets a face with nothing baked for it. It writes only its
/// own keys, each on top of the weight the model has under it: a key the wardrobe's body options also set
/// (BodyShapes.csv: Mayo's blush, Ichigo's cheeks, a catchlight) takes the player's choice as its floor and follows it
/// when they change it, so a blushing mood blushes more and lets go back to exactly what they picked. The ears the
/// creators' expressions move (an annoyed flick, droopy ears when crying) turn from their rest with the mood. Faces
/// nobody can see, or too far off, keep their moods' clocks but write nothing (FaceLife.csv Watch.range).
/// </summary>
[DisallowMultipleComponent]
public sealed class CharacterFace : MonoBehaviour
{
    /// <summary>The mood held while the character is being looked at (Showing).</summary>
    public const string Cute = "cute";
    private static readonly string[] Vowels = { "talk.a", "talk.i", "talk.u", "talk.e", "talk.o" };
    private static readonly string[] Looks = { "look.left", "look.right", "look.up", "look.down" };

    private sealed class Channel
    {
        public SkinnedMeshRenderer[] Meshes;
        public int[] Indices;
        /// <summary>The weight the model has for it under the face; the face adds to it.</summary>
        public float Base, Target, Shown = -1f;
        /// <summary>The wardrobe's body options set it too: what they set since the face last wrote it is the new Base.</summary>
        public bool Shared;
    }

    // An ear (a bone a creator's expression turns): its rest as the model has it, the turn from the creator's rest to
    // its rest pose, and where it is now on its way to the mood's pose.
    private sealed class Ear
    {
        public Transform Bone;
        public Quaternion Rest, FromRest, Now = Quaternion.identity;
    }

    private struct Set
    {
        public int[] Channels;
        public float[] Weights;
        public bool Any => Channels != null && Channels.Length > 0;
    }

    private sealed class Slot
    {
        public FaceTables.Mood Mood;
        public Set Keys;
        /// <summary>Each ear's turn from rest in this mood (null: the mood leaves the ears at rest).</summary>
        public Quaternion[] Ears;
        public float Weight, Strength = 1f, Until = float.NegativeInfinity;
        public bool On;
    }

    // Each mesh's shape key names (trimmed: Mayo's "Other_Eyes_Heart " ends in a space), read once per mesh asset.
    private static readonly Dictionary<Mesh, Dictionary<string, int>> namesOf = new Dictionary<Mesh, Dictionary<string, int>>();
    // The keys each character's wardrobe body options set (BodyShapes.csv): the face writes them on top of the choice.
    private static readonly Dictionary<string, HashSet<string>> wardrobeOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> warned = new HashSet<string>();
    private static readonly List<int> foundChannels = new List<int>();
    private static readonly List<float> foundWeights = new List<float>();
    private static readonly List<SkinnedMeshRenderer> foundMeshes = new List<SkinnedMeshRenderer>();
    private static readonly List<int> foundIndices = new List<int>();
    private static bool listening;

    private readonly List<Channel> channels = new List<Channel>();
    private readonly Dictionary<string, int> channelOf = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, Slot> slotOf = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
    private Slot[] slots = Array.Empty<Slot>(), poses = Array.Empty<Slot>();
    private float poolTotal;
    private Set blink;
    private readonly Set[] talk = new Set[5], look = new Set[4];
    private SkinnedMeshRenderer[] meshes = Array.Empty<SkinnedMeshRenderer>();
    private Renderer[] seenBy = Array.Empty<Renderer>();
    private Transform head;
    private Slot winner;
    // The face's own clock: it stops while the game is paused, so a moment of a mood isn't used up behind the menu.
    private float clock;
    private float blinkWait, blinkTime = -1f, closed;
    private bool again;
    private float lookWait, lookX, lookY, lookToX, lookToY;
    private float poseWait;
    private AudioSource voice;
    private AudioClip voiceClip;
    private FaceVoice.Track track;
    private float open, flap, flapRate, talkUntil = float.NegativeInfinity;
    private readonly float[] vowel = new float[5];
    // The line's own vowels, syllable by syllable (FaceVoice.VowelsOf), for a mouth that flaps or follows a clip.
    private readonly byte[] said = new byte[64];
    private int saidCount;
    private Ear[] ears = Array.Empty<Ear>();
    private string[] earNames = Array.Empty<string>();
    private bool showing, refit;

    /// <summary>The character (PlayerBodyProfile skin id) whose keys it wears; "" for one without rows of its own.</summary>
    public string Skin { get; private set; } = "";

    /// <summary>
    /// Being looked at (the title menu, the hub's mirror, the dressing room, the results stage): the cute face is held
    /// and now and then a pose (Faces.csv pool) comes over it.
    /// </summary>
    public bool Showing
    {
        get => showing;
        set
        {
            showing = value;
            Hold(Cute, value);
        }
    }

    /// <summary>No moods and slower blinks: the wardrobe is working on the face or the eyes themselves.</summary>
    public bool Calm { get; set; }

    /// <summary>Runs on real time (the title menu), not the game's.</summary>
    public bool Unscaled { get; set; }

    /// <summary>Always moves, seen or not: this PC's own player, whom the mirror, F5 and the results stage show.</summary>
    public bool Watched { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        namesOf.Clear();
        wardrobeOf.Clear();
        warned.Clear();
        listening = false;
    }

    /// <summary>
    /// Puts a face on this character model (or fits the one it has again) on <paramref name="skinId"/>'s keys. Call it
    /// once the model is dressed, so a mask's or a helmet's keys of the same name move with the face.
    /// </summary>
    public static CharacterFace Wear(GameObject model, string skinId)
    {
        if (model == null) return null;
        if (!listening)
        {
            // The wardrobe's keys may change with its table.
            GameTables.Reloaded += wardrobeOf.Clear;
            listening = true;
        }
        if (!model.TryGetComponent(out CharacterFace face)) face = model.AddComponent<CharacterFace>();
        face.Fit(skinId);
        return face;
    }

    /// <summary>
    /// A moment of this mood (Faces.csv id): it wants the face for <paramref name="seconds"/> (its table hold when
    /// negative) at <paramref name="strength"/> (0-1), then eases out. Asked again while it lasts, it lasts longer.
    /// </summary>
    public void Pulse(string mood, float strength = 1f, float seconds = -1f)
    {
        if (mood == null || !slotOf.TryGetValue(mood, out var slot)) return;
        float hold = seconds >= 0f ? seconds : slot.Mood.Hold;
        bool lasting = clock < slot.Until;
        slot.Strength = lasting ? Mathf.Max(slot.Strength, Mathf.Clamp01(strength)) : Mathf.Clamp01(strength);
        slot.Until = Mathf.Max(lasting ? slot.Until : clock, clock + hold);
    }

    /// <summary>A mood held on or let go (downed, aiming, low on health…).</summary>
    public void Hold(string mood, bool on)
    {
        if (mood != null && slotOf.TryGetValue(mood, out var slot)) slot.On = on;
    }

    /// <summary>The mouth makes the vowels of what <paramref name="source"/> plays, as long as it plays.</summary>
    public void Speak(AudioSource source) => Speak(source, null, 0f);

    /// <summary>
    /// The mouth says <paramref name="text"/>: following what <paramref name="source"/> plays for as long as it plays
    /// (its loudness, and the line's own vowels syllable by syllable), or with no voice at all (no clip: a line a voice
    /// pack hasn't got) moving through the line's vowels for <paramref name="seconds"/>, the way Skyrim's Fuz Ro D-oh
    /// gives silent lines a talking mouth. A clip whose samples can't be read flaps through the vowels for its length.
    /// </summary>
    public void Speak(AudioSource source, string text, float seconds)
    {
        bool sounds = source != null && source.clip != null;
        voice = sounds ? source : null;
        voiceClip = null;
        track = null;
        saidCount = FaceVoice.VowelsOf(text, said);
        float length = sounds ? source.clip.length / Mathf.Max(.1f, Mathf.Abs(source.pitch)) : Mathf.Max(0f, seconds);
        talkUntil = sounds ? float.NegativeInfinity : clock + length;
        // The flapping paces the line's syllables over how long it is said; a line of unknown words at a talking pace.
        flapRate = saidCount > 0 && length > .1f ? saidCount / length : FaceTables.Number("Talk.flap", 5.5f);
        flap = 0f;
    }

    private void Fit(string skinId)
    {
        Release();
        Skin = skinId ?? "";
        channels.Clear();
        channelOf.Clear();
        slotOf.Clear();
        meshes = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var wardrobe = Wardrobe(Skin);
        var moods = FaceTables.Moods;
        slots = new Slot[moods.Count];
        var pool = new List<Slot>();
        poolTotal = 0f;
        for (int i = 0; i < moods.Count; i++)
        {
            var slot = new Slot { Mood = moods[i], Keys = KeysFor(moods[i].Id, wardrobe) };
            slots[i] = slot;
            slotOf[moods[i].Id] = slot;
            if (moods[i].Pool <= 0f || !slot.Keys.Any) continue;
            pool.Add(slot);
            poolTotal += moods[i].Pool;
        }
        poses = pool.ToArray();
        blink = KeysFor("blink", wardrobe);
        for (int i = 0; i < Vowels.Length; i++) talk[i] = KeysFor(Vowels[i], wardrobe);
        for (int i = 0; i < Looks.Length; i++) look[i] = KeysFor(Looks[i], wardrobe);
        FitEars();
        foreach (var slot in slots) slot.Ears = EarsFor(slot.Mood.Id);
        var seen = new List<Renderer>();
        foreach (var channel in channels)
        {
            foreach (var mesh in channel.Meshes)
                if (!seen.Contains(mesh)) seen.Add(mesh);
            channel.Base = channel.Meshes[0].GetBlendShapeWeight(channel.Indices[0]);
        }
        seenBy = seen.ToArray();
        var animator = GetComponentInChildren<Animator>();
        head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) head = transform;
        if (showing) Hold(Cute, true);
        blinkWait = NextBlink();
        lookWait = Next("Look.every", "Look.spread", 2.4f, 1.6f);
        poseWait = Next("Show.every", "Show.spread", 6f, 3f) * .5f;
    }

    // The character's own keys for the set, else the common names' (a modder's character, or a mood its own rows skip).
    private Set KeysFor(string set, HashSet<string> wardrobe)
    {
        var own = Resolve(FaceTables.Own(Skin, set), wardrobe, true);
        return own.Any ? own : Resolve(FaceTables.Fallback(set), wardrobe, false);
    }

    private Set Resolve(FaceTables.Key[] keys, HashSet<string> wardrobe, bool own)
    {
        if (keys == null) return default;
        foundChannels.Clear();
        foundWeights.Clear();
        foreach (var key in keys)
        {
            int found = -1;
            foreach (string name in key.Names)
            {
                found = ChannelFor(name);
                if (found < 0) continue;
                // A key the wardrobe sets too (a blush, a catchlight the player picked): their pick is its floor (Write).
                if (wardrobe.Contains(name)) channels[found].Shared = true;
                break;
            }
            if (found < 0)
            {
                if (own) Warn($"FaceKeys.csv {Skin}: '{string.Join("/", key.Names)}' 키가 모델에 없습니다.");
                continue;
            }
            int at = foundChannels.IndexOf(found);
            if (at >= 0) foundWeights[at] += key.Weight;
            else
            {
                foundChannels.Add(found);
                foundWeights.Add(key.Weight);
            }
        }
        return new Set { Channels = foundChannels.ToArray(), Weights = foundWeights.ToArray() };
    }

    // The ears the creator's clips turn (FacePresets.csv preset rest), found on the model by name: their rest as the
    // model has them, and the turn from the creator's rest to it, so a mood's pose turns them by the creator's amount.
    private void FitEars()
    {
        var rest = FaceTables.Rest(Skin);
        if (rest == null)
        {
            ears = Array.Empty<Ear>();
            earNames = Array.Empty<string>();
            return;
        }
        var found = new List<Ear>();
        var names = new List<string>();
        foreach (var bone in rest)
        {
            var at = FindBone(transform, bone.Name);
            if (at == null) continue;
            found.Add(new Ear { Bone = at, Rest = at.localRotation, FromRest = Quaternion.Inverse(Quaternion.Euler(bone.Euler)) });
            names.Add(bone.Name);
        }
        ears = found.ToArray();
        earNames = names.ToArray();
    }

    // Each ear's turn in the set (FaceKeys.csv bones, or its preset's), from the creator's rest to the creator's pose.
    private Quaternion[] EarsFor(string set)
    {
        if (ears.Length == 0) return null;
        var bones = FaceTables.Bones(Skin, set);
        if (bones == null) return null;
        var turns = new Quaternion[ears.Length];
        bool any = false;
        for (int i = 0; i < ears.Length; i++)
        {
            turns[i] = Quaternion.identity;
            foreach (var bone in bones)
            {
                if (!string.Equals(bone.Name, earNames[i], StringComparison.Ordinal)) continue;
                turns[i] = ears[i].FromRest * Quaternion.Euler(bone.Euler);
                any = true;
            }
        }
        return any ? turns : null;
    }

    private static Transform FindBone(Transform under, string name)
    {
        if (under.name == name) return under;
        for (int i = 0; i < under.childCount; i++)
        {
            var found = FindBone(under.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    // The key on every mesh of the character that has it (the face, and a mask made to follow it), or -1.
    private int ChannelFor(string name)
    {
        if (channelOf.TryGetValue(name, out int index)) return index;
        foundMeshes.Clear();
        foundIndices.Clear();
        foreach (var mesh in meshes)
        {
            var shared = mesh != null ? mesh.sharedMesh : null;
            if (shared != null && NamesOf(shared).TryGetValue(name, out int shape))
            {
                foundMeshes.Add(mesh);
                foundIndices.Add(shape);
            }
        }
        index = foundMeshes.Count > 0 ? channels.Count : -1;
        if (index >= 0) channels.Add(new Channel { Meshes = foundMeshes.ToArray(), Indices = foundIndices.ToArray() });
        channelOf[name] = index;
        return index;
    }

    private static Dictionary<string, int> NamesOf(Mesh mesh)
    {
        if (namesOf.TryGetValue(mesh, out var names)) return names;
        // Meshes made at run time (a hand-corrected copy for each new body) go with their body: forget the gone ones.
        if (namesOf.Count >= 32)
        {
            var gone = new List<Mesh>();
            foreach (var known in namesOf.Keys)
                if (known == null) gone.Add(known);
            foreach (var known in gone) namesOf.Remove(known);
        }
        names = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string name = mesh.GetBlendShapeName(i).Trim();
            if (!names.ContainsKey(name)) names[name] = i;
        }
        namesOf[mesh] = names;
        return names;
    }

    private static HashSet<string> Wardrobe(string skin)
    {
        if (wardrobeOf.TryGetValue(skin, out var keys)) return keys;
        keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in BodyShapes.For(skin))
            foreach (var choice in option.Keys)
                foreach (string key in choice)
                    keys.Add(key.Trim());
        wardrobeOf[skin] = keys;
        return keys;
    }

    private void OnEnable() => GameTables.Reloaded += AskRefit;

    // A saved table is read again (GameTables.Reload): the face takes it on its next frame, once every table has let go
    // of its old rows, so a key picked in FaceKeys.csv shows without making the body again.
    private void AskRefit() => refit = true;

    private void LateUpdate()
    {
        if (refit)
        {
            refit = false;
            Fit(Skin);
        }
        float dt = Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
        // Paused, the face holds as it is, as the body does (PlayerBody.HoldFrozenPose).
        if (dt <= 0f || (!Unscaled && GamePause.IsPaused)) return;
        clock += dt;
        Choose();
        Ease(dt);
        Blink(dt);
        Glance(dt);
        Pose(dt);
        bool seen = Seen();
        Talk(dt, seen);
        if (!seen) return;
        Compose();
        Write();
        Turn(dt);
    }

    // The mood with the highest priority that wants the face and has keys on this character takes it.
    private void Choose()
    {
        winner = null;
        if (Calm) return;
        foreach (var slot in slots)
        {
            if (!slot.Keys.Any || (!slot.On && clock >= slot.Until)) continue;
            if (winner == null || slot.Mood.Priority > winner.Mood.Priority) winner = slot;
        }
    }

    private void Ease(float dt)
    {
        foreach (var slot in slots)
        {
            float target = slot != winner ? 0f : slot.On ? 1f : slot.Strength;
            if (slot.Weight == target) continue;
            float time = target > slot.Weight ? slot.Mood.In : slot.Mood.Out;
            slot.Weight = Mathf.MoveTowards(slot.Weight, target, dt / time);
        }
    }

    // A blink: the lids come down fast, stay a moment and lift slower; now and then a second right after.
    private void Blink(float dt)
    {
        if (blinkTime < 0f)
        {
            closed = 0f;
            blinkWait -= dt;
            if (blinkWait <= 0f) blinkTime = 0f;
            return;
        }
        blinkTime += dt;
        float close = Mathf.Max(.01f, FaceTables.Number("Blink.close", .06f));
        float shut = Mathf.Max(0f, FaceTables.Number("Blink.shut", .04f));
        float lift = Mathf.Max(.01f, FaceTables.Number("Blink.open", .11f));
        if (blinkTime < close) closed = Smooth(blinkTime / close);
        else if (blinkTime < close + shut) closed = 1f;
        else if (blinkTime < close + shut + lift) closed = 1f - Smooth((blinkTime - close - shut) / lift);
        else
        {
            closed = 0f;
            blinkTime = -1f;
            again = !again && UnityEngine.Random.value < FaceTables.Number("Blink.twice", .15f);
            blinkWait = again ? .07f : NextBlink();
        }
    }

    private float NextBlink() => Next("Blink.every", "Blink.spread", 3.6f, 2.2f) * (Calm ? Mathf.Max(1f, FaceTables.Number("Blink.calm", 2f)) : 1f);

    // The eyes rest a while, then glance a little aside and come back (FaceKeys look.*: only a character with those keys).
    private void Glance(float dt)
    {
        if (!look[0].Any && !look[1].Any && !look[2].Any && !look[3].Any) return;
        lookWait -= dt;
        if (lookWait <= 0f)
        {
            lookWait = Next("Look.every", "Look.spread", 2.4f, 1.6f);
            bool home = UnityEngine.Random.value < .45f;
            lookToX = home ? 0f : UnityEngine.Random.Range(-1f, 1f);
            lookToY = home ? 0f : UnityEngine.Random.Range(-.5f, .5f);
        }
        float share = 1f - Mathf.Exp(-dt / Mathf.Max(.01f, FaceTables.Number("Look.time", .07f)));
        lookX += (lookToX - lookX) * share;
        lookY += (lookToY - lookY) * share;
    }

    // Being looked at, a pose now and then over the cute face (never over a mood that means something).
    private void Pose(float dt)
    {
        if (!showing || poses.Length == 0) return;
        poseWait -= dt;
        if (poseWait > 0f) return;
        poseWait = Next("Show.every", "Show.spread", 6f, 3f);
        if (winner != null && winner.Mood.Id != Cute && winner.Mood.Pool <= 0f) return;
        float pick = UnityEngine.Random.value * poolTotal;
        foreach (var pose in poses)
        {
            pick -= pose.Mood.Pool;
            if (pick > 0f) continue;
            Pulse(pose.Mood.Id);
            return;
        }
    }

    private void Talk(float dt, bool seen)
    {
        float want = 0f;
        int shape = 0;
        if (seen && voice != null && voice.isPlaying && voice.clip != null)
        {
            if (voice.clip != voiceClip || track == null)
            {
                voiceClip = voice.clip;
                track = FaceVoice.Of(voiceClip);
            }
            if (track != null)
            {
                track.Sample(voice.time, out float loud, out shape, out int syllable);
                // The line's own vowels over the clip's syllables, spread to fit when the counts differ.
                if (saidCount > 0 && track.Syllables > 0) shape = said[Mathf.Min(saidCount - 1, syllable * saidCount / track.Syllables)];
                float floor = Mathf.Clamp01(FaceTables.Number("Talk.floor", .08f));
                want = Mathf.Clamp01((loud - floor) / Mathf.Max(.01f, 1f - floor) * FaceTables.Number("Talk.gain", 1.25f));
            }
            // No samples to read: the mouth works through the line at a talking pace for as long as it plays.
            else Flap(dt, out want, out shape);
        }
        // A line with no voice at all: said all the same, for as long as its words take (Speak).
        else if (seen && clock < talkUntil) Flap(dt, out want, out shape);
        float rise = Mathf.Max(.01f, FaceTables.Number("Talk.attack", .035f));
        float drop = Mathf.Max(.01f, FaceTables.Number("Talk.release", .08f));
        open = Mathf.MoveTowards(open, want, dt / (want > open ? rise : drop));
        float step = dt / Mathf.Max(.01f, FaceTables.Number("Talk.vowel", .07f));
        for (int i = 0; i < vowel.Length; i++) vowel[i] = Mathf.MoveTowards(vowel[i], i == shape ? 1f : 0f, step);
    }

    // A mouth with no samples to follow: it opens and shuts once a syllable, through the line's vowels when they are known.
    private void Flap(float dt, out float want, out int shape)
    {
        flap += dt * flapRate;
        want = .35f + .45f * Mathf.Abs(Mathf.Sin(flap * Mathf.PI));
        int beat = (int)flap;
        if (saidCount > 0)
        {
            shape = said[beat % saidCount];
            return;
        }
        int turn = beat % 3;
        shape = turn == 0 ? 0 : turn == 1 ? 4 : 3;
    }

    // The ears follow the mood that has the face (its pose, as far as it has come in), quickly, as ears flick.
    private void Turn(float dt)
    {
        if (ears.Length == 0) return;
        float share = 1f - Mathf.Exp(-dt / Mathf.Max(.01f, FaceTables.Number("Ear.time", .09f)));
        var turns = winner != null ? winner.Ears : null;
        float amount = winner != null ? winner.Weight : 0f;
        for (int i = 0; i < ears.Length; i++)
        {
            var ear = ears[i];
            if (ear.Bone == null) continue;
            var want = turns != null ? Quaternion.Slerp(Quaternion.identity, turns[i], amount) : Quaternion.identity;
            ear.Now = Quaternion.Slerp(ear.Now, want, share);
            ear.Bone.localRotation = ear.Rest * ear.Now;
        }
    }

    private bool Seen()
    {
        if (Watched) return true;
        bool visible = false;
        for (int i = 0; i < seenBy.Length && !visible; i++) visible = seenBy[i] != null && seenBy[i].isVisible;
        if (!visible) return false;
        var view = Camera.main;
        if (view == null || head == null) return true;
        float range = FaceTables.Number("Watch.range", 25f);
        return (view.transform.position - head.position).sqrMagnitude <= range * range;
    }

    private void Compose()
    {
        foreach (var channel in channels) channel.Target = 0f;
        float total = 0f, blinks = 0f, talks = 0f;
        foreach (var slot in slots)
        {
            if (slot.Weight <= 0f) continue;
            Add(slot.Keys, slot.Weight);
            total += slot.Weight;
            blinks += slot.Weight * slot.Mood.Blink;
            talks += slot.Weight * slot.Mood.Talk;
        }
        // What no mood holds blinks and talks freely.
        float rest = 1f - Mathf.Clamp01(total);
        blinks = Mathf.Clamp01(blinks + rest);
        talks = Mathf.Clamp01(talks + rest);
        if (closed > 0f) Add(blink, closed * blinks);
        float reach = Mathf.Clamp01(FaceTables.Number("Look.reach", .35f)) * blinks;
        if (lookX < 0f) Add(look[0], -lookX * reach);
        else Add(look[1], lookX * reach);
        if (lookY > 0f) Add(look[2], lookY * reach);
        else Add(look[3], -lookY * reach);
        if (open <= 0f) return;
        for (int i = 0; i < vowel.Length; i++)
            if (vowel[i] > 0f) Add(talk[i], open * vowel[i] * talks);
    }

    private void Add(in Set set, float amount)
    {
        if (amount <= 0f || set.Channels == null) return;
        for (int i = 0; i < set.Channels.Length; i++) channels[set.Channels[i]].Target += set.Weights[i] * amount;
    }

    private void Write()
    {
        foreach (var channel in channels)
        {
            if (channel.Shared && channel.Meshes[0] != null)
            {
                // The wardrobe set the key itself since the face last wrote it (a chip or a slider of the body page, a
                // body made again): the player's pick is the new floor, and the mood goes on top of it.
                float now = channel.Meshes[0].GetBlendShapeWeight(channel.Indices[0]);
                if (channel.Shown < 0f || Mathf.Abs(now - channel.Shown) > .01f) channel.Base = now;
            }
            float weight = Mathf.Clamp(channel.Base + channel.Target, 0f, 100f);
            if (Mathf.Abs(weight - channel.Shown) < .05f) continue;
            channel.Shown = weight;
            for (int i = 0; i < channel.Meshes.Length; i++)
                if (channel.Meshes[i] != null) channel.Meshes[i].SetBlendShapeWeight(channel.Indices[i], weight);
        }
    }

    // Every key back to the weight the model had, so a face taken off or fitted again leaves nothing behind.
    private void Release()
    {
        foreach (var channel in channels)
        {
            if (channel.Shown >= 0f)
                for (int i = 0; i < channel.Meshes.Length; i++)
                    if (channel.Meshes[i] != null) channel.Meshes[i].SetBlendShapeWeight(channel.Indices[i], channel.Base);
            channel.Shown = -1f;
        }
        foreach (var ear in ears)
        {
            if (ear.Bone != null) ear.Bone.localRotation = ear.Rest;
            ear.Now = Quaternion.identity;
        }
    }

    private void OnDisable()
    {
        GameTables.Reloaded -= AskRefit;
        Release();
    }

    // A wait round a FaceLife.csv mean, give or take its spread (keys whole, so asking makes no garbage).
    private static float Next(string everyKey, string spreadKey, float every, float spread)
    {
        float mean = Mathf.Max(.2f, FaceTables.Number(everyKey, every));
        float range = Mathf.Max(0f, FaceTables.Number(spreadKey, spread));
        return Mathf.Max(.15f, mean + UnityEngine.Random.Range(-range, range));
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static void Warn(string message)
    {
        if (warned.Add(message)) Debug.LogWarning("[Faces] " + message);
    }
}

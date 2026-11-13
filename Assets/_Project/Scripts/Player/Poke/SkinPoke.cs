using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Poking a friend (the user, 2026-11-12: "F 꾹 누르고 있으면 손가락질 계속 하고 있을 수 있고 그걸로 친구 건드리면 이
/// 첨부한 파일처럼 스킨 터치 꾹 돼서 볼 누르고 뱃살 누르고 그런 기능 있었으면 좋겠어. 재밌을 거 같아."): a pointing
/// fingertip (PointArm) that meets a friend's cheek, belly, arm, shoulder or the top of their head sinks into them, and
/// their skin, hair and clothes give way round it in a soft dimple; when it lifts, the skin springs back, a little past
/// where it was, and settles. A small sound goes with each, and Poked tells faces (PlayerFace's moods) on every PC.
/// The attached VRChat package (DocMe Skin Touch) was only looked at for the feel; this is our own code throughout.
/// Every PC works the pokes out for itself from the pointer's finger on its own screen (the point and the look are all
/// the network carries, PlayerPose's marks), and the dent is drawn only, never felt by the game.
/// Up to four fingertips at once (Slots, one a player). A slot follows its fingertip while it presses; when it lifts, the
/// slot stays on the bone it pressed and springs the skin's middle back (Poke.csv per zone: hz, bounce), and is freed
/// once still. Each frame the slots go to the shader (SkinDent.hlsl in lilToon's vertex pass) as globals, and a slot's
/// bit goes on every renderer of the body it presses (Renderer.SetShaderUserValue), so only that body bends.
/// Runs after every PointArm (GunFramework + 6) and the cloth.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Cloth + 5)]
public sealed class SkinPoke : MonoBehaviour
{
    /// <summary>Fingertips pressing at once anywhere: SkinDent.hlsl's slots, a room's four players.</summary>
    public const int Slots = 4;

    /// <summary>
    /// A friend's fingertip began pressing into a player (true) or lifted off them (false): the one poked, the one
    /// poking, and where. Raised on every PC as it works the poke out on its own screen, the poked player's own included.
    /// </summary>
    public static event Action<PlayerMovement, PlayerMovement, PokeZone, bool> Poked;

    private sealed class Slot
    {
        public PlayerMovement Poker, Target;
        public PokeBody Body;
        public PokeZone Zone;
        public PokeTables.Feel Feel;
        public Transform Bone;
        // Where the fingertip met the skin and the way it presses, in the world and on the bone carrying them.
        public Vector3 Surface, Push, LocalSurface, LocalPush;
        // The skin's middle: how far in it is (m; under 0, bulging out) and how fast it moves; the fingertip's depth.
        public float Depth, Speed, Finger, LastFinger;
        public bool InUse, Pressed, Touching, HadFinger;
        public int PressedFrame, OpenedFrame;
        public readonly List<Renderer> Marked = new List<Renderer>();
    }

    // A fingertip in deeper than this (m) is touching: the press begins.
    private const float TouchDepth = .0005f;
    // Still enough to free a slot: depth (m) and speed (m/s).
    private const float RestDepth = .0002f, RestSpeed = .003f;
    // The spring's steps are never longer than this (s), so a stiff zone stays steady at a low frame rate.
    private const float SpringStep = .005f;
    private const int Rate = 44100;

    private static readonly Slot[] slots = MakeSlots();
    private static readonly Dictionary<Renderer, uint> marks = new Dictionary<Renderer, uint>();
    private static readonly Vector4[] tips = new Vector4[Slots], pushes = new Vector4[Slots], shapes = new Vector4[Slots];
    private static readonly int CountId = Shader.PropertyToID("_EnfDentCount"), TipId = Shader.PropertyToID("_EnfDentTip"),
        PushId = Shader.PropertyToID("_EnfDentPush"), ShapeId = Shader.PropertyToID("_EnfDentShape");
    private static SkinPoke driver;
    private static bool published;
    private static AudioClip pressClip, liftClip;

    private static Slot[] MakeSlots()
    {
        var made = new Slot[Slots];
        for (int i = 0; i < Slots; i++) made[i] = new Slot();
        return made;
    }

    /// <summary>
    /// The nearest friend's body along a pointing player's look, within <paramref name="range"/> (m): where it meets them,
    /// which zone, and whether they let it poke. Only bodies near the line do any work, and a body is read the first
    /// time a finger comes near it (PokeBody).
    /// </summary>
    internal static bool Find(PlayerMovement pointer, Vector3 origin, Vector3 direction, float range, out PokeHit hit)
    {
        hit = default;
        bool found = false;
        float nearest = range;
        foreach (var other in PlayerRoster.All)
        {
            if (other == null || other == pointer || !other.isActiveAndEnabled || other.IsDead) continue;
            // The body's middle, a metre up from the feet: further than the reach and a body's size, it can't be met.
            Vector3 middle = other.transform.position + Vector3.up;
            if ((middle - origin).sqrMagnitude > (range + 1.2f) * (range + 1.2f)) continue;
            var body = PokeBody.Of(other);
            if (body == null || !body.Prepare()) continue;
            if (!body.Raycast(origin, direction, nearest, out Vector3 point, out float distance, out Transform bone, out PokeZone zone)) continue;
            nearest = distance;
            hit = new PokeHit(other, body, point, bone, zone, Allows(other), distance);
            found = true;
        }
        return found;
    }

    // They let friends poke them: their own setting on their PC, their pose's mark on the others (PlayerPoint).
    private static bool Allows(PlayerMovement who)
    {
        if (who.TryGetComponent(out PlayerPoint point)) return point.Pokable;
        return who.IsNetworkDriven || GameSettings.Current.letOthersPoke;
    }

    /// <summary>
    /// <paramref name="poker"/>'s fingertip is on <paramref name="hit"/> this frame, at <paramref name="fingertip"/>,
    /// pressing along <paramref name="push"/> (PointArm, every frame it touches). A press that jumps elsewhere (another
    /// body, another zone, further than Touch.slide) lets the old dent spring back and makes a new one.
    /// </summary>
    internal static void Press(PlayerMovement poker, in PokeHit hit, Vector3 fingertip, Vector3 push)
    {
        if (poker == null || hit.Body == null || hit.Bone == null || !hit.Pokes || push.sqrMagnitude < 1e-8f) return;
        Ensure();
        var slot = PressedBy(poker);
        float slide = PokeTables.Number("Touch.slide", .06f);
        if (slot != null && (slot.Body != hit.Body || slot.Zone != hit.Zone || (slot.Surface - hit.Point).sqrMagnitude > slide * slide))
        {
            slot.Pressed = false;
            slot = null;
        }
        // Poked again where it just let go (콕콕콕): the same dent, still springing back, is pressed again rather than a
        // second one opened on top of it, which would push the skin in twice.
        if (slot == null) slot = Springing(poker, hit, slide);
        if (slot == null) slot = Open(poker, hit);
        if (slot == null) return;
        push.Normalize();
        slot.Bone = hit.Bone;
        slot.Surface = hit.Point;
        slot.Push = push;
        slot.LocalSurface = hit.Bone.InverseTransformPoint(hit.Point);
        slot.LocalPush = hit.Bone.InverseTransformDirection(push);
        slot.Finger = Mathf.Min(Vector3.Dot(fingertip - hit.Point, push), slot.Feel.Deepest);
        slot.Pressed = true;
        slot.PressedFrame = Time.frameCount;
    }

    /// <summary><paramref name="poker"/>'s fingertip touches nobody this frame: a press of theirs lets go and springs back.</summary>
    internal static void Lift(PlayerMovement poker)
    {
        var slot = PressedBy(poker);
        if (slot != null) slot.Pressed = false;
    }

    private static Slot PressedBy(PlayerMovement poker)
    {
        if (poker == null) return null;
        foreach (var slot in slots) if (slot.InUse && slot.Pressed && slot.Poker == poker) return slot;
        return null;
    }

    // This finger's own dent springing back at the same place on the same body and zone, if there is one.
    private static Slot Springing(PlayerMovement poker, in PokeHit hit, float slide)
    {
        foreach (var slot in slots)
            if (slot.InUse && !slot.Pressed && slot.Poker == poker && slot.Body == hit.Body && slot.Zone == hit.Zone
                && (slot.Surface - hit.Point).sqrMagnitude <= slide * slide) return slot;
        return null;
    }

    // A free slot for a new press, else the one springing back longest; none while every slot is pressed.
    private static Slot Open(PlayerMovement poker, in PokeHit hit)
    {
        int chosen = -1;
        for (int i = 0; i < Slots && chosen < 0; i++) if (!slots[i].InUse) chosen = i;
        if (chosen < 0)
        {
            int oldest = int.MaxValue;
            for (int i = 0; i < Slots; i++)
                if (!slots[i].Pressed && slots[i].OpenedFrame < oldest) { oldest = slots[i].OpenedFrame; chosen = i; }
            if (chosen < 0) return null;
            Free(chosen);
        }
        // What the body wears may have changed since it was read.
        hit.Body.Recheck();
        var slot = slots[chosen];
        slot.InUse = true;
        slot.Poker = poker;
        slot.Target = hit.Target;
        slot.Body = hit.Body;
        slot.Zone = hit.Zone;
        slot.Feel = PokeTables.Of(hit.Zone);
        slot.Depth = slot.Speed = 0f;
        slot.Touching = slot.HadFinger = false;
        slot.OpenedFrame = Time.frameCount;
        uint bit = 1u << chosen;
        slot.Marked.Clear();
        foreach (var renderer in hit.Body.Renderers)
        {
            if (renderer == null) continue;
            slot.Marked.Add(renderer);
            Mark(renderer, bit, true);
        }
        return slot;
    }

    private static void Free(int index, bool tell = true)
    {
        var slot = slots[index];
        if (slot.Touching && tell) Announce(slot, false);
        uint bit = 1u << index;
        foreach (var renderer in slot.Marked) Mark(renderer, bit, false);
        slot.Marked.Clear();
        slot.InUse = slot.Pressed = slot.Touching = false;
        slot.Poker = slot.Target = null;
        slot.Body = null;
        slot.Bone = null;
        tips[index] = pushes[index] = shapes[index] = Vector4.zero;
    }

    // A renderer's shader user value (SkinDent.hlsl reads it): this file's slot bits, kept here as set.
    private static void Mark(Renderer renderer, uint bit, bool on)
    {
        if (renderer == null) return;
        marks.TryGetValue(renderer, out uint value);
        value = on ? value | bit : value & ~bit;
        if (value == 0u) marks.Remove(renderer);
        else marks[renderer] = value;
        if (renderer is SkinnedMeshRenderer skinned) skinned.SetShaderUserValue(value);
        else if (renderer is MeshRenderer mesh) mesh.SetShaderUserValue(value);
    }

    private static void Ensure()
    {
        if (driver != null) return;
        var host = new GameObject("Skin Poke");
        RuntimeSceneHierarchy.Keep(host);
        driver = host.AddComponent<SkinPoke>();
    }

    private void OnDestroy()
    {
        if (driver != this) return;
        // Going with the scene or the game: no sound or face is started on the way out.
        for (int i = 0; i < Slots; i++) if (slots[i].InUse) Free(i, false);
        Publish(0);
        driver = null;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        int top = 0;
        for (int i = 0; i < Slots; i++)
        {
            var slot = slots[i];
            if (!slot.InUse) continue;
            // The body went (left the room, made again): its dent goes with it.
            if (slot.Body == null || slot.Bone == null || slot.Target == null) { Free(i); continue; }
            // An arm that stopped saying it presses (switched off, made again) has let go.
            if (slot.Pressed && slot.PressedFrame != Time.frameCount) slot.Pressed = false;
            if (!slot.Pressed)
            {
                // Springing back where it was pressed, on the bone that carries it.
                slot.Surface = slot.Bone.TransformPoint(slot.LocalSurface);
                slot.Push = slot.Bone.TransformDirection(slot.LocalPush).normalized;
            }
            if (dt > 0f) Spring(slot, dt);
            bool touching = slot.Pressed && slot.Finger > TouchDepth;
            if (touching != slot.Touching)
            {
                slot.Touching = touching;
                Announce(slot, touching);
            }
            if (!slot.Pressed && Mathf.Abs(slot.Depth) < RestDepth && Mathf.Abs(slot.Speed) < RestSpeed) { Free(i); continue; }
            Write(i, slot);
            top = i + 1;
        }
        Publish(top);
    }

    // The skin's middle as a damped spring toward rest (Feel.hz, Feel.bounce), never in front of a pressing fingertip:
    // while it presses the skin moves with it, and when it lets go faster than the skin comes back, the skin springs out
    // past the surface (a bulge, Feel.bulge) and settles.
    private static void Spring(Slot slot, float dt)
    {
        var feel = slot.Feel;
        float omega = 2f * Mathf.PI * Mathf.Max(.1f, feel.Hz), damping = 2f * Mathf.Max(.02f, feel.Bounce) * omega;
        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / SpringStep), 1, 40);
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            slot.Speed += (-omega * omega * slot.Depth - damping * slot.Speed) * step;
            slot.Depth += slot.Speed * step;
        }
        if (slot.Pressed && slot.Finger > slot.Depth)
        {
            slot.Speed = slot.HadFinger ? (slot.Finger - slot.LastFinger) / dt : 0f;
            slot.Depth = slot.Finger;
        }
        slot.HadFinger = slot.Pressed;
        slot.LastFinger = slot.Finger;
        slot.Depth = Mathf.Min(slot.Depth, feel.Deepest);
    }

    // The slot's numbers as SkinDent.hlsl reads them: the fingertip where the skin's middle is, the bowl, the push and
    // the deepest, the rim's softness, the reach, and the bulge past the surface with its width.
    private static void Write(int index, Slot slot)
    {
        var feel = slot.Feel;
        Vector3 tip = slot.Surface + slot.Push * slot.Depth;
        tips[index] = new Vector4(tip.x, tip.y, tip.z, feel.Bowl);
        pushes[index] = new Vector4(slot.Push.x, slot.Push.y, slot.Push.z, feel.Deepest);
        float bulge = slot.Depth < 0f ? -slot.Depth * feel.Bulge : 0f;
        shapes[index] = new Vector4(feel.Soft, feel.Reach, bulge, feel.Reach * .6f);
    }

    private static void Publish(int count)
    {
        if (count == 0 && !published) return;
        Shader.SetGlobalVectorArray(TipId, tips);
        Shader.SetGlobalVectorArray(PushId, pushes);
        Shader.SetGlobalVectorArray(ShapeId, shapes);
        Shader.SetGlobalFloat(CountId, count);
        published = count > 0;
    }

    private static void Announce(Slot slot, bool began)
    {
        if (slot.Target != null) Poked?.Invoke(slot.Target, slot.Poker, slot.Zone, began);
        Sound(slot, began);
    }

    // A small soft pop where the finger goes in, a lighter one as it comes off; higher on a cheek, lower on a belly.
    private static void Sound(Slot slot, bool began)
    {
        var clip = began ? PressClip : LiftClip;
        float volume = PokeTables.Number("Sound.volume", .55f) * (began ? 1f : PokeTables.Number("Sound.lift", .6f));
        if (clip == null || volume <= 0f) return;
        var source = new GameObject("Poke Sound").AddComponent<AudioSource>();
        RuntimeSceneHierarchy.Group(source.gameObject, RuntimeSceneHierarchy.Effects, "Audio");
        source.transform.position = slot.Surface;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        float near = PokeTables.Number("Sound.near", 1f), far = PokeTables.Number("Sound.far", 12f);
        if (near > 0f && far > near)
        {
            source.minDistance = near;
            source.maxDistance = far;
            source.rolloffMode = AudioRolloffMode.Linear;
        }
        source.pitch = Mathf.Max(.1f, slot.Feel.Pitch * UnityEngine.Random.Range(.96f, 1.04f));
        GameAudio.Bind(source, AudioChannel.Movement);
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
        Destroy(source.gameObject, clip.length / source.pitch + .1f);
    }

    // A recording in Resources/Audio/Poke (Press, Lift) plays if there is one; else a made one.
    private static AudioClip PressClip => pressClip != null ? pressClip
        : pressClip = Resources.Load<AudioClip>("Audio/Poke/Press").OrNull() ?? Synth("Poke Press", 780f, 330f, .11f, .6f);
    private static AudioClip LiftClip => liftClip != null ? liftClip
        : liftClip = Resources.Load<AudioClip>("Audio/Poke/Lift").OrNull() ?? Synth("Poke Lift", 360f, 840f, .08f, .2f);

    // A rounded little chirp gliding from one pitch to another (Hz) over its length (s), with a soft lower body.
    private static AudioClip Synth(string name, float from, float to, float length, float body)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(length * Rate));
        var data = new float[count];
        float phase = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)Rate, u = t / length;
            float glide = 1f - (1f - u) * (1f - u);
            phase += 2f * Mathf.PI * Mathf.Lerp(from, to, glide) / Rate;
            float envelope = Mathf.Clamp01(t / .004f) * Mathf.Exp(-t / (length * .32f));
            data[i] = (Mathf.Sin(phase) + .22f * Mathf.Sin(2f * phase) + body * .5f * Mathf.Sin(.5f * phase)) * envelope * .45f;
        }
        var clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Poked = null;
        driver = null;
        foreach (var slot in slots)
        {
            slot.InUse = slot.Pressed = slot.Touching = false;
            slot.Poker = slot.Target = null;
            slot.Body = null;
            slot.Bone = null;
            slot.Marked.Clear();
        }
        marks.Clear();
        for (int i = 0; i < Slots; i++) tips[i] = pushes[i] = shapes[i] = Vector4.zero;
        published = true;
        Publish(0);
        pressClip = liftClip = null;
    }
}

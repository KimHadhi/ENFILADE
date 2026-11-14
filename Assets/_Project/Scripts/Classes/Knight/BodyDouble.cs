using UnityEngine;

/// <summary>
/// A Knight's shadow double, seen on every PC while it is out (the shadow card's seconds after a perfect parry or dodge,
/// or the ultimate's): the Knight's own character beside them, black and see-through, doing what they do a beat late.
/// The user, 2026-11-14: "플레이어 분신(플레이어랑 똑같이 생겼는데 검은색이고 투명함) 생기게 해서 네 행동 살짝 딜레이 있이
/// 똑같이 따라하게(무적이고 좀비 어그로 안 끌림)" (before, 2026-11-12: "본인 캐릭터인데 완전 보랏빛 검은색에 투명하게").
/// It stands a step ahead and to the Knight's left, where the Knight's own eyes see it at the edge of the view and the
/// others see it at their side ("내가 보는 화면이 진실"), and it is where the Knight stood, in the pose they had,
/// Knight.shadowDelay ago (Tables/ClassAbilities: the same beat the double's blow lands after, ClassCards' echo, so its
/// fist meets the cut). For that the body is baked a few times a second into a short ring (BodyBake, the dodge's
/// afterimage shader) and the bake from that long ago is drawn, at the place the Knight's path had then.
/// It is only a picture: no collider, no health, nothing a zombie can see, hear or reach, so it can't be hurt and draws no
/// zombie off the Knight. In an execution it only mimes, an execution being the Knight's alone (no echo: ClassCards).
/// It rises in, holds, and fades over its last half second. Numbers: Tables/MomentFx.csv, row Double.
/// </summary>
[DisallowMultipleComponent]
public sealed class BodyDouble : MonoBehaviour
{
    private const string Row = "Double";
    // The most bakes the ring keeps (at 15 a second, .14 s behind takes 3), and the frames of the Knight's path it
    // remembers (the delay's worth at a few hundred frames a second).
    private const int MostBakes = 6, Steps = 128;

    private BodyBake[] bakes = new BodyBake[0];
    private float[] bakedAt = new float[0];
    private int newest = -1;
    // The Knight's path: when, where their feet were, and the flat way they faced.
    private readonly float[] stepAt = new float[Steps];
    private readonly Vector3[] stepFeet = new Vector3[Steps], stepFacing = new Vector3[Steps];
    private int stepHead = -1, stepCount;
    private float until, since, nextBake;
    private PlayerMovement player;

    /// <summary>The double comes out beside <paramref name="knight"/> for so long (or stays out longer).</summary>
    public static void Show(Component knight, float seconds)
    {
        if (!Application.isPlaying || knight == null || seconds <= 0f || !MomentLook.On(Row)) return;
        var body = knight.GetComponentInParent<PlayerMovement>();
        var host = body != null ? body.gameObject : knight.gameObject;
        if (!host.TryGetComponent(out BodyDouble shadow)) shadow = host.AddComponent<BodyDouble>();
        shadow.Begin(seconds);
    }

    private void Begin(float seconds)
    {
        if (!enabled || Time.time >= until)
        {
            since = Time.time;
            nextBake = 0f;
            newest = stepHead = -1;
            stepCount = 0;
        }
        until = Mathf.Max(until, Time.time + seconds);
        enabled = true;
    }

    // How far behind the Knight it is (s): the Knight row's, read the same on every PC whoever's double it is.
    private static float Delay => Mathf.Clamp(GameTables.Number(PlayerClass.AbilityTable, PlayerClass.KnightRow, "shadowDelay", .14f), 0f, 1f);

    private void LateUpdate()
    {
        if (player == null) player = GetComponent<PlayerMovement>();
        if (Time.time >= until || (player != null && (player.IsDead || player.IsDowned)))
        {
            Release();
            enabled = false;
            return;
        }
        Remember();
        float delay = Delay, then = Time.time - delay;
        float rate = Mathf.Max(1f, MomentLook.Number(Row, "bakesPerSecond", 15f));
        BakeNow(delay, rate);
        int shown = Pick(then, .5f / rate);
        if (shown < 0) return;
        float rise = Mathf.Clamp01((Time.time - since) / Mathf.Max(.01f, MomentLook.Number(Row, "rise", .15f)));
        float fade = Mathf.Clamp01((until - Time.time) / Mathf.Max(.01f, MomentLook.Number(Row, "fade", .5f)));
        float alpha = MomentLook.Number(Row, "alpha", .5f) * rise * fade;
        // From where the body stood when baked to the double's spot beside where the Knight was then, the pose as it was.
        var bake = bakes[shown];
        Color tint = MomentLook.Colour(Row, "tint", new Color(.03f, .03f, .05f, .92f));
        bake.Draw(Matrix4x4.Translate(SpotAt(then) - bake.BakedAt), alpha, tint);
    }

    // This frame's place on the Knight's path.
    private void Remember()
    {
        Vector3 facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;
        stepHead = (stepHead + 1) % Steps;
        stepAt[stepHead] = Time.time;
        stepFeet[stepHead] = transform.position;
        stepFacing[stepHead] = facing;
        stepCount = Mathf.Min(stepCount + 1, Steps);
    }

    // The double's spot at time <paramref name="then"/>: ahead and to the left of where the Knight's path was, in the way
    // they faced, between the two frames either side of it (the oldest kept, if the path doesn't reach back that far).
    private Vector3 SpotAt(float then)
    {
        int later = stepHead, earlier = stepHead;
        for (int i = 1; i < stepCount; i++)
        {
            int index = (stepHead - i + Steps) % Steps;
            earlier = index;
            if (stepAt[index] <= then) break;
            later = index;
        }
        float span = stepAt[later] - stepAt[earlier];
        float k = span > 1e-5f ? Mathf.Clamp01((then - stepAt[earlier]) / span) : 1f;
        Vector3 feet = Vector3.Lerp(stepFeet[earlier], stepFeet[later], k);
        Vector3 facing = Vector3.Slerp(stepFacing[earlier], stepFacing[later], k);
        facing = facing.sqrMagnitude > 1e-4f ? facing.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, facing);
        return feet + right * MomentLook.Number(Row, "side", -.95f) + facing * MomentLook.Number(Row, "ahead", 1.1f);
    }

    // A fresh bake into the ring when one is due; the ring holds as many as the delay needs at this rate.
    private void BakeNow(float delay, float rate)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(delay * rate) + 1, 1, MostBakes);
        if (bakes.Length != count)
        {
            Release();
            bakes = new BodyBake[count];
            bakedAt = new float[count];
            for (int i = 0; i < count; i++) bakes[i] = new BodyBake();
        }
        if (Time.time < nextBake) return;
        nextBake = Time.time + 1f / rate;
        int slot = (newest + 1) % count;
        if (!bakes[slot].Bake(transform, Mathf.RoundToInt(MomentLook.Number(Row, "vertexBudget", 150000f)))) return;
        bakedAt[slot] = Time.time;
        newest = slot;
    }

    // The bake nearest <paramref name="then"/> and not later than half a bake past it; at first, before the ring reaches
    // that far back, the oldest there is. -1 while there is none.
    private int Pick(float then, float half)
    {
        int best = -1, oldest = -1;
        float bestAt = float.NegativeInfinity, oldestAt = float.PositiveInfinity;
        for (int i = 0; i < bakes.Length; i++)
        {
            if (bakes[i] == null || !bakes[i].Ready) continue;
            float at = bakedAt[i];
            if (at <= then + half && at > bestAt) { best = i; bestAt = at; }
            if (at < oldestAt) { oldest = i; oldestAt = at; }
        }
        return best >= 0 ? best : oldest;
    }

    private void Release()
    {
        foreach (var bake in bakes) bake?.Release();
        newest = -1;
    }

    private void OnDestroy() => Release();
}

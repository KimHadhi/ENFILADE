using UnityEngine;

/// <summary>
/// A Knight's shadow double, seen on every PC while it is out (the shadow card's seconds after a parry, or the
/// ultimate's): the Knight's own character beside them, violet-black and see-through. The user's note, 2026-11-12:
/// "그림자 분신은 본인 캐릭터인데 완전 보랏빛 검은색에 투명하게".
/// It stands a step ahead and to the Knight's left, where the Knight's own eyes see it at the edge of the view and the
/// others see it at their side ("내가 보는 화면이 진실"), facing the same way. It takes the Knight's pose a few times a second
/// (BodyBake, the dodge's afterimage shader), so it moves with them and strikes as they do; ClassCards' Echo is its
/// blow. It rises in, holds, and fades over its last half second. Numbers: Tables/MomentFx.csv, row Double.
/// </summary>
[DisallowMultipleComponent]
public sealed class BodyDouble : MonoBehaviour
{
    private const string Row = "Double";

    private readonly BodyBake bake = new BodyBake();
    private float until, since, nextBake;
    private Vector3 place;
    private bool placed;

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
        if (!enabled || Time.time >= until) { since = Time.time; placed = false; nextBake = 0f; }
        until = Mathf.Max(until, Time.time + seconds);
        enabled = true;
    }

    private void LateUpdate()
    {
        var player = GetComponent<PlayerMovement>();
        if (Time.time >= until || (player != null && (player.IsDead || player.IsDowned)))
        {
            bake.Release();
            enabled = false;
            return;
        }
        // Its spot: ahead and to the left of the Knight, in the Knight's facing, following them a little behind time.
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 spot = transform.position + right * MomentLook.Number(Row, "side", -.95f) + forward * MomentLook.Number(Row, "ahead", 1.1f);
        float follow = Mathf.Max(.01f, MomentLook.Number(Row, "follow", .12f));
        place = placed ? Vector3.Lerp(place, spot, 1f - Mathf.Exp(-Time.deltaTime / follow)) : spot;
        placed = true;
        if (Time.time >= nextBake)
        {
            nextBake = Time.time + 1f / Mathf.Max(1f, MomentLook.Number(Row, "bakesPerSecond", 15f));
            bake.Bake(transform, Mathf.RoundToInt(MomentLook.Number(Row, "vertexBudget", 150000f)));
        }
        if (!bake.Ready) return;
        float rise = Mathf.Clamp01((Time.time - since) / Mathf.Max(.01f, MomentLook.Number(Row, "rise", .15f)));
        float fade = Mathf.Clamp01((until - Time.time) / Mathf.Max(.01f, MomentLook.Number(Row, "fade", .5f)));
        float alpha = MomentLook.Number(Row, "alpha", .5f) * rise * fade;
        // From where the body stood when baked to the double's spot, the pose kept as it was.
        var move = Matrix4x4.Translate(place - bake.BakedAt);
        Color tint = MomentLook.Colour(Row, "tint", new Color(.16f, .06f, .3f, .92f));
        bake.Draw(move, alpha, tint);
    }

    private void OnDestroy() => bake.Release();
}

using UnityEngine;

/// <summary>
/// Low cover meant to be shot over: a Support's barricade, its barbed wire and its ammo crate (Barricade adds this), and
/// the map's cover kinds listed in Tables/ShootOverCover (sandbags, HESCO, jersey barriers…: GreyboxBuilder.Dress adds it
/// to their boxes). The gun's wall rule (WeaponAimSolver.IsObstructed) looks past it while the player's eyes are over its
/// top, so standing behind it never lifts the gun into high ready and aiming over it works (the user, 2026-11-12: "서포트
/// 바리케이드 때문에 high ready돼서 조준하는데 불편하니까 이거 좀 개선해 줘"). Crouched under its top it counts as a wall like
/// any other, and the gun rises rather than sink into it. Shots still stop on it: it is cover, not a hole.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShootOverCover : MonoBehaviour
{
    /// <summary>The table of map dressing kinds (Tables/MapArt ids) that are cover meant to be shot over.</summary>
    public const string Table = "ShootOverCover";
    // The eyes this far over its top count as looking over it (m): a hand's width, so cover at eye height still blocks.
    private const float LookOver = .05f;

    /// <summary>Map dressing of <paramref name="kind"/> (a Tables/MapArt id) is cover meant to be shot over (its row's cover).</summary>
    public static bool IsKind(string kind)
        => !string.IsNullOrEmpty(kind) && GameTables.TryGetRow(Table, kind, out var row)
            && row.TryGetValue("cover", out string cover) && string.Equals(cover, "true", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="collider"/> is shoot-over cover whose top is under eyes at <paramref name="eyeHeight"/> (world y),
    /// so the gun's wall rule looks past it.
    /// </summary>
    public static bool LookedOver(Collider collider, float eyeHeight)
        => collider != null && collider.GetComponentInParent<ShootOverCover>() != null && collider.bounds.max.y < eyeHeight - LookOver;
}

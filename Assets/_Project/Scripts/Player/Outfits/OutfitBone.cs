using UnityEngine;

/// <summary>
/// Marks a transform an outfit hung on one of the character's bones (OutfitDresser.Merge): a coat's tail, a belt, a
/// bell's chain that hung from the outfit's copy of that bone and now hangs from the character's own. The character's
/// cloth rows leave it, and everything under it, to the outfit's rows (ClothBones.Collect). Magica takes every transform
/// under a row's root for a particle of that row, so the Sera Yura shirt's hem and its bikini's bells, which hang from
/// the breast bone, swung inside Mayo's breast spring besides their own rows (2026-11-12).
/// </summary>
[DisallowMultipleComponent]
public sealed class OutfitBone : MonoBehaviour
{
}

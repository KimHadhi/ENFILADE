using UnityEngine;

/// <summary>
/// Marks a prefab or scene text with its role (FontRoles, Fonts.csv) instead of leaving it tied to a font file.
/// Tools → Fonts → 역할 맞추기 gives every marked text its role's face and preset, and 글꼴 검사 lists texts with no mark
/// or out of step with theirs. The mark does nothing at runtime on purpose: scripts restyle some prefab texts after they
/// load (MissionBarLook's count takes the Label face, FeelUI.ShadowScore adds a shadow), and a role applied in Awake
/// would race them for the last word.
/// </summary>
[DisallowMultipleComponent]
public sealed class TextRole : MonoBehaviour
{
    [Tooltip("A role of Resources/Tables/Core/Fonts.csv: Label, Prompt, Damage, Ammo, HudText, HudBold, Shield, Alarm, Chat or Body.")]
    public string role = FontRoles.Body;
}

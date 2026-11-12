using UnityEngine;

/// <summary>
/// What is left of the Esc menu's old 캐릭터 tab: the colour the character cards stand on. The tab itself went
/// (user, 2026-11-12: "설정에서 캐릭터 바꾸는 거 이제 없애도 될 듯"); the character, colours, clothes and body are chosen in
/// the wardrobe (MainMenuWardrobe), on the title screen and at the hub's mirror (HubDressing). The wardrobe's character
/// cards and their pictures (SkinPortraits) still meet on this colour.
/// </summary>
public static class CharacterSkinView
{
    /// <summary>The card colour. Opaque, because the pictures are taken on it and have to meet it without a seam.</summary>
    public static readonly Color CardFill = new Color(.12f, .13f, .155f);
}

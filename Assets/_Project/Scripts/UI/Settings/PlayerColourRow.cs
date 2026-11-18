using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The controls a player chooses their colour with (PlayerColours; the user, 2026-11-18: "플레이어가 본인의 대표 색상을
/// 직접 커스텀할 수 있도록 하고, Tab 현황판·콜아웃·인게임 표시 전체에 일관되게 동기화"): 자동 (no choice: a free colour is
/// given), a swatch for every row of Tables/Hud/PlayerColours.csv (a mod's rows too), and, where there is room, a box for
/// any colour of their own as #RRGGBB. The choice in use wears a white frame. A pick goes to GameSettings.playerColour
/// at once: the player's tally reads it the same frame and their PC tells the room, so the board, the chat and their
/// pings change while the menu is still open.
/// One builder for every place the choice is offered (the settings' HUD tab, the profile window).
/// </summary>
public static class PlayerColourRow
{
    // A swatch's side and the gap between two, the 자동 button's width, the controls' height and the colour box's width (px).
    private const float Swatch = 30f, Gap = 6f, AutoWidth = 84f, Height = 34f, BoxWidth = 118f;
    // What the colour box holds when no colour was chosen: the form a typed colour takes.
    private const string BoxHint = "#RRGGBB";

    /// <summary>
    /// Adds the controls to <paramref name="row"/> (a horizontal row). <paramref name="save"/> writes the settings file at
    /// each pick, for a window with no save of its own; <paramref name="ownColour"/> adds the #RRGGBB box.
    /// </summary>
    public static void Controls(Transform row, bool save, bool ownColour)
    {
        var frames = new List<(Outline frame, string value)>();
        TMP_InputField box = null;

        // The frame goes to the choice in use, and the box shows that colour as it would be typed.
        void Paint()
        {
            string now = GameSettings.Current.playerColour ?? "";
            foreach (var (frame, value) in frames)
            {
                bool on = string.Equals(value, now, System.StringComparison.OrdinalIgnoreCase);
                frame.effectColor = on ? W.Text : W.Track;
                frame.effectDistance = on ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            }
            if (box == null || box.isFocused) return;
            uint wish = PlayerColours.WishOf(now);
            box.SetTextWithoutNotify(wish == 0u ? BoxHint : "#" + ColorUtility.ToHtmlStringRGB(PlayerColours.Colour(wish)));
        }

        void Pick(string value)
        {
            GameSettings.Current.playerColour = value;
            GameSettings.Apply();
            if (save) GameSettings.Save();
            Paint();
        }

        var auto = W.ActionButton(row, Texts.Get("profile.colour.auto"), false, () => Pick(""));
        W.Size(auto, width: AutoWidth, height: Height);
        frames.Add((auto.gameObject.AddComponent<Outline>(), ""));

        var strip = W.Rect("Swatches", row);
        var layout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = Gap;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var ids = PlayerColours.PaletteIds;
        var colours = PlayerColours.Palette;
        W.Size(strip, width: ids.Count * Swatch + Mathf.Max(0, ids.Count - 1) * Gap, height: Height);
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];
            var swatch = W.Rect("Swatch " + id, strip);
            W.Size(swatch, width: Swatch, height: Swatch);
            var image = W.Fill(swatch, colours[i]);
            frames.Add((swatch.gameObject.AddComponent<Outline>(), id));
            var button = swatch.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => Pick(id));
        }

        if (ownColour)
        {
            box = W.TextInput(row, BoxHint, _ => { });
            W.Size(box, width: BoxWidth, height: Height, flexibleWidth: 0f);
            box.characterLimit = 7;
            // Plain to see while typed in, as the number boxes are; the row's name turns pink with it.
            TypingLook.On(box, row.childCount > 0 ? row.GetChild(0).GetComponent<TMP_Text>() : null);
            box.onEndEdit.AddListener(text =>
            {
                string typed = (text ?? "").Trim();
                if (typed.Length > 0 && !typed.StartsWith("#")) typed = "#" + typed;
                // "#" and six hex digits is a colour; anything else leaves the choice as it was.
                if (typed.Length == 7 && ColorUtility.TryParseHtmlString(typed, out _)) Pick(typed.ToUpperInvariant());
                else Paint();
            });
        }
        Paint();
    }
}

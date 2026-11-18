using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using W = SettingsWidgets;

/// <summary>
/// The controls a player chooses their colour with (PlayerColours; the user, 2026-11-18: "플레이어가 본인의 대표 색상을
/// 직접 커스텀할 수 있도록 하고, Tab 현황판·콜아웃·인게임 표시 전체에 일관되게 동기화"): 자동 (no choice: a free colour
/// is given, pink where it is free), and a row of slots, each one of the player's own colours. Pressing a slot uses its
/// colour and opens the colour board on it (ColourPanel), where the colour is made by moving the pointer over it, or by
/// its code or its R, G and B; pressing it again puts the board away. The slot in use wears a white frame.
/// Until 2026-11-18 this row was a swatch for each palette row and a box for a code; the user, that day: "미리 정해둔
/// 프리셋 방식이 아닌 여러 색깔 슬롯을 좀 두고 직접 그 색상 판에서 색깔을 커스텀할 수 있게 해 주십쇼. 색상 코드로 하는
/// 사람이 몇이나 되겠습니까?" The palette is now only what those who chose nothing are given, and what the slots hold at
/// first.
/// What is kept: the slots (GameSettings.playerColourSlots) and the colour in use (GameSettings.playerColour, one of
/// them or nothing). A colour being dragged over the board shows in its slot at once; the colour in use, and with it the
/// board, the chat and the pings, changes when the pointer lets go, so the room is told a colour once and not every one
/// passed over. A colour too dark to read on the HUD cannot be made: the board is handed PlayerColours.Dim and holds none.
/// One builder for every place the choice is offered (the settings' HUD tab, the profile window).
/// </summary>
public static class PlayerColourRow
{
    // A slot's side and the gap between two, the 자동 button's width and the controls' height (px).
    private const float Swatch = 30f, Gap = 6f, AutoWidth = 84f, Height = 34f;

    /// <summary>
    /// Adds the controls to <paramref name="row"/> (a horizontal row). <paramref name="save"/> writes the settings file at
    /// each choice, for a window with no save of its own.
    /// </summary>
    public static void Controls(Transform row, bool save)
    {
        int inUse = PlayerColours.FillSlots(GameSettings.Current);
        var frames = new List<Outline>();
        var fills = new List<Image>();
        Outline autoFrame = null;

        static void Frame(Outline frame, bool on)
        {
            frame.effectColor = on ? W.Text : W.Track;
            frame.effectDistance = on ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
        }

        // The frame goes to the choice in use, and every slot shows the colour it holds.
        void Paint()
        {
            var slots = GameSettings.Current.playerColourSlots;
            Frame(autoFrame, inUse < 0);
            for (int i = 0; i < frames.Count; i++)
            {
                Frame(frames[i], i == inUse);
                if (i < slots.Count && ColourCode.TryRead(slots[i], out Color32 held)) fills[i].color = held;
            }
        }

        void Keep()
        {
            GameSettings.Apply();
            if (save) GameSettings.Save();
        }

        // This slot's colour is the player's from now on (-1: none, a free one is given).
        void Use(int slot)
        {
            var settings = GameSettings.Current;
            inUse = slot;
            settings.playerColour = slot < 0 ? "" : settings.playerColourSlots[slot];
            Keep();
            Paint();
        }

        var auto = W.ActionButton(row, Texts.Get("profile.colour.auto"), false, () =>
        {
            ColourPanel.Close();
            Use(-1);
        });
        W.Size(auto, width: AutoWidth, height: Height);
        autoFrame = auto.gameObject.AddComponent<Outline>();

        var strip = W.Rect("Slots", row);
        var layout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = Gap;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        int count = PlayerColours.SlotCount;
        W.Size(strip, width: count * Swatch + Mathf.Max(0, count - 1) * Gap, height: Height);
        // The least a colour of theirs may weigh: the board holds only colours that read on the HUD's dark.
        float dim = PlayerColours.Dim;
        for (int i = 0; i < count; i++)
        {
            int slot = i;
            var swatch = W.Rect("Slot " + (i + 1), strip);
            W.Size(swatch, width: Swatch, height: Swatch);
            var image = W.Fill(swatch, Color.white);
            fills.Add(image);
            frames.Add(swatch.gameObject.AddComponent<Outline>());
            var button = swatch.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                // The slot whose board is open, pressed again: the board goes away and the colour stays in use.
                if (ColourPanel.IsOpenFor(image)) { ColourPanel.Close(); return; }
                Use(slot);
                var settings = GameSettings.Current;
                ColourCode.TryRead(settings.playerColourSlots[slot], out Color32 held);
                ColourPanel.Open(strip, held,
                    moved =>
                    {
                        GameSettings.Current.playerColourSlots[slot] = ColourCode.Of(moved);
                        image.color = moved;
                    },
                    made =>
                    {
                        var now = GameSettings.Current;
                        now.playerColourSlots[slot] = ColourCode.Of(made);
                        if (inUse == slot) now.playerColour = now.playerColourSlots[slot];
                        Keep();
                        Paint();
                    },
                    dim, GameSettings.Current.DisplayName, image);
            });
        }
        Paint();
    }
}

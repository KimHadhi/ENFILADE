using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The weapon selector across the screen's middle (the addon's cl_selector.lua): for Selector.seconds after the player
/// touches the slots (a number key, the wheel: PlayerWeaponSlots.SlotActivity) a row of boxes, one a weapon held, the one
/// in hand light with dark words and the row sliding to keep it in the middle, the others dark; each says the weapon's
/// name over its rounds and reserve, red when its magazine is at 30 % or under, with its round's icon in the corner;
/// boxes further from the middle are fainter. It shows what our slot keys already did: the game changes weapons at the
/// key as it always has (the addon's own selector waits for a click to confirm; changing that is not the HUD's call), so
/// this is the addon's look over our switching. The old slot plates on the right edge are gone (WeaponSlotsHUD).
/// </summary>
public sealed class MkSelector : MkPiece
{
    private const int Count = PlayerWeaponSlots.SlotCount, MeleeSlot = 2;

    private sealed class Box
    {
        public TextMeshProUGUI Name, Rounds;
        public RawImage Icon;
        public Color Back, Ink;
        public bool Coloured;
    }

    private readonly MkShapes shapes;
    private readonly Box[] boxes = new Box[Count];
    private PlayerWeaponSlots bound;
    private float until = float.NegativeInfinity, alpha, offset;

    public MkSelector(MkHud hud, RectTransform canvas) : base(hud, canvas, "Mk Selector", new Vector2(.5f, .5f))
    {
        shapes = MkShapes.Make("Shapes", Frame);
        for (int i = 0; i < Count; i++)
            boxes[i] = new Box
            {
                Name = MkText.Make("Weapon " + (i + 1), Frame, Look, .5f, 1f),
                Rounds = MkText.Make("Rounds " + (i + 1), Frame, Look, .5f, 0f),
                Icon = Picture("Round " + (i + 1), 0f, 0f, Look.Slots.height / 3f),
            };
    }

    public override void Dispose() => Bind(null);

    private void Bind(PlayerWeaponSlots slots)
    {
        if (bound == slots) return;
        if (bound != null) bound.SlotActivity -= Touched;
        bound = slots;
        if (bound != null) bound.SlotActivity += Touched;
    }

    private void Touched() => until = Time.unscaledTime + Look.Slots.seconds;

    public override void Tick(float dt)
    {
        var s = Look.Slots;
        var c = Look.Colour;
        var player = Hud.Player;
        Bind(player != null ? player.GetComponent<PlayerWeaponSlots>() : null);
        alpha = MkText.Follow(alpha, Time.unscaledTime < until && player != null && !player.IsDead ? 1f : 0f, s.follow, dt);
        Group.alpha = alpha;
        shapes.Clear();
        if (alpha <= .005f || bound == null)
        {
            shapes.Show();
            return;
        }
        int selected = bound.SelectedSlot, shown = 0, selectedAt = 0;
        for (int i = 0; i < Count; i++)
        {
            if (!Held(i, out _)) continue;
            if (i == selected) selectedAt = shown;
            shown++;
        }
        float step = s.width + s.gap;
        offset = MkText.Follow(offset, selectedAt * step, s.follow, dt);
        int at = 0;
        for (int i = 0; i < Count; i++)
        {
            var box = boxes[i];
            if (!Held(i, out string name))
            {
                MkText.Say(box.Name, ""); MkText.Say(box.Rounds, "");
                Show(box.Icon, null, Color.clear);
                continue;
            }
            float x = s.x + at * step - offset, y = s.y;
            at++;
            var runtime = bound.GetRuntime(i);
            bool low = runtime != null && runtime.Data.Category != WeaponCategory.Melee && runtime.Ammo <= runtime.Data.MagazineSize * s.low;
            bool held = i == selected;
            Color back = held ? s.held : s.back, ink = low ? c.low : held ? s.heldText : c.text;
            box.Back = box.Coloured ? MkText.Follow(box.Back, back, s.colourFollow, dt) : back;
            box.Ink = box.Coloured ? MkText.Follow(box.Ink, ink, s.colourFollow, dt) : ink;
            box.Coloured = true;
            float fade = Mathf.Clamp01(1f - Mathf.Abs(s.x - x) / 1920f * s.falloff);
            shapes.Box(x - s.width * .5f, y - s.height * .5f, s.width, s.height, MkText.Fade(box.Back, fade), Look.Health.corner);
            MkText.Size(box.Name, Look, Look.Text.selector);
            MkText.Say(box.Name, name.ToUpperInvariant());
            MkText.At(box.Name, x, y);
            box.Name.color = MkText.Fade(box.Ink, fade);
            MkText.Size(box.Rounds, Look, Look.Text.ammo);
            bool ranged = runtime != null && runtime.Data.Category != WeaponCategory.Melee;
            MkText.Say(box.Rounds, ranged ? runtime.Ammo + "/" + runtime.ReserveAmmo : "1");
            MkText.At(box.Rounds, x, y);
            box.Rounds.color = MkText.Fade(box.Ink, fade);
            Place(box.Icon, x - s.width * .5f + s.height / 6f, y - s.height * .5f + s.height / 5f, s.height / 3f);
            Show(box.Icon, ranged ? Hud.AmmoIcon(runtime.Data) : MkHud.Icon("weapon"), MkText.Fade(box.Ink, s.iconAlpha * fade));
        }
        shapes.Show();
    }

    // A slot with something in it: a weapon, or the class knife in the melee slot (as the old slots showed it).
    private bool Held(int index, out string name)
    {
        var data = bound.GetWeapon(index);
        name = data != null ? data.DisplayName ?? "" : index == MeleeSlot ? MeleeKnife.SlotName(Hud.Player) : "";
        return !string.IsNullOrEmpty(name);
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The title screen's 무기 page (user, 2026-11-11: "무기 스킨 탭도 만들 것"): every gun down the left as slanted plates,
/// each with its kill-count rank before its name and its kills (GunKills); the gun picked turning slowly in the middle,
/// drawn by a camera of the page's own far below the scene into the page (a drag turns it); and at the right its rank, its
/// kills and headshots, how far to the next rank, and its skins (GunSkins): its own look, solid finishes, camouflage, and
/// the pack's other materials for it. A skin picked is worn at once by the gun shown, kept in gun_setups.json, worn by
/// this player's gun in every game and, in a room, seen by friends. The classes page's look.
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuWeapons : MonoBehaviour
{
    private const float LeftWidth = 500f, PlateHeight = 56f, PlateGap = 10f, Slant = 12f, RightWidth = 560f, ChipWidth = 176f,
        ChipHeight = 40f, ChipGap = 8f, ViewWidth = 700f, ViewHeight = 440f;
    // A plate's gun picture: where its box starts (left of the kills) and how tall it is.
    private const float IconAt = 252f, IconHeight = 34f;
    private static readonly float[] Tilts = { 1f, -.8f, .6f, -1f, .8f, -.6f, .9f };
    // Where the booth stands, far below anything a map or the title scene puts up, and its camera's field of view.
    private static readonly Vector3 BoothAt = new Vector3(0f, -600f, 0f);
    private const float BoothFov = 22f, SwayDegrees = 18f, SwaySpeed = .35f, DragTurn = .35f;

    private RectTransform root, content;
    private float edge;
    private WeaponData shown;
    private RawImage view;
    private RenderTexture target;
    private GameObject booth, model;
    private Camera boothCamera;
    private Quaternion modelRest = Quaternion.identity;
    private Vector3 modelCenter;
    private float dragYaw, swayTime;

    public bool IsOpen => root != null && root.gameObject.activeSelf;

    public static MainMenuWeapons Make(RectTransform canvas, float edge, float top, float bottom)
    {
        var root = FeelUI.Rect("Weapons", canvas);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(0f, bottom);
        root.offsetMax = new Vector2(0f, -top);
        KitStyle.Fill(root, KitStyle.With(KitStyle.Night, .9f)).raycastTarget = true;
        var page = root.gameObject.AddComponent<MainMenuWeapons>();
        page.root = root;
        page.edge = edge;
        root.gameObject.SetActive(false);
        return page;
    }

    public void Open()
    {
        if (shown == null) shown = Guns().Count > 0 ? Guns()[0] : null;
        Build(true);
    }

    public void Close()
    {
        if (root != null) root.gameObject.SetActive(false);
        DropBooth();
    }

    private void OnDestroy() => DropBooth();

    // The guns the body can hold (FPSBodyAssets), each once, in their list's order.
    private static List<WeaponData> Guns()
    {
        var list = new List<WeaponData>();
        var guns = FPSBodyAssets.Instance != null ? FPSBodyAssets.Instance.guns : null;
        if (guns == null) return list;
        foreach (var entry in guns)
            if (entry.weapon != null && entry.prefab != null && entry.weapon.Category != WeaponCategory.Melee && !list.Contains(entry.weapon))
                list.Add(entry.weapon);
        return list;
    }

    private static GameObject PrefabOf(WeaponData gun)
    {
        var guns = FPSBodyAssets.Instance != null ? FPSBodyAssets.Instance.guns : null;
        if (guns != null)
            foreach (var entry in guns)
                if (entry.weapon == gun) return entry.prefab;
        return null;
    }

    // The page as it is now. Opening, it grows in; a gun or skin picked builds it again at once without the reveal.
    private void Build(bool grow)
    {
        if (content != null) Destroy(content.gameObject);
        root.gameObject.SetActive(true);
        content = KitStyle.Stretch("Content", root);
        var reveal = content.gameObject.AddComponent<KitReveal>();
        reveal.Hide();
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", content, new Vector2(edge, 26f), new Vector2(LeftWidth, 80f)), Texts.Get("weapons.title"), 64f,
            TextAlignmentOptions.MidlineLeft);
        title.rectTransform.parent.localRotation = Quaternion.Euler(0f, 0f, 3f);
        KitStyle.Flat(KitStyle.TopLeft("Where", content, new Vector2(edge + 4f, 108f), new Vector2(1300f, 22f)), Texts.Get("weapons.where"), 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        var guns = Guns();
        float y = 150f, lastX = 0f, lastTilt = 0f, at = .05f;
        for (int i = 0; i < guns.Count; i++)
        {
            float tilt = Tilts[i % Tilts.Length], x = (i % 3) * 10f;
            if (i > 0) y = KitStack.Below(lastX, y, LeftWidth, PlateHeight, lastTilt, x, LeftWidth, tilt, PlateGap);
            Plate(guns[i], new Vector2(edge + x, y), tilt, grow ? reveal : null, at);
            at += .04f;
            lastX = x;
            lastTilt = tilt;
        }
        BuildView(reveal, grow ? at : 0f);
        BuildRight(reveal, grow ? at + .05f : 0f);
        ShowModel();
        reveal.Open(grow);
    }

    // One gun's plate: its rank and name, its kills at the right; the one shown dark with a pink edge.
    private void Plate(WeaponData gun, Vector2 where, float tilt, KitReveal reveal, float at)
    {
        bool picked = gun == shown;
        var plate = KitStyle.TopLeft("Gun " + gun.name, content, where, new Vector2(LeftWidth, PlateHeight));
        plate.localRotation = Quaternion.Euler(0f, 0f, tilt);
        var back = KitStyle.Fill(KitStyle.Stretch("Plate", plate), picked ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, Slant);
        back.raycastTarget = true;
        if (picked)
        {
            var rim = KitStyle.Stretch("Edge", plate);
            rim.sizeDelta = new Vector2(2f, 2f);
            rim.SetAsFirstSibling();
            KitSkew.On(KitStyle.Fill(rim, KitStyle.With(KitStyle.Accent, .75f)), Slant);
        }
        KitSkew.On(KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(4f, PlateHeight), picked ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f)), Slant);
        int kills = GunKills.Kills(gun);
        // The gun's own picture (WeaponIcons) between the name and the kills, the gun at its size; the name then keeps to the
        // room left of it and shrinks to fit there.
        var picture = WeaponIcons.For(gun);
        float named = picture != null ? IconAt - 30f : LeftWidth - 150f;
        var title = KitStyle.Flat(KitStyle.TopLeft("Name", plate, new Vector2(22f, 4f), new Vector2(named, 30f)), gun.DisplayName, 24f,
            TextAlignmentOptions.MidlineLeft, picked ? KitStyle.Text : KitStyle.With(KitStyle.Text, .85f));
        if (picture != null)
        {
            title.enableAutoSizing = true;
            title.fontSizeMin = 15f;
            title.fontSizeMax = 24f;
            var icon = KitStyle.TopLeft("Icon", plate, new Vector2(IconAt, (PlateHeight - IconHeight) * .5f),
                new Vector2(IconHeight * WeaponIcons.Aspect, IconHeight)).gameObject.AddComponent<Image>();
            icon.sprite = picture;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.color = picked ? KitStyle.Text : KitStyle.With(KitStyle.Text, .55f);
        }
        var rank = KitStyle.Flat(KitStyle.TopLeft("Rank", plate, new Vector2(24f, 32f), new Vector2(named, 20f)),
            GunKills.RankName(GunKills.RankOf(kills)), 13f, TextAlignmentOptions.MidlineLeft, KitStyle.With(KitStyle.Accent, .9f));
        rank.fontStyle = FontStyles.Normal;
        KitStyle.Flat(KitStyle.TopLeft("Kills", plate, new Vector2(LeftWidth - 130f, 0f), new Vector2(110f, PlateHeight)), kills.ToString("N0"), 22f,
            TextAlignmentOptions.MidlineRight, picked ? KitStyle.Text : KitStyle.Soft);
        if (reveal != null)
        {
            var lid = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
            KitSkew.On(lid, Slant);
            reveal.Card(plate, lid, at);
        }
        var button = plate.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(() => Pick(gun));
        plate.gameObject.AddComponent<KitHover>();
    }

    private void Pick(WeaponData gun)
    {
        if (gun == shown) return;
        shown = gun;
        dragYaw = 0f;
        Build(false);
    }

    // The middle: the booth's picture, turned by a drag.
    private void BuildView(KitReveal reveal, float at)
    {
        float x = edge + LeftWidth + 40f;
        var frame = KitStyle.TopLeft("View", content, new Vector2(x, 160f), new Vector2(ViewWidth, ViewHeight));
        KitStyle.Fill(KitStyle.Stretch("Back", frame), KitStyle.With(KitStyle.Ice, .03f));
        KitStyle.Bar("Floor", frame, new Vector2(0f, ViewHeight - 2f), new Vector2(ViewWidth, 1f), KitStyle.Faint);
        view = new GameObject("Gun", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        view.rectTransform.SetParent(frame, false);
        view.rectTransform.anchorMin = Vector2.zero;
        view.rectTransform.anchorMax = Vector2.one;
        view.rectTransform.offsetMin = view.rectTransform.offsetMax = Vector2.zero;
        view.raycastTarget = true;
        var trigger = view.gameObject.AddComponent<EventTrigger>();
        var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        drag.callback.AddListener(data => { if (data is PointerEventData pointer) dragYaw -= pointer.delta.x * DragTurn; });
        trigger.triggers.Add(drag);
        if (reveal != null && at > 0f)
        {
            var lid = KitStyle.Fill(KitStyle.Stretch("Flash", frame), KitStyle.Flash);
            reveal.Card(frame, lid, at);
        }
    }

    // The right: the gun's rank and counts, the way to the next rank, and its skins in groups.
    private void BuildRight(KitReveal reveal, float at)
    {
        if (shown == null) return;
        float x = 1920f - edge - RightWidth;
        var panel = KitStyle.TopLeft("Detail", content, new Vector2(x, 150f), new Vector2(RightWidth, 760f));
        int kills = GunKills.Kills(shown), rank = GunKills.RankOf(kills), next = GunKills.NextAt(rank);
        KitStyle.Flat(KitStyle.TopLeft("Name", panel, new Vector2(0f, 0f), new Vector2(RightWidth, 48f)), shown.DisplayName, 40f,
            TextAlignmentOptions.MidlineLeft);
        KitStyle.Flat(KitStyle.TopLeft("Rank", panel, new Vector2(2f, 50f), new Vector2(RightWidth, 26f)), GunKills.RankName(rank), 20f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        var counts = KitStyle.Flat(KitStyle.TopLeft("Counts", panel, new Vector2(2f, 80f), new Vector2(RightWidth, 22f)),
            Texts.Format("weapons.kills", kills, GunKills.Headshots(shown)), 16f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        counts.fontStyle = FontStyles.Normal;
        // How far into this rank, on the way to the next.
        float share = next < 0 ? 1f : Mathf.InverseLerp(GunKills.RankStart(rank), next, kills);
        KitStyle.Bar("Track", panel, new Vector2(2f, 110f), new Vector2(RightWidth - 4f, 4f), KitStyle.Faint);
        KitStyle.Bar("Fill", panel, new Vector2(2f, 110f), new Vector2((RightWidth - 4f) * Mathf.Clamp01(share), 4f), KitStyle.Accent);
        var toNext = KitStyle.Flat(KitStyle.TopLeft("Next", panel, new Vector2(2f, 118f), new Vector2(RightWidth, 20f)),
            next < 0 ? Texts.Get("weapons.top") : Texts.Format("weapons.next", GunKills.RankName(rank + 1), next - kills), 13f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        toNext.fontStyle = FontStyles.Normal;

        string worn = GunSkins.ChosenFor(shown);
        float y = 160f;
        foreach (GunSkins.Kind kind in new[] { GunSkins.Kind.Default, GunSkins.Kind.Finish, GunSkins.Kind.Camo, GunSkins.Kind.Pack })
        {
            var skins = GunSkins.For(shown).FindAll(skin => skin.Kind == kind);
            if (skins.Count == 0) continue;
            KitStyle.Flat(KitStyle.TopLeft("Group " + kind, panel, new Vector2(2f, y), new Vector2(RightWidth, 22f)),
                Texts.Get("weapons.group." + kind.ToString().ToLowerInvariant()), 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
            y += 26f;
            for (int i = 0; i < skins.Count; i++)
            {
                int column = i % 3;
                if (i > 0 && column == 0) y += ChipHeight + ChipGap;
                Chip(panel, skins[i], new Vector2(column * (ChipWidth + ChipGap), y), skins[i].Id == worn);
            }
            y += ChipHeight + 18f;
        }
        if (reveal != null && at > 0f)
        {
            var lid = KitStyle.Fill(KitStyle.Stretch("Flash", panel), KitStyle.Flash);
            reveal.Card(panel, lid, at);
        }
    }

    // One skin: its colour (a camo shows its colours side by side) and its name; the one worn edged in pink.
    private void Chip(RectTransform panel, GunSkins.Skin skin, Vector2 at, bool worn)
    {
        var chip = KitStyle.TopLeft("Skin " + skin.Id, panel, at, new Vector2(ChipWidth, ChipHeight));
        var back = KitStyle.Fill(KitStyle.Stretch("Back", chip), worn ? KitStyle.With(KitStyle.Night, .95f) : KitStyle.With(KitStyle.Ice, .07f));
        KitSkew.On(back, 6f);
        back.raycastTarget = true;
        if (worn)
        {
            var rim = KitStyle.Stretch("Edge", chip);
            rim.sizeDelta = new Vector2(2f, 2f);
            rim.SetAsFirstSibling();
            KitSkew.On(KitStyle.Fill(rim, KitStyle.Accent), 6f);
        }
        var colors = skin.Kind == GunSkins.Kind.Default ? new Color[0] : skin.Colors;
        for (int i = 0; i < Mathf.Min(4, colors.Length); i++)
            KitStyle.Bar("Swatch", chip, new Vector2(8f + i * 9f, 8f), new Vector2(9f, ChipHeight - 16f), colors[i]);
        float textX = colors.Length > 0 ? 14f + Mathf.Min(4, colors.Length) * 9f : 12f;
        var label = KitStyle.Flat(KitStyle.TopLeft("Name", chip, new Vector2(textX, 0f), new Vector2(ChipWidth - textX - 6f, ChipHeight)), skin.Name, 14f,
            TextAlignmentOptions.MidlineLeft, worn ? KitStyle.Text : KitStyle.With(KitStyle.Text, .8f));
        label.fontStyle = FontStyles.Normal;
        label.overflowMode = CombatUIAssets.ClippedOverflow(label);
        var button = chip.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        string id = skin.Id;
        button.onClick.AddListener(() => Wear(id));
        chip.gameObject.AddComponent<KitHover>();
    }

    private void Wear(string id)
    {
        if (shown == null || GunSkins.ChosenFor(shown) == id) return;
        GunSkins.Choose(shown, id);
        Build(false);
    }

    // ---- The booth: the gun picked, its light and its camera, far below the scene ----

    private void ShowModel()
    {
        EnsureBooth();
        if (model != null) Destroy(model);
        model = null;
        var prefab = shown != null ? PrefabOf(shown) : null;
        if (prefab == null || booth == null) return;
        // Made under a switched-off holder so none of the gun's own scripts wake: its first sight and no parts, as it
        // comes, then only its look copied out (HolsteredGuns.CopyLook).
        var holder = new GameObject("Gun (building)");
        holder.SetActive(false);
        var instance = Instantiate(prefab, holder.transform, false);
        if (instance.TryGetComponent(out GunSights sights) && sights.Count > 0) sights.Select(sights.initial);
        if (instance.TryGetComponent(out GunAttachments attachments)) attachments.Begin();
        model = HolsteredGuns.CopyLook(instance.transform, out var renderers, out var map);
        Destroy(holder);
        model.name = shown.name + " (shown)";
        model.hideFlags = HideFlags.DontSave;
        foreach (var renderer in renderers)
            if (renderer != null) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GunSkins.Wear(model, shown, GunSkins.ChosenFor(shown));
        // Its own frame: the sight line forward and up (the pack's AimPoint), else the model's.
        Transform aim = null;
        foreach (var part in model.GetComponentsInChildren<Transform>(true))
            if (part.name == "AimPoint") { aim = part; break; }
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Vector3 forward = aim != null ? aim.forward : Vector3.forward, up = aim != null ? aim.up : Vector3.up;
        // Shown from its right side, muzzle to the right of the picture.
        modelRest = Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(forward, up));
        var bounds = new Bounds();
        bool any = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            var b = renderer.bounds;
            if (b.size.sqrMagnitude > 4f) continue; // a part parked far off the gun
            if (!any) { bounds = b; any = true; }
            else bounds.Encapsulate(b);
        }
        modelCenter = any ? bounds.center : Vector3.zero;
        float size = any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : .8f;
        model.transform.SetParent(booth.transform, false);
        swayTime = 0f;
        Pose();
        // Far enough that the whole gun fits the picture's width with room round it.
        float aspect = ViewWidth / ViewHeight;
        float distance = size * .62f / Mathf.Tan(BoothFov * .5f * Mathf.Deg2Rad) / Mathf.Max(.5f, aspect * .75f);
        boothCamera.transform.localPosition = new Vector3(0f, size * .06f, -distance);
        boothCamera.transform.localRotation = Quaternion.identity;
        boothCamera.nearClipPlane = Mathf.Max(.01f, distance * .2f);
        boothCamera.farClipPlane = distance * 4f;
    }

    // The gun turned about its middle: a slow sway and whatever the drag added.
    private void Pose()
    {
        if (model == null) return;
        float yaw = Mathf.Sin(swayTime * SwaySpeed * Mathf.PI * 2f) * SwayDegrees + dragYaw;
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f) * modelRest;
        model.transform.localRotation = turn;
        model.transform.localPosition = -(turn * modelCenter);
    }

    private void Update()
    {
        if (!IsOpen || model == null) return;
        swayTime += Time.unscaledDeltaTime;
        Pose();
    }

    private void EnsureBooth()
    {
        if (booth != null)
        {
            boothCamera.enabled = true;
            if (view != null) view.texture = target;
            return;
        }
        booth = new GameObject("Weapons Booth") { hideFlags = HideFlags.DontSave };
        booth.transform.position = BoothAt;
        target = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32) { name = "Weapons Booth", antiAliasing = 4, hideFlags = HideFlags.DontSave };
        var eye = new GameObject("Camera");
        eye.transform.SetParent(booth.transform, false);
        boothCamera = eye.AddComponent<Camera>();
        boothCamera.fieldOfView = BoothFov;
        boothCamera.clearFlags = CameraClearFlags.SolidColor;
        boothCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        boothCamera.targetTexture = target;
        boothCamera.allowHDR = false;
        boothCamera.useOcclusionCulling = false;
        // A key light high on the left, a pink rim behind on the right, a soft fill from below: near, so nothing in the
        // title scene far above takes their light.
        Lamp("Key", new Vector3(-1.4f, 1.3f, -1.6f), Color.white, 2.2f);
        Lamp("Rim", new Vector3(1.6f, .6f, 1.4f), KitStyle.Accent, 1.6f);
        Lamp("Fill", new Vector3(.4f, -1.2f, -1f), new Color(.75f, .8f, .9f), .7f);
        if (view != null) view.texture = target;
    }

    private void Lamp(string name, Vector3 at, Color color, float intensity)
    {
        var lamp = new GameObject(name);
        lamp.transform.SetParent(booth.transform, false);
        lamp.transform.localPosition = at;
        var light = lamp.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 6f;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    private void DropBooth()
    {
        if (model != null) Destroy(model);
        model = null;
        if (booth != null) Destroy(booth);
        booth = null;
        boothCamera = null;
        if (target != null)
        {
            target.Release();
            Destroy(target);
        }
        target = null;
    }
}

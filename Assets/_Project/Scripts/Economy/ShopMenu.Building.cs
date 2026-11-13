using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ShopMenu: Building. One part of the class; the rest is in ShopMenu.cs and the files beside it.
public sealed partial class ShopMenu
{
    // The canvas and the panel's frame, title, money and X; the pages go in between (ShowPage).
    private void Build()
    {
        if (canvas != null) Destroy(canvas);
        rows.Clear();
        keyed.Clear();
        page = null;
        pageReveal = null;
        LoadStock();
        canvas = new GameObject("Shop", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas.hideFlags = HideFlags.DontSave;
        canvas.transform.SetParent(transform, false);
        var layer = canvas.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = 95;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        builtFor = new Vector2Int(Screen.width, Screen.height);

        float height = PanelHeight();
        panel = FeelUI.Rect("Panel", canvas.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0f, .5f);
        panel.pivot = new Vector2(0f, .5f);
        panel.anchoredPosition = new Vector2(Margin, 0f);
        panel.sizeDelta = new Vector2(Width, height);
        KitStyle.Fill(panel, KitStyle.Panel);
        reveal = panel.gameObject.AddComponent<KitReveal>();

        // Frame lines: down the left and right edges, and under the title.
        var left = KitStyle.Bar("Left Line", panel, Vector2.zero, new Vector2(KitStyle.LineWidth, height), KitStyle.Line).rectTransform;
        reveal.Line(left, true, 0f);
        var right = KitStyle.Bar("Right Line", panel, new Vector2(Width - KitStyle.LineWidth, 0f), new Vector2(KitStyle.LineWidth, height * .35f), KitStyle.Line).rectTransform;
        reveal.Line(right, true, .1f);
        var under = KitStyle.Bar("Title Line", panel, new Vector2(Inner, HeaderHeight - 10f), new Vector2(Width - Inner * 2f, 1f), KitStyle.Line).rectTransform;
        reveal.Line(under, false, .06f, .3f);

        // The title and its line of sections slide in from the left behind a mask.
        var titleMask = KitStyle.TopLeft("Title Mask", panel, new Vector2(Inner, 14f), new Vector2(300f, 56f));
        titleMask.gameObject.AddComponent<RectMask2D>();
        var titleBox = KitStyle.TopLeft("Title", titleMask, Vector2.zero, new Vector2(300f, 34f));
        KitStyle.Label(titleBox, Texts.Get("shop.title"), 30f, TextAlignmentOptions.Left);
        reveal.Slide(titleBox, new Vector2(300f, 0f), .05f);
        var subBox = KitStyle.TopLeft("Sections", titleMask, new Vector2(0f, 36f), new Vector2(300f, 18f));
        KitStyle.Label(subBox, Texts.Get("shop.subtitle"), 14f, TextAlignmentOptions.Left, KitStyle.Accent);
        reveal.Slide(subBox, new Vector2(300f, 0f), .1f);

        // The money in a dark box, and the X. The box is sized to its number every frame (Refresh), so what is inside
        // hangs from its right edge: the number 10 px in, the banknote left of the number, a lid that flashes on a buy.
        moneyBox = KitStyle.TopLeft("Money", panel, new Vector2(Width - Inner - CloseSize - 8f - MoneyWidth, 18f), new Vector2(MoneyWidth, CloseSize));
        KitStyle.Fill(moneyBox, KitStyle.Card);
        moneyFlash = KitStyle.Fill(KitStyle.Stretch("Flash", moneyBox), Color.clear);
        var moneyText = KitStyle.Stretch("Value", moneyBox);
        moneyText.offsetMax = new Vector2(-10f, 0f);
        money = KitStyle.Label(moneyText, "0", MoneySize, TextAlignmentOptions.Right);
        var holder = FeelUI.Rect("Icon Holder", moneyBox);
        holder.anchorMin = holder.anchorMax = new Vector2(1f, .5f);
        holder.anchoredPosition = new Vector2(-10f, 0f);
        moneyIcon = MoneyIcon.Make(holder, 20f).rectTransform;
        // What a buy cost, under the money box's right end (Refresh moves and fades it).
        spentBox = KitStyle.TopLeft("Spent", panel, new Vector2(Width - Inner - CloseSize - 18f - 190f, 18f + CloseSize + 1f), new Vector2(190f, 18f));
        spent = KitStyle.Label(spentBox, "", 14f, TextAlignmentOptions.Right, Color.clear);
        closeBox = KitStyle.TopLeft("Close", panel, new Vector2(Width - Inner - CloseSize, 18f), new Vector2(CloseSize, CloseSize));
        closeBox.pivot = new Vector2(.5f, .5f);
        closeBox.anchoredPosition += new Vector2(CloseSize * .5f, -CloseSize * .5f);
        closeFill = KitStyle.Fill(closeBox, KitStyle.Card);
        closeFill.raycastTarget = true;
        KitStyle.Label(closeBox, "X", 18f, TextAlignmentOptions.Center);
        var closeButton = closeBox.gameObject.AddComponent<Button>();
        closeButton.transition = Selectable.Transition.None;
        closeButton.onClick.AddListener(BeginClose);
        closeBox.gameObject.AddComponent<KitHover>();

        sweep = KitStyle.Bar("Sweep", panel, Vector2.zero, new Vector2(Width, 1f), KitStyle.Line).rectTransform;
        reveal.Sweep(sweep, new Vector2(0f, -HeaderHeight), new Vector2(0f, -(height - FooterHeight)), .15f, .5f);
        var footer = KitStyle.TopLeft("Message", panel, new Vector2(Inner, height - FooterHeight), new Vector2(Width - Inner * 2f, FooterHeight));
        message = KitStyle.Label(footer, "", 15f, TextAlignmentOptions.Center, KitStyle.Soft);
        message.fontStyle = FontStyles.Normal;
    }

    // The screen changed shape while open (a window resized, the display mode changed): laid out again, on the same page.
    private void Relayout()
    {
        string shownId = shownGroup?.Id;
        Build();
        canvas.SetActive(true);
        reveal.Open(false);
        Say(null, KitStyle.Text);
        ShowPage(shownId != null ? groups.Find(group => group.Id == shownId) : null, PageFrom);
    }

    // As tall as the first page or a category's page needs (up to MostCards guns; more scroll), never taller than the
    // screen leaves: 21:9 is 935 px tall to 16:9's 1080, and an ultrawide less.
    private float PanelHeight()
    {
        float body = FrontHeight();
        foreach (var group in groups) body = Mathf.Max(body, GroupPageHeight(Mathf.Min(group.Guns.Count, MostCards)));
        float natural = HeaderHeight + 2f * KitScroll.Pad + body + FooterHeight;
        return Mathf.Max(HeaderHeight + FooterHeight + 160f, Mathf.Min(natural, CanvasHeight() - 2f * ScreenRoom));
    }

    // The canvas's height in its own px: the screen scaled from 1920x1080, width and height weighed alike (the scaler's
    // match of .5), worked out here because the canvas sizes itself only when it is next drawn.
    private static float CanvasHeight()
    {
        float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
        return h / Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w / 1920f, 2f), Mathf.Log(h / 1080f, 2f), .5f));
    }

    // The first page's height: what BuildFront lays out.
    private float FrontHeight()
    {
        int big = 0, small = 0;
        foreach (var item in stock)
            if (item.Kind != Kind.Gun && Offered(item)) { if (IsBig(item.Kind)) big++; else small++; }
        float y = 0f;
        if (big + small > 0) y = SectionHeight + Lines(big, 3) * (TileHeight + Gap) + Lines(small, 2) * (SmallTileHeight + Gap) - Gap;
        if (groups.Count > 0) y += (y > 0f ? SectionGap : 0f) + SectionHeight + Lines(groups.Count, 2) * (GroupHeight + Gap) - Gap;
        return y;
    }

    // A category's page's height with this many guns: what BuildGroup lays out.
    private static float GroupPageHeight(int guns) => BackHeight + Gap + guns * (CardHeight + CardGap) - (guns > 0 ? CardGap : 0f);

    private static int Lines(int count, int across) => across <= 0 ? 0 : (count + across - 1) / across;

    // Puts a page into the panel, the first (group null) or a category's, its cards growing in from `from` (s); a message
    // still showing (what was just bought) stays.
    private void ShowPage(GunGroup group, float from)
    {
        shownGroup = group;
        if (page != null) Destroy(page.gameObject);
        rows.Clear();
        keyed.Clear();
        page = KitScroll.Make("Page", panel, Vector2.zero, Vector2.one, new Vector2(Inner, FooterHeight), new Vector2(-Inner, -HeaderHeight));
        // Under the opening's sweep line and the message.
        page.transform.SetSiblingIndex(sweep.GetSiblingIndex());
        pageReveal = page.gameObject.AddComponent<KitReveal>();
        pageReveal.Hide();
        float width = Width - Inner * 2f;
        page.Fit(group == null ? BuildFront(page.Rows, width, from) : BuildGroup(page.Rows, group, width, from));
        page.Offset = 0f;
        if (messageLeft <= 0f) Say(null, KitStyle.Text);
        pageReveal.Open(false);
    }

    // The first page: the refills (big tiles, then the class's own), then the gun categories. Returns its height.
    private float BuildFront(RectTransform parent, float width, float from)
    {
        var big = new List<Item>();
        var small = new List<Item>();
        foreach (var item in stock)
            if (item.Kind != Kind.Gun && Offered(item)) (IsBig(item.Kind) ? big : small).Add(item);
        float y = 0f, at = from;
        if (big.Count + small.Count > 0)
        {
            SectionLabel(parent, Texts.Get("shop.section.supplies"), y, width);
            y += SectionHeight;
            y = Tiles(parent, big, 3, TileHeight, y, width, ref at);
            y = Tiles(parent, small, 2, SmallTileHeight, y, width, ref at) - Gap;
        }
        if (groups.Count > 0)
        {
            if (y > 0f) y += SectionGap;
            SectionLabel(parent, Texts.Get("shop.section.guns"), y, width);
            y += SectionHeight;
            float plate = (width - Gap) * .5f;
            for (int i = 0; i < groups.Count; i++)
            {
                rows.Add(GroupPlate(parent, groups[i], new Vector2((i % 2) * (plate + Gap), y + (i / 2) * (GroupHeight + Gap)), plate, at));
                at += CardStagger;
            }
            y += Lines(groups.Count, 2) * (GroupHeight + Gap) - Gap;
        }
        return y;
    }

    // A category's page: the plate back to the first page with the category's name beside it, then its guns cheapest
    // first. Returns its height.
    private float BuildGroup(RectTransform parent, GunGroup group, float width, float from)
    {
        var back = Plate(parent, "Back", Vector2.zero, new Vector2(BackWidth, BackHeight), KitStyle.Accent, out var plate);
        back.Back = true;
        KeyBox(plate, Texts.Get("shop.key.back"), new Vector2(14f, (BackHeight - KeySize) * .5f));
        KitStyle.Label(KitStyle.TopLeft("Label", plate, new Vector2(62f, 0f), new Vector2(BackWidth - 70f, BackHeight)), Texts.Get("shop.back"), 17f,
            TextAlignmentOptions.MidlineLeft);
        Finish(back, plate, from);
        rows.Add(back);
        var count = KitStyle.Label(KitStyle.TopLeft("Count", parent, new Vector2(width - 110f, 0f), new Vector2(110f, BackHeight)),
            Texts.Format("shop.cat.count", group.Guns.Count), 14f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        count.fontStyle = FontStyles.Normal;
        Fit(KitStyle.Label(KitStyle.TopLeft("Title", parent, new Vector2(BackWidth + 18f, 0f), new Vector2(width - BackWidth - 136f, BackHeight)),
            group.Name, 24f, TextAlignmentOptions.MidlineLeft), 15f);
        float y = BackHeight + Gap, at = from + CardStagger;
        foreach (var item in group.Guns)
        {
            rows.Add(GunCard(parent, item, y, width, at));
            at += CardStagger;
            y += CardHeight + CardGap;
        }
        return y - CardGap;
    }

    private static void SectionLabel(RectTransform parent, string text, float y, float width)
    {
        var header = KitStyle.TopLeft("Section", parent, new Vector2(0f, y), new Vector2(width, SectionHeight));
        KitStyle.Label(header, text, 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
    }

    // Refill tiles `across` to a row (fewer share the row's width), each `height` tall, from `y`; returns the y under
    // them, a Gap below the last row.
    private float Tiles(RectTransform parent, List<Item> items, int across, float height, float y, float width, ref float at)
    {
        if (items.Count == 0) return y;
        int columns = Mathf.Min(across, items.Count);
        float w = (width - Gap * (columns - 1)) / columns;
        for (int i = 0; i < items.Count; i++)
        {
            var where = new Vector2((i % columns) * (w + Gap), y + (i / columns) * (height + Gap));
            rows.Add(Tile(parent, items[i], where, new Vector2(w, height), at));
            at += CardStagger;
        }
        return y + Lines(items.Count, columns) * (height + Gap);
    }

    // A refill tile: its key, what it is and gives, how much is held now at its top right, its price. A big tile stacks
    // them (key, then name, small print, price at its foot); a small one sets them side by side.
    private Row Tile(RectTransform parent, Item item, Vector2 at, Vector2 size, float from)
    {
        var row = Plate(parent, "Tile " + item.Id, at, size, AccentOf(item.Kind), out var plate);
        row.Item = item;
        row.Detail = item.Detail;
        float w = size.x, h = size.y;
        bool big = h >= TileHeight - 1f;
        KeyBox(plate, NextKey(row), new Vector2(14f, big ? 10f : 8f));
        var count = KitStyle.TopLeft("Count", plate, new Vector2(w - (big ? 84f : 74f), big ? 9f : 7f), new Vector2(big ? 70f : 60f, KeySize));
        row.Count = KitStyle.Label(count, "", 13f, TextAlignmentOptions.MidlineRight, KitStyle.Soft);
        row.Count.fontStyle = FontStyles.Normal;
        if (big)
        {
            row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(14f, 34f), new Vector2(w - 28f, 28f)), item.Name, 21f,
                TextAlignmentOptions.MidlineLeft), 14f);
            row.Info = Small(KitStyle.TopLeft("Detail", plate, new Vector2(14f, 62f), new Vector2(w - 28f, 18f)), item.Detail);
            row.Price = PriceLabel(plate, Wallet.Format(item.Price), new Vector2(14f, h - 32f), new Vector2(w - 28f, 26f), 21f);
        }
        else
        {
            row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(44f, 5f), new Vector2(w - 124f, 26f)), item.Name, 18f,
                TextAlignmentOptions.MidlineLeft), 13f);
            row.Info = Small(KitStyle.TopLeft("Detail", plate, new Vector2(44f, 33f), new Vector2(w - 134f, 20f)), item.Detail);
            row.Price = PriceLabel(plate, Wallet.Format(item.Price), new Vector2(w - 88f, 30f), new Vector2(74f, 26f), 19f);
        }
        Finish(row, plate, from);
        return row;
    }

    // A category's plate: its key and name, how many guns it sells and a 가진 총 tag while one of them is held, the
    // cheapest and dearest of them, and a chevron: it opens a page.
    private Row GroupPlate(RectTransform parent, GunGroup group, Vector2 at, float width, float from)
    {
        var row = Plate(parent, "Group " + group.Id, at, new Vector2(width, GroupHeight), KitStyle.With(KitStyle.Ice, .35f), out var plate);
        row.Group = group;
        KeyBox(plate, NextKey(row), new Vector2(14f, 11f));
        row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(44f, 5f), new Vector2(width - 82f, 34f)), group.Name, 24f,
            TextAlignmentOptions.MidlineLeft), 15f);
        row.Info = Small(KitStyle.TopLeft("Count", plate, new Vector2(16f, 41f), new Vector2(64f, 18f)), Texts.Format("shop.cat.count", group.Guns.Count));
        Tag(row, plate, new Vector2(84f, 40f), new Vector2(66f, 20f));
        long cheapest = group.Guns[0].Price, dearest = group.Guns[group.Guns.Count - 1].Price;
        string prices = cheapest == dearest ? Wallet.Format(cheapest) : Texts.Format("shop.cat.prices", Wallet.Format(cheapest), Wallet.Format(dearest));
        // The prices after the banknote, left under the count.
        var icon = MoneyIcon.Make(plate, 14f);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, 1f);
        icon.rectTransform.anchoredPosition = new Vector2(24f, -69f);
        row.Price = KitStyle.Label(KitStyle.TopLeft("Prices", plate, new Vector2(36f, 60f), new Vector2(width - 76f, 18f)), prices, 13f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        Chevron(plate, new Vector2(width - 24f, GroupHeight * .5f));
        Finish(row, plate, from);
        return row;
    }

    // A gun's card on its category's page: its key, its picture, its name with its shot and magazine under it, a tag
    // (구매 완료, 가진 총, or what the money is short by) and its price.
    private Row GunCard(RectTransform parent, Item item, float y, float width, float from)
    {
        var row = Plate(parent, "Gun " + item.Id, new Vector2(0f, y), new Vector2(width, CardHeight), AccentOf(Kind.Gun), out var plate);
        row.Item = item;
        row.Detail = item.Detail;
        KeyBox(plate, NextKey(row), new Vector2(14f, (CardHeight - KeySize) * .5f));
        float x = 46f;
        // The gun's own picture (WeaponIcons: every gun on one canvas, so each shows at its size).
        var picture = WeaponIcons.For(item.Weapon);
        if (picture != null)
        {
            var box = KitStyle.TopLeft("Picture", plate, new Vector2(x, (CardHeight - PictureHeight) * .5f), new Vector2(PictureHeight * WeaponIcons.Aspect, PictureHeight));
            row.Picture = box.gameObject.AddComponent<Image>();
            row.Picture.sprite = picture;
            row.Picture.preserveAspect = true;
            row.Picture.raycastTarget = false;
            x += PictureHeight * WeaponIcons.Aspect + 8f;
        }
        float named = width - 168f - x;
        row.Name = Fit(KitStyle.Label(KitStyle.TopLeft("Name", plate, new Vector2(x, 6f), new Vector2(named, 26f)), item.Name, 19f,
            TextAlignmentOptions.MidlineLeft), 13f);
        row.Info = Small(KitStyle.TopLeft("Detail", plate, new Vector2(x, 33f), new Vector2(named, 18f)), item.Detail);
        Tag(row, plate, new Vector2(width - 164f, (CardHeight - 22f) * .5f), new Vector2(70f, 22f));
        row.Price = PriceLabel(plate, Wallet.Format(item.Price), new Vector2(width - 90f, 0f), new Vector2(76f, CardHeight), 20f);
        Finish(row, plate, from);
        return row;
    }

    // A dark plate leaning by Slant (KitSkew, so clicks follow the lean: KitHit) with its kind's bar down its left side.
    private static Row Plate(RectTransform parent, string name, Vector2 at, Vector2 size, Color bar, out RectTransform plate)
    {
        plate = KitStyle.TopLeft(name, parent, at, size);
        var row = new Row { PlateColor = KitStyle.Card, BarColor = bar };
        row.Plate = KitStyle.Fill(KitStyle.Stretch("Plate", plate), KitStyle.Card);
        row.Plate.raycastTarget = true;
        KitSkew.On(row.Plate, Slant);
        row.Bar = KitStyle.Bar("Bar", plate, Vector2.zero, new Vector2(KitStyle.AccentWidth, size.y), bar);
        KitSkew.On(row.Bar, Slant);
        return row;
    }

    // The pink lid a plate grows in under (and a buy's pulse lights), its click and its hover.
    private void Finish(Row row, RectTransform plate, float from)
    {
        row.Flash = KitStyle.Fill(KitStyle.Stretch("Flash", plate), KitStyle.Flash);
        KitSkew.On(row.Flash, Slant);
        pageReveal.Card(plate, row.Flash, from);
        var button = plate.gameObject.AddComponent<Button>();
        button.targetGraphic = row.Plate;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Pick(row));
        plate.gameObject.AddComponent<KitHover>();
    }

    // The next number key on the page for a row, or null once they run out (it is still clicked).
    private string NextKey(Row row)
    {
        keyed.Add(row);
        return keyed.Count <= KeyNames.Length ? KeyNames[keyed.Count - 1] : null;
    }

    // The key that picks it: a small white box with the key in black (the perk cards' look).
    private static void KeyBox(RectTransform plate, string key, Vector2 at)
    {
        if (string.IsNullOrEmpty(key)) return;
        var box = KitStyle.TopLeft("Key", plate, at, new Vector2(Mathf.Max(KeySize, 12f + 9f * key.Length), KeySize));
        KitSkew.On(KitStyle.Fill(box, KitStyle.Ice), 4f);
        KitStyle.Flat(box, key, 14f, TextAlignmentOptions.Center, KitStyle.Ink);
    }

    // A tag box Refresh fills: white with black words for 가진 총, words alone for the rest, nothing while it has nothing to say.
    private static void Tag(Row row, RectTransform plate, Vector2 at, Vector2 size)
    {
        var box = KitStyle.TopLeft("Tag", plate, at, size);
        row.TagBack = KitStyle.Fill(box, Color.clear);
        KitSkew.On(row.TagBack, 4f);
        row.Tag = Fit(KitStyle.Flat(box, "", 12f, TextAlignmentOptions.Center, KitStyle.Ink), 9f);
    }

    // A price right-aligned in its box, the banknote just left of the number.
    private static TextMeshProUGUI PriceLabel(RectTransform plate, string price, Vector2 at, Vector2 size, float fontSize)
    {
        var box = KitStyle.TopLeft("Price", plate, at, size);
        var label = KitStyle.Label(box, price, fontSize, TextAlignmentOptions.MidlineRight);
        var icon = MoneyIcon.Make(box, fontSize * .9f);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(1f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(-label.GetPreferredValues(price).x - fontSize * .45f - 5f, 0f);
        return label;
    }

    // A small > at `center` (px from the plate's top left): the plate opens a page.
    private static void Chevron(RectTransform plate, Vector2 center)
    {
        for (int i = 0; i < 2; i++)
        {
            var arm = KitStyle.TopLeft("Chevron", plate, center, new Vector2(11f, 2.5f));
            arm.pivot = new Vector2(1f, .5f);
            arm.anchoredPosition = new Vector2(center.x + 4f, -center.y);
            arm.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            KitStyle.Fill(arm, KitStyle.Soft);
        }
    }

    // Shrinks a label to fit its box on one line, down to `smallest`.
    private static TextMeshProUGUI Fit(TextMeshProUGUI label, float smallest)
    {
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableAutoSizing = true;
        label.fontSizeMin = smallest;
        label.fontSizeMax = label.fontSize;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    // The small print under a name: soft, not bold, shrinking to fit.
    private static TextMeshProUGUI Small(RectTransform box, string text)
    {
        var label = KitStyle.Label(box, text, 12.5f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        label.fontStyle = FontStyles.Normal;
        return Fit(label, 10f);
    }
}

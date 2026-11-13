using UnityEngine;
using UnityEngine.UI;

// KillFlairFx: pieces. One part of the class; the rest is in KillFlairFx.cs and the files beside it.
public sealed partial class KillFlairFx
{
    private float ChipY => -KillFeedRow.Height * .5f - 12f;

    private bool Layer()
    {
        if (layer != null) return true;
        if (shader == null) shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null) return false;
        material = new Material(shader) { name = "Kill Flair (Runtime)", hideFlags = HideFlags.DontSave };
        layer = FeelUI.Rect("Flair", weapon.rectTransform).gameObject.AddComponent<Image>();
        Stretch(layer.rectTransform, 0f);
        layer.preserveAspect = true;
        layer.raycastTarget = false;
        layer.material = material;
        layer.gameObject.AddComponent<OwnedMaterial>().Own(material);
        layer.gameObject.SetActive(false);
        return true;
    }

    private void PlaceStamp(string words, Color colour)
    {
        MakeStamp();
        stampWords.text = words;
        stampWords.color = colour;
        stampEdge.color = colour;
        stampFill.color = Night;
        float width = stampWords.GetPreferredValues(words).x + 18f, height = 26f;
        stamp.sizeDelta = new Vector2(width, height);
        // Over the row's end, a little above it, clear of the names under it; the feed makes the room (Above).
        stamp.anchoredPosition = new Vector2(-14f - width * .5f, KillFeedRow.Height * .5f + height * .5f - 6f);
        stamp.SetAsLastSibling();
        stamp.gameObject.SetActive(true);
        stampGroup.alpha = 0f;
    }

    private void PlaceSeal(string words, Color colour)
    {
        MakeSeal();
        // Two lines, broken at the space ("계약 완료" → 계약 / 완료).
        sealWords.text = words.Replace(' ', '\n');
        sealWords.color = colour;
        sealRing.color = colour;
        sealFill.color = Night;
        for (int i = 0; i < specks.Length; i++)
        {
            specks[i].color = colour;
            float angle = Random.value * Mathf.PI * 2f, reach = 26f + Random.value * 16f;
            speckWay[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
        }
        seal.sizeDelta = Vector2.one * 46f;
        // On the row's end, over its end bar, standing a little out past it.
        seal.anchoredPosition = new Vector2(8f, 5f);
        seal.SetAsLastSibling();
        seal.gameObject.SetActive(true);
        sealGroup.alpha = 0f;
    }

    private void PlaceChip(bool money, string words, Color colour, bool besideSeal)
    {
        MakeChip();
        chipMoney.gameObject.SetActive(money);
        chipWords.text = money ? paid.ToString() : words;
        chipWords.color = colour;
        chipPlate.color = Night;
        float text = chipWords.GetPreferredValues(money ? Mathf.Max(paid, 10).ToString() : words).x;
        float left = money ? 24f : 8f, width = left + text + 8f, height = 20f;
        chip.sizeDelta = new Vector2(width, height);
        chipMoney.rectTransform.anchoredPosition = new Vector2(13f, 0f);
        chipWords.rectTransform.anchoredPosition = new Vector2(left, 0f);
        chipWords.rectTransform.sizeDelta = new Vector2(text + 4f, height);
        // Under the row's end; beside a seal, left of it so the two never cover each other; the feed makes the room (Below).
        chip.anchoredPosition = new Vector2((besideSeal ? -40f : -6f) - width * .5f, ChipY);
        chip.SetAsLastSibling();
        chip.gameObject.SetActive(true);
        chipGroup.alpha = 0f;
    }

    private void PlaceStrike(Vector2 at, float wide, Color colour)
    {
        MakeStrike();
        strike.color = colour;
        strike.rectTransform.sizeDelta = new Vector2(wide + 2f, 2.5f);
        strike.rectTransform.anchoredPosition = new Vector2(at.x - wide * .5f - 1f, at.y);
        strike.rectTransform.localScale = new Vector3(0f, 1f, 1f);
        strike.rectTransform.SetAsLastSibling();
        strike.gameObject.SetActive(true);
    }

    private void PlaceFrame(Vector2 at, float wide, Color colour)
    {
        MakeFrame();
        float w = wide + 14f, h = KillFeedRow.Height - 6f, arm = 8f, line = 2.5f;
        frame.sizeDelta = new Vector2(w, h);
        frame.anchoredPosition = at;
        // The brackets lean as the plate's ends do (KillRowPlate: its top 9 px right of its bottom over the row's 32), so
        // every outline a flair draws keeps the row's own shape.
        float lean = PlateShear / KillFeedRow.Height, tilt = -Mathf.Atan(lean) * Mathf.Rad2Deg;
        for (int i = 0; i < 4; i++)
        {
            float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? 1f : -1f;
            float cx = sx * w * .5f + sy * lean * h * .5f, cy = sy * h * .5f;
            var across = corners[i * 2].rectTransform;
            across.sizeDelta = new Vector2(arm, line);
            across.anchoredPosition = new Vector2(cx - sx * arm * .5f, cy - sy * line * .5f);
            across.localRotation = Quaternion.identity;
            var down = corners[i * 2 + 1].rectTransform;
            down.sizeDelta = new Vector2(line, arm);
            down.anchoredPosition = new Vector2(cx - sx * line * .5f - sy * lean * arm * .5f, cy - sy * arm * .5f);
            down.localRotation = Quaternion.Euler(0f, 0f, tilt);
            corners[i * 2].color = corners[i * 2 + 1].color = colour;
        }
        frame.SetAsLastSibling();
        frame.gameObject.SetActive(true);
        frameGroup.alpha = 0f;
    }

    private void PlaceTick(Vector2 at, float wide, Color colour)
    {
        MakeTick();
        // A check across the one killed: a short stroke down, then a long one up to the right.
        float span = Mathf.Max(24f, wide * .7f);
        tick.anchoredPosition = at;
        tickShort.color = tickLong.color = colour;
        tickShort.rectTransform.sizeDelta = new Vector2(span * .32f, 3.5f);
        tickShort.rectTransform.anchoredPosition = new Vector2(-span * .42f, 2f);
        tickShort.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -38f);
        tickLong.rectTransform.sizeDelta = new Vector2(span * .78f, 3.5f);
        tickLong.rectTransform.anchoredPosition = new Vector2(-span * .2f, -5f);
        tickLong.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 30f);
        tick.SetAsLastSibling();
        tick.gameObject.SetActive(true);
        tickGroup.alpha = 0f;
    }

    private void PlaceTag(string words, Color colour)
    {
        MakeTag();
        tag.text = words;
        tag.color = colour;
        tag.alpha = 0f;
        tag.gameObject.SetActive(true);
    }

    private void PlaceVeil()
    {
        MakeVeil();
        veil.Cover(0f, 1f);
        veil.rectTransform.SetAsLastSibling();
        veil.gameObject.SetActive(true);
    }

    private void PlaceBeam(Color colour)
    {
        MakeBeam();
        // From the muzzle (where the drawing ends on the icon's canvas, WeaponIconSpans.csv) through the one killed and
        // 10 px past the row's end.
        var box = weapon.rectTransform;
        float muzzle = WeaponIcons.Span(play.Picture, out _).y;
        float from = box.anchoredPosition.x + box.sizeDelta.x * (muzzle - .5f);
        beam.rectTransform.anchoredPosition = new Vector2(from, 2f);
        beam.rectTransform.sizeDelta = new Vector2(10f - from, 2f);
        beam.rectTransform.localScale = new Vector3(0f, 1f, 1f);
        beam.color = Color.Lerp(Color.white, colour, .35f);
        beam.rectTransform.SetAsLastSibling();
        beam.gameObject.SetActive(true);
    }

    private void PlaceMarkRing(Color colour)
    {
        MakeMarkRing();
        markRing.color = colour;
        markRing.gameObject.SetActive(true);
    }
}

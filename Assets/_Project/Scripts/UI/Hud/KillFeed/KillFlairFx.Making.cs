using TMPro;
using UnityEngine;
using UnityEngine.UI;

// KillFlairFx: making the pieces. One part of the class; the rest is in KillFlairFx.cs and the files beside it.
public sealed partial class KillFlairFx
{
    private void MakeStamp()
    {
        if (stamp != null) return;
        stamp = FeelUI.Rect("Flair Stamp", row);
        stamp.anchorMin = stamp.anchorMax = new Vector2(1f, .5f);
        stampGroup = Group(stamp);
        stampEdge = Fill("Edge", stamp, 0f);
        stampFill = Fill("Fill", stamp, 2.5f);
        stampWords = FeelUI.ScoreLabel("Words", stamp, 15f, TextAlignmentOptions.Center);
        Stretch(stampWords.rectTransform, 0f);
        stamp.gameObject.SetActive(false);
    }

    private void MakeSeal()
    {
        if (seal != null) return;
        seal = FeelUI.Rect("Flair Seal", row);
        seal.anchorMin = seal.anchorMax = new Vector2(1f, .5f);
        sealGroup = Group(seal);
        sealFill = SpriteImage("Fill", seal, CombatUIAssets.Disc);
        Stretch(sealFill.rectTransform, 0f);
        sealRing = SpriteImage("Ring", seal, CombatUIAssets.Ring);
        Stretch(sealRing.rectTransform, 0f);
        sealWords = FeelUI.ScoreLabel("Words", seal, 11f, TextAlignmentOptions.Center);
        sealWords.lineSpacing = -18f;
        Stretch(sealWords.rectTransform, 4f);
        for (int i = 0; i < specks.Length; i++)
        {
            specks[i] = SpriteImage("Speck", seal, CombatUIAssets.Disc);
            specks[i].rectTransform.sizeDelta = Vector2.one * 4f;
        }
        seal.gameObject.SetActive(false);
    }

    private void MakeChip()
    {
        if (chip != null) return;
        chip = FeelUI.Rect("Flair Chip", row);
        chip.anchorMin = chip.anchorMax = new Vector2(1f, .5f);
        chipGroup = Group(chip);
        chipPlate = Fill("Plate", chip, 0f);
        chipMoney = MoneyIcon.Make(chip, 16f);
        chipMoney.rectTransform.anchorMin = chipMoney.rectTransform.anchorMax = new Vector2(0f, .5f);
        chipWords = FeelUI.ScoreLabel("Words", chip, 13f, TextAlignmentOptions.MidlineLeft);
        chipWords.rectTransform.anchorMin = chipWords.rectTransform.anchorMax = new Vector2(0f, .5f);
        chipWords.rectTransform.pivot = new Vector2(0f, .5f);
        chip.gameObject.SetActive(false);
    }

    private void MakeStrike()
    {
        if (strike != null) return;
        strike = FeelUI.Rect("Flair Strike", row).gameObject.AddComponent<Image>();
        strike.raycastTarget = false;
        var rect = strike.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        strike.gameObject.SetActive(false);
    }

    private void MakeFrame()
    {
        if (frame != null) return;
        frame = FeelUI.Rect("Flair Frame", row);
        frame.anchorMin = frame.anchorMax = new Vector2(1f, .5f);
        frameGroup = Group(frame);
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = FeelUI.Rect("Corner", frame).gameObject.AddComponent<Image>();
            corners[i].raycastTarget = false;
        }
        frame.gameObject.SetActive(false);
    }

    private void MakeTick()
    {
        if (tick != null) return;
        tick = FeelUI.Rect("Flair Tick", row);
        tick.anchorMin = tick.anchorMax = new Vector2(1f, .5f);
        tickGroup = Group(tick);
        tickShort = Stroke("Short", tick);
        tickLong = Stroke("Long", tick);
        tick.gameObject.SetActive(false);
    }

    private void MakeTag()
    {
        if (tag != null) return;
        tag = FeelUI.ScoreLabel("Flair Tag", weapon.rectTransform, 11f, TextAlignmentOptions.Center);
        var rect = tag.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.sizeDelta = new Vector2(90f, 14f);
        rect.anchoredPosition = new Vector2(0f, 6f);
        tag.fontStyle = FontStyles.Italic;
        tag.characterSpacing = 12f;
        tag.gameObject.SetActive(false);
    }

    private void MakeVeil()
    {
        if (veil != null) return;
        veil = FeelUI.Rect("Flair Veil", row).gameObject.AddComponent<KillRowCover>();
        veil.raycastTarget = false;
        Stretch(veil.rectTransform, 0f);
        veil.color = new Color(0f, 0f, 0f, .94f);
        veil.gameObject.SetActive(false);
    }

    private void MakeBeam()
    {
        if (beam != null) return;
        beam = FeelUI.Rect("Flair Beam", row).gameObject.AddComponent<Image>();
        beam.raycastTarget = false;
        var rect = beam.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
        rect.pivot = new Vector2(0f, .5f);
        beam.gameObject.SetActive(false);
    }

    private void MakeMarkRing()
    {
        if (markRing != null || mark == null) return;
        markRing = SpriteImage("Flair Mark Ring", mark.rectTransform, CombatUIAssets.Ring);
        Stretch(markRing.rectTransform, -3f);
        markRing.gameObject.SetActive(false);
    }

    private void MakeEchoes()
    {
        for (int i = 0; i < echoes.Length; i++)
        {
            if (echoes[i] != null) continue;
            echoes[i] = FeelUI.Rect("Flair Echo", weapon.rectTransform).gameObject.AddComponent<Image>();
            echoes[i].raycastTarget = false;
            echoes[i].preserveAspect = true;
            Stretch(echoes[i].rectTransform, 0f);
            echoes[i].gameObject.SetActive(false);
        }
    }

    private void MakeSpark()
    {
        if (spark != null) return;
        spark = Raw("Flair Spark", weapon.rectTransform, CombatUIAssets.CriticalBurst);
        spark.gameObject.SetActive(false);
    }

    private void MakeRing()
    {
        if (ring != null) return;
        ring = SpriteImage("Flair Ring", weapon.rectTransform, CombatUIAssets.Ring);
        ring.gameObject.SetActive(false);
    }

    private void MakeSlash()
    {
        if (slash != null) return;
        slash = Raw("Flair Slash", weapon.rectTransform, CombatUIAssets.BladeSlash);
        slash.rectTransform.pivot = new Vector2(0f, .5f);
        slash.gameObject.SetActive(false);
    }

    private void MakeRays()
    {
        for (int i = 0; i < rays.Length; i++)
        {
            if (rays[i] != null) continue;
            rays[i] = FeelUI.Rect("Flair Ray", weapon.rectTransform).gameObject.AddComponent<Image>();
            rays[i].raycastTarget = false;
            rays[i].rectTransform.pivot = new Vector2(0f, .5f);
            rays[i].rectTransform.sizeDelta = new Vector2(12f, 2.5f);
            rays[i].gameObject.SetActive(false);
        }
    }

    private void MakeCrosses()
    {
        for (int i = 0; i < crosses.Length; i++)
        {
            if (crosses[i] != null) continue;
            crosses[i] = FeelUI.Rect("Flair Cross", weapon.rectTransform).gameObject.AddComponent<Image>();
            crosses[i].raycastTarget = false;
            crosses[i].gameObject.SetActive(false);
        }
    }
}

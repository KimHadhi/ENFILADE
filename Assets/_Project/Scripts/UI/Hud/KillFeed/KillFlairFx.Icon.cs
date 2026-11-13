using UnityEngine;

// KillFlairFx: the icon. One part of the class; the rest is in KillFlairFx.cs and the files beside it.
public sealed partial class KillFlairFx
{
    private bool UsesLayer(KillFlairs.Icon kind) => kind != KillFlairs.Icon.None && kind != KillFlairs.Icon.Echo && kind != KillFlairs.Icon.Rays;

    private void PlaceIcon(KillFlairs.Flair icon, Color colour)
    {
        var kind = icon.Icon;
        if (kind == KillFlairs.Icon.None || weapon == null || weapon.sprite == null) return;
        if (UsesLayer(kind) && Layer())
        {
            // The pieces round the icon (a spark, a ring, a cut, crossing lines) flash the shape in their colour too.
            float mode = kind == KillFlairs.Icon.Fire ? 4f : kind == KillFlairs.Icon.Engulf ? 1f : kind == KillFlairs.Icon.Gild ? 2f
                : kind == KillFlairs.Icon.Trail ? 3f : 0f;
            layer.sprite = weapon.sprite;
            layer.gameObject.SetActive(true);
            material.SetFloat(ModeId, mode);
            material.SetColor(ColourId, kind == KillFlairs.Icon.Flash ? Color.white : colour);
            material.SetFloat(SeedId, seed);
            material.SetFloat(LickId, kind == KillFlairs.Icon.Trail ? .16f : .14f);
            material.SetVector(SpanId, WeaponIcons.Span(play.Picture, out float front));
            material.SetFloat(FrontId, front);
            material.SetFloat(HeatId, 0f);
        }
        var box = weapon.rectTransform.sizeDelta;
        switch (kind)
        {
            case KillFlairs.Icon.Echo:
                MakeEchoes();
                for (int i = 0; i < echoes.Length; i++)
                {
                    echoes[i].sprite = weapon.sprite;
                    echoes[i].color = Clear(colour);
                    echoes[i].gameObject.SetActive(true);
                }
                break;
            case KillFlairs.Icon.Spark:
                MakeSpark();
                spark.rectTransform.sizeDelta = Vector2.one * 34f;
                spark.color = Color.white;
                spark.gameObject.SetActive(true);
                break;
            case KillFlairs.Icon.Ring:
                MakeRing();
                ring.rectTransform.sizeDelta = Vector2.one * 40f;
                ring.color = colour;
                ring.gameObject.SetActive(true);
                break;
            case KillFlairs.Icon.Slash:
                MakeSlash();
                slash.rectTransform.sizeDelta = new Vector2(box.x * 1.25f, 10f);
                slash.rectTransform.anchoredPosition = new Vector2(-box.x * .62f, 0f);
                slash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -16f);
                slash.color = colour;
                slash.gameObject.SetActive(true);
                break;
            case KillFlairs.Icon.Rays:
                MakeRays();
                for (int i = 0; i < rays.Length; i++)
                {
                    rays[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f + (i - 2.5f) * 36f);
                    rays[i].color = colour;
                    rays[i].gameObject.SetActive(true);
                }
                break;
            case KillFlairs.Icon.Cross:
                MakeCrosses();
                for (int i = 0; i < crosses.Length; i++)
                {
                    crosses[i].rectTransform.sizeDelta = new Vector2(box.x * .72f, 2.5f);
                    crosses[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 24f : -24f);
                    crosses[i].color = colour;
                    crosses[i].gameObject.SetActive(true);
                }
                break;
        }
    }

    // Moves the icon's effect on; whether anything of it still shows.
    private bool TickIcon(KillFlairs.Flair icon, float t)
    {
        var kind = icon.Icon;
        float life = icon.Seconds;
        bool showing = t <= life + Fade;
        if (layer != null && layer.gameObject.activeSelf)
        {
            float heat, reach = 1f;
            switch (kind)
            {
                case KillFlairs.Icon.Fire: heat = Out(t, life); reach = Ease.Out(t / FireReach); break;
                case KillFlairs.Icon.Engulf: heat = Out(t, life); reach = Ease.Out(t / EngulfReach); break;
                case KillFlairs.Icon.Gild: heat = Out(t, life); reach = Ease.Out(t / GildReach); break;
                case KillFlairs.Icon.Trail: heat = Mathf.Clamp01(t / .06f) * Out(t, life); break;
                case KillFlairs.Icon.Flash: heat = t < 0f ? 0f : 1f - Mathf.Clamp01(t / .2f); break;
                // A tint, and the flash under a spark, a ring, a cut or crossing lines: full at once, gone over its seconds.
                default: heat = t < 0f ? 0f : 1f - Mathf.Clamp01(t / Mathf.Min(life, .4f)); break;
            }
            material.SetFloat(HeatId, heat);
            material.SetFloat(ProgressId, reach);
            if (t > life + Fade) layer.gameObject.SetActive(false);
        }
        switch (kind)
        {
            case KillFlairs.Icon.Echo:
                for (int i = 0; i < echoes.Length; i++)
                {
                    if (echoes[i] == null || !echoes[i].gameObject.activeSelf) continue;
                    // Two quick draws: each slides in from the left onto the gun and is gone as it lands.
                    float s = Mathf.Clamp01((t - i * .09f) / .26f);
                    echoes[i].rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-22f - i * 12f, 0f, Ease.Out(s / .7f)), 0f);
                    var c = echoes[i].color;
                    c.a = t < i * .09f ? 0f : s < .7f ? .9f * (s / .7f) : .9f * (1f - (s - .7f) / .3f);
                    echoes[i].color = c;
                    if (s >= 1f) echoes[i].gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Spark:
                if (spark != null && spark.gameObject.activeSelf)
                {
                    float s = Mathf.Clamp01(t / .17f);
                    spark.rectTransform.localScale = Vector3.one * (s < .5f ? Mathf.Lerp(.2f, 1.4f, s / .5f) : Mathf.Lerp(1.4f, 1.7f, (s - .5f) / .5f));
                    spark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 40f * s);
                    SetAlpha(spark, t < 0f ? 0f : s < .5f ? 1f : 1f - (s - .5f) / .5f);
                    if (s >= 1f) spark.gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Ring:
                if (ring != null && ring.gameObject.activeSelf)
                {
                    float s = Mathf.Clamp01(t / .28f);
                    ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(.3f, 2.3f, Ease.Out(s));
                    SetAlpha(ring, t < 0f ? 0f : 1f - s);
                    if (s >= 1f) ring.gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Slash:
                if (slash != null && slash.gameObject.activeSelf)
                {
                    slash.rectTransform.localScale = new Vector3(Ease.Out(t / .13f), 1f, 1f);
                    SetAlpha(slash, t < 0f ? 0f : 1f - Mathf.Clamp01((t - .13f) / .13f));
                    if (t > .3f) slash.gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Rays:
                for (int i = 0; i < rays.Length; i++)
                {
                    if (rays[i] == null || !rays[i].gameObject.activeSelf) continue;
                    float s = Mathf.Clamp01(t / .17f), angle = (90f + (i - 2.5f) * 36f) * Mathf.Deg2Rad;
                    rays[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(5f, 15f, Ease.Out(s));
                    rays[i].rectTransform.localScale = new Vector3(Mathf.Lerp(.3f, 1f, Ease.Out(s)), 1f, 1f);
                    SetAlpha(rays[i], t < 0f ? 0f : 1f - s);
                    if (s >= 1f) rays[i].gameObject.SetActive(false);
                }
                break;
            case KillFlairs.Icon.Cross:
                for (int i = 0; i < crosses.Length; i++)
                {
                    if (crosses[i] == null || !crosses[i].gameObject.activeSelf) continue;
                    float s = Mathf.Clamp01((t - i * .08f) / .18f);
                    crosses[i].rectTransform.localScale = new Vector3(Ease.Out(s), 1f, 1f);
                    SetAlpha(crosses[i], t < i * .08f ? 0f : 1f - Mathf.Clamp01((t - i * .08f - .18f) / .14f));
                    if (t > i * .08f + .34f) crosses[i].gameObject.SetActive(false);
                }
                break;
        }
        return showing;
    }
}

using TMPro;
using UnityEngine;

/// <summary>
/// The HUD's shared moves: an ease that overshoots and settles (things that land), and words that land letter by
/// letter (TMP's own vertices). Used by the new-objective alert (ObjectiveAlert): its card and its ribbon.
/// </summary>
public static class KitMotion
{
    /// <summary>Overshoots a little past 1 and settles back: a landing's bounce. t outside 0..1 is held at the ends.</summary>
    public static float BackOut(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float x = Mathf.Clamp01(t) - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    /// <summary>
    /// Each visible letter grows in from <paramref name="from"/> times its size and fades in, <paramref name="step"/> s
    /// after the one before, over <paramref name="seconds"/>; <paramref name="age"/> is how long since the first began
    /// (before 0, all hidden). A letter that has landed is left as TMP drew it. Call it every frame while letters land.
    /// </summary>
    public static void LandLetters(TMP_Text text, float age, float step, float seconds, float from)
    {
        text.ForceMeshUpdate();
        var info = text.textInfo;
        bool moved = false;
        for (int i = 0; i < info.characterCount; i++)
        {
            var letter = info.characterInfo[i];
            if (!letter.isVisible) continue;
            float t = Mathf.Clamp01((age - i * step) / Mathf.Max(.01f, seconds));
            if (t >= 1f) continue;
            moved = true;
            float grow = Mathf.LerpUnclamped(from, 1f, BackOut(t));
            float alpha = Mathf.Clamp01(t * 2f);
            var mesh = info.meshInfo[letter.materialReferenceIndex];
            int v = letter.vertexIndex;
            Vector3 middle = (mesh.vertices[v] + mesh.vertices[v + 2]) * .5f;
            for (int k = 0; k < 4; k++)
            {
                mesh.vertices[v + k] = middle + (mesh.vertices[v + k] - middle) * grow;
                var colour = mesh.colors32[v + k];
                colour.a = (byte)(colour.a * alpha);
                mesh.colors32[v + k] = colour;
            }
        }
        if (moved) text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
    }
}

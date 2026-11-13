using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// PerkCardScreen: Bursts and shards. One part of the class; the rest is in PerkCardScreen.cs and the files beside it.
public sealed partial class PerkCardScreen
{
    private void Ring(Vector2 at, Color color, float from, float to, float life)
    {
        var burst = bursts.FirstOrDefault(b => b.Age >= b.Life);
        if (burst == null)
        {
            var rect = FeelUI.Rect("Ring", fx);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = KitShapes.Ring;
            image.raycastTarget = false;
            burst = new Burst { Rect = rect, Image = image };
            bursts.Add(burst);
        }
        burst.Rect.anchoredPosition = at;
        burst.Color = color;
        burst.From = from;
        burst.To = to;
        burst.Age = 0f;
        burst.Life = life;
        burst.Rect.gameObject.SetActive(true);
    }

    private void Shards(Vector2 at)
    {
        for (int i = 0; i < ShardCount; i++)
        {
            var spark = sparks.FirstOrDefault(s => s.Age >= s.Life);
            if (spark == null)
            {
                var rect = FeelUI.Rect("Shard", fx);
                var image = rect.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                spark = new Spark { Rect = rect, Image = image };
                sparks.Add(spark);
            }
            float angle = (i / (float)ShardCount + (float)random.NextDouble() * .05f) * Mathf.PI * 2f;
            float speed = 900f + (float)random.NextDouble() * 900f;
            spark.Rect.anchoredPosition = at;
            spark.Rect.sizeDelta = new Vector2(6f + (float)random.NextDouble() * 6f, 20f + (float)random.NextDouble() * 24f);
            spark.Rect.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg - 90f);
            spark.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            spark.Spin = ((float)random.NextDouble() - .5f) * 720f;
            spark.Image.color = i % 3 == 0 ? Color.white : KitStyle.Accent;
            spark.Age = 0f;
            spark.Life = .38f + (float)random.NextDouble() * .2f;
            spark.Rect.gameObject.SetActive(true);
        }
    }

    private void AnimateEffects(float dt)
    {
        foreach (var burst in bursts)
        {
            if (burst.Age >= burst.Life) continue;
            burst.Age += dt;
            float k = Mathf.Clamp01(burst.Age / burst.Life);
            float size = Mathf.Lerp(burst.From, burst.To, KitReveal.EaseOut(k));
            burst.Rect.sizeDelta = new Vector2(size, size);
            burst.Image.color = KitStyle.With(burst.Color, .9f * (1f - k));
            if (k >= 1f) burst.Rect.gameObject.SetActive(false);
        }
        foreach (var spark in sparks)
        {
            if (spark.Age >= spark.Life) continue;
            spark.Age += dt;
            float k = Mathf.Clamp01(spark.Age / spark.Life);
            spark.Velocity *= Mathf.Exp(-4f * dt);
            spark.Rect.anchoredPosition += spark.Velocity * dt;
            spark.Rect.localRotation *= Quaternion.Euler(0f, 0f, spark.Spin * dt);
            spark.Rect.localScale = Vector3.one * (1f - k * .6f);
            var color = spark.Image.color;
            color.a = 1f - k * k;
            spark.Image.color = color;
            if (k >= 1f) spark.Rect.gameObject.SetActive(false);
        }
    }

    private void ClearEffects()
    {
        foreach (var burst in bursts) { burst.Age = burst.Life; burst.Rect.gameObject.SetActive(false); }
        foreach (var spark in sparks) { spark.Age = spark.Life; spark.Rect.gameObject.SetActive(false); }
        stage.anchoredPosition = Vector2.zero;
        if (landing != null) landing.localScale = Vector3.one;
    }

    // Where a point of <paramref name="rect"/> (in its own rect space, from its middle) falls in the effects layer.
    private Vector2 Local(RectTransform rect, Vector2 offset)
    {
        Vector3 world = rect.TransformPoint((Vector3)(rect.rect.center + offset));
        return fx.InverseTransformPoint(world);
    }

    // Where the middle of <paramref name="target"/> falls inside <paramref name="space"/> (from its middle).
    private static Vector2 Local(RectTransform space, RectTransform target, Vector2 offset)
    {
        Vector3 world = target.TransformPoint((Vector3)(target.rect.center + offset));
        Vector3 local = space.InverseTransformPoint(world);
        return (Vector2)local - space.rect.center;
    }

    private float Jitter() => (float)random.NextDouble() * 2f - 1f;

    // Past the end a little, then back: the card snaps into place.
    private static float OutBack(float k)
    {
        const float c1 = 1.9f, c3 = c1 + 1f;
        float t = k - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }
}

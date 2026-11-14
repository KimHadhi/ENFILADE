using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// PerkCardScreen: Rings, sparks, embers and smoke, and the light a picked card draws in, leaves behind and lands with. One part of the class; the rest is in PerkCardScreen.cs and the files beside it.
public sealed partial class PerkCardScreen
{
    // Light gathering into a picked card sets out GatherReach to GatherReach + GatherSpread from its middle (px); a trail's
    // dot lasts TrailLife (s); ArriveSparks fly off the badge the card lands in.
    private const float GatherReach = 336f, GatherSpread = 196f, TrailLife = .32f;
    private const int ArriveSparks = 12;

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

    // A pick's sparks (the user, 2026-11-14, of the old shards and rings: "화면은 가리는데 느낌이 없음 … 더 화려하게"): from
    // round the card's edge, a short way out and falling, in the card's tier colour and white, never far past the card.
    private void Sparks(Card card, Look look)
    {
        Vector2 middle = Local(card.Root, Vector2.zero);
        Color tint = look.Colour;
        for (int i = 0; i < look.Sparks; i++)
        {
            float angle = (i / (float)Mathf.Max(1, look.Sparks)) * Mathf.PI * 2f + (float)random.NextDouble() * .4f;
            Vector2 way = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float speed = 260f + (float)random.NextDouble() * 360f;
            Spawn(middle + new Vector2(way.x * 120f, way.y * 160f), way * speed + new Vector2(0f, 120f), 8f + (float)random.NextDouble() * 8f,
                i % 3 == 0 ? Color.white : tint, .45f + (float)random.NextDouble() * .3f, -900f, 0f, FleckKind.Spark);
        }
    }

    // A burning card's embers, off its flame line: rising, drifting, fading.
    private void Embers(Card card, int count)
    {
        float width = card.Edge.rect.width;
        for (int i = 0; i < count; i++)
        {
            Vector2 at = Local(card.Edge, new Vector2(((float)random.NextDouble() - .5f) * width, EdgeBelow - EdgeHeight * .5f));
            double pick = random.NextDouble();
            Color colour = pick < .6 ? new Color(1f, .7f, .28f) : pick < .8 ? new Color(1f, .89f, .6f) : new Color(1f, .42f, .24f);
            Spawn(at, new Vector2(((float)random.NextDouble() - .5f) * 60f, 80f + (float)random.NextDouble() * 140f), 5f + (float)random.NextDouble() * 6f,
                colour, .6f + (float)random.NextDouble() * .6f, 40f, 0f, FleckKind.Spark);
        }
    }

    // A puff of smoke off a burning card's flame line, swelling as it rises and gone soon.
    private void Smoke(Card card)
    {
        float width = card.Edge.rect.width;
        Vector2 at = Local(card.Edge, new Vector2(((float)random.NextDouble() - .5f) * width, EdgeBelow - EdgeHeight * .5f + 10f));
        Spawn(at, new Vector2(((float)random.NextDouble() - .5f) * 30f, 50f + (float)random.NextDouble() * 50f), 70f,
            new Color(.16f, .14f, .16f, .9f), 1f + (float)random.NextDouble() * .4f, 20f, 90f, FleckKind.Smoke);
    }

    // Light drawn into the picked card a beat after it is let go, so the pick is seen, not read (the user, 2026-11-14, of the
    // stamp that said what it became: "텍스트로 알려 주는 거 짜쳐 … 이펙트로 알아야지. Show but don't tell"): from a ring
    // well outside the card, a little taller than wide as the card is, each setting out a moment apart, into the card's
    // middle wherever it is by then. More of them for a grander card (Tables/PerkCards gather).
    private void Gather(Card card, Look look)
    {
        Vector2 middle = Local(card.Lift, Vector2.zero);
        int count = look.Gather;
        for (int i = 0; i < count; i++)
        {
            float angle = (i / (float)count) * Mathf.PI * 2f + (float)random.NextDouble() * .3f;
            float reach = GatherReach + (float)random.NextDouble() * GatherSpread;
            Vector2 from = middle + new Vector2(Mathf.Cos(angle) * reach, Mathf.Sin(angle) * reach * 1.2f);
            var fleck = Spawn(from, Vector2.zero, 13f + (float)random.NextDouble() * 11f, i % 3 == 0 ? Color.white : look.Colour,
                .3f + (float)random.NextDouble() * .15f, 0f, 0f, FleckKind.Gather);
            fleck.Into = card.Lift;
            // Not all at once: each sets out up to .12 s late (clear until then).
            fleck.Age = -(float)random.NextDouble() * .12f;
        }
    }

    // The light the picked card leaves on its way to its badge: dots of its colour along the way it went since the last
    // frame (so a fast card leaves no gaps), each fading where it was left, a short comet's tail (Tables/PerkCards trail:
    // dots a second).
    private void Trail(Card card, Look look, float dt)
    {
        Vector2 now = Local(card.Lift, Vector2.zero);
        if (!trailing)
        {
            trailing = true;
            trailLast = now;
        }
        trailOwed += look.Trail * dt;
        int count = Mathf.FloorToInt(trailOwed);
        trailOwed -= count;
        for (int i = 0; i < count; i++)
        {
            Vector2 at = Vector2.Lerp(trailLast, now, (i + 1f) / count) + new Vector2(Jitter(), Jitter()) * 7f;
            Spawn(at, Vector2.zero, 16f + (float)random.NextDouble() * 13f, look.Colour, TrailLife, 0f, 0f, FleckKind.Trail);
        }
        trailLast = now;
    }

    // The picked card lands in its badge: a small ring in its colour and a few sparks off the badge, not over the screen.
    private void Arrive(Vector2 at, Look look)
    {
        Ring(at, look.Colour, 30f, 320f, .4f);
        for (int i = 0; i < ArriveSparks; i++)
        {
            float angle = (i / (float)ArriveSparks) * Mathf.PI * 2f + (float)random.NextDouble() * .4f;
            float speed = 200f + (float)random.NextDouble() * 220f;
            Spawn(at, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed + new Vector2(0f, 60f), 10f + (float)random.NextDouble() * 8f,
                i % 3 == 0 ? Color.white : look.Colour, .35f + (float)random.NextDouble() * .2f, -700f, 0f, FleckKind.Spark);
        }
    }

    private Fleck Spawn(Vector2 at, Vector2 velocity, float size, Color colour, float life, float gravity, float grow, FleckKind kind)
    {
        var fleck = flecks.FirstOrDefault(f => f.Age >= f.Life);
        if (fleck == null)
        {
            var rect = FeelUI.Rect("Fleck", fx);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            fleck = new Fleck { Rect = rect, Image = image };
            flecks.Add(fleck);
        }
        fleck.Image.texture = kind == FleckKind.Smoke ? KitShapes.Glow : KitShapes.Dot;
        fleck.Rect.anchoredPosition = at;
        fleck.Velocity = velocity;
        fleck.Size = size;
        fleck.Grow = grow;
        fleck.Gravity = gravity;
        fleck.Colour = colour;
        fleck.Kind = kind;
        fleck.From = at;
        fleck.Into = null;
        fleck.Age = 0f;
        fleck.Life = life;
        fleck.Rect.sizeDelta = new Vector2(size, size);
        // Smoke and gathering light come up from nothing; the rest start at their colour.
        fleck.Image.color = kind == FleckKind.Smoke || kind == FleckKind.Gather ? Color.clear : colour;
        fleck.Rect.gameObject.SetActive(true);
        return fleck;
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
        foreach (var fleck in flecks)
        {
            if (fleck.Age >= fleck.Life) continue;
            fleck.Age += dt;
            if (fleck.Kind == FleckKind.Gather)
            {
                AnimateGather(fleck);
                continue;
            }
            float k = Mathf.Clamp01(fleck.Age / fleck.Life);
            fleck.Velocity.y += fleck.Gravity * dt;
            fleck.Velocity.x *= Mathf.Exp(-1.5f * dt);
            fleck.Rect.anchoredPosition += fleck.Velocity * dt;
            float size = fleck.Size + fleck.Grow * fleck.Age;
            fleck.Rect.sizeDelta = new Vector2(size, size);
            // Smoke swells in and out; a trail's dot fades where it was left; a spark or an ember fades as it goes. Never a blink.
            float alpha = fleck.Kind == FleckKind.Smoke ? .3f * Mathf.Sin(k * Mathf.PI) : fleck.Kind == FleckKind.Trail ? .85f * (1f - k) : 1f - k * k;
            fleck.Image.color = KitStyle.With(fleck.Colour, fleck.Colour.a * alpha);
            if (k >= 1f) fleck.Rect.gameObject.SetActive(false);
        }
    }

    // Light gathering into a card: drawn in faster and faster from where it set out to the card's middle, shrinking to half
    // its size; it comes up over the first quarter of its way (never a blink) and dims a little as it arrives.
    private void AnimateGather(Fleck fleck)
    {
        if (fleck.Age < 0f) return;
        float k = Mathf.Clamp01(fleck.Age / fleck.Life);
        Vector2 to = fleck.Into != null ? Local(fleck.Into, Vector2.zero) : fleck.From;
        fleck.Rect.anchoredPosition = Vector2.LerpUnclamped(fleck.From, to, k * k);
        float size = fleck.Size * (1f - .5f * k);
        fleck.Rect.sizeDelta = new Vector2(size, size);
        fleck.Image.color = KitStyle.With(fleck.Colour, fleck.Colour.a * Mathf.Min(1f, k * 4f) * (1f - .6f * k * k));
        if (k >= 1f) fleck.Rect.gameObject.SetActive(false);
    }

    private void ClearEffects()
    {
        foreach (var burst in bursts) { burst.Age = burst.Life; burst.Rect.gameObject.SetActive(false); }
        foreach (var fleck in flecks) { fleck.Age = fleck.Life; fleck.Rect.gameObject.SetActive(false); }
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

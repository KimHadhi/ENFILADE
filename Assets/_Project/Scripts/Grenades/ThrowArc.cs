using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The path a held throw would fly, drawn from the hand to where it lands: the stone's T (PlayerThrow) and the class
/// throwable's G (PlayerGrenades) share it, so both aim the same way. Its flight is stepped under gravity (a share of it:
/// the thrown knife barely drops) with a sphere the size of what is thrown, and drawn as a line faint at the hand and
/// solid along the flight; a throwable's burst reach can lie as a ring on what it lands on.
/// </summary>
public sealed class ThrowArc
{
    /// <summary>A step of the flight (s), the longest flight drawn (s), and how thick the line is (m).</summary>
    public const float Step = 1f / 30f, Longest = 3f, Width = .022f;
    private const int RingPoints = 48;
    private const float RingWidth = .03f, RingLift = .04f;

    private readonly Transform owner;
    private readonly List<Vector3> path = new List<Vector3>(96);
    private LineRenderer line, ring;

    public ThrowArc(Transform owner) => this.owner = owner;

    /// <summary>The last traced flight met the world.</summary>
    public bool Landed { get; private set; }
    /// <summary>Where the last traced flight ends: where it lands, or where it is after Longest.</summary>
    public Vector3 Landing { get; private set; }
    /// <summary>The face it lands on (up when it lands nowhere).</summary>
    public Vector3 LandingNormal { get; private set; } = Vector3.up;
    /// <summary>How long the last traced flight takes to land (s).</summary>
    public float FlightSeconds { get; private set; }

    /// <summary>
    /// Steps a flight from <paramref name="from"/> at <paramref name="velocity"/> under that share of gravity, as a sphere
    /// of <paramref name="radius"/> (the world only: Throwables.WorldMask unless a mask is given). True when it lands.
    /// </summary>
    public bool Trace(Vector3 from, Vector3 velocity, float radius, float gravityShare = 1f, int mask = 0, float longest = Longest)
    {
        if (mask == 0) mask = ThrownStone.WorldMask;
        path.Clear();
        path.Add(from);
        Vector3 gravity = Physics.gravity * gravityShare, at = from;
        Landed = false;
        LandingNormal = Vector3.up;
        FlightSeconds = longest;
        for (float t = 0f; t < longest; t += Step)
        {
            Vector3 next = at + velocity * Step + .5f * Step * Step * gravity;
            Vector3 step = next - at;
            float length = step.magnitude;
            if (length > 1e-4f && Physics.SphereCast(at, radius, step / length, out RaycastHit hit, length, mask, QueryTriggerInteraction.Ignore))
            {
                at = hit.point;
                path.Add(at);
                Landed = true;
                LandingNormal = hit.normal;
                FlightSeconds = t + Step * hit.distance / length;
                break;
            }
            at = next;
            velocity += gravity * Step;
            path.Add(at);
        }
        Landing = at;
        return Landed;
    }

    /// <summary>
    /// Draws the last traced flight: clear at the hand, <paramref name="body"/> along the flight at
    /// <paramref name="alpha"/>, and <paramref name="end"/> where it lands.
    /// </summary>
    public void Draw(Color body, Color end, float alpha)
    {
        if (line == null) line = MakeLine("Throw Arc", Width, false);
        line.enabled = true;
        var colors = new Gradient();
        colors.SetKeys(new[] { new GradientColorKey(body, 0f), new GradientColorKey(body, .8f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, .12f), new GradientAlphaKey(alpha, 1f) });
        line.colorGradient = colors;
        line.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++) line.SetPosition(i, path[i]);
    }

    /// <summary>A ring of this radius lying on the face at <paramref name="at"/> (a burst's reach where it would land).</summary>
    public void Ring(Vector3 at, Vector3 normal, float radius, Color color)
    {
        if (radius <= 0f) { HideRing(); return; }
        if (ring == null) ring = MakeLine("Throw Reach", RingWidth, true);
        ring.enabled = true;
        ring.startColor = ring.endColor = color;
        if (normal.sqrMagnitude < 1e-4f) normal = Vector3.up;
        normal.Normalize();
        // Steep faces (a wall) still show the reach as a ring on the ground below the spot, the way it bursts.
        if (Vector3.Dot(normal, Vector3.up) < .5f) normal = Vector3.up;
        Vector3 across = Vector3.Cross(normal, Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up).normalized;
        Vector3 other = Vector3.Cross(normal, across);
        ring.positionCount = RingPoints;
        for (int i = 0; i < RingPoints; i++)
        {
            float angle = i * Mathf.PI * 2f / RingPoints;
            ring.SetPosition(i, at + normal * RingLift + (across * Mathf.Cos(angle) + other * Mathf.Sin(angle)) * radius);
        }
    }

    public void HideRing()
    {
        if (ring != null && ring.enabled) ring.enabled = false;
    }

    /// <summary>Nothing drawn.</summary>
    public void Hide()
    {
        if (line != null && line.enabled) line.enabled = false;
        HideRing();
    }

    private LineRenderer MakeLine(string name, float width, bool loop)
    {
        var host = new GameObject(name) { hideFlags = HideFlags.DontSave };
        if (owner != null) host.transform.SetParent(owner, false);
        var made = host.AddComponent<LineRenderer>();
        made.useWorldSpace = true;
        made.widthMultiplier = width;
        made.numCapVertices = 2;
        made.numCornerVertices = 0;
        made.loop = loop;
        made.alignment = LineAlignment.View;
        made.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        made.receiveShadows = false;
        made.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        return made;
    }
}

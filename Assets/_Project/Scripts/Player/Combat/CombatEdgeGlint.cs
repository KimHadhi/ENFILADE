using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A brief light along one side edge of this PC's screen: the side a blow just went by (a micro dodge's slip), or the
/// side a strike landed out of sight (the counter's back elbow). It rises at once and fades over the given real seconds.
/// The overlay is built at spawn, so the first one costs nothing in a fight.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatEdgeGlint : MonoBehaviour
{
    // How much of the screen's width each edge's light covers.
    private const float EdgeShare = .12f;
    private GameObject root;
    private Texture2D ramp;
    private readonly Edge[] edges = new Edge[2];

    private sealed class Edge
    {
        public RawImage image;
        public Color color;
        public float strength, seconds, time = 1f;
    }

    /// <summary>The player's edge light, made on first use.</summary>
    public static CombatEdgeGlint For(Component of)
    {
        if (of == null || !Application.isPlaying) return null;
        var player = of.GetComponentInParent<PlayerMovement>();
        var host = player != null ? player.gameObject : of.gameObject;
        return host.TryGetComponent(out CombatEdgeGlint glint) ? glint : host.AddComponent<CombatEdgeGlint>();
    }

    private void Awake()
    {
        root = new GameObject("Combat edge glint", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 41;
        // Bright at the edge, gone a little way in.
        ramp = new Texture2D(32, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int x = 0; x < ramp.width; x++)
        {
            float a = 1f - x / (ramp.width - 1f);
            ramp.SetPixel(x, 0, new Color(1f, 1f, 1f, a * a));
        }
        ramp.Apply(false, true);
        edges[0] = Make("Left", 0f);
        edges[1] = Make("Right", 1f);
    }

    private Edge Make(string name, float x)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(root.transform, false);
        image.raycastTarget = false; image.texture = ramp; image.enabled = false;
        var rect = image.rectTransform;
        rect.anchorMin = new Vector2(x > .5f ? 1f - EdgeShare : 0f, 0f);
        rect.anchorMax = new Vector2(x > .5f ? 1f : EdgeShare, 1f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        // The right edge reads the ramp backwards, so both are brightest at the screen's border.
        image.uvRect = x > .5f ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        return new Edge { image = image };
    }

    /// <summary>Lights the edge on side (+1 right, otherwise left) at strength (0-1), fading over seconds of real time.</summary>
    public void Show(int side, Color color, float strength, float seconds)
    {
        var edge = edges[side > 0 ? 1 : 0];
        if (edge == null || strength <= 0f) return;
        edge.color = color; edge.strength = Mathf.Clamp01(strength);
        edge.seconds = Mathf.Max(.01f, seconds); edge.time = 0f;
        Apply(edge);
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
        foreach (var edge in edges)
        {
            if (edge == null || edge.time >= 1f) continue;
            edge.time = Mathf.Min(1f, edge.time + dt / edge.seconds);
            Apply(edge);
        }
    }

    private static void Apply(Edge edge)
    {
        float fade = 1f - edge.time;
        float alpha = edge.strength * fade * fade;
        edge.image.enabled = alpha > .002f;
        edge.image.color = new Color(edge.color.r, edge.color.g, edge.color.b, alpha);
    }

    private void OnDisable()
    {
        foreach (var edge in edges) if (edge != null) { edge.time = 1f; edge.image.enabled = false; }
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root);
        if (ramp != null) Destroy(ramp);
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A CHARGE seen (the user, 2026-11-12: "어썰트 CHARGE는 G&amp;B처럼 주위에 버프 받은 아군이랑 본인이 노란색으로 빛나게"): on
/// every PC, everyone the CHARGE took (alive and within its reach of where it was sounded, as that PC sees them then)
/// glows gold while it lasts, Guts &amp; Blackpowder's charging line lit up: a warm light on them, a soft halo behind them
/// and embers rising round them, so the whole team sees at a glance who is charging. This PC's own player, seen from
/// inside, gets the light on their hands and a gold rim round the screen instead of a halo and embers in their eyes.
/// It dims over its last second. Drawn the way the game's other effects are, flat shapes facing the camera (CardFx),
/// nothing left lying about. The reach and time are the CHARGE's own (ClassAbilities Assault.chargeRadius,
/// chargeSeconds), the same ClassKit.ReceiveCharge gives this PC's player.
/// </summary>
[DisallowMultipleComponent]
public sealed class ChargeGlow : MonoBehaviour
{
    /// <summary>The CHARGE's gold.</summary>
    public static readonly Color Gold = new Color(1f, .8f, .22f, 1f);
    private const int Embers = 16;
    // How long it dims at the end (s); the halo's size (m); how high the embers rise and how long one takes (m, s); the
    // light's reach and strength; the screen rim's strength.
    private const float FadeOut = 1f, HaloWidth = 1.5f, HaloHeight = 2.3f, EmberRise = 2f, EmberSeconds = 1.3f,
        LightRange = 3.2f, LightIntensity = 2.6f, RimAlpha = .34f;

    private static Canvas rimCanvas;
    private static Image rimImage;
    private static Sprite rimSprite;
    private static ChargeGlow rimOwner;

    private PlayerMovement player;
    private float until, total = 1f;
    private Light lamp;
    private GameObject drawn;
    private Mesh mesh;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        rimCanvas = null;
        rimImage = null;
        rimSprite = null;
        rimOwner = null;
    }

    /// <summary>
    /// A CHARGE was sounded at <paramref name="at"/> (this PC's Assault's, or a friend's as their PC said it): everyone it
    /// takes glows for its seconds.
    /// </summary>
    public static void Sounded(Vector3 at)
    {
        if (!Application.isPlaying) return;
        float reach = PlayerClass.Ability("Assault", "chargeRadius", 15f);
        float seconds = Mathf.Max(.1f, PlayerClass.Ability("Assault", "chargeSeconds", 7f));
        foreach (var player in PlayerRoster.All)
        {
            if (player == null || !player.IsInitialized || player.IsDead) continue;
            if ((player.transform.position - at).sqrMagnitude > reach * reach) continue;
            if (!player.TryGetComponent(out ChargeGlow glow)) glow = player.gameObject.AddComponent<ChargeGlow>();
            glow.Kindle(player, seconds);
        }
    }

    private void Kindle(PlayerMovement who, float seconds)
    {
        player = who;
        total = seconds;
        until = Time.time + seconds;
        enabled = true;
        if (lamp == null)
        {
            lamp = new GameObject("Charge Light").AddComponent<Light>();
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            lamp.color = Gold;
            lamp.range = LightRange;
        }
        lamp.gameObject.SetActive(true);
        if (!Own && drawn == null)
        {
            drawn = new GameObject("Charge Glow") { hideFlags = HideFlags.DontSave };
            mesh = new Mesh { name = "Charge Glow", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            drawn.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = drawn.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, true);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        if (Own) rimOwner = this;
    }

    // This PC's own player, seen from inside.
    private bool Own => player != null && player.IsLocal;

    private void Update()
    {
        float left = until - Time.time;
        if (left <= 0f || player == null || player.IsDead)
        {
            Douse();
            return;
        }
        float fade = Mathf.Clamp01(left / FadeOut), pulse = .82f + .18f * Mathf.Sin(Time.time * 7f);
        lamp.intensity = LightIntensity * fade * pulse;
        if (Own) Rim(RimAlpha * fade * pulse);
        else Draw(fade, pulse);
    }

    // Over: the light out, the shapes gone, the rim off (if it is this one's).
    private void Douse()
    {
        if (lamp != null) lamp.gameObject.SetActive(false);
        if (drawn != null) { Destroy(drawn); drawn = null; }
        if (mesh != null) { Destroy(mesh); mesh = null; }
        if (rimOwner == this) { Rim(0f); rimOwner = null; }
        enabled = false;
    }

    private void OnDisable()
    {
        if (drawn != null || (rimOwner == this && rimImage != null && rimImage.enabled)) Douse();
    }

    private void OnDestroy()
    {
        if (drawn != null) Destroy(drawn);
        if (mesh != null) Destroy(mesh);
        if (rimOwner == this) { Rim(0f); rimOwner = null; }
    }

    // A friend charging, seen from outside: a soft gold halo behind them and embers rising round them, turned to the eye.
    private void Draw(float fade, float pulse)
    {
        var eye = Camera.main;
        if (eye == null || mesh == null) return;
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        triangles.Clear();
        Vector3 feet = player.transform.position, right = eye.transform.right, up = Vector3.up;
        Vector3 toEye = eye.transform.position - feet;
        toEye.y = 0f;
        Vector3 back = toEye.sqrMagnitude > 1e-4f ? -toEye.normalized * .25f : Vector3.zero;
        Quad(feet + back + up * (HaloHeight * .5f), right * (HaloWidth * .5f), up * (HaloHeight * .5f), KitStyle.With(Gold, .26f * fade * pulse));
        // Each ember on its own beat round the body, rising and fading as it goes, a little sway as it climbs.
        int seed = player.GetInstanceID();
        for (int i = 0; i < Embers; i++)
        {
            float phase = Mathf.Repeat(Time.time / EmberSeconds + Hash(seed, i, 1), 1f);
            float angle = Hash(seed, i, 2) * Mathf.PI * 2f + phase * 1.5f, radius = .3f + .25f * Hash(seed, i, 3);
            Vector3 at = feet + new Vector3(Mathf.Cos(angle) * radius, .1f + phase * EmberRise, Mathf.Sin(angle) * radius);
            float size = Mathf.Lerp(.09f, .04f, phase) * (.8f + .4f * Hash(seed, i, 4));
            float alpha = Mathf.Sin(phase * Mathf.PI) * .95f * fade;
            Quad(at, right * size, up * size, KitStyle.With(Color.Lerp(Gold, Color.white, .35f * (1f - phase)), alpha));
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private void Quad(Vector3 middle, Vector3 across, Vector3 upward, Color color)
    {
        int start = vertices.Count;
        vertices.Add(middle - across - upward);
        vertices.Add(middle - across + upward);
        vertices.Add(middle + across + upward);
        vertices.Add(middle + across - upward);
        Color32 shade = color;
        for (int i = 0; i < 4; i++) colors.Add(shade);
        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(1f, 1f));
        uvs.Add(new Vector2(1f, 0f));
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }

    private static float Hash(int seed, int index, int salt)
    {
        uint h = (uint)(seed * 73856093) ^ (uint)(index * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xffff) / 65535f;
    }

    // This PC's own player charging: a gold rim round the screen (alpha 0: off).
    private static void Rim(float alpha)
    {
        if (alpha <= 0f)
        {
            if (rimImage != null) rimImage.enabled = false;
            return;
        }
        if (rimCanvas == null)
        {
            var host = new GameObject("Charge Rim", typeof(RectTransform), typeof(Canvas)) { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(host);
            rimCanvas = host.GetComponent<Canvas>();
            rimCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rimCanvas.sortingOrder = 79;
            var lid = FeelUI.Rect("Rim", host.transform);
            lid.anchorMin = Vector2.zero;
            lid.anchorMax = Vector2.one;
            lid.offsetMin = lid.offsetMax = Vector2.zero;
            rimImage = lid.gameObject.AddComponent<Image>();
            rimImage.raycastTarget = false;
            rimImage.sprite = RimSprite;
            rimImage.type = Image.Type.Simple;
        }
        rimImage.enabled = true;
        rimImage.color = KitStyle.With(Gold, alpha);
    }

    // Clear in the middle, gold toward the edges and corners, made once.
    private static Sprite RimSprite
    {
        get
        {
            if (rimSprite != null) return rimSprite;
            const int Size = 128;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                { name = "Charge Rim", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = Mathf.Abs(x / (Size - 1f) * 2f - 1f), v = Mathf.Abs(y / (Size - 1f) * 2f - 1f);
                // Toward the nearer edge, with the corners a little more.
                float edge = Mathf.Max(Mathf.Pow(u, 6f), Mathf.Pow(v, 6f)) * .75f + Mathf.Pow(u * v, 3f) * .4f;
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(edge) * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return rimSprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(.5f, .5f));
        }
    }
}

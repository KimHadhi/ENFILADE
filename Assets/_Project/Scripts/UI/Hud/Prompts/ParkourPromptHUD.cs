using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// "[CTRL] 눌러 떨어지기": while a parkour move is on that the crouch key lets go of (hanging from a ledge, on a ladder,
/// riding a zipline, on a bar, partway through a vault or a climb), what that key does is said under the crosshair, the
/// key drawn as a key cap, as Call of Duty says "drop" (user, 2026-11-11: "ctrl로 캔슬할 수 있는 파쿠르 동작들은 ctrl
/// 눌러 낙하 콜옵처럼 뜨게"). It comes in a moment after the move starts (a little later for a vault or a climb, which
/// are over quickly) so a short move never flickers one, and the key's name is the binding's, so a rebind shows. The
/// moves and what Ctrl does in them: LedgeHangState, LadderState, ZiplineState, ParkourTraversal.ProcessBarInput,
/// ObstacleTraversalState. Local player only (PlayerMovement). Words: Texts.csv hud.parkour.*.
/// </summary>
[DisallowMultipleComponent]
public sealed class ParkourPromptHUD : MonoBehaviour
{
    // How long a move holds before its prompt shows (s): hanging on, and a vault or a climb under way. Then its fade
    // in and out (s), and the prompt's place under the screen's middle, the key cap's height and the type (px at 1080p).
    private const float HoldDelay = .15f, MoveDelay = .3f, FadeIn = .1f, FadeOut = .15f, Under = 140f, CapHeight = 30f,
        CapPad = 9f, KeySize = 15f, WordSize = 21f, Gap = 10f;

    private PlayerMovement movement;
    private CanvasGroup group;
    private RectTransform root, capRect, wordsRect;
    private KeyCapGraphic cap;
    private TextMeshProUGUI key, words;
    private InputAction crouch;
    private string current, shownText;
    private float since, alpha;

    private void OnEnable() => movement = GetComponent<PlayerMovement>();

    private void LateUpdate()
    {
        string id = Wanted(out bool quick);
        if (id != current) { current = id; since = 0f; }
        since += Time.unscaledDeltaTime;
        bool show = id != null && since >= (quick ? MoveDelay : HoldDelay);
        if (!show && group == null) return;
        Build();
        if (show)
        {
            string text = Texts.Get(id);
            if (text != shownText) Lay(text);
        }
        alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, Time.unscaledDeltaTime / (show ? FadeIn : FadeOut));
        group.alpha = alpha;
        // It settles in from a touch lower as it comes.
        root.anchoredPosition = new Vector2(0f, -Under - 6f * (1f - alpha));
        group.gameObject.SetActive(alpha > 0f);
    }

    // The words for what the crouch key does now, or null; quick: a move over in a moment (a vault or a climb).
    private string Wanted(out bool quick)
    {
        quick = false;
        if (movement == null || !movement.IsLocal || !movement.InputAvailable || movement.IsDead) return null;
        switch (movement.CurrentState)
        {
            case PlayerMovement.MovementState.LedgeHang: return "hud.parkour.ledge";
            case PlayerMovement.MovementState.Ladder: return "hud.parkour.ladder";
            case PlayerMovement.MovementState.Zipline: return "hud.parkour.zipline";
            case PlayerMovement.MovementState.BarGrab:
            case PlayerMovement.MovementState.BarSwing: return "hud.parkour.bar";
            case PlayerMovement.MovementState.Vault:
            case PlayerMovement.MovementState.Mantle: quick = true; return "hud.parkour.stop";
            default: return null;
        }
    }

    // The key's name (the crouch binding's, CTRL unless rebound) in its cap, then the words, the pair centred.
    private void Lay(string text)
    {
        shownText = text;
        key.text = KeyName();
        words.text = text;
        float capWidth = Mathf.Max(CapHeight, key.GetPreferredValues(key.text).x + CapPad * 2f);
        float wordsWidth = words.GetPreferredValues(text).x + 4f;
        float total = capWidth + Gap + wordsWidth;
        capRect.sizeDelta = new Vector2(capWidth, CapHeight);
        capRect.anchoredPosition = new Vector2(-total * .5f + capWidth * .5f, 0f);
        wordsRect.sizeDelta = new Vector2(wordsWidth, CapHeight + 8f);
        wordsRect.anchoredPosition = new Vector2(-total * .5f + capWidth + Gap + wordsWidth * .5f, 0f);
        cap.SetVerticesDirty();
    }

    private string KeyName()
    {
        if (crouch == null) crouch = KeyBindings.Find("Player/Crouch");
        // KeyBindings.Name: the first key the player set, not every binding joined by " | " (the empty second slot too).
        string name = crouch != null ? KeyBindings.Name(crouch) : null;
        return string.IsNullOrEmpty(name) ? "CTRL" : name.ToUpperInvariant();
    }

    private void Build()
    {
        if (group != null) return;
        var host = new GameObject("Parkour prompt HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        host.hideFlags = HideFlags.DontSave;
        host.transform.SetParent(transform, false);
        var canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 79;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        group = host.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = group.blocksRaycasts = false;

        root = new GameObject("Prompt", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(host.transform, false);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);

        capRect = new GameObject("Key", typeof(RectTransform), typeof(CanvasRenderer), typeof(KeyCapGraphic)).GetComponent<RectTransform>();
        capRect.SetParent(root, false);
        capRect.anchorMin = capRect.anchorMax = capRect.pivot = new Vector2(.5f, .5f);
        cap = capRect.GetComponent<KeyCapGraphic>();
        cap.raycastTarget = false;
        cap.color = KitStyle.Ice;
        key = KitStyle.Label(capRect, "", KeySize, TextAlignmentOptions.Center, KitStyle.Ice);
        key.textWrappingMode = TextWrappingModes.NoWrap;

        wordsRect = new GameObject("Words", typeof(RectTransform)).GetComponent<RectTransform>();
        wordsRect.SetParent(root, false);
        wordsRect.anchorMin = wordsRect.anchorMax = wordsRect.pivot = new Vector2(.5f, .5f);
        words = KitStyle.Label(wordsRect, "", WordSize, TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        words.textWrappingMode = TextWrappingModes.NoWrap;
        words.overflowMode = TextOverflowModes.Overflow;
    }

    private void OnDisable()
    {
        current = null;
        alpha = 0f;
        if (group != null) { group.alpha = 0f; group.gameObject.SetActive(false); }
    }

    private void OnDestroy()
    {
        if (group == null) return;
        if (Application.isPlaying) Destroy(group.gameObject); else DestroyImmediate(group.gameObject);
    }
}

/// <summary>
/// A key cap for prompts (ParkourPromptHUD): a rounded rectangle's outline, with a faint dark fill so the key's name
/// reads over anything. A mesh, so it stays sharp at any size and needs no sprite.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class KeyCapGraphic : MaskableGraphic
{
    private const int CornerSteps = 5;
    private const float Radius = 5f, Stroke = 2f;
    private static readonly Color Fill = new Color(0f, 0f, 0f, .35f);
    private static readonly List<Vector2> outer = new List<Vector2>(32), inner = new List<Vector2>(32);

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        float radius = Mathf.Min(Radius, rect.height * .5f, rect.width * .5f);
        Rounded(rect, radius, outer);
        var inside = new Rect(rect.xMin + Stroke, rect.yMin + Stroke, rect.width - Stroke * 2f, rect.height - Stroke * 2f);
        Rounded(inside, Mathf.Max(0f, radius - Stroke), inner);
        // The faint fill inside the rim.
        Color32 fill = Fill * new Color(1f, 1f, 1f, color.a);
        int middle = mesh.currentVertCount;
        mesh.AddVert(inside.center, fill, Vector4.zero);
        for (int i = 0; i < inner.Count; i++) mesh.AddVert(inner[i], fill, Vector4.zero);
        for (int i = 0; i < inner.Count; i++) mesh.AddTriangle(middle, middle + 1 + i, middle + 1 + (i + 1) % inner.Count);
        // The rim: a band between the two outlines, corner for corner.
        Color32 rim = color;
        int first = mesh.currentVertCount;
        for (int i = 0; i < outer.Count; i++)
        {
            mesh.AddVert(outer[i], rim, Vector4.zero);
            mesh.AddVert(inner[i], rim, Vector4.zero);
        }
        for (int i = 0; i < outer.Count; i++)
        {
            int a = first + i * 2, b = first + (i + 1) % outer.Count * 2;
            mesh.AddTriangle(a, b, b + 1);
            mesh.AddTriangle(a, b + 1, a + 1);
        }
    }

    // The outline of a rounded rectangle, the same number of points for any size, clockwise from the top left corner.
    private static void Rounded(Rect rect, float radius, List<Vector2> into)
    {
        into.Clear();
        var centres = new[]
        {
            new Vector2(rect.xMin + radius, rect.yMax - radius), new Vector2(rect.xMax - radius, rect.yMax - radius),
            new Vector2(rect.xMax - radius, rect.yMin + radius), new Vector2(rect.xMin + radius, rect.yMin + radius)
        };
        for (int corner = 0; corner < 4; corner++)
        {
            // Top left starts pointing left (180°) and each corner turns a quarter clockwise.
            float from = 180f - corner * 90f;
            for (int step = 0; step <= CornerSteps; step++)
            {
                float angle = (from - 90f * step / CornerSteps) * Mathf.Deg2Rad;
                into.Add(centres[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        }
    }
}

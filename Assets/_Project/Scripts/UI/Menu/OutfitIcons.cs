using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pictures of what can be worn, for the wardrobe's tiles and choices (MainMenuWardrobe): each item in a colour set, or
/// a character's own piece of a slot, stood alone in a studio far to the side of the menu's preview and photographed
/// against nothing, so the wardrobe can lay it see-through over its plates. The user found the wardrobe all words
/// (2026-11-12: "텍스트만 보고 입히려면 하나하나 다 봐야 하는데 무슨 의상인지 보이면 걍 원하는 거 바로 클릭할 수 있잖아").
/// One picture every few frames, kept for the run; a picture taken while the editor compiled shaders (flat cyan) is
/// thrown away and taken again, as the portrait cards' are (SkinPortraits). Pieces stand in the pose their file was made
/// in, framed from their meshes as made, so a skinned piece is measured without waiting for a pose.
/// </summary>
[DisallowMultipleComponent]
public sealed class OutfitIcons : MonoBehaviour
{
    public const int Size = 160;
    private const float FieldOfView = 24f, Fill = .86f, Lift = .02f;
    // The menu's lights are directional and reach the studio; it only needs to be out of every camera's sight.
    private static readonly Vector3 Studio = new Vector3(-600f, 0f, 0f);
    private const int RendersPerPicture = 2;

    private readonly struct Ask
    {
        public readonly string Key;
        public readonly PlayerBodyProfile.Skin Skin;
        public readonly OutfitItem Item;
        public readonly string Colour, Slot;

        public Ask(string key, PlayerBodyProfile.Skin skin, OutfitItem item, string colour, string slot)
        {
            Key = key;
            Skin = skin;
            Item = item;
            Colour = colour ?? "";
            Slot = slot;
        }
    }

    private static readonly Dictionary<string, RenderTexture> taken = new Dictionary<string, RenderTexture>();
    private static readonly HashSet<string> asked = new HashSet<string>();
    private static OutfitIcons current;
    private readonly Queue<Ask> waiting = new Queue<Ask>();
    private Camera lens;
    private RenderTexture film;
    private GameObject sitter;
    private Ask sitting;
    private int shotFrame;
    private bool spoiled;

    /// <summary>Raised whenever a picture is ready, so the wardrobe can put it in.</summary>
    public static event Action Taken;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        foreach (var picture in taken.Values)
            if (picture != null) { picture.Release(); Destroy(picture); }
        taken.Clear();
        asked.Clear();
        Taken = null;
        current = null;
    }

    /// <summary>The picture of an item in a colour set for this character, or null until it has been taken (asked for then).</summary>
    public static Texture OfItem(PlayerBodyProfile.Skin skin, OutfitItem item, string colour)
    {
        if (skin == null || item == null) return null;
        string key = "item@" + skin.Id + "@" + item.id + "#" + (colour ?? "");
        return Get(key, () => new Ask(key, skin, item, colour, null));
    }

    /// <summary>The picture of the character's own piece in a slot, in its colour set, or null until taken (asked for then).</summary>
    public static Texture OfOwn(PlayerBodyProfile.Skin skin, string slot, SkinVariant variant)
    {
        if (skin == null || string.IsNullOrEmpty(slot) || !OutfitCatalog.HasOwnPiece(skin.Id, slot)) return null;
        string key = "own@" + skin.Id + "@" + slot + "#" + (variant != null ? variant.Id : "");
        return Get(key, () => new Ask(key, skin, null, variant != null ? variant.Id : "", slot));
    }

    private static Texture Get(string key, Func<Ask> ask)
    {
        if (taken.TryGetValue(key, out var picture) && picture != null && picture.IsCreated()) return picture;
        if (!Application.isPlaying || !asked.Add(key)) return null;
        if (current == null) current = new GameObject("Outfit Icons").AddComponent<OutfitIcons>();
        current.waiting.Enqueue(ask());
        return null;
    }

    private void Awake()
    {
        if (current == null) current = this;
        lens = new GameObject("Outfit Icon Camera", typeof(Camera)).GetComponent<Camera>();
        lens.transform.SetParent(transform, false);
        lens.enabled = false;
        lens.clearFlags = CameraClearFlags.SolidColor;
        // Nothing behind the piece: the wardrobe lays the picture over its plates.
        lens.backgroundColor = new Color(0f, 0f, 0f, 0f);
        lens.allowHDR = false;
        lens.fieldOfView = FieldOfView;
        lens.nearClipPlane = .02f;
        lens.farClipPlane = 20f;
    }

    private void LateUpdate()
    {
        if (sitter != null)
        {
            spoiled |= ShadersCompiling;
            if (Time.frameCount - shotFrame >= RendersPerPicture) Develop();
        }
        while (sitter == null && waiting.Count > 0 && !ShadersCompiling)
            if (Sit(waiting.Dequeue())) break;
        if (sitter == null) lens.enabled = false;
    }

    private static bool ShadersCompiling
    {
        get
        {
#if UNITY_EDITOR
            return UnityEditor.ShaderUtil.anythingCompiling;
#else
            return false;
#endif
        }
    }

    // Stands the piece in the studio and points the lens at it; false when there is nothing to photograph.
    private bool Sit(Ask ask)
    {
        sitting = ask;
        spoiled = false;
        var shown = new List<Renderer>();
        if (ask.Item != null)
        {
            var entry = OutfitCatalog.Model(ask.Item.id, ask.Skin.Id);
            if (entry == null || entry.Model == null) return false;
            sitter = Instantiate(entry.Model, Studio, Quaternion.identity, transform);
            foreach (var animator in sitter.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var wanted = new HashSet<string>(ask.Item.PartNames, StringComparer.OrdinalIgnoreCase);
            var drop = new HashSet<string>(OutfitCatalog.Split(OutfitCatalog.FitOf(ask.Item.id, ask.Skin.Id)?.drop), StringComparer.OrdinalIgnoreCase);
            var character = ask.Skin.Model;
            var body = character != null ? character.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true))
            {
                // Its own meshes only: an outfit's file often carries the avatar's body it was fitted on, told from the
                // piece by dressing's own rule (a mirrored copy of the character's mesh is a piece, not her body).
                bool mine = wanted.Count > 0 ? wanted.Contains(renderer.name)
                    : !drop.Contains(renderer.name) && (character == null || !OutfitDresser.CopiesOwn(renderer, sitter.transform, body, character.transform));
                renderer.enabled = mine;
                if (mine) shown.Add(renderer);
            }
            SkinVariant.WearOn(shown, entry.VariantOf(ask.Colour));
        }
        else
        {
            sitter = Instantiate(ask.Skin.Model, Studio, Quaternion.identity, transform);
            foreach (var animator in sitter.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            SkinVariant.Wear(sitter, null, ask.Skin.VariantOf(ask.Colour));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in OutfitCatalog.PartsOf(ask.Skin.Id))
                if (string.Equals(part.slot, ask.Slot, StringComparison.OrdinalIgnoreCase)) names.UnionWith(OutfitCatalog.Split(part.parts));
            foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true))
            {
                bool mine = names.Contains(renderer.name);
                renderer.enabled = mine;
                if (mine)
                {
                    renderer.gameObject.SetActive(true);
                    shown.Add(renderer);
                }
            }
        }
        if (shown.Count == 0)
        {
            Dismiss();
            return false;
        }
        foreach (var renderer in shown)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        var bounds = AsMade(shown);
        // Faces +Z as the preview's character does, the lens in front looking back, the piece filling the square.
        float span = Mathf.Max(.05f, Mathf.Max(bounds.size.y, bounds.size.x));
        float distance = span * .5f / Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad) / Fill + bounds.extents.z;
        lens.transform.position = bounds.center + Vector3.forward * distance + Vector3.up * span * Lift;
        lens.transform.rotation = Quaternion.LookRotation(Vector3.back);
        if (film == null)
        {
            film = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Outfit Icon Film", antiAliasing = 4 };
            film.Create();
        }
        lens.targetTexture = film;
        lens.enabled = true;
        shotFrame = Time.frameCount;
        return true;
    }

    private static Mesh MeshOf(Renderer renderer) =>
        renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;

    // The pieces' box as their meshes were made, which is how a fresh copy stands (its bones in the file's pose).
    private static Bounds AsMade(List<Renderer> renderers)
    {
        bool any = false;
        var bounds = new Bounds();
        foreach (var renderer in renderers)
        {
            var mesh = MeshOf(renderer);
            Bounds piece;
            if (mesh != null)
            {
                var local = mesh.bounds;
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corners[i] = renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)));
                piece = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 8; i++) piece.Encapsulate(corners[i]);
            }
            else piece = renderer.bounds;
            if (!any) { bounds = piece; any = true; }
            else bounds.Encapsulate(piece);
        }
        return bounds;
    }

    // The film is multisampled; each kept picture is a plain copy of it. A spoiled one goes back in the queue.
    private void Develop()
    {
        if (spoiled)
        {
            waiting.Enqueue(sitting);
            Dismiss();
            return;
        }
        if (!taken.TryGetValue(sitting.Key, out var picture) || picture == null)
        {
            picture = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { name = "Outfit Icon " + sitting.Key };
            taken[sitting.Key] = picture;
        }
        Graphics.Blit(film, picture);
        Dismiss();
        Taken?.Invoke();
    }

    private void Dismiss()
    {
        if (sitter != null) Destroy(sitter);
        sitter = null;
    }

    private void OnDestroy()
    {
        if (current == this) current = null;
        Dismiss();
        if (lens != null) lens.targetTexture = null;
        if (film != null) { film.Release(); Destroy(film); }
        // Asked-for pictures this copy never took are asked for again by whoever comes next.
        foreach (var ask in waiting) asked.Remove(ask.Key);
        waiting.Clear();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Card pictures of every skin and colour set for the character screen (<see cref="CharacterSkinView"/>). They are taken
/// on the main menu, under its preview light, and kept for the run, so the Esc menu's character tab shows them too.
/// One picture every few frames: the model stands far to the side of the preview, a camera renders it into a texture, and
/// it goes. A run started straight in a level has none until the main menu opens; its cards show names only.
/// The editor compiles shaders in the background and draws them flat cyan meanwhile, so a picture taken while anything
/// compiled is thrown away and taken again once compiling stops. Builds ship compiled shaders and never wait.
/// </summary>
[DisallowMultipleComponent]
public sealed class SkinPortraits : MonoBehaviour
{
    public const int Width = 256, Height = 384;
    private const float FieldOfView = 20f, Fill = .9f;
    // The first render asks for any missing shader; the second shows it if it compiled in between.
    private const int RendersPerPicture = 2;
    // Out of the preview camera's narrow view (MainMenuPreview), so the sitter never shows beside the menu.
    private static readonly Vector3 Studio = new Vector3(600f, 0f, 0f);
    private static readonly Dictionary<string, RenderTexture> taken = new Dictionary<string, RenderTexture>();
    private readonly Queue<(PlayerBodyProfile.Skin skin, SkinVariant variant)> waiting =
        new Queue<(PlayerBodyProfile.Skin, SkinVariant)>();
    private PlayerBodyProfile profile;
    private Camera lens;
    private RenderTexture film;
    private GameObject sitter;
    private PlayableGraph pose;
    private (PlayerBodyProfile.Skin skin, SkinVariant variant) sitting;
    private int shotFrame;
    private bool spoiled;

    /// <summary>Raised whenever a picture is ready.</summary>
    public static event Action Taken;

    /// <summary>The picture of this skin in this colour set (null: as imported), or null when it has not been taken.</summary>
    public static Texture Of(PlayerBodyProfile.Skin skin, SkinVariant variant)
    {
        if (skin == null) return null;
        return taken.TryGetValue(Key(skin, variant), out var picture) && picture != null && picture.IsCreated() ? picture : null;
    }

    private static string Key(PlayerBodyProfile.Skin skin, SkinVariant variant) => PlayerBodyProfile.LookId(skin.Id, variant?.Id);

    private void Awake()
    {
        lens = new GameObject("Portrait Camera", typeof(Camera)).GetComponent<Camera>();
        lens.transform.SetParent(transform, false);
        lens.enabled = false;
        lens.clearFlags = CameraClearFlags.SolidColor;
        lens.backgroundColor = CharacterSkinView.CardFill;
        lens.fieldOfView = FieldOfView;
        lens.nearClipPlane = .05f;
        lens.farClipPlane = 30f;
    }

    // Every look is queued at once: the main menu is a loading moment, so the pictures are ready when the screen opens.
    private void Start()
    {
        profile = PlayerBodyProfile.Load();
        if (profile == null) return;
        foreach (var skin in profile.Skins)
        {
            if (skin == null || skin.Model == null) continue;
            if (skin.Variants.Count == 0) Queue(skin, null);
            foreach (var variant in skin.Variants) Queue(skin, variant);
        }
    }

    private void Queue(PlayerBodyProfile.Skin skin, SkinVariant variant)
    {
        if (Of(skin, variant) == null) waiting.Enqueue((skin, variant));
    }

    private void LateUpdate()
    {
        if (sitter != null)
        {
            spoiled |= ShadersCompiling;
            // The sitter placed earlier has been rendered RendersPerPicture times by now.
            if (Time.frameCount - shotFrame >= RendersPerPicture) Develop();
        }
        if (sitter == null && waiting.Count > 0 && !ShadersCompiling) Sit(waiting.Dequeue());
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

    private void Sit((PlayerBodyProfile.Skin skin, SkinVariant variant) look)
    {
        sitting = look;
        spoiled = false;
        sitter = Instantiate(look.skin.Model, Studio, Quaternion.identity, transform);
        SkinVariant.Wear(sitter, null, look.variant);
        var bounds = new Bounds(sitter.transform.position, Vector3.zero);
        foreach (var renderer in sitter.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(renderer.bounds);
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }
        var animator = sitter.GetComponentInChildren<Animator>();
        var idle = profile.Find(MainMenuPreview.IdleClip);
        if (animator != null && idle != null)
        {
            animator.applyRootMotion = false;
            AnimationPlayableUtilities.PlayClip(animator, idle, out pose);
            pose.Evaluate(); // the animators have run this frame, so pose it now, before the render
        }

        // Faces +Z like the preview, framed head to foot in the middle of the card.
        float height = Mathf.Max(.1f, bounds.size.y);
        float distance = height * .5f / Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad) / Fill;
        lens.transform.position = bounds.center + Vector3.forward * distance;
        lens.transform.rotation = Quaternion.LookRotation(Vector3.back);
        if (film == null)
        {
            film = new RenderTexture(Width, Height, 24) { name = "Portrait Film", antiAliasing = 4 };
            film.Create();
        }
        lens.targetTexture = film;
        lens.enabled = true;
        shotFrame = Time.frameCount;
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
        string key = Key(sitting.skin, sitting.variant);
        if (!taken.TryGetValue(key, out var picture) || picture == null)
        {
            picture = new RenderTexture(Width, Height, 0) { name = "Portrait " + key };
            taken[key] = picture;
        }
        Graphics.Blit(film, picture);
        Dismiss();
        Taken?.Invoke();
    }

    private void Dismiss()
    {
        if (pose.IsValid()) pose.Destroy();
        if (sitter != null) Destroy(sitter);
        sitter = null;
    }

    private void OnDestroy()
    {
        Dismiss();
        if (lens != null) lens.targetTexture = null;
        if (film != null) { film.Release(); Destroy(film); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        foreach (var picture in taken.Values)
            if (picture != null) { picture.Release(); Destroy(picture); }
        taken.Clear();
        Taken = null;
    }
}

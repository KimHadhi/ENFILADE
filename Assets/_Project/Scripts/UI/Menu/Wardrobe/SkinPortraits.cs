using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Card pictures of every skin and colour set for the character screen (<see cref="CharacterSkinView"/>). They are taken
/// on the main menu, under its preview light, and kept for the run, so the Esc menu's character tab shows them too.
/// One picture every few frames: the model stands far to the side of the preview, a camera renders it into a texture, and
/// it goes. A run started straight in a level takes them when the character tab first opens (Ensure): far below the
/// level, under a small light of its own that reaches nothing else. Before, those cards showed names only, and the user
/// found the character tab without a picture (2026-11-07).
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
    // In a level: far under it, where no player's view or light reaches, and nothing of the level is near the lens.
    private static readonly Vector3 LevelStudio = new Vector3(600f, -2000f, 0f);
    // The level studio's light: this far out from the sitter's middle, toward the lens, up and to its right, as a share of
    // its height, and this bright for that distance. Its range ends just past the sitter.
    private static readonly Vector3 LampAt = new Vector3(.35f, .3f, 1f);
    private const float LampBrightness = 1.4f, LampReach = 2f;
    private static readonly Dictionary<string, RenderTexture> taken = new Dictionary<string, RenderTexture>();
    private static SkinPortraits current;
    private Vector3 studio = Studio;
    private Light lamp;
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

    /// <summary>
    /// Makes sure pictures are being taken: in a run started straight in a level there is no main menu to take them, so the
    /// first character tab does, in a studio of its own (LevelStudio).
    /// </summary>
    public static void Ensure()
    {
        if (current != null) return;
        var portraits = new GameObject("Skin Portraits").AddComponent<SkinPortraits>();
        portraits.studio = LevelStudio;
        var light = new GameObject("Portrait Light", typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(portraits.transform, false);
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.enabled = false;
        portraits.lamp = light;
    }

    private void Awake()
    {
        if (current == null) current = this;
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
        if (sitter == null)
        {
            lens.enabled = false;
            if (lamp != null) lamp.enabled = false;
        }
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
        sitter = Instantiate(look.skin.Model, studio, Quaternion.identity, transform);
        SkinVariant.Wear(sitter, null, look.variant);
        foreach (var renderer in sitter.GetComponentsInChildren<Renderer>())
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        // Feet to crown on its skeleton, as the preview frames it, so every card shows its character at one size.
        var bounds = CharacterFrame.Measure(sitter);
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
        if (lamp != null)
        {
            // Point light falls off with the square of distance, so its brightness is set for where it stands.
            Vector3 offset = LampAt * height;
            lamp.transform.position = bounds.center + offset;
            lamp.range = offset.magnitude * LampReach;
            lamp.intensity = LampBrightness * offset.sqrMagnitude;
            lamp.enabled = true;
        }
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
        if (current == this) current = null;
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
        current = null;
    }
}

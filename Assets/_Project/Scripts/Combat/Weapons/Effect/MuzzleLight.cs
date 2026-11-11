using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The light a shot throws around itself (backlog 326): a point light just ahead of the muzzle that casts shadows, so
/// walls, pillars and bodies stop it and throw their shadows away from the gun, and the room behind a wall stays dark,
/// plus a faint one that casts none, for the glow that slips round doors and under walls from a shot in the next room.
/// Colour, reach and time come from the L4D2 addon "No VSLib Dynamic Light: Optimized Muzzle Flash" (3605084814):
/// 250 150 30 as Unity reads it (the soft orange the user picked over the yellower linear reading), 500 units (12.7 m) and
/// 0.08 s, which also follows MuzzleFlash's flame. The addon hangs its light at the shooter's middle, 50 units ahead;
/// this one rides the muzzle, and stops short of a wall the barrel is pressed into.
/// The look has no tonemapping, so at the brightness that gives the walls their glow the floor under the gun, and the
/// ceiling over it, the nearest surfaces, burned to one flat colour and looked like the light's source. A cookie therefore
/// dims the light straight down and straight up (GunFeel muzzleLightBelow) and leaves it full sideways: the walls keep the
/// glow, the floor still falls away from under the muzzle, and the shadows show. Only one's own gun casts shadows;
/// someone else's lights without them, dimmer. Tuning: GunFeel → Muzzle light. Added by WeaponController.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ShotEffects)]
public sealed class MuzzleLight : MonoBehaviour
{
    // Back along the barrel to a point surely on the shooter's side of a wall the muzzle is pressed into, and how far short
    // of that wall the light then stops.
    private const float Barrel = .6f, WallGap = .05f;
    // The cookie's faces (pixels a side): it only changes with the angle from level, so a few are plenty.
    private const int CookieSize = 32;

    // One cookie for every muzzle light, remade when its share changes.
    private static Cubemap cookie;
    private static float cookieBelow = -1f;

    private WeaponController weapon;
    private PlayerMovement movement;
    private Transform root;
    private Light direct, leak;
    // `hold`: this shot's share of its time at full brightness before it falls away (GunFeel muzzleLightHold).
    private float remaining, duration, hold, directIntensity, leakIntensity;
    private int warmFrames;

    private bool IsMine => movement == null || movement.IsLocal;

    private void Awake()
    {
        weapon = GetComponent<WeaponController>();
        movement = GetComponentInParent<PlayerMovement>();
    }

    // Built while the map loads, and shown once with its shadow, so the first shot doesn't stop to prepare either.
    private void Start()
    {
        root = FeelAssets.CreateRuntimeRoot("Muzzle Light", gameObject).transform;
        // The cookie is laid out by the lights' own axes: kept level, its up and down are the world's.
        root.rotation = Quaternion.identity;
        direct = Build("Muzzle Light", IsMine && GunFeelSettings.Instance.muzzleLightShadows);
        leak = Build("Muzzle Light Leak", false);
        if (direct.shadows == LightShadows.None) return;
        Transform view = weapon != null && weapon.AimOrigin != null ? weapon.AimOrigin : transform;
        root.position = view.position + view.forward * .5f;
        direct.intensity = .01f;
        direct.range = 1f;
        direct.enabled = true;
        warmFrames = 2;
    }

    private Light Build(string name, bool shadows)
    {
        var host = new GameObject(name);
        host.transform.SetParent(root, false);
        var light = host.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.shadowNearPlane = .1f;
        light.cookie = Cookie(GunFeelSettings.Instance.muzzleLightBelow);
        light.enabled = false;
        if (shadows)
        {
            // Six faces at GunFeel's tier (medium: 512 each). A new light takes the pipeline's highest, six of which would
            // fill most of the shadow atlas; URP only lets the inspector set it, so it goes in as the serialized field would.
            int tier = Mathf.Clamp(GunFeelSettings.Instance.muzzleLightShadowTier, UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow,
                UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh);
            var data = light.GetUniversalAdditionalLightData();
            JsonUtility.FromJsonOverwrite("{\"m_AdditionalLightsShadowResolutionTier\":" + tier + "}", data);
        }
        return light;
    }

    // Full strength sideways, `below` of it straight down and straight up, eased between by the square of the angle's cosine
    // (1 − up², up being the direction's height). The same either way up, so no face's orientation can turn it over; null
    // at 1, an even point light.
    private static Cubemap Cookie(float below)
    {
        below = Mathf.Clamp(below, .1f, 1f);
        if (below >= .999f) return null;
        if (cookie != null && Mathf.Approximately(cookieBelow, below)) return cookie;
        // A changed share makes a new one; lights already lit keep theirs.
        cookie = new Cubemap(CookieSize, TextureFormat.RGBA32, false)
        {
            name = "Muzzle Light Cookie", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color[CookieSize * CookieSize];
        for (int face = 0; face < 6; face++)
        {
            // The top and bottom faces look straight up or down; the side faces climb from level by their rows.
            bool cap = (CubemapFace)face == CubemapFace.PositiveY || (CubemapFace)face == CubemapFace.NegativeY;
            for (int y = 0; y < CookieSize; y++)
                for (int x = 0; x < CookieSize; x++)
                {
                    float u = (x + .5f) / CookieSize * 2f - 1f, v = (y + .5f) / CookieSize * 2f - 1f;
                    float up = (cap ? 1f : v * v) / (1f + u * u + v * v);
                    float share = Mathf.Lerp(below, 1f, 1f - up);
                    pixels[y * CookieSize + x] = new Color(share, share, share, share);
                }
            cookie.SetPixels(pixels, (CubemapFace)face);
        }
        cookie.Apply(false, false);
        cookieBelow = below;
        return cookie;
    }

    private void OnEnable()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.ShotFired += OnShot;
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= OnShot;
        Hide();
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private void OnShot(WeaponShot shot)
    {
        // One flash of light per shot, however many pellets it threw.
        if (shot.Pellet > 0) return;
        var settings = GunFeelSettings.Instance;
        var data = weapon != null ? weapon.EquippedData : null;
        // The muzzle this shot left from: a dual pistol's left gun's for its shots (AkimboGuns).
        Transform muzzle = weapon != null ? weapon.ShotMuzzle : null;
        // A suppressor throws no flash of light.
        if (!settings.muzzleEffects || direct == null || data == null || data.MuzzleFlashScale <= 0f
            || muzzle == null || !muzzle.gameObject.activeInHierarchy || weapon.Suppressed) return;
        warmFrames = 0;
        float size = Mathf.Min(1.5f, data.MuzzleFlashScale);
        float brightness = settings.muzzleLightIntensity * size * (IsMine ? 1f : settings.muzzleLightOthers);
        bool shadows = IsMine && settings.muzzleLightShadows;
        directIntensity = brightness;
        leakIntensity = shadows ? brightness * settings.muzzleLightLeak : 0f;
        direct.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        direct.color = leak.color = settings.muzzleLightColor;
        direct.range = leak.range = settings.muzzleLightRange;
        duration = remaining = settings.muzzleLightSeconds;
        hold = Mathf.Clamp(settings.muzzleLightHold, 0f, .99f);
        Place(muzzle, settings);
        Shine(1f);
    }

    private void Update()
    {
        if (warmFrames > 0 && --warmFrames == 0) { Hide(); return; }
        if (remaining <= 0f) return;
        remaining -= Time.deltaTime;
        if (remaining <= 0f) { Hide(); return; }
        Transform muzzle = weapon != null ? weapon.ShotMuzzle : null;
        if (muzzle != null && muzzle.gameObject.activeInHierarchy) Place(muzzle, GunFeelSettings.Instance);
        float left = remaining / Mathf.Max(.001f, duration);
        float fall = Mathf.Clamp01(left / (1f - hold));
        Shine(fall * fall);
    }

    // Just ahead of the muzzle, so the gun itself doesn't hide the light it makes, but short of a wall (or a body) the barrel
    // is pressed into or poking through, so a shot never lights the room beyond it from the inside.
    private void Place(Transform muzzle, GunFeelSettings settings)
    {
        Vector3 forward = muzzle.forward, at = muzzle.position + forward * settings.muzzleLightAhead;
        Vector3 back = muzzle.position - forward * Barrel, line = at - back;
        GameObject owner = movement != null ? movement.gameObject : gameObject;
        if (AttackTrace.FirstExternalHit(owner, back, line.normalized, line.magnitude, Physics.DefaultRaycastLayers, out RaycastHit hit))
            at = hit.point - forward * WallGap;
        root.position = at;
    }

    private void Shine(float share)
    {
        direct.intensity = directIntensity * share;
        direct.enabled = direct.intensity > 0f;
        leak.intensity = leakIntensity * share;
        leak.enabled = leak.intensity > 0f;
    }

    private void Hide()
    {
        remaining = 0f;
        if (direct != null) direct.enabled = false;
        if (leak != null) leak.enabled = false;
    }
}

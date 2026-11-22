using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The Grenades module on the screen, every frame: one tracker of live grenades (GrenadeTracker) feeding the four looks
    /// of the two addons, each as its original draws it.
    /// <list type="bullet">
    /// <item>Arctic's hologram: each grenade within reach drawn small before the eye, orange, turning (GrenadeHoloPass).</item>
    /// <item>Arctic's triangle: a red triangle on a circle round the screen's middle, on the grenade's side, pointing at it.</item>
    /// <item>MW's fuse ring (Indicator:DrawTimer): a thin circle round the crosshair closing in as the held fuse burns.</item>
    /// <item>MW's frag indicator (Indicator:DrawFrag): the frag picture and an arrow round it on a wider circle, red when close.</item>
    /// </list>
    /// The two addons drew in two HUDPaint hooks of no set order; here the 3D hologram is under the 2D layer, Arctic's
    /// triangles under MW's pieces. The quick grenade's sounds play from here too (QuickNadeSounds).
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HitMarkers + 1)]
    internal sealed class GrenadesHud : MonoBehaviour
    {
        private const string HoloShader = "UI/Addons/Grenades/GrenadeHolo";
        private static readonly int FrameAId = Shader.PropertyToID("_FrameA"), FrameBId = Shader.PropertyToID("_FrameB"),
            MixId = Shader.PropertyToID("_Mix"), ColorId = Shader.PropertyToID("_Color");

        private readonly GrenadeRingLook ring = new GrenadeRingLook();
        private readonly GrenadeFragLook frag = new GrenadeFragLook();
        private readonly GrenadeArcticLook arctic = new GrenadeArcticLook();
        private readonly GrenadeHoloLook holo = new GrenadeHoloLook();
        private readonly QuickNadeSoundTimes soundTimes = new QuickNadeSoundTimes();
        private readonly GrenadeTracker tracker = new GrenadeTracker();
        private readonly QuickNadeSounds sounds = new QuickNadeSounds();

        private Overlay overlay;
        private Painter painter;
        private Texture2D fragPicture, arrowPicture, trianglePicture;
        private Texture2D[] holoFrames;
        private Material holoMaterial;

        // MW's FragTimer: its strength and colour, and m_FragSafeTime / the fuse as last told (kept while it fades out).
        private float ringAlpha, burnt, fuse;
        private bool everCooked;
        private Color ringColor = Color.white;
        // DrawArc's circle for the current segment count.
        private float[] cosines, sines;

        /// <summary>The fuse ring is on the screen this frame (its strength above 0).</summary>
        public bool RingShown => ringAlpha > 0f;

        public void Build(Overlay owner)
        {
            overlay = owner;
            GameTables.BindNested(GrenadesTable.Name, GrenadesTable.RingRow, this, ring);
            GameTables.BindNested(GrenadesTable.Name, GrenadesTable.FragRow, this, frag);
            GameTables.BindNested(GrenadesTable.Name, GrenadesTable.ArcticRow, this, arctic);
            GameTables.BindNested(GrenadesTable.Name, GrenadesTable.HoloRow, this, holo);
            GameTables.BindNested(GrenadesTable.Name, GrenadesTable.SoundRow, this, soundTimes);
            painter = overlay.AddPainter("Grenades");
            fragPicture = Pictures.Get("Grenades/frag");
            arrowPicture = Pictures.Get("Grenades/frag_arrow");
            trianglePicture = Pictures.Get("Grenades/triangle");
            var shader = Resources.Load<Shader>(HoloShader);
            if (shader != null) holoMaterial = new Material(shader) { name = "Grenade hologram (Runtime)", hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogWarning("[HudAddons] No shader at Art/Resources/" + HoloShader + ".shader: Arctic's hologram is off.");
        }

        private void OnEnable() => GrenadeHoloPass.Hook();

        private void OnDisable()
        {
            GrenadeHoloPass.Unhook();
            sounds.Stop();
        }

        private void OnDestroy()
        {
            tracker.Clear();
            if (GrenadeHoloPass.Material == holoMaterial) GrenadeHoloPass.Material = null;
            if (holoMaterial != null) Destroy(holoMaterial);
        }

        // ---- What the game tells ---------------------------------------------------------------------------------------

        /// <summary>The hand takes a grenade out (see GrenadesModule.PullOut).</summary>
        public void PullOut() => sounds.PullOut(soundTimes);

        /// <summary>The pin comes out (see GrenadesModule.PinPulled).</summary>
        public void PinPulled() => sounds.PinPulled(soundTimes);

        /// <summary>The hand throws (see GrenadesModule.Throw).</summary>
        public void Throw(float secondsToRelease) => sounds.Throw(soundTimes, secondsToRelease);

        /// <summary>The hand put it back (see GrenadesModule.PutBack).</summary>
        public void PutBack() => sounds.Stop();

        private void Update() => sounds.Tick();

        // ---- Each frame ------------------------------------------------------------------------------------------------

        private void LateUpdate()
        {
            var options = GameSettings.Current?.addonGrenades;
            var view = Camera.main;
            bool seen = options != null && view != null && painter != null;
            if (overlay.Visible != seen) overlay.Visible = seen;
            GrenadeHoloPass.Draws.Clear();
            if (!seen)
            {
                ringAlpha = 0f;
                return;
            }
            var eye = view.transform;
            var player = PlayerRoster.Local;
            tracker.Sweep(eye, player != null ? player.transform.position : eye.position, options.listClaymores);

            float now = Smooth.Now;
            Hologram(view, now);
            painter.Begin();
            Triangles(now);
            Ring(options);
            Frags(options);
            painter.End();
        }

        // ---- Arctic's hologram -----------------------------------------------------------------------------------------

        private void Hologram(Camera view, float now)
        {
            if (holoMaterial == null) return;
            var look = holo;
            float range = Mathf.Max(1e-3f, arctic.range), unit = GrenadesTable.Unit;
            var eye = view.transform;
            // Angle(pitch, CurTime() * spin, 0) in the world, carried into the eye's space; Source's yaw turns the other way.
            var turn = Quaternion.Inverse(eye.rotation) * Quaternion.Euler(look.pitch, -now * look.spin, 0f);
            var all = tracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var grenade = all[i];
                if (!grenade.Live || grenade.EyeDistance > range || grenade.Parts.Count == 0) continue;
                float share = (Mathf.Clamp(grenade.EyeDistance, 0f, range) / range + look.distAdd) * look.distScale;
                float radians = grenade.CenterBearing * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(radians) * look.side * share, Mathf.Sin(radians) * look.up * share, look.ahead) * unit;
                var place = Matrix4x4.TRS(at, turn, Vector3.one) * grenade.Source.transform.worldToLocalMatrix;
                var parts = grenade.Parts;
                for (int p = 0; p < parts.Count; p++)
                {
                    var part = parts[p];
                    if (part.Transform == null || part.Mesh == null) continue;
                    var matrix = place * part.Transform.localToWorldMatrix;
                    for (int s = 0; s < part.Mesh.subMeshCount; s++)
                        GrenadeHoloPass.Draws.Add(new GrenadeHoloPass.Draw { Mesh = part.Mesh, Submesh = s, Matrix = matrix });
                }
            }
            if (GrenadeHoloPass.Draws.Count == 0) return;
            GrenadeHoloPass.Eye = view;
            GrenadeHoloPass.Material = holoMaterial;
            // Source's fov is the width of a 4:3 screen; Unity's is the height.
            GrenadeHoloPass.FieldOfView = 2f * Mathf.Atan(Mathf.Tan(look.fov * .5f * Mathf.Deg2Rad) * .75f) * Mathf.Rad2Deg;
            GrenadeHoloPass.Near = look.near * unit;
            GrenadeHoloPass.Far = look.far * unit;
            HoloFrames(look, now);
        }

        // ugi/holo's static: frame to frame as a smooth fade at frameRate a second (the flicker rule), not 20 cuts.
        private void HoloFrames(GrenadeHoloLook look, float now)
        {
            int count = Mathf.Clamp(look.frames, 1, 64);
            if (holoFrames == null || holoFrames.Length != count)
            {
                holoFrames = new Texture2D[count];
                for (int i = 0; i < count; i++)
                {
                    holoFrames[i] = Pictures.Get("Grenades/holo_" + i);
                    // A model's texture repeats past its edges (the vtf has no clamp flags); the UI import clamps them.
                    if (holoFrames[i] != null) holoFrames[i].wrapMode = TextureWrapMode.Repeat;
                }
            }
            float position = now * Mathf.Max(0f, look.frameRate);
            float whole = Mathf.Floor(position);
            int from = (int)(whole % count), to = (from + 1) % count;
            float mix = position - whole;
            holoMaterial.SetTexture(FrameAId, holoFrames[from] != null ? holoFrames[from] : Texture2D.whiteTexture);
            holoMaterial.SetTexture(FrameBId, holoFrames[to] != null ? holoFrames[to] : Texture2D.whiteTexture);
            holoMaterial.SetFloat(MixId, mix * mix * (3f - 2f * mix));
            holoMaterial.SetColor(ColorId, new Color(look.color.x, look.color.y, look.color.z, look.blend));
        }

        // ---- Arctic's triangle -----------------------------------------------------------------------------------------

        private void Triangles(float now)
        {
            var look = arctic;
            // clamp(sin(CurTime() * 15), 0, 1) blinked 2.4 times a second; a smooth rise and fall between the same ends.
            float pulse = .5f - .5f * Mathf.Cos(2f * Mathf.PI * Mathf.Max(0f, look.pulseHz) * now);
            float alpha = 255f * (pulse + look.floor);
            float size = overlay.ScreenScale(look.size), radius = overlay.ScreenScale(look.radius);
            float cx = overlay.Width * .5f, cy = overlay.Height * .5f;
            painter.Picture = trianglePicture;
            painter.SetColor(look.color.x, look.color.y, look.color.z, alpha);
            var all = tracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var grenade = all[i];
                if (!grenade.Live || grenade.EyeDistance > look.range) continue;
                float radians = grenade.Bearing * Mathf.Deg2Rad;
                painter.TexturedRectRotated(Mathf.Cos(radians) * radius + cx, -Mathf.Sin(radians) * radius + cy, size, size, grenade.Bearing - 90f);
            }
        }

        // ---- MW's fuse ring (Indicator:DrawTimer) ----------------------------------------------------------------------

        private void Ring(GrenadesOptions options)
        {
            var look = ring;
            if (!options.fuseIndicator)
            {
                // The convar off drew nothing at all; the crosshair, which steps aside for the ring, must not wait on a
                // strength left over from before.
                ringAlpha = 0f;
                ringColor = GrenadesTable.Rgb(look.color);
                return;
            }
            float held = GrenadesModule.CookHeld;
            if (held >= 0f)
            {
                everCooked = true;
                burnt = held;
                fuse = GrenadesModule.CookFuse;
            }
            // m_FragSafeTime is nil until the first quick grenade.
            if (!everCooked) return;
            if (held < 0f || burnt < look.showAfter)
            {
                if (ringAlpha > look.gone) ringAlpha = Smooth.Lerp(Rate(look.fadeOut), ringAlpha, 0f);
                else
                {
                    ringAlpha = 0f;
                    ringColor = GrenadesTable.Rgb(look.color);
                    return;
                }
            }
            else ringAlpha = Smooth.Lerp(Rate(look.fadeIn), ringAlpha, 1f);
            ringColor = Smooth.Lerp(Rate(look.colorTime), ringColor, GrenadesTable.Rgb(burnt >= fuse - look.redLead ? look.lateColor : look.color));
            // x / 0 is inf in Lua and clamps to 1: a fuse of nothing is all burnt.
            float share = fuse > 0f ? Mathf.Clamp01(burnt / fuse) : 1f;
            var color = Gm.Rgba(ringColor.r * 255f, ringColor.g * 255f, ringColor.b * 255f, 255f * ringAlpha);
            if (color.a == 0) return;
            Arc(overlay.Width * .5f, overlay.Height * .5f, look.radius - look.radius * share, look.thickness, look.segments, color);
        }

        // DrawArc(x, y, radius, thickness, 0, 360, segments): a band from radius out to radius + thickness (a negative
        // thickness lays it inward), one quad a segment, flat white tinted.
        private void Arc(float x, float y, float radius, float thickness, int segments, Color32 color)
        {
            segments = Mathf.Clamp(segments, 1, 256);
            if (cosines == null || cosines.Length != segments + 1)
            {
                cosines = new float[segments + 1];
                sines = new float[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    float angle = 2f * Mathf.PI * i / segments;
                    cosines[i] = Mathf.Cos(angle);
                    sines[i] = Mathf.Sin(angle);
                }
            }
            if (thickness < 0f)
            {
                radius += thickness;
                thickness = -thickness;
            }
            float outer = radius + thickness;
            var quads = painter.Quads(null);
            for (int i = 0; i < segments; i++)
            {
                float c0 = cosines[i], s0 = sines[i], c1 = cosines[i + 1], s1 = sines[i + 1];
                quads.Quad(new PolyVertex(x + c0 * outer, y + s0 * outer), new PolyVertex(x + c1 * outer, y + s1 * outer),
                    new PolyVertex(x + c1 * radius, y + s1 * radius), new PolyVertex(x + c0 * radius, y + s0 * radius), color);
            }
        }

        // ---- MW's frag indicator (Indicator:DrawFrag) ------------------------------------------------------------------

        private void Frags(GrenadesOptions options)
        {
            var look = frag;
            if (!options.indicator)
            {
                // DrawFrag was the only place records were let go: with it off they piled up. Gone ones go now.
                tracker.ReleaseGone();
                return;
            }
            float rate = Rate(look.time);
            float size = overlay.ScreenScale(look.size), radius = overlay.ScreenScale(look.radius);
            float cx = overlay.Width * .5f, cy = overlay.Height * .5f;
            Color far = GrenadesTable.Rgb(look.color), near = GrenadesTable.Rgb(look.nearColor);
            var all = tracker.All;
            for (int i = 0; i < all.Count; i++)
            {
                var grenade = all[i];
                if (!grenade.Frag) continue;
                if (!grenade.Live && grenade.Alpha < look.gone)
                {
                    tracker.Release(i--);
                    continue;
                }
                if (!grenade.Placed)
                {
                    // Indicator:Add starts it at the screen's middle, so it slides out to its place.
                    grenade.Placed = true;
                    grenade.X = cx;
                    grenade.Y = cy;
                }
                if (grenade.Live)
                {
                    grenade.Ang = grenade.Bearing;
                    grenade.Dist = grenade.FeetDistance;
                    float radians = grenade.Ang * Mathf.Deg2Rad;
                    grenade.X = Smooth.Lerp(rate, grenade.X, Mathf.Cos(radians) * radius + cx);
                    grenade.Y = Smooth.Lerp(rate, grenade.Y, -Mathf.Sin(radians) * radius + cy);
                }
                if (!grenade.Live || grenade.Dist > look.reach) grenade.Alpha = Smooth.Lerp(rate, grenade.Alpha, 0f);
                else
                {
                    grenade.Alpha = Smooth.Lerp(rate, grenade.Alpha, 1f);
                    grenade.Tint = Smooth.Lerp(rate, grenade.Tint, grenade.Dist > look.near ? far : near);
                }
                // Nothing to see (an unseen far one): no quads.
                if (Gm.Byte(255f * grenade.Alpha) == 0) continue;
                painter.AlphaScale = grenade.Alpha;
                painter.SetColor(grenade.Tint.r * 255f, grenade.Tint.g * 255f, grenade.Tint.b * 255f);
                painter.Picture = fragPicture;
                painter.TexturedRectRotated(grenade.X, grenade.Y, size, size, 0f);
                painter.Picture = arrowPicture;
                painter.TexturedRectRotated(grenade.X, grenade.Y, size * look.arrow, size * look.arrow, grenade.Ang - 90f);
            }
            painter.AlphaScale = 1f;
        }

        // Lerp(FrameTime() / t, ...) is a rate of 1/t a second; a time of 0 is there at once (a finite rate: ∞·0 is NaN).
        private static float Rate(float seconds) => seconds > 0f ? 1f / seconds : 1e6f;
    }
}

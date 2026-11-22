using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The player's own HUD as the HUD Mk II addon draws it (lua/hud_mkii: shared.lua's HUDPaint and the elements it calls,
    /// in its order): health and armour (cl_health, cl_armor), environment hazards (cl_hazard), ammo and the grenade count
    /// (cl_ammo), the pickup history (cl_history), the damage direction arcs (cl_indicator), all on one painter, swaying
    /// with the view; then the damage, heal and armour vignette (cl_effects) over it, and the view shake on the camera. With
    /// the special effects on (sfx.lua) the painter's canvas goes through PlayerHudFx: scanlines, red and cyan copies, the
    /// glitch on a hit. Our own portrait, ultimate magazine and rank stand over the health bar in the addon's boxes
    /// (PlayerHud.Portrait, PlayerHud.Magazine), the photo itself over the effects. The defaults set it as the video the
    /// user gave shows it (PlayerHudOptions): the bars at the foot of the left side, the Hitmarker Normal face. The weapon
    /// selector (cl_selector) is not drawn: our weapon slots keep their own look (WeaponSlotsHUD; the user, 11-22: "기존의
    /// 무기 슬롯 디자인이나 이펙트는 좋았는데 ... 아예 저걸로 바꾸지 말고").
    /// <para>What it reads, never writes: the local player's PlayerDamageReceiver (health, shield, its HitResolved event: the
    /// addon's EntityTakeDamage net messages), PlayerWeaponSlots (the held slot's weapon and rounds), PlayerGrenades (the
    /// grenade box), DetenteKit
    /// (the stims box: a Détente's doses), the player's UltimateCharge, ProfilePicture and RankProgress (the portrait
    /// panel). Pickups come in through PlayerHudModule (FieldPickups).</para>
    /// <para>The addon's frame-rate bound Lerp(FrameTime() * k) runs as Smooth's time-based equivalent, on unscaled time, so
    /// a paused game's HUD still settles. Nothing is allocated a frame: texts are rebuilt only when what they say changes.</para>
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.ViewPunch - 1)]
    public sealed partial class PlayerHud : MonoBehaviour
    {
        private const string Folder = "PlayerHud/";
        // The melee slot: with no weapon in it, it holds the class knife (MeleeKnife), as our slots show it.
        private const int MeleeSlot = 2;

        private static PlayerHud instance;

        private readonly PlayerHudLook look = new PlayerHudLook();
        private Overlay hud, top;
        private Painter painter, picturePainter, flarePainter;
        private PlayerHudFx fx;
        private Texture2D crate, stimsPicture, fallPicture, blastPicture, vignette;
        private readonly Texture2D[] ammoPictures = new Texture2D[11];

        private PlayerMovement player;
        private PlayerDamageReceiver receiver;
        private PlayerWeaponSlots slots;
        private Camera view;
        private float lookForView = float.NegativeInfinity;

        // This frame: the screen (GMod's ScrW()/ScrH() and their halves sw, sh), the addon's Scale, the sway, the time.
        private float screenWidth, screenHeight, sw, sh, scale, swayX, swayY, now, dt;
        // The view copy the sway trails (shared.lua lagger), in Garry's Mod's angles (yaw left, pitch down).
        private float lagYaw, lagPitch;
        private bool lagSet;

        /// <summary>The installed HUD's host, or null before Install.</summary>
        internal static PlayerHud Instance => instance;

        private PlayerHudOptions Options => GameSettings.Current.addonPlayerHud;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        /// <summary>Brings the HUD up once (PlayerHudModule.Install); again after a scene took the last one with it.</summary>
        internal static void Install()
        {
            if (!Application.isPlaying || instance != null) return;
            int order = Mathf.RoundToInt(GameTables.Number(PlayerHudLook.TableName, "Hud", "order", 26f));
            var overlay = Overlay.Create("PlayerHud", order);
            overlay.gameObject.AddComponent<PlayerHud>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            look.Bind(this);
            hud = GetComponent<Overlay>();
            painter = hud.AddPainter("HUD");
            // Over the HUD: its picture when the special effects draw it, the profile photo (clear of the effects), then
            // the vignette, which the addon draws after the effects (Nene.DamageEffect after Nene.PostDrawHUDFX), so neither
            // scanlines nor copies touch it.
            top = Overlay.Create("PlayerHud Over", hud.Canvas.sortingOrder + 1);
            fx = new PlayerHudFx(hud, top.AddLayer("Effects"));
            picturePainter = top.AddPainter("Portrait");
            flarePainter = top.AddPainter("Vignette");
            crate = Pictures.Get(Folder + "weapon");
            ammoPictures[0] = crate;
            for (int i = 1; i < ammoPictures.Length; i++) ammoPictures[i] = Pictures.Get(Folder + "ammotype/" + i);
            stimsPicture = Pictures.Get(Folder + "stims");
            fallPicture = Pictures.Get(Folder + "fall");
            blastPicture = Pictures.Get(Folder + "blast");
            vignette = Pictures.Get(Folder + "vig");
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            instance = null;
            Unbind();
            fx?.Destroy();
            if (scanlines != null) Destroy(scanlines);
            if (top != null) Destroy(top.gameObject);
        }

        // ---- the frame (shared.lua DrawHUD) ----

        private void LateUpdate()
        {
            var o = Options;
            now = Smooth.Now;
            dt = Smooth.Dt;
            Bind(PlayerRoster.Local);
            bool show = o != null && o.enabled && player != null && receiver != null && !LoadingScreen.Visible;
            if (hud.Visible != show) hud.Visible = show;
            if (top.Visible != show) top.Visible = show;
            fx.Set(show && o.sfx);
            if (!show)
            {
                HoldShake();
                // Health that came back while nothing showed is no heal to flare when the HUD shows again.
                lastHealth = float.NaN;
                return;
            }
            WatchHealing();
            Measure(o);

            painter.Begin();
            DrawStatus(o);
            DrawPortrait();
            DrawClass(o);
            DrawHazards(o);
            DrawAmmo(o);
            if (o.history) DrawHistory(o);
            DrawSense(o);
            painter.End();

            fx.Draw(look.Fx, o, now, dt, screenWidth, screenHeight);
            picturePainter.Begin();
            DrawPicture(o);
            picturePainter.End();
            flarePainter.Begin();
            DrawVignette(o);
            flarePainter.End();
            Shake(o);
        }

        // The screen, the Scale, the fonts, the colours and the sway for this frame (configs.lua's Think, shared.lua's sway).
        private void Measure(PlayerHudOptions o)
        {
            screenWidth = hud.Width;
            screenHeight = hud.Height;
            sw = screenWidth * .5f;
            sh = screenHeight * .5f;
            scale = hud.ScreenScale(look.Hud.scale) * o.scale;
            MeasureFonts(o);
            MeasureColours();

            // The HUD trails the view: a copy of the eye's angles eases after them and the HUD stands off by the gap,
            // times the intensity (x right when the view turns left, y up when it looks down).
            var eye = Eye();
            if (eye != null)
            {
                Vector3 angles = eye.eulerAngles;
                float yaw = -angles.y, pitch = Mathf.DeltaAngle(0f, angles.x);
                if (!lagSet)
                {
                    lagYaw = yaw;
                    lagPitch = pitch;
                    lagSet = true;
                }
                float follow = Smooth.Factor(look.Hud.swayFollow);
                lagYaw = Mathf.LerpAngle(lagYaw, yaw, follow);
                lagPitch = Mathf.LerpAngle(lagPitch, pitch, follow);
                swayX = o.sway ? Mathf.DeltaAngle(lagYaw, yaw) * o.swayIntensity : 0f;
                swayY = o.sway ? Mathf.DeltaAngle(lagPitch, pitch) * o.swayIntensity : 0f;
            }
            else swayX = swayY = 0f;
        }

        // The eye the addon reads (EyePos, EyeAngles): this player's view camera, else the main one.
        private Transform Eye()
        {
            if (view == null && now >= lookForView && player != null)
            {
                lookForView = now + 1f;
                view = player.GetComponentInChildren<Camera>();
            }
            if (view != null && view.isActiveAndEnabled) return view.transform;
            var main = Camera.main;
            return main != null ? main.transform : null;
        }

        // ---- the player ----

        private void Bind(PlayerMovement local)
        {
            if (local != player)
            {
                Unbind();
                player = local;
                lagSet = false;
                lastHealth = float.NaN;
            }
            if (player == null) return;
            if (receiver == null)
            {
                receiver = player.DamageReceiver;
                if (receiver != null) receiver.HitResolved += OnHit;
            }
            if (slots == null) player.TryGetComponent(out slots);
        }

        private void Unbind()
        {
            if (receiver != null) receiver.HitResolved -= OnHit;
            HoldShake();
            receiver = null;
            slots = null;
            view = null;
            shake = null;
            lookForView = float.NegativeInfinity;
            player = null;
        }

        // A hit that wounded (the addon's EntityTakeDamage on the victim: sv_indicator, sv_effects, the hazards' type).
        // Being grabbed is no wound in this game (DamageHit.TakesHold; the user, 2026-11-11: no hurt look on a grab).
        private void OnHit(DamageHit hit, DamageResult result)
        {
            if (result != DamageResult.Damaged || hit.TakesHold || receiver == null || !Application.isPlaying) return;
            var o = Options;
            if (o == null) return;
            now = Smooth.Now;
            // GMod asks Armor() before the damage lands: the shield had something if it took some or has some left.
            bool shielded = receiver.LastShieldTaken > 0f || receiver.Shield > 0f;
            bool blast = IsBlast(hit);
            Sensed(hit, shielded, blast, o);
            if (hit.Kind == DamageKind.Fall) AddHazard(HazardFall, fallPicture);
            else if (blast) AddHazard(HazardBlast, blastPicture);
            Flare(shielded ? FlareArmor : FlareDamage, o);
        }

        // DMG_BLAST: a frag's burst, or an exploder zombie's (its blow carries no method of its own).
        private static bool IsBlast(in DamageHit hit)
            => hit.Method == DamageMethod.Blast || (hit.Source != null && hit.Source.GetComponent<ZombieExploder>() != null);
    }
}

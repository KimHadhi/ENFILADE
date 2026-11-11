using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The throwing knife in a player's off hand (porting bundle 6, after [MW] Quick Throwing Knife, GMod 3650480912: its
/// +throwknife held draws, let go throws). The knife key (Player/ThrowKnife, H unless changed in Settings → 조작) held
/// draws a knife into the off hand; let go, it is thrown at what the crosshair is on, on the addon's timeline: 0.1 s
/// later the swing's sound and a small view punch, at 0.3 s the knife leaves the hand (ThrownKnife, 50 m/s end over
/// end), at 0.5 s the gun is ready again (Tables/ThrowingKnife swingTime, releaseTime, recoverTime).
/// While the knife is out the gun neither fires, aims nor reloads (WeaponController.HoldBack), nor do the fists punch or
/// shove (PlayerMeleeCombat.HoldOffense): the off hand has left the gun, so a reload under way is cut off and the sights
/// come down; once the gun is back, the aim key still held aims again.
/// The aim key pressed, another weapon taken, a move that needs the hands (a climb, a ladder, a fuel can), being grabbed,
/// going down or dying put the knife back unthrown; once it is thrown only a grab, a fall or death before it leaves the
/// hand keeps it. A player carries a few (carry), takes them back with F (ThrowingKnives), gets one from an ammo pile
/// (FieldPickups) and fills up at the shop, and has the starting count again after a respawn. The count shows by the
/// crosshair while a knife is out and a moment after. Every other PC sees the knife come out in the hand and fly
/// (ThrowingKnives' notes). On every player; the input runs on this PC's own.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 1)]
public sealed class PlayerKnives : MonoBehaviour
{
    private enum Step { Idle, Drawn, Throwing }

    // Where the knife leaves the hand: in front of the eyes, a little left and down (m, in the view's axes).
    private static readonly Vector3 LaunchOffset = new Vector3(-.12f, -.06f, .3f);
    // Where it is held with no body to hold it: low left in the view (m).
    private static readonly Vector3 BodilessHold = new Vector3(-.2f, -.2f, .38f);
    // The crosshair's point nearer than this is no aim, the knife going along the view; how far the aim is looked for (m).
    private const float NearestAim = 1.5f, FarthestAim = 120f;
    // How long the count stays by the crosshair after the knife is put away, thrown or taken (real s), and its fade.
    private const float CountShown = 1.8f, CountFade = .35f;
    // A friend's knife shown drawn with no word since is put away after this long (s).
    private const float DrawnLongest = 8f;

    private static Texture2D icon;

    private PlayerMovement movement;
    private WeaponController weapon;
    private PlayerWeaponInput weaponInput;
    private PlayerWeaponSlots slots;
    private PlayerBody body;
    private InputAction key;
    private Step step;
    private float clock, drawnAt = -99f, countAt = -99f, refusedAt = -99f;
    private bool swung, released, guardWas, wasAlive = true;
    private int count = -1, slotAtDraw;
    private Transform held;
    private AudioSource own;
    private GameObject countHost;
    private TextMeshProUGUI countLabel;
    private CanvasGroup countGroup;

    /// <summary>This PC's own player's knives, or null.</summary>
    public static PlayerKnives Local => Of(PlayerRoster.Local);

    /// <summary>A player's knives, or null.</summary>
    public static PlayerKnives Of(PlayerMovement player) => player != null && player.TryGetComponent(out PlayerKnives knives) ? knives : null;

    // The map's knives, as they are; only drawing one makes them where a map has none (ThrowingKnives.Instance).
    private static ThrowingKnives Home => ThrowingKnives.Current;
    private static ThrowingKnifeLibrary Library => ThrowingKnifeLibrary.Instance;

    /// <summary>Knives carried now.</summary>
    public int Count => count >= 0 ? count : Starting;
    /// <summary>The most a player carries (Tables/ThrowingKnife carry).</summary>
    public int Most => Home != null ? Mathf.Max(0, Home.carry) : 0;
    public bool Full => Count >= Most;
    /// <summary>A knife is out or on its way: the gun waits.</summary>
    public bool Busy => step != Step.Idle;
    private int Starting => Home != null ? Mathf.Clamp(Home.start, 0, Mathf.Max(0, Home.carry)) : 0;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        weapon = GetComponent<WeaponController>();
        weaponInput = GetComponent<PlayerWeaponInput>();
        slots = GetComponent<PlayerWeaponSlots>();
    }

    private void Update()
    {
        if (movement == null) return;
        if (!movement.IsLocal || movement.IsNetworkDriven)
        {
            // A friend's: the hand shows what their PC said, and lets go should the word that it was put away be lost.
            if (held != null && held.gameObject.activeSelf && Time.time - drawnAt > DrawnLongest) ShowInHand(false);
            return;
        }
        Revived();
        if (key == null && InputSystem.actions != null)
        {
            KeyBindings.EnsureRestored();
            key = InputSystem.actions.FindAction("Player/ThrowKnife", false);
        }
        if (step == Step.Idle) { if (key != null && key.WasPressedThisFrame()) Draw(); }
        else if (step == Step.Drawn) Wait();
        else Throwing(movement.DeltaTime);
        // The off hand is on the knife: the gun neither fires, aims nor reloads until the throw is over, nor do the fists
        // punch or shove (their frame came first, so a punch started now never lands).
        if (step != Step.Idle)
        {
            if (weapon != null) weapon.HoldBack(Time.deltaTime + .02f);
            if (movement.MeleeCombat != null) movement.MeleeCombat.HoldOffense();
        }
        ShowCount();
    }

    private void LateUpdate()
    {
        if (held != null && held.gameObject.activeSelf) Place();
    }

    private void OnDisable()
    {
        step = Step.Idle;
        ShowInHand(false);
    }

    private void OnDestroy()
    {
        if (countHost != null) Destroy(countHost);
    }

    // ---- The throw ----

    // Back from the dead (a respawn, a Détente's Resurrection): the starting knives again, as the guns' ammo is.
    private void Revived()
    {
        var receiver = movement.DamageReceiver;
        bool alive = receiver != null && receiver.IsAlive;
        if (alive && !wasAlive) count = Mathf.Max(Count, Starting);
        if (!alive && step != Step.Idle) PutBack();
        wasAlive = alive;
    }

    private void Draw()
    {
        if (!movement.InputAvailable || !TakesInput() || ThrowingKnives.Instance == null || !Able()) return;
        if (Count <= 0)
        {
            refusedAt = Time.unscaledTime;
            KitSound.Play(KitSound.Cue.Refuse, .35f);
            return;
        }
        step = Step.Drawn;
        clock = 0f;
        swung = released = false;
        slotAtDraw = slots != null ? slots.SelectedSlot : -1;
        guardWas = weaponInput != null && weaponInput.GuardHeld;
        // The off hand leaves the gun for the knife: a reload under way stops and the sights come down.
        if (weapon != null)
        {
            weapon.InterruptForSurvival();
            FPSBody.Holding(weapon)?.Reach();
        }
        if (movement.MeleeCombat != null) movement.MeleeCombat.CancelAction();
        ShowInHand(true);
        PlayOwn(Library != null ? Library.draw : null, .75f);
        PlayOwn(Library != null ? Library.drawCloth : null, .45f);
        Home.ShareHand(true);
    }

    // The knife is out: letting go of the key throws it; the aim key, another weapon, or anything that takes the hands
    // puts it back.
    private void Wait()
    {
        bool guard = weaponInput != null && weaponInput.GuardHeld;
        bool aimed = guard && !guardWas;
        guardWas = guard;
        if (!Able() || !movement.InputAvailable || !TakesInput() || aimed || (slots != null && slots.SelectedSlot != slotAtDraw))
        {
            PutBack();
            return;
        }
        if (key != null && key.IsPressed()) return;
        step = Step.Throwing;
        clock = 0f;
    }

    private void Throwing(float dt)
    {
        var home = Home;
        if (home == null) { step = Step.Idle; ShowInHand(false); return; }
        clock += dt;
        // Grabbed, down or dead before it left: it never leaves the hand.
        if (!released && !Able()) { PutBack(); return; }
        if (!swung && clock >= home.swingTime) Swing(home);
        if (!released && clock >= home.releaseTime) Release(home);
        if (released && clock >= Mathf.Max(home.recoverTime, home.releaseTime)) step = Step.Idle;
    }

    private void Swing(ThrowingKnives home)
    {
        swung = true;
        PlayOwn(Library != null ? Library.swing : null, .9f);
        PlayOwn(Library != null ? Library.swingCloth : null, .55f);
        var effects = GetComponentInChildren<PlayerCameraEffects>();
        if (effects != null) effects.ApplyCameraImpulse(new Vector3(home.viewPunch.x, 0f, Random.Range(-home.viewPunch.y, home.viewPunch.y)));
        if (weapon != null) FPSBody.Holding(weapon)?.Point();
    }

    private void Release(ThrowingKnives home)
    {
        released = true;
        ShowInHand(false);
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null || Count <= 0) { home.ShareHand(false); return; }
        Launch(eyes, home, out Vector3 from, out Vector3 velocity);
        count = Count - 1;
        countAt = Time.unscaledTime;
        home.Throw(movement, eyes.position, from, velocity);
    }

    // From the off hand toward what the crosshair is on, so it goes where the player looks.
    private void Launch(Transform eyes, ThrowingKnives home, out Vector3 from, out Vector3 velocity)
    {
        Vector3 eye = eyes.position;
        from = eye + eyes.rotation * LaunchOffset;
        // Face to a wall, it leaves from just this side of it.
        if (Physics.Linecast(eye, from, out RaycastHit wall, ThrowingKnives.Mask, QueryTriggerInteraction.Ignore))
            from = wall.point - (from - eye).normalized * .05f;
        Vector3 aim = eye + eyes.forward * FarthestAim;
        if (AttackTrace.FirstExternalHit(gameObject, eye, eyes.forward, FarthestAim, ThrowingKnives.Mask, out RaycastHit seen)
            && seen.distance >= NearestAim) aim = seen.point;
        Vector3 way = (aim - from).normalized;
        if (Vector3.Dot(way, eyes.forward) < .5f) way = eyes.forward;
        velocity = way * home.speed;
    }

    private void PutBack()
    {
        step = Step.Idle;
        ShowInHand(false);
        countAt = Time.unscaledTime;
        if (Home != null) Home.ShareHand(false);
    }

    // The hands are free for a knife: not down, held, in a takedown or a climb that needs them, nor carrying a can, and
    // the off hand isn't holding a weapon of its own (a dual pistol's left gun, the katana's grip), where the knife would
    // show inside the other hand's grip.
    private bool Able()
    {
        if (movement.IsDead || movement.IsDowned || movement.IsHeld || movement.CombatInputLocked) return false;
        if (!PlayerBodyHands.HoldsWeapons(movement.CurrentState) || (slots != null && slots.HandsFull)) return false;
        if (slots != null && OffHandBusy(slots.SelectedWeapon)) return false;
        return Home != null;
    }

    private static bool OffHandBusy(WeaponData held) => held != null && (AkimboGuns.Is(held) || MeleeWeapons.IndexOf(held) >= 0);

    // As the mouse look: not while the cursor is free (chat, menus), the window is in the background or the console is open.
    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard;

    // ---- Knives carried ----

    /// <summary>Adds knives up to the most carried. Returns how many were added.</summary>
    public int Gain(int knives)
    {
        int before = Count;
        int after = Mathf.Clamp(before + Mathf.Max(0, knives), 0, Mathf.Max(before, Most));
        count = after;
        if (after != before) countAt = Time.unscaledTime;
        return after - before;
    }

    /// <summary>The shop: back up to the most. False if already full.</summary>
    public bool Fill() => Gain(Most) > 0;

    /// <summary>A knife taken back with F reached this PC's player: pulled out of a wall or a body, or picked up.</summary>
    internal void TakeBack(bool fromWall)
    {
        if (Gain(1) <= 0) return;
        PlayOwn(Library != null ? (fromWall ? Library.draw : Library.pickup) : null, .8f);
        PickupFeedHUD.Show(Icon, KitStyle.Cyan, Texts.Get("knife.name"), Texts.Format("pickup.gives.knife", 1));
        if (weapon != null) FPSBody.Holding(weapon)?.Reach();
    }

    /// <summary>This PC's player took an ammo pile (FieldPickups): a knife or so comes with it (ammoKnives).</summary>
    public static void FromAmmo(PlayerMovement player)
    {
        var knives = Of(player);
        var home = Home;
        if (knives == null || home == null) return;
        int added = knives.Gain(home.ammoKnives);
        if (added > 0) PickupFeedHUD.Show(Icon, KitStyle.Cyan, Texts.Get("knife.name"), Texts.Format("pickup.gives.knife", added));
    }

    /// <summary>The player carries fewer knives than the most, and an ammo pile would give them one.</summary>
    public static bool Short(PlayerMovement player)
    {
        var knives = Of(player);
        var home = Home;
        return knives != null && home != null && home.ammoKnives > 0 && !knives.Full;
    }

    // ---- What a friend's PC said ----

    /// <summary>A friend drew a knife into the hand, or put it back unthrown.</summary>
    internal void ShowDrawn(bool drawn)
    {
        ShowInHand(drawn);
        if (drawn && held != null && Home != null) Home.Play(Library != null ? Library.draw : null, held.position, .45f);
    }

    /// <summary>A friend's knife left their hand at <paramref name="at"/>: the hand lets go and the swing is heard.</summary>
    internal void ShowThrown(Vector3 at)
    {
        ShowInHand(false);
        if (Home != null) Home.Play(Library != null ? Library.swing : null, at, .6f);
    }

    // ---- The knife in the hand ----

    private void ShowInHand(bool on)
    {
        if (on && held == null)
        {
            var home = Home;
            if (home == null) return;
            held = new GameObject("Knife In Hand").transform;
            held.gameObject.layer = gameObject.layer;
            held.SetParent(transform, false);
            home.Dress(held);
        }
        if (held == null) return;
        if (on) drawnAt = Time.time;
        if (held.gameObject.activeSelf != on) held.gameObject.SetActive(on);
        if (on) Place();
    }

    // In the off hand, blade up and a little forward with its flat to the view, after the hands are placed; over the
    // swing it goes out to where it leaves the hand, so the knife that flies starts where this one ends.
    private void Place()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return;
        var home = Home;
        float half = home != null ? home.HalfLength : .14f;
        Vector3 tip = (eyes.up * .8f + eyes.forward * .55f - eyes.right * .1f).normalized;
        Quaternion turn = Quaternion.LookRotation(tip, eyes.right);
        Vector3 at = Palm(eyes) + tip * Mathf.Max(0f, half - .05f);
        if (step == Step.Throwing && swung && !released && home != null && home.releaseTime > home.swingTime)
        {
            float share = Mathf.Clamp01((clock - home.swingTime) / (home.releaseTime - home.swingTime));
            share *= share;
            at = Vector3.Lerp(at, eyes.position + eyes.rotation * LaunchOffset, share);
            turn = Quaternion.Slerp(turn, ThrowingKnives.LaunchTurn(eyes.forward), share);
        }
        held.SetPositionAndRotation(at, turn);
    }

    // The off hand's palm, on the body's own left hand; with no body, low left in the view.
    private Vector3 Palm(Transform eyes)
    {
        if (body == null) body = GetComponent<PlayerBody>();
        var animator = body != null ? body.BodyAnimator : null;
        var hand = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        if (hand == null) return eyes.position + eyes.rotation * BodilessHold;
        var middle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        return middle != null ? Vector3.Lerp(hand.position, middle.position, .6f) : hand.position;
    }

    // ---- Sound and the count ----

    // This PC's own player's knife sounds, flat as their gun's are.
    private void PlayOwn(AudioClip[] clips, float level)
    {
        var clip = Library != null ? Library.Pick(clips) : null;
        if (clip == null) return;
        if (own == null)
        {
            // On a child of its own: things that look for a source on the player find theirs, not this.
            var host = new GameObject("Knife Audio");
            host.transform.SetParent(transform, false);
            own = host.AddComponent<AudioSource>();
            own.playOnAwake = false;
            own.spatialBlend = 0f;
            own.dopplerLevel = 0f;
            GameAudio.Bind(own, AudioChannel.Combat);
        }
        own.PlayOneShot(clip, Mathf.Clamp01(level * (Home != null ? Home.volume : 1f)));
    }

    private void ShowCount()
    {
        float now = Time.unscaledTime;
        bool refused = now - refusedAt < CountShown;
        float since = now - Mathf.Max(countAt, refusedAt);
        bool shown = step != Step.Idle || since < CountShown + CountFade;
        if (!shown && countHost == null) return;
        BuildCount();
        countGroup.alpha = step != Step.Idle ? 1f : 1f - Mathf.Clamp01((since - CountShown) / CountFade);
        string text = refused && step == Step.Idle ? Texts.Get("knife.none") : Texts.Format("knife.count", Count, Most);
        if (countLabel.text != text) countLabel.text = text;
        countLabel.color = Count <= 0 ? KitStyle.Red : KitStyle.Ice;
    }

    // Left of the crosshair and a little under it, across from where the [F] prompts stand.
    private void BuildCount()
    {
        if (countHost != null) return;
        countHost = new GameObject("Knife Count", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        FeelUI.SetupOverlay(countHost, 84);
        countGroup = countHost.GetComponent<CanvasGroup>();
        countGroup.interactable = countGroup.blocksRaycasts = false;
        countLabel = FeelUI.ScoreLabel("Count", countHost.transform, 24f, TextAlignmentOptions.Right);
        countLabel.rectTransform.sizeDelta = new Vector2(240f, 34f);
        countLabel.rectTransform.anchoredPosition = new Vector2(-170f, -46f);
        countLabel.raycastTarget = false;
    }

    /// <summary>A knife's outline for the pickup list (PickupFeedHUD), drawn once: grip, guard and blade, corner to corner.</summary>
    internal static Texture2D Icon
    {
        get
        {
            if (icon != null) return icon;
            const int Size = 64;
            icon = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                { name = "Knife Icon", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float along = (x + y) / (2f * (Size - 1)), across = Mathf.Abs(x - y) / (float)(Size - 1);
                float half = along < .36f ? .07f : along < .41f ? .15f : Mathf.Lerp(.1f, 0f, (along - .41f) / .55f);
                float edge = along < .05f || along > .96f ? 0f : Mathf.Clamp01((half - across) * Size * .5f);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(edge * 255f));
            }
            icon.SetPixels32(pixels);
            icon.Apply(false, true);
            return icon;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// This PC's player's hands on loose physics props (the user, 2026-11-21: "F to pick up and left click to throw. Let the
/// asset cook."): F on a prop looked at, light and small enough, lifts it in both hands; carried, the left button throws
/// it along the aim and F puts it down in front. It is the fuel can's carry for every prop (CarryHold: where it sits, how a
/// throw leaves the hands), so a thrown prop flies as its mass says, clatters as Impact CFX makes it (CollisionEffects),
/// and a hard hit on a zombie is the thrower's blow (PropStrike: the breaking block on a kill). Carried, it fills both
/// hands (PlayerWeaponSlots.HoldHandsFull, the hands' fact carry.can): no gun until it is down. Going down or dying puts it
/// down. F is InteractKey's: rows prop.pick and prop.hand. Every PC sees it carried and thrown (PropNet). Not the
/// campaign's fuel cans, whose carry is the run's (CampaignCans). Numbers: Tables/Maps/PropCarry.csv.
/// </summary>
[DisallowMultipleComponent]
public sealed class PropCarry : MonoBehaviour
{
    public const string PickOffer = "prop.pick", HandOffer = "prop.hand";
    // How wide the look for a prop is (m): a thin ray misses a bottle's neck.
    private const float LookRadius = .08f;
    private readonly RaycastHit[] hits = new RaycastHit[12];
    private PhysicsProp held, looked;
    // The prompt: on the prop looked at, or under the crosshair while one is carried.
    private CanvasGroup group;
    private RectTransform root, plate;
    private TextMeshProUGUI words;
    private Object shownFor;
    private float appearance;

    private void Update()
    {
        var mine = PlayerRoster.Local;
        bool able = mine != null && mine.IsInitialized && !mine.IsDead && mine.DamageReceiver != null && !mine.DamageReceiver.IsDowned;
        var slots = mine != null && mine.TryGetComponent(out PlayerWeaponSlots own) ? own : null;
        var view = mine != null && mine.TryGetComponent(out PlayerBody body) ? body.View : null;
        // Taken from the hands elsewhere: another PC's newer claim on it (PropNet), or it is gone.
        if (held != null && held.Holder != mine) held = null;
        if (held != null && !able) PutDown(mine);
        looked = null;
        if (held != null)
        {
            InteractKey.Offer(HandOffer, target: held);
            if (InteractKey.Pressed(HandOffer)) PutDown(mine);
            else if (InputFocus.TakesPlayInput() && KeyBindings.Pressed("Combat/Fire")) Throw(mine, view);
        }
        // Hands already full of something else (a fuel can) lift nothing more.
        else if (able && view != null && (slots == null || !slots.HandsFull))
        {
            looked = Looked(mine, view);
            if (looked != null)
            {
                InteractKey.Offer(PickOffer, target: looked);
                if (InteractKey.Pressed(PickOffer)) PickUp(mine, looked);
            }
        }
        if (slots != null) slots.HoldHandsFull(this, held != null);
    }

    private void LateUpdate()
    {
        var mine = PlayerRoster.Local;
        if (held != null && mine != null)
        {
            var view = mine.TryGetComponent(out PlayerBody body) ? body.View : null;
            CarryHold.Place(held.transform, mine, mine, view, held.HalfSize);
        }
        ShowPrompt();
    }

    // The nearest thing straight ahead within reach, if it is a prop that can be lifted: something else in front hides it.
    private PhysicsProp Looked(PlayerMovement mine, Transform view)
    {
        float reach = CarryHold.Number("Carry", "reach", 2.2f);
        int count = Physics.SphereCastNonAlloc(view.position, LookRadius, view.forward, hits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        Collider first = null;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(mine.transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            first = hit.collider;
        }
        var prop = PhysicsProp.Of(first);
        return Liftable(prop) ? prop : null;
    }

    // Loose, nobody's, the run's own cans aside, and no heavier or bigger than two hands lift.
    private static bool Liftable(PhysicsProp prop)
        => prop != null && !prop.OwnCarry && !prop.FixedBody && prop.Holder == null && prop.isActiveAndEnabled
            && prop.Body.mass <= CarryHold.Number("Carry", "maxMass", 25f) && prop.HalfSize * 2f <= CarryHold.Number("Carry", "maxSize", 1.6f);

    private void PickUp(PlayerMovement mine, PhysicsProp prop)
    {
        held = prop;
        PropNet.Hold(prop, mine);
    }

    // Down in front of the feet, on whatever is there, at rest.
    private void PutDown(PlayerMovement mine)
    {
        var prop = held;
        held = null;
        if (prop == null) return;
        if (mine == null) { PropNet.Release(prop, prop.transform.position, prop.transform.rotation, Vector3.zero, Vector3.zero); return; }
        Vector3 ahead = mine.transform.position + mine.transform.forward * (.7f + prop.HalfSize);
        Vector3 at = Physics.Raycast(ahead + Vector3.up * 1.2f, Vector3.down, out RaycastHit floor, 4f, Physics.DefaultRaycastLayers & ~(1 << 31),
            QueryTriggerInteraction.Ignore) ? floor.point + Vector3.up * (prop.HalfSize + .02f) : ahead + Vector3.up * .5f;
        PropNet.Release(prop, at, Quaternion.Euler(0f, mine.transform.eulerAngles.y, 0f), Vector3.zero, Vector3.zero);
    }

    // Out of the hands along the aim, tumbling, as fast as its mass lets the arms send it; if something stands closer than
    // the hands' reach, it starts just short of it rather than inside it.
    private void Throw(PlayerMovement mine, Transform view)
    {
        var prop = held;
        held = null;
        if (prop == null) return;
        Vector3 aim = view != null ? view.forward : mine.transform.forward;
        Vector3 from = CarryHold.ThrowFrom(mine, aim, prop.HalfSize);
        Vector3 eye = view != null ? view.position : mine.transform.position + Vector3.up * 1.6f;
        Vector3 toFrom = from - eye;
        if (Physics.SphereCast(eye, Mathf.Min(.15f, prop.HalfSize), toFrom.normalized, out RaycastHit wall, toFrom.magnitude,
                Physics.DefaultRaycastLayers & ~(1 << 31), QueryTriggerInteraction.Ignore))
            from = eye + toFrom.normalized * Mathf.Max(0f, wall.distance - .05f);
        var turn = Quaternion.LookRotation(Vector3.ProjectOnPlane(aim, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(aim, Vector3.up) : mine.transform.forward);
        PropNet.Release(prop, from, turn, CarryHold.ThrowVelocity(prop.Body.mass, aim), Random.onUnitSphere * CarryHold.ThrowSpin);
        prop.SetAttacker(mine.gameObject);
    }

    private void OnDisable()
    {
        if (held != null) PutDown(PlayerRoster.Local);
        if (group != null) group.alpha = 0f;
    }

    // ---- The prompt ----

    // "[F] 들기" pinned to the prop looked at, or "[button] 던지기 · [F] 내려놓기" under the crosshair while one is carried;
    // only while F is ours to show (InteractKey.Shows).
    private void ShowPrompt()
    {
        string text = null;
        Vector3 screen = default;
        bool pinned = false;
        var camera = Camera.main;
        if (held != null && InteractKey.Shows(HandOffer))
            text = Texts.Format("hud.prop.held", Shown(KeyBindings.Name("Combat/Fire"), "LMB"), Shown(KeyBindings.Name("Player/Interact"), "F"));
        else if (looked != null && camera != null && InteractKey.Shows(PickOffer))
        {
            screen = camera.WorldToScreenPoint(looked.transform.position + Vector3.up * (looked.HalfSize + .15f));
            if (screen.z > 0f) { text = Texts.Format("hud.prop.pick", Shown(KeyBindings.Name("Player/Interact"), "F")); pinned = true; }
        }
        Object about = held != null ? held : looked;
        if (text == null && group == null) return;
        EnsurePanel();
        float dt = Time.unscaledDeltaTime;
        if (text != null && about != shownFor) appearance = 0f;
        shownFor = text != null ? about : null;
        appearance += dt;
        group.alpha = Mathf.MoveTowards(group.alpha, text != null ? 1f : 0f, dt * 12f);
        if (text != null)
        {
            words.text = text;
            if (pinned && RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local)) plate.anchoredPosition = local;
            else plate.anchoredPosition = new Vector2(0f, -150f);
        }
        float t = Mathf.Clamp01(appearance / .2f);
        plate.localScale = Vector3.one * Mathf.Lerp(1.2f, 1f, 1f - Mathf.Pow(1f - t, 3f));
        group.gameObject.SetActive(group.alpha > 0f);
    }

    // A key's name as the player set it (KeyBindings.Name: the first key), or the default's when none is set.
    private static string Shown(string key, string fallback) => string.IsNullOrEmpty(key) ? fallback : key;

    // Drawn as the ladder's and the execution's prompts are (ExecutionPromptBackdrop).
    private void EnsurePanel()
    {
        if (group != null) return;
        var canvasObject = new GameObject("Prop prompt HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 79;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        group = canvasObject.GetComponent<CanvasGroup>(); group.alpha = 0f; group.interactable = group.blocksRaycasts = false;
        root = canvasObject.GetComponent<RectTransform>();
        plate = new GameObject("Prop prompt", typeof(RectTransform), typeof(ExecutionPromptBackdrop)).GetComponent<RectTransform>();
        plate.SetParent(root, false); plate.sizeDelta = new Vector2(360f, 64f);
        plate.anchorMin = plate.anchorMax = new Vector2(.5f, .5f);
        plate.GetComponent<ExecutionPromptBackdrop>().raycastTarget = false;
        words = new GameObject("Prop prompt text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        words.transform.SetParent(plate, false); words.rectTransform.anchorMin = Vector2.zero; words.rectTransform.anchorMax = Vector2.one;
        words.rectTransform.offsetMin = words.rectTransform.offsetMax = Vector2.zero;
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null && settings.promptFont != null) words.font = settings.promptFont;
        words.fontSize = 22f; words.alignment = TextAlignmentOptions.Center;
        words.color = new Color(1f, .94f, .9f); words.raycastTarget = false;
        words.fontStyle = FontStyles.Bold;
        canvasObject.SetActive(false);
    }
}

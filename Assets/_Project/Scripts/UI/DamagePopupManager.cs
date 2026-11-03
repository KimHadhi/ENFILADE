using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Damage numbers for enemies and practice targets. Created on the first hit during Play; no scene setup.
/// The player picks the style in Settings (데미지 숫자): one running total per target (default), one number per hit, or
/// none. Numbers stand beside the target's body on the side away from the crosshair and grow away from it
/// (DamageNumberAnchor), so neither the aim point nor the target disappears under its own hits; a target's numbers
/// stack upward in a short column. A finishing blow holds its number where the target stood, even if the target is
/// destroyed. Look and motion: Resources/Combat/Feedback (Damage popups); placement: Hud.csv's Damage row.
/// </summary>
// Runs after CameraViewPunch (10000) so numbers stay on their targets in the kicked view.
[DisallowMultipleComponent, DefaultExecutionOrder(10010)]
public sealed class DamagePopupManager : MonoBehaviour
{
    private const int MaxNumbers = 48, Prewarm = 12;
    private const float ColumnGap = 2f;
    private static DamagePopupManager instance;
    public static DamagePopupManager Instance => instance;
    public int ActiveCount { get; private set; }

    [SerializeField] private DamageNumberLayout layout = new DamageNumberLayout();

    private RectTransform canvasRect;
    private readonly List<Group> groups = new List<Group>();
    private readonly Stack<Group> spareGroups = new Stack<Group>();
    private readonly Stack<DamageNumber> pool = new Stack<DamageNumber>();

    // One target's numbers, oldest first. Damage the player did not deal keeps a group of its own.
    private sealed class Group
    {
        public Component target;
        public bool environmental;
        public readonly DamageNumberAnchor anchor = new DamageNumberAnchor();
        public readonly List<DamageNumber> numbers = new List<DamageNumber>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
    }

    private static void OnDamaged(CombatDamageReport report)
    {
        if (!Application.isPlaying || report.Target == null || report.ByOtherPlayer) return;
        if (GameSettings.Current.damageNumbers == DamageNumberMode.Off) return;
        Prepare();
        instance.Show(report);
    }

    public static void Prepare()
    {
        if (!Application.isPlaying || instance != null) return;
        var root = new GameObject("Damage Popups", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        instance = root.AddComponent<DamagePopupManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelAssets.GroupRuntimeRoot(gameObject, "Screen");
        var canvas = GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
        var scaler = GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = .5f;
        canvasRect = (RectTransform)transform;
        GameTables.BindNested(HudCamera.TableName, "Damage", this, layout);
        // Built up front: the first hits of a fight must not pay for creating text objects.
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        for (int i = 0; i < Prewarm; i++) pool.Push(new DamageNumber(transform, settings));
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    public void Show(CombatDamageReport report)
    {
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        var hit = report.Hit;
        bool stacked = GameSettings.Current.damageNumbers != DamageNumberMode.Each;
        // Anything the player did not deal (enemy friendly fire, falls, hazards) reads as grey environment damage.
        bool environmental = !IsFromPlayer(hit);
        bool kill = report.IsKillingBlow;
        bool critical = report.IsCritical && !environmental, counter = hit.IsCounter && !environmental;
        float amount = Mathf.Max(1, Mathf.RoundToInt(hit.Amount));

        var group = Find(report.Target, environmental) ?? Open(report.Target, environmental);
        if (!kill) group.anchor.Thaw();
        var numbers = group.numbers;
        var newest = numbers.Count > 0 ? numbers[numbers.Count - 1] : null;
        // A running total takes every hit until it leaves; per-hit numbers still fold one shot's pellets together.
        if (newest != null && !newest.Leaving && !newest.Killed && (stacked || newest.SinceHit <= layout.sameShot))
            newest.Add(amount, critical, counter, kill, layout, settings);
        else
        {
            var number = Rent();
            number.Begin(amount, environmental, critical, counter, kill, stacked, layout, settings);
            numbers.Add(number);
            ActiveCount++;
            if (!stacked) TrimColumn(numbers);
        }
        if (kill) group.anchor.Freeze();
        Draw(group, Camera.main, settings); // in place on the frame it appears
    }

    private void LateUpdate()
    {
        var view = Camera.main;
        var settings = CombatFeedbackSettings.InstanceOrDefaults;
        bool off = GameSettings.Current.damageNumbers == DamageNumberMode.Off;
        float dt = Time.unscaledDeltaTime;
        for (int g = groups.Count - 1; g >= 0; g--)
        {
            var numbers = groups[g].numbers;
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                numbers[i].Tick(dt, layout, settings);
                if (off || numbers[i].Finished(settings)) { Return(numbers[i]); numbers.RemoveAt(i); }
            }
            if (numbers.Count == 0) Close(g);
            else Draw(groups[g], view, settings);
        }
    }

    // The newest number stands on the spot; each older one stands on top of the ones after it.
    private void Draw(Group group, Camera view, CombatFeedbackSettings settings)
    {
        var numbers = group.numbers;
        float room = 0f;
        foreach (var number in numbers) room = Mathf.Max(room, number.Width(settings));
        if (!group.anchor.Place(view, canvasRect, layout, room, out Vector2 spot, out float distance))
        {
            foreach (var number in numbers) number.Hide();
            return;
        }
        float far = Mathf.Clamp(settings.popupFullSizeDistance / Mathf.Max(.01f, distance), settings.popupMinimumDistanceScale, 1f);
        float lift = 0f;
        for (int i = numbers.Count - 1; i >= 0; i--)
        {
            numbers[i].SetLift(lift);
            lift += numbers[i].Height(settings) * far + ColumnGap;
        }
        foreach (var number in numbers) number.Draw(spot, group.anchor.Side, far, layout, settings);
    }

    // Per-hit numbers: past the column's room the oldest still standing leave early.
    private void TrimColumn(List<DamageNumber> numbers)
    {
        int standing = 0;
        for (int i = numbers.Count - 1; i >= 0; i--)
            if (!numbers[i].Leaving && ++standing > layout.column) numbers[i].Leave();
    }

    private Group Find(Component target, bool environmental)
    {
        // By reference: a destroyed target still holds its numbers until they finish.
        foreach (var group in groups)
            if (ReferenceEquals(group.target, target) && group.environmental == environmental) return group;
        return null;
    }

    private Group Open(Component target, bool environmental)
    {
        var group = spareGroups.Count > 0 ? spareGroups.Pop() : new Group();
        group.target = target;
        group.environmental = environmental;
        group.anchor.Begin(target);
        groups.Add(group);
        return group;
    }

    private void Close(int index)
    {
        var group = groups[index];
        group.anchor.Release();
        group.target = null;
        groups.RemoveAt(index);
        spareGroups.Push(group);
    }

    private DamageNumber Rent()
    {
        // At the cap the oldest group's oldest number makes room; an emptied group closes on the next update.
        if (ActiveCount >= MaxNumbers)
            foreach (var group in groups)
            {
                if (group.numbers.Count == 0) continue;
                Return(group.numbers[0]);
                group.numbers.RemoveAt(0);
                break;
            }
        return pool.Count > 0 ? pool.Pop() : new DamageNumber(transform, CombatFeedbackSettings.InstanceOrDefaults);
    }

    private void Return(DamageNumber number)
    {
        number.Release();
        pool.Push(number);
        ActiveCount--;
    }

    private static bool IsFromPlayer(DamageHit hit)
        => hit.Kind != DamageKind.Fall && hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() != null;
}

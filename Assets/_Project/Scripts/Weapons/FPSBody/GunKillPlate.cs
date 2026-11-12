using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A small kill counter on the left side of this PC's own player's guns, as a StatTrak module is (user, 2026-11-11, the
/// plan: "할 수 있으면 총에 작은 숫자판"): the kills the gun has made on this PC (GunKills) in glowing figures, set into
/// the receiver's side where the eyes see the gun. It comes up only while the gun is looked over (Y; user, 2026-11-12:
/// "킬스트릭 스탯은 무기 관찰할 때만 뜨게 할 것"), and flashes white there when the look-over announces a rank the gun
/// reached in the fight. Its place is measured once from the gun's own shape (its left surface, part way back from the
/// muzzle). Friends' copies have none: their counts are on their PCs. Numbers: Tables/KillCounter.csv, row Plate.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunKillPlate : MonoBehaviour, ITableBound
{
    public const string TableRow = "Plate";

    [SerializeField, Tooltip("Show the counter on the guns at all (while they are looked over).")]
    private bool show = true;
    [SerializeField, Min(1), Tooltip("Figures shown, zero-padded.")]
    private int digits = 6;
    [SerializeField, Min(.001f), Tooltip("Height of the figures (m).")]
    private float height = .011f;
    [SerializeField, Range(0f, 1f), Tooltip("Where along the gun it sits, from its back end (0) to its muzzle (1).")]
    private float along = .35f;
    [SerializeField, Range(0f, 1f), Tooltip("How high on the gun's side it sits, from its lowest point (0) to its highest (1).")]
    private float rise = .55f;
    [SerializeField, Tooltip("The figures' colour (HDR above 1 glows).")]
    private Color color = new Color(1f, .6f, .18f);
    [SerializeField, Min(0f), Tooltip("How long an announced rank flashes the figures white (s).")]
    private float flashSeconds = .35f;
    [SerializeField, Min(0f), Tooltip("How long the figures take to come up as the look-over begins and to go as it ends (s).")]
    private float fadeSeconds = .15f;

    private FPSGun gun;
    private PlayerMovement owner;
    private WeaponController weapons;
    private TextMeshPro text;
    private int shownKills = -1;
    private float flash, shown;
    private bool placed;

    // The counts are this PC's: only this PC's own player's guns show them (a body built before the network said whose
    // it was may have been given one).
    private bool Mine
    {
        get
        {
            if (owner == null) owner = GetComponentInParent<PlayerMovement>();
            return owner == null || (owner.IsLocal && !owner.IsNetworkDriven);
        }
    }

    // This gun, in the hands and being looked over (Y).
    private bool LookedOver
    {
        get
        {
            if (weapons == null && owner != null) weapons = owner.GetComponentInChildren<WeaponController>();
            return weapons != null && weapons.IsInspecting && weapons.EquippedData == gun.Data;
        }
    }

    /// <summary>Puts one on this PC's own player's gun (FPSBody).</summary>
    internal static void On(FPSGun gun)
    {
        if (gun == null) return;
        if (!gun.TryGetComponent(out GunKillPlate plate))
        {
            plate = gun.gameObject.AddComponent<GunKillPlate>();
            GameTables.Bind(GunKills.TableName, TableRow, plate);
        }
        plate.gun = gun;
    }

    public void OnTableApplied()
    {
        shownKills = -1;
        placed = false;
    }

    private void OnEnable() => GunKills.RankUpAnnounced += OnRankUpAnnounced;
    private void OnDisable()
    {
        GunKills.RankUpAnnounced -= OnRankUpAnnounced;
        // Put away mid look-over, it is gone when the gun comes out again.
        shown = 0f;
        if (text != null) text.gameObject.SetActive(false);
    }

    private void OnRankUpAnnounced(WeaponData data)
    {
        if (gun != null && data == gun.Data) flash = flashSeconds;
    }

    // After the gun's clips and parts have posed it.
    private void LateUpdate()
    {
        if (gun == null || gun.Data == null) return;
        if (!show || !Mine)
        {
            shown = 0f;
            if (text != null && text.gameObject.activeSelf) text.gameObject.SetActive(false);
            return;
        }
        if (text == null) Build();
        // Placed while the gun is at rest in the hands, hidden, so a look-over's turning parts don't measure into it.
        if (!placed) placed = Place();
        bool lookedOver = LookedOver;
        // A rank waiting since the fight is announced as the look-over begins, here too when no ammo display is up to.
        if (lookedOver) GunKills.AnnounceRankUp(gun.Data);
        shown = fadeSeconds > 0f ? Mathf.MoveTowards(shown, lookedOver ? 1f : 0f, Time.deltaTime / fadeSeconds) : lookedOver ? 1f : 0f;
        bool visible = shown > 0f && placed;
        if (text.gameObject.activeSelf != visible) text.gameObject.SetActive(visible);
        if (!visible) { flash = 0f; return; }
        int kills = GunKills.Kills(gun.Data);
        if (kills != shownKills)
        {
            shownKills = kills;
            text.text = Mathf.Min(kills, (int)Mathf.Pow(10f, Mathf.Min(digits, 9)) - 1).ToString(new string('0', digits));
        }
        flash = Mathf.Max(0f, flash - Time.deltaTime);
        var figures = Color.Lerp(color, Color.white * 2f, flashSeconds > 0f ? flash / flashSeconds : 0f);
        figures.a = shown;
        text.color = figures;
    }

    private void Build()
    {
        text = new GameObject("Kill Counter", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
        text.transform.SetParent(transform, false);
        text.gameObject.layer = gameObject.layer;
        text.gameObject.SetActive(false);
        var font = CombatUIAssets.ResolveFont(null);
        if (font != null) text.font = font;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = color;
        if (text.TryGetComponent(out MeshRenderer renderer))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    // On the gun's left surface, along and up its shape, in the gun's own frame (its sight line forward), facing left:
    // measured from its renderers' bounds as it sits at rest under the gun.
    private bool Place()
    {
        var aim = gun.AimPoint;
        if (aim == null) return false;
        var corners = new Vector3[8];
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        bool any = false;
        foreach (var renderer in gun.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || !renderer.enabled || renderer.transform.IsChildOf(text.transform)) continue;
            Bounds local;
            Transform space = renderer.transform;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                local = skinned.localBounds;
                if (skinned.rootBone != null) space = skinned.rootBone;
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) local = filter.sharedMesh.bounds;
            else continue;
            Vector3 c = local.center, e = local.extents;
            for (int i = 0; i < 8; i++)
                corners[i] = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            foreach (var corner in corners)
            {
                Vector3 p = aim.InverseTransformPoint(space.TransformPoint(corner));
                // A part parked far off the sight line (a revolver's speedloader) isn't the gun's side.
                if (new Vector2(p.x, p.y).sqrMagnitude > .16f) continue;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                any = true;
            }
        }
        if (!any) return false;
        Vector3 spot = new Vector3(min.x - .0015f, Mathf.Lerp(min.y, max.y, rise), Mathf.Lerp(min.z, max.z, along));
        // A TextMeshPro reads from the side its forward points away from: forward into the gun, up the gun's up.
        Vector3 world = aim.TransformPoint(spot);
        Quaternion facing = aim.rotation * Quaternion.LookRotation(Vector3.right, Vector3.up);
        text.transform.SetPositionAndRotation(world, facing);
        float scale = Mathf.Max(.0001f, Mathf.Abs(transform.lossyScale.x));
        text.fontSize = 10f;
        // fontSize 10 is about a unit tall in world space: scale it to the figures' height on this gun.
        text.transform.localScale = Vector3.one * (height / scale);
        text.rectTransform.sizeDelta = new Vector2(digits * .7f + 1f, 1.4f);
        return true;
    }
}

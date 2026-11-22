using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The step dodge seen in the world, on every PC: dust off the floor as the foot kicks off and as the slide stops (bought,
/// DAVFX: ImpactEffects.csv DodgeKick and DodgeStop), and the character's own afterimage, faintly see-through, where it
/// began. An evade (a blow let through, PlayerPerfectDefense) turns the afterimage red and holds it a little longer on every
/// PC: Succeed here on the evader's PC, ShowEvaded on the others' copies of them (PlayerNetworkCombat), so friends see what
/// the evader sees (the user's rule, "내가 보는 화면이 진실"). A Q/E slip has no dash and nothing here.
/// The user, 2026-11-22: "이펙트가 구림", then "먼지 + 잔상". Until 0.9.334 it drew a Roblox dash video frame by frame (their
/// pick on 2026-11-12): kinked white lines, long streaks, a white puff and smoke, wind round the body, an echo of the
/// afterimage, the same lines across the screen in first person (DodgeFlash) and a field-of-view kick; all of it went.
/// The afterimage is the body's skinned meshes baked at that moment and drawn with one shared shader (DodgeGhost) handed
/// each renderer's own main texture and colour per draw, so any outfit or modded character needs nothing of its own. In
/// first person the camera is in the body as it is baked, so it shows once the body has left it (ghostFrom). Real time, so
/// the evade's slow motion doesn't hold it. Numbers: Tables/Player.csv, row DodgeLook.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Cloth + 1)]
public sealed class DodgeDash : MonoBehaviour
{
    [SerializeField, Range(0f, 1f), Tooltip("How solid a fresh afterimage is (the character itself, see-through; 1 would be solid).")]
    private float ghostAlpha = .5f;
    [SerializeField, Min(.05f), Tooltip("How long it takes to go (real s).")]
    private float ghostSeconds = .4f;
    [SerializeField, Min(0f), Tooltip("In first person it shows once the body has gone this far from it (m), so the camera is out of it.")]
    private float ghostFrom = .6f;
    [SerializeField, Min(1000), Tooltip("Most vertices baked into one: the biggest meshes of a heavy outfit are left out past it.")]
    private int ghostVertexBudget = 150000;
    [SerializeField, Tooltip("An evade (a blow let through) turns it this colour, on every PC.")]
    private Color evadedTint = new Color(1f, .14f, .12f, 1f);
    [SerializeField, Min(.05f), Tooltip("How long an evade's afterimage takes to go from then (real s).")]
    private float evadedSeconds = .7f;
    [SerializeField, Min(0f), Tooltip("The slide stops, with its dust, this long after the dodge began (s).")]
    private float stopAt = .24f;

    // The ghost shader's properties (Resources/Combat/Shaders/DodgeGhost), set per draw from each renderer's own look.
    private const string GhostShader = "Combat/Shaders/DodgeGhost";
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex"), MainTexStId = Shader.PropertyToID("_MainTex_ST"),
        ColorId = Shader.PropertyToID("_Color"), CutId = Shader.PropertyToID("_Cut"), AlphaId = Shader.PropertyToID("_Alpha");
    // How long a copy holds word of an evade for a dodge that hasn't begun on it yet (real s).
    private const float RedWait = .35f;

    private sealed class Ghost
    {
        public readonly List<Mesh> meshes = new List<Mesh>();
        public readonly List<Matrix4x4> places = new List<Matrix4x4>();
        // Each mesh's sub-mesh looks start at its entry here in looks.
        public readonly List<int> firstLook = new List<int>();
        // Each piece's own look (MaterialLook): the texture, its tiling, the colour its material draws it with, and
        // whether the texture's alpha cuts it.
        public readonly List<MaterialLook> looks = new List<MaterialLook>();
        public int used;
        public Vector3 at;
        public float age, seconds;
        public bool live, seen, red;
    }

    private static Material ghostMaterial;

    private PlayerMovement player;
    private DodgeController dodge;
    // One for a dodge and one for a dodge chained on it while the first's is still fading.
    private readonly Ghost[] ghosts = { new Ghost(), new Ghost() };
    private readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
    private readonly List<Material> materials = new List<Material>();
    // Made on the first draw: Unity refuses one made in a field initializer (the component's constructor).
    private MaterialPropertyBlock block;
    private Ghost latest;
    private Vector3 direction;
    private float since, redUntil;
    private int shown = -1;
    private bool sliding;

    /// <summary>This PC's own player let a blow through a step dodge: PlayerNetworkCombat tells the other PCs (ShowEvaded).</summary>
    public event System.Action Evaded;

    private bool Mine => player != null && player.IsLocal && !player.IsNetworkDriven;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        GameTables.Bind(PlayerMovement.TableName, "DodgeLook", this);
    }

    /// <summary>Another PC's player let a blow through a dodge (EvadedRpc): their copy's afterimage goes red as it did there.</summary>
    public void ShowEvaded() => Flare();

    private void OnSucceeded(DodgeKind kind, int side)
    {
        if (kind != DodgeKind.Step) return;
        Flare();
        Evaded?.Invoke();
    }

    private void Flare()
    {
        if (latest != null && latest.live)
        {
            latest.red = true;
            latest.age = 0f;
            latest.seconds = evadedSeconds;
        }
        // A copy can hear of an evade before its own dodge has begun (the word and the movement come apart): that one
        // begins red.
        else redUntil = Time.unscaledTime + RedWait;
    }

    // This PC's own player seen from its own eyes (first person): the camera is in the body as it is baked.
    private bool ViewInBody()
    {
        if (!Mine) return false;
        var view = Camera.main;
        Transform eyes = PlayerView.EyesOf(player);
        return view == null || eyes == null || (view.transform.position - eyes.position).sqrMagnitude < .36f;
    }

    private void LateUpdate()
    {
        if (player == null || Time.timeScale <= 0f || player.Abilities == null) return;
        // This PC's own dodges are judged here; the others' by word (ShowEvaded).
        if (dodge == null && Mine)
        {
            dodge = player.Abilities.Dodge;
            dodge.Succeeded += OnSucceeded;
        }
        bool dodging = player.CurrentState == MovementState.StepDodge;
        int serial = player.Abilities.DodgeMove.Serial;
        if (dodging && serial != shown) Begin(serial);
        since += Time.deltaTime;
        if (sliding && (since >= stopAt || !dodging))
        {
            sliding = false;
            if (dodging && player.IsGrounded) Dust("DodgeStop", transform.position + direction * .25f, direction * .4f + Vector3.up);
        }
        Draw(Mathf.Min(.05f, Time.unscaledDeltaTime));
    }

    private void Begin(int serial)
    {
        shown = serial;
        since = 0f;
        sliding = true;
        direction = Vector3.ProjectOnPlane(player.Abilities.ActionDirection, Vector3.up);
        if (direction.sqrMagnitude < 1e-4f) direction = -Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        direction.Normalize();
        Dust("DodgeKick", transform.position, Vector3.up - direction * .7f);
        Bake(Free());
        latest.red = Time.unscaledTime < redUntil;
        latest.seconds = latest.red ? evadedSeconds : ghostSeconds;
        redUntil = 0f;
    }

    private static void Dust(string id, Vector3 at, Vector3 up) => ImpactPool.Play(id, at + Vector3.up * .05f, up.normalized);

    private void Draw(float dt)
    {
        var material = GhostMaterial();
        if (material == null) return;
        foreach (var ghost in ghosts)
        {
            if (!ghost.live) continue;
            if (ghost.used > ghost.meshes.Count) { ghost.live = false; continue; }
            if (!ghost.seen && ViewInBody())
            {
                // Out of sight until the camera is out of it; a dodge that went nowhere leaves none.
                if ((transform.position - ghost.at).sqrMagnitude < ghostFrom * ghostFrom)
                {
                    if (player.CurrentState != MovementState.StepDodge) ghost.live = false;
                    continue;
                }
            }
            ghost.seen = true;
            ghost.age += dt;
            float left = 1f - ghost.age / ghost.seconds;
            if (left <= 0f) { ghost.live = false; continue; }
            // One block for every draw: DrawMesh copies it as it is when called.
            block ??= new MaterialPropertyBlock();
            block.SetFloat(AlphaId, ghostAlpha * left * left);
            for (int i = 0; i < ghost.used; i++)
            {
                var mesh = ghost.meshes[i];
                int first = i < ghost.firstLook.Count ? ghost.firstLook[i] : 0;
                for (int part = 0; part < mesh.subMeshCount; part++)
                {
                    int at = Mathf.Min(first + part, ghost.looks.Count - 1);
                    var look = at >= 0 ? ghost.looks[at] : MaterialLook.Plain;
                    block.SetTexture(MainTexId, look.Texture != null ? look.Texture : Texture2D.whiteTexture);
                    block.SetVector(MainTexStId, look.Tiling);
                    block.SetColor(ColorId, ghost.red ? look.Color * evadedTint : look.Color);
                    block.SetFloat(CutId, look.Cut);
                    Graphics.DrawMesh(mesh, ghost.places[i], material, 0, null, part, block, ShadowCastingMode.Off, false);
                }
            }
        }
    }

    // The body as it is this moment, every skinned mesh of it that shows, up to the vertex budget, with each of its
    // sub-meshes' own texture, tiling and colour.
    private void Bake(Ghost ghost)
    {
        latest = ghost;
        ghost.live = ghost.seen = ghost.red = false;
        ghost.used = 0;
        ghost.age = 0f;
        ghost.at = transform.position;
        if (GhostMaterial() == null) return;
        skins.Clear();
        GetComponentsInChildren(false, skins);
        int budget = ghostVertexBudget;
        ghost.firstLook.Clear(); ghost.looks.Clear();
        foreach (var skin in skins)
        {
            var shared = skin.sharedMesh;
            if (!skin.enabled || shared == null || !skin.gameObject.activeInHierarchy) continue;
            int vertices = shared.vertexCount;
            if (vertices > budget) continue;
            budget -= vertices;
            if (ghost.used == ghost.meshes.Count)
            {
                var made = new Mesh { name = "Dodge ghost" };
                made.MarkDynamic();
                ghost.meshes.Add(made); ghost.places.Add(Matrix4x4.identity);
            }
            var mesh = ghost.meshes[ghost.used];
            // Scaled as the renderer is, so it is drawn without its scale again.
            skin.BakeMesh(mesh, true);
            Transform place = skin.transform;
            ghost.places[ghost.used] = Matrix4x4.TRS(place.position, place.rotation, Vector3.one);
            ghost.firstLook.Add(ghost.looks.Count);
            materials.Clear();
            skin.GetSharedMaterials(materials);
            for (int part = 0; part < mesh.subMeshCount; part++)
                ghost.looks.Add(MaterialLook.Of(materials.Count > 0 ? materials[Mathf.Min(part, materials.Count - 1)] : null));
            ghost.used++;
        }
        ghost.live = ghost.used > 0;
    }

    // The older ghost if both are still showing.
    private Ghost Free()
    {
        Ghost oldest = ghosts[0];
        foreach (var ghost in ghosts)
        {
            if (!ghost.live) return ghost;
            if (ghost.age > oldest.age) oldest = ghost;
        }
        return oldest;
    }

    private static Material GhostMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;
        var shader = Resources.Load<Shader>(GhostShader);
        if (shader == null) return null;
        return ghostMaterial = new Material(shader) { name = "Dodge Ghost (Runtime)", hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnDisable()
    {
        sliding = false;
        foreach (var ghost in ghosts) ghost.live = false;
    }

    private void OnDestroy()
    {
        if (dodge != null) dodge.Succeeded -= OnSucceeded;
        foreach (var ghost in ghosts) foreach (var mesh in ghost.meshes) if (mesh != null) Destroy(mesh);
    }
}

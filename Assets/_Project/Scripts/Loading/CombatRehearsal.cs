using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// What a fight draws for the first time, drawn once in front of the player's camera while the loading screen covers it
/// (SceneWarmup): the shot and hit particles, every game shader (Resources/Combat/Shaders: tracer, muzzle flame, kill-feed
/// flair, dodge ghost ...), gore's gut pieces and blood decals, every zombie look (skinned, as a zombie is), the class
/// throwables and every gun's pickup model. Drawn through the real camera in the real scene, each is drawn with the
/// scene's own lights, fog, shadows and screen-space passes, so the shader variant the fight will need is the one compiled
/// here (in the editor) and its pipeline state the one made here (in a build). Before (to 0.8.194) the first zombie, the
/// first gore and the first pickup made theirs in the fight: the 18:53 run of 2026-11-12 spent 9.8 s in
/// Shader.EditorCompileVariant on the chapter's first frame and 3.6 s more coming back to the hub.
/// Everything stands small on a grid that follows the camera, a few metres ahead, with colliders off; it is built within a
/// time budget a frame and thrown away when the warm-up ends. The zombie looks also stock ragdoll dolls for their rig
/// (ZombieCorpse.Stock), so the first death finds one ready. The looks are the pared ones zombies now wear
/// (ZombieLookTemplates), so the grid no longer holds 40 copies of the whole Synty pack (6275 objects in the 22:13 run).
/// The face lens (Face) does what the callout feed's camera does on the first callout of a run (the brute's leg shout):
/// it draws this PC's own head, which first person keeps to shadows only, from the front into a small texture with the
/// feed's camera settings, so the face's materials are compiled and their states made here.
/// </summary>
[DisallowMultipleComponent]
internal sealed class CombatRehearsal : MonoBehaviour
{
    private const string ShaderFolder = "Combat/Shaders", FeedTileShader = "UI/Custom/SquarePortrait";
    // The callout feed's picture and lens, as CalloutFeedHUD makes them (Faces.csv Feed.size 256, fov 34, near 0.05).
    private const int FaceSize = 256;
    private const float FaceFov = 34f, FaceNear = .05f, FaceFar = 40f, FaceDistance = .85f;
    // The grid's lowest row, counted from the middle of the view, so most of it stands where the view is widest.
    private const int LowestRow = -3;

    private Camera eye;
    private float distance, cell;
    private int columns, layer, placed;
    private readonly List<Animator> rigs = new List<Animator>();
    private readonly HashSet<Avatar> stood = new HashSet<Avatar>();
    private readonly List<Object> owned = new List<Object>();
    private readonly List<Renderer> own = new List<Renderer>(), shownOwn = new List<Renderer>();
    private Camera faceLens;
    private Transform faceHead;
    private PlayerMovement faceOf;
    private RenderTexture faceTexture;

    /// <summary>How many things stand on the grid.</summary>
    public int Placed => placed;

    /// <summary>A full-size rig for each zombie avatar, for stocking ragdoll dolls (ZombieCorpse.Stock).</summary>
    public IReadOnlyList<Animator> Rigs => rigs;

    /// <summary>Opens the grid <paramref name="distance"/> m ahead of <paramref name="eye"/>, cells <paramref name="cell"/> m wide.</summary>
    public static CombatRehearsal Open(Camera eye, float distance, float cell, int columns)
    {
        if (eye == null) return null;
        var rehearsal = new GameObject("Combat Rehearsal").AddComponent<CombatRehearsal>();
        rehearsal.eye = eye;
        rehearsal.distance = Mathf.Max(.5f, distance);
        rehearsal.cell = Mathf.Max(.05f, cell);
        rehearsal.columns = Mathf.Max(1, columns);
        rehearsal.layer = SeenLayer(eye.cullingMask);
        rehearsal.Follow();
        return rehearsal;
    }

    /// <summary>
    /// Puts everything on the grid, spending at most <paramref name="budgetMs"/> of each frame;
    /// <paramref name="told"/> hears the share done.
    /// </summary>
    public IEnumerator Build(float budgetMs, Action<float> told)
    {
        var steps = Steps();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < steps.Count; i++)
        {
            try { steps[i](); }
            catch (Exception error) { Debug.LogException(error); }
            told?.Invoke((i + 1) / (float)steps.Count);
            if (clock.Elapsed.TotalMilliseconds < budgetMs) continue;
            yield return null;
            clock.Restart();
        }
    }

    private List<Action> Steps()
    {
        var steps = new List<Action>();
        foreach (var material in FeelAssets.CombatMaterials()) steps.Add(() => Quad(material));
        foreach (var shader in Resources.LoadAll<Shader>(ShaderFolder))
            if (shader != null && shader.isSupported) steps.Add(() => Quad(Own(new Material(shader) { name = shader.name + " (Rehearsal)" })));
        // The grey of a frag's thrown grit, and of a throwable drawn from plain shapes.
        steps.Add(() => Quad(Throwables.Look(Color.gray)));
        // The callout feed's picture tile (CalloutFeedHUD), a UI shader found by name.
        var tile = Shader.Find(FeedTileShader);
        if (tile != null && tile.isSupported) steps.Add(() => Quad(Own(new Material(tile) { name = FeedTileShader + " (Rehearsal)" })));

        var gore = GoreLibrary.Instance;
        if (gore != null)
        {
            int guts = gore.guts != null ? gore.guts.Length : 0;
            for (int i = 0; i < guts; i++)
            {
                int index = i;
                steps.Add(() => { if (gore.Gut(index, out var mesh, out var material)) Piece(mesh, material); });
            }
            if (gore.splats != null)
                foreach (var splat in gore.splats) steps.Add(() => Decal(splat));
        }

        var zombies = ZombieLooks.Instance;
        if (zombies != null && zombies.characters != null)
            foreach (var character in zombies.characters) steps.Add(() => Look(ZombieLookTemplates.Of(character), true));
        // The elites' own bodies, which the list above leaves out (EliteLooks: the brute's riot cop, the reaper's
        // firefighter and so on), and what they hold (the reaper's axe).
        var elites = EliteLooks.Instance;
        if (elites != null && elites.looks != null)
            foreach (var elite in elites.looks)
            {
                var eliteBody = elite.character;
                if (eliteBody != null) steps.Add(() => Look(ZombieLookTemplates.Of(eliteBody), true));
                var eliteHand = elite.handProp;
                if (eliteHand != null) steps.Add(() => Look(eliteHand, false));
            }

        var throwables = Resources.Load<GrenadeLibrary>(GrenadeLibrary.ResourcePath);
        if (throwables != null)
            foreach (var model in new[] { throwables.knife, throwables.frag, throwables.smoke, throwables.flash, throwables.mist })
                steps.Add(() => Look(model, false));

        var arms = FPSBodyAssets.Instance;
        if (arms != null && arms.guns != null)
        {
            var seen = new HashSet<WeaponData>();
            foreach (var entry in arms.guns)
                if (entry.weapon != null && seen.Add(entry.weapon))
                {
                    var weapon = entry.weapon;
                    steps.Add(() => Gun(weapon));
                }
        }
        return steps;
    }

    // The first layer the camera draws, Default when it does.
    private static int SeenLayer(int mask)
    {
        if ((mask & 1) != 0) return 0;
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0) return i;
        return 0;
    }

    private Material Own(Material material)
    {
        material.hideFlags = HideFlags.HideAndDontSave;
        owned.Add(material);
        return material;
    }

    private GameObject Item(string name)
    {
        var item = new GameObject(name) { layer = layer };
        item.transform.SetParent(transform, false);
        int column = placed % columns, row = placed / columns + LowestRow;
        item.transform.localPosition = new Vector3((column - (columns - 1) * .5f) * cell, row * cell, 0f);
        placed++;
        return item;
    }

    private void Quad(Material material)
    {
        if (material == null) return;
        Piece(FeelAssets.Quad, material);
    }

    private void Piece(Mesh mesh, Material material)
    {
        if (mesh == null || material == null) return;
        var item = Item(material.name);
        item.AddComponent<MeshFilter>().sharedMesh = mesh;
        item.AddComponent<MeshRenderer>().sharedMaterial = material;
        Fit(item.transform);
    }

    // A projected blood decal lands on a small grey wall just behind it, as it would on the map.
    private void Decal(Material material)
    {
        if (material == null) return;
        var item = Item(material.name);
        var wall = new GameObject("Wall") { layer = layer };
        wall.transform.SetParent(item.transform, false);
        wall.transform.localPosition = new Vector3(0f, 0f, cell * .2f);
        wall.transform.localScale = Vector3.one * cell * .9f;
        wall.AddComponent<MeshFilter>().sharedMesh = FeelAssets.Quad;
        wall.AddComponent<MeshRenderer>().sharedMaterial = Throwables.Look(Color.gray);
        // Its box reaches from just in front of the wall to just behind it, projecting away from the camera.
        var projector = item.AddComponent<DecalProjector>();
        projector.material = material;
        projector.size = new Vector3(cell * .9f, cell * .9f, cell * .6f);
        projector.pivot = new Vector3(0f, 0f, cell * .3f);
    }

    private void Look(GameObject prefab, bool zombie)
    {
        if (prefab == null) return;
        var item = Item(prefab.name);
        var copy = Instantiate(prefab, item.transform, false);
        Calm(copy);
        Fit(item.transform);
        if (!zombie) return;
        var animator = copy.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid) Stand(prefab, animator.avatar);
    }

    // One full-size copy per zombie rig, far under the camera where nothing draws it, worn as ZombieBody wears a look (the
    // pack's switched-off characters dropped, at its own size): the rig the ragdoll dolls are made from. A doll keeps the
    // size and bones of the rig it was made from, so the shrunken copy on the grid would make dolls too small for a real
    // zombie.
    private void Stand(GameObject prefab, Avatar avatar)
    {
        if (!stood.Add(avatar)) return;
        var under = (eye != null ? eye.transform.position : transform.position) + Vector3.down * 5000f;
        var stand = Instantiate(prefab, under, Quaternion.identity);
        stand.transform.localScale = Vector3.one;
        foreach (var part in stand.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (!part.gameObject.activeSelf) Destroy(part.gameObject);
        Calm(stand);
        owned.Add(stand);
        var animator = stand.GetComponentInChildren<Animator>();
        if (animator != null) rigs.Add(animator);
    }

    private void Gun(WeaponData weapon)
    {
        var item = Item(weapon.name);
        var model = PickupProps.Make(FieldPickups.Kind.Gun, weapon, item.transform);
        if (model == null) return;
        Calm(model);
        Fit(item.transform);
    }

    // Seen by the camera, touching nothing: no collider meets the player or a shot, no body falls.
    private void Calm(GameObject copy)
    {
        foreach (var part in copy.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
        foreach (var collider in copy.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in copy.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
        foreach (var skin in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = true;
    }

    // Shrinks the item's look to most of its cell and stands its middle on the cell's middle.
    private void Fit(Transform item)
    {
        var renderers = item.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (size < 1e-4f) return;
        float factor = cell * .85f / size;
        Vector3 pivot = item.position;
        item.localScale *= factor;
        item.position += (pivot - bounds.center) * factor;
    }

    /// <summary>
    /// Points a lens like the callout feed's at <paramref name="mine"/>'s face and keeps drawing it every frame the rehearsal
    /// stands. False when there is no body with a head to draw.
    /// </summary>
    public bool Face(PlayerMovement mine)
    {
        if (faceLens != null || mine == null || !mine.TryGetComponent(out PlayerBody body) || body.BodyAnimator == null) return false;
        faceHead = body.BodyAnimator.isHuman ? body.BodyAnimator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (faceHead == null) return false;
        faceOf = mine;
        faceTexture = new RenderTexture(FaceSize, FaceSize, 16, RenderTextureFormat.ARGB32)
        {
            name = "Face Rehearsal", antiAliasing = 1, useMipMap = false, hideFlags = HideFlags.HideAndDontSave
        };
        faceTexture.Create();
        var host = new GameObject("Face Lens");
        host.transform.SetParent(transform, false);
        faceLens = host.AddComponent<Camera>();
        faceLens.allowHDR = false;
        faceLens.allowMSAA = false;
        faceLens.useOcclusionCulling = true;
        faceLens.fieldOfView = FaceFov;
        faceLens.nearClipPlane = FaceNear;
        faceLens.farClipPlane = FaceFar;
        faceLens.targetTexture = faceTexture;
        if (eye != null)
        {
            int ui = LayerMask.NameToLayer("UI");
            faceLens.cullingMask = ui >= 0 ? eye.cullingMask & ~(1 << ui) : eye.cullingMask;
            faceLens.clearFlags = eye.clearFlags;
            faceLens.backgroundColor = eye.backgroundColor;
            faceLens.depth = eye.depth - 2f;
        }
        var data = faceLens.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresColorOption = CameraOverrideOption.On;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.renderShadows = false;
        data.stopNaN = false;
        data.dithering = false;
        own.Clear();
        mine.GetComponentsInChildren(true, own);
        RenderPipelineManager.beginCameraRendering += BeforeRender;
        RenderPipelineManager.endCameraRendering += AfterRender;
        AimFace();
        return true;
    }

    // In front of the face, a little aside and above, looking at it: the feed's framing.
    private void AimFace()
    {
        if (faceLens == null || faceHead == null || faceOf == null) return;
        var root = faceOf.transform;
        Vector3 face = faceHead.position, at = face + root.forward * FaceDistance + root.right * .12f + root.up * .04f;
        faceLens.transform.SetPositionAndRotation(at, Quaternion.LookRotation(face - at, root.up));
    }

    // This PC's head and what is worn over the face are shadows only in first person; on for the face lens alone, as the
    // feed does it.
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != faceLens) return;
        shownOwn.Clear();
        foreach (var renderer in own)
        {
            if (renderer == null || renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            shownOwn.Add(renderer);
        }
    }

    private void AfterRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != faceLens) return;
        HideOwnAgain();
    }

    private void HideOwnAgain()
    {
        foreach (var renderer in shownOwn)
            if (renderer != null) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        shownOwn.Clear();
    }

    private void LateUpdate()
    {
        Follow();
        AimFace();
    }

    // In front of the camera wherever it turns while the screen is up.
    private void Follow()
    {
        if (eye == null) return;
        var view = eye.transform;
        transform.SetPositionAndRotation(view.position + view.forward * distance, view.rotation);
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        RenderPipelineManager.endCameraRendering -= AfterRender;
        HideOwnAgain();
        if (faceLens != null) faceLens.targetTexture = null;
        if (faceTexture != null)
        {
            faceTexture.Release();
            Destroy(faceTexture);
        }
        foreach (var made in owned)
            if (made != null) Destroy(made);
        owned.Clear();
    }
}

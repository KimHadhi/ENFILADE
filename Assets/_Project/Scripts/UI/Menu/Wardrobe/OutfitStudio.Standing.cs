using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// OutfitStudio: Standing a piece. One part of the class; the rest is in OutfitStudio.cs and the files beside it.
public sealed partial class OutfitStudio
{
    /// <summary>
    /// Stands a piece alone: an item in a colour set for this character, or (no item) the character's own piece of
    /// <paramref name="slot"/> in one of its colour sets. False when there is nothing of it to photograph.
    /// </summary>
    public bool Stand(PlayerBodyProfile.Skin skin, OutfitItem item, string colour, string slot)
    {
        Clear();
        if (skin == null) return false;
        if (item != null)
        {
            var entry = OutfitCatalog.Model(item.id, skin.Id);
            if (entry == null || entry.Model == null) return false;
            sitter = Object.Instantiate(entry.Model, root.position, Quaternion.identity, root);
            front = FrontOf(sitter);
            Still(sitter);
            var wanted = new HashSet<string>(item.PartNames, StringComparer.OrdinalIgnoreCase);
            var drop = new HashSet<string>(OutfitCatalog.Split(OutfitCatalog.FitOf(item.id, skin.Id)?.drop), StringComparer.OrdinalIgnoreCase);
            var character = skin.Model;
            var body = character != null ? character.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true))
            {
                // Its own meshes only: an outfit's file often carries the avatar's body it was fitted on, told from the
                // piece by dressing's own rule (a mirrored copy of the character's mesh is a piece, not her body).
                bool mine = wanted.Count > 0 ? wanted.Contains(renderer.name)
                    : !drop.Contains(renderer.name) && (character == null || !OutfitDresser.CopiesOwn(renderer, sitter.transform, body, character.transform));
                if (mine) shown.Add(renderer);
            }
            SkinVariant.WearOn(shown, entry.VariantOf(colour));
        }
        else
        {
            if (skin.Model == null || string.IsNullOrEmpty(slot)) return false;
            sitter = Object.Instantiate(skin.Model, root.position, Quaternion.identity, root);
            front = FrontOf(sitter);
            Still(sitter);
            SkinVariant.Wear(sitter, null, skin.VariantOf(colour));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in OutfitCatalog.PartsOf(skin.Id))
                if (string.Equals(part.slot, slot, StringComparison.OrdinalIgnoreCase)) names.UnionWith(OutfitCatalog.Split(part.parts));
            foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true))
            {
                if (!names.Contains(renderer.name)) continue;
                renderer.gameObject.SetActive(true);
                shown.Add(renderer);
            }
        }
        return Ready();
    }

    // What every stood piece gets once its renderers are picked: all of it off but for the studio's own picture, the
    // studio's own ambient on what is shown, and its points measured for the framing.
    private bool Ready()
    {
        // Every renderer of it stays off but for the studio's own picture.
        foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        if (shown.Count == 0)
        {
            Clear();
            return false;
        }
        if (block == null) block = new MaterialPropertyBlock();
        var flat = new SphericalHarmonicsL2();
        flat.AddAmbientLight(Ambient);
        var probes = new[] { flat };
        foreach (var renderer in shown)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            // The studio's own ambient, whatever the scene's is.
            renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
            renderer.GetPropertyBlock(block);
            block.CopySHCoefficientArraysFrom(probes);
            renderer.SetPropertyBlock(block);
        }
        Measure();
        return true;
    }

    /// <summary>
    /// Stands a character whole, in its colour set and dressed as <paramref name="look"/> says, in the title screen's idle
    /// pose: a wardrobe preset's portrait (OutfitPresets; the user, 2026-11-13: "옷장 내가 지금까지 옷 입혀서 꾸민 커스텀 프리셋으로
    /// 만들어서 저장할 수 있게 해 줘"). Dressed by the very rule the game dresses the body by (OutfitDresser), so what is
    /// covered stays hidden; only what the dressing left drawn is photographed. Frame it with part -1 (all of it): a
    /// character is one piece, but a wide pose must never be split. False when there is nothing of it to photograph.
    /// </summary>
    public bool StandDressed(PlayerBodyProfile.Skin skin, SkinVariant variant, OutfitLook look)
    {
        Clear();
        if (skin == null || skin.Model == null) return false;
        var profile = PlayerBodyProfile.Load();
        var source = profile != null ? profile.ModelOf(skin) : skin.Model;
        sitter = Object.Instantiate(source, root.position, Quaternion.identity, root);
        front = FrontOf(sitter);
        SkinVariant.Wear(sitter, null, variant);
        try
        {
            OutfitDresser.Dress(sitter, source, skin, look ?? new OutfitLook());
        }
        catch (Exception problem)
        {
            Debug.LogWarning("[OutfitStudio] " + problem.Message);
        }
        // The idle pose, held: the animators stop once it is struck, so nothing moves it before the picture.
        var animator = sitter.GetComponentInChildren<Animator>(true);
        var idle = profile != null ? profile.Find(MainMenuPreview.IdleClip) : null;
        if (animator != null && idle != null)
        {
            animator.applyRootMotion = false;
            AnimationPlayableUtilities.PlayClip(animator, idle, out pose);
            pose.Evaluate();
        }
        Still(sitter);
        foreach (var renderer in sitter.GetComponentsInChildren<Renderer>(false))
            if (renderer.enabled && !(renderer is ParticleSystemRenderer)) shown.Add(renderer);
        return Ready();
    }

    // The way a character faces: across its upper arms (as the player's body measures it), else +Z.
    private static Vector3 FrontOf(GameObject model)
    {
        var animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null || animator.avatar == null || !animator.isHuman) return Vector3.forward;
        var left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (left == null || right == null) return Vector3.forward;
        Vector3 facing = Vector3.Cross(right.position - left.position, Vector3.up);
        facing.y = 0f;
        return facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;
    }

    // Stands as its file was made: no clips play on it.
    private static void Still(GameObject model)
    {
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
    }

    // Every vertex of the piece where it stands posed, a skinned one baked, for a framing that holds all of it; a mesh
    // that can't be read (a build's) gives the corners of its box instead. Then the piece's parts well apart, if it has.
    private void Measure()
    {
        points.Clear();
        foreach (var renderer in shown)
        {
            int before = points.Count;
            if (renderer is SkinnedMeshRenderer skinned) AddSkinned(skinned);
            else if (renderer.TryGetComponent(out MeshFilter filter)) AddPlain(renderer, filter.sharedMesh);
            if (points.Count == before) AddCorners(renderer);
        }
        if (points.Count > MostPoints)
        {
            int step = Mathf.CeilToInt(points.Count / (float)MostPoints), kept = 0;
            for (int i = 0; i < points.Count; i += step) points[kept++] = points[i];
            points.RemoveRange(kept, points.Count - kept);
        }
        SplitParts();
        framedPart = int.MinValue;
    }

    private static bool Readable(Mesh mesh) => mesh != null && (mesh.isReadable || Application.isEditor);

    private void AddSkinned(SkinnedMeshRenderer skinned)
    {
        if (!Readable(skinned.sharedMesh)) return;
        if (baked == null) baked = new Mesh { name = "Outfit Studio Bake", hideFlags = HideFlags.HideAndDontSave };
        try { skinned.BakeMesh(baked, true); }
        catch (Exception) { return; }
        baked.GetVertices(scratch);
        // Baked with the renderer's scale taken out (useScale): the mesh in the renderer's own space, which its whole matrix
        // takes to the world (as SkinSample and the zombie hitboxes read it). Put down with its place and turn alone, a
        // piece whose renderer is scaled small was measured as many times too big as its scale is small: the Ene earrings'
        // renderers are scaled 0.0067-0.07, so they were framed as if 14-149 times their size and came out a speck in
        // their pictures (the user, 2026-11-12: "귀걸이는 너무 작아서 아예 안 보여").
        var place = skinned.transform.localToWorldMatrix;
        foreach (var vertex in scratch) points.Add(place.MultiplyPoint3x4(vertex));
    }

    private void AddPlain(Renderer renderer, Mesh mesh)
    {
        if (!Readable(mesh)) return;
        mesh.GetVertices(scratch);
        var place = renderer.transform.localToWorldMatrix;
        foreach (var vertex in scratch) points.Add(place.MultiplyPoint3x4(vertex));
    }

    private void AddCorners(Renderer renderer)
    {
        var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
            : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        var local = mesh != null ? mesh.bounds : new Bounds(renderer.transform.InverseTransformPoint(renderer.bounds.center), renderer.bounds.size);
        for (int i = 0; i < 8; i++)
            points.Add(renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f))));
    }
}

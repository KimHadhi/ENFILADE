using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The see-through barricade a Support sees while holding its class key (the user, 2026-11-12: "능력 키 꾹 누르고 있으면
/// 어디에 설치할지 배치 푸른색으로 투명화 된 바리케이드 보이면서 … 설치 불가능하면 빨간색으로 뜨고"): the barricade's own
/// boxes (Barricade.Shape), stood exactly where and how it would go up, blue where it may, red where it can't, with the
/// outline of its feet on the ground; and amber, blinking, over the oldest of its barricades that the new one would take
/// down. Each face is shaded by the way it faces, so the planks read as solid through the tint. Only on the builder's
/// own PC (SupportKit), and with no collider: nothing else sees or meets it.
/// </summary>
[DisallowMultipleComponent]
public sealed class BarricadeGhost : MonoBehaviour
{
    /// <summary>How it is drawn: where it may go up, where it can't, or the oldest one that would come down for it.</summary>
    public enum Look { Fits, Refused, Doomed }

    // Its blue (the user's "푸른색"), and the amber of the one to come down; red is the HUD's (KitStyle.Red).
    private static readonly Color Blue = new Color(.3f, .64f, 1f), Amber = new Color(1f, .62f, .18f);
    private static readonly int ColorId = Shader.PropertyToID("_Color"), BaseColorId = Shader.PropertyToID("_BaseColor");
    // How high its feet's outline is drawn over the ground (m), how thick (m), and how far out past its feet (m).
    private const float Lift = .03f, LineWidth = .035f, Margin = .06f;
    // The same mesh for every preview, made from the barricade's look once.
    private static Mesh mesh;

    private Material tint;
    private LineRenderer feet;
    private Color shown;
    private bool fresh = true;

    /// <summary>A preview, hidden until shown.</summary>
    public static BarricadeGhost Make(string name)
    {
        // A plain object of the scene, as a barricade is: gone with it, and with its Support (SupportKit.OnDestroy).
        var host = FeelAssets.CreateRuntimeRoot(name);
        var ghost = host.AddComponent<BarricadeGhost>();
        ghost.Build();
        host.SetActive(false);
        return ghost;
    }

    /// <summary>
    /// Draws it with its feet at <paramref name="at"/>, facing along <paramref name="yaw"/>, this way, its fill at about
    /// <paramref name="alpha"/> (Support.previewAlpha) and the outline of its feet stronger.
    /// </summary>
    public void Show(Vector3 at, float yaw, Look look, float alpha)
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
            fresh = true;
        }
        transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        Color want = look == Look.Fits ? Blue : look == Look.Refused ? KitStyle.Red : Amber;
        // A quick blend between blue and red, so stepping across where it may go reads as a change, not a flicker.
        shown = fresh ? want : Color.Lerp(shown, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 24f));
        fresh = false;
        // Breathing gently, as a projection; the one to come down blinks.
        float now = Time.unscaledTime;
        float beat = look == Look.Doomed ? .4f + .6f * Mathf.Abs(Mathf.Sin(now * 5f)) : .86f + .14f * Mathf.Sin(now * 5.5f);
        Color fill = KitStyle.With(shown, Mathf.Clamp01(alpha * beat));
        if (tint.HasProperty(ColorId)) tint.SetColor(ColorId, fill);
        if (tint.HasProperty(BaseColorId)) tint.SetColor(BaseColorId, fill);
        feet.startColor = feet.endColor = KitStyle.With(Color.Lerp(shown, Color.white, .25f), Mathf.Clamp01((alpha * 2.2f + .1f) * beat));
    }

    public void Hide()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void Build()
    {
        var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
        body.transform.SetParent(transform, false);
        body.GetComponent<MeshFilter>().sharedMesh = Mesh;
        var renderer = body.GetComponent<MeshRenderer>();
        // Its own copy of the effects' see-through material, so its colour is its own.
        tint = new Material(FeelAssets.Particle(Texture2D.whiteTexture, false)) { name = "Barricade Preview (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        renderer.sharedMaterial = tint;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // Its feet's outline, laid flat on the ground (the line faces up: TauntRing's way), in its own frame so it moves with it.
        var outline = new GameObject("Feet");
        outline.transform.SetParent(transform, false);
        outline.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        feet = outline.AddComponent<LineRenderer>();
        feet.useWorldSpace = false;
        feet.loop = true;
        feet.alignment = LineAlignment.TransformZ;
        feet.widthMultiplier = LineWidth;
        feet.numCornerVertices = 2;
        feet.numCapVertices = 0;
        feet.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        feet.receiveShadows = false;
        feet.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        // In the line's frame (turned 90° about x) a point (x, z, -lift) is (x, lift, z) in the barricade's.
        float x = Barricade.Width * .5f + Margin, z = Barricade.Depth * .5f + Margin + .05f;
        feet.positionCount = 4;
        feet.SetPositions(new[] { new Vector3(-x, -z, -Lift), new Vector3(x, -z, -Lift), new Vector3(x, z, -Lift), new Vector3(-x, z, -Lift) });
    }

    // Every box of the barricade's look, its faces shaded by the way they face (top brightest, underside darkest), in one mesh.
    private static Mesh Mesh
    {
        get
        {
            if (mesh != null) return mesh;
            var vertices = new List<Vector3>();
            var colors = new List<Color32>();
            var triangles = new List<int>();
            foreach (var part in Barricade.Shape) Box(part, vertices, colors, triangles);
            mesh = new Mesh { name = "Barricade Preview", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // One box: six faces of four corners each, a unit cube scaled, turned and placed as the barricade's own box is.
    private static void Box(Barricade.Part part, List<Vector3> vertices, List<Color32> colors, List<int> triangles)
    {
        var matrix = Matrix4x4.TRS(part.At, part.Turn, part.Size);
        for (int axis = 0; axis < 3; axis++)
        for (int sign = -1; sign <= 1; sign += 2)
        {
            Vector3 normal = Vector3.zero, across = Vector3.zero, up = Vector3.zero;
            normal[axis] = sign;
            across[(axis + 1) % 3] = 1f;
            up[(axis + 2) % 3] = 1f;
            int start = vertices.Count;
            vertices.Add(matrix.MultiplyPoint3x4(normal * .5f - across * .5f - up * .5f));
            vertices.Add(matrix.MultiplyPoint3x4(normal * .5f + across * .5f - up * .5f));
            vertices.Add(matrix.MultiplyPoint3x4(normal * .5f + across * .5f + up * .5f));
            vertices.Add(matrix.MultiplyPoint3x4(normal * .5f - across * .5f + up * .5f));
            Vector3 facing = part.Turn * normal;
            float light = facing.y > .7f ? 1f : facing.y < -.7f ? .45f : Mathf.Abs(facing.z) > .7f ? .8f : .62f;
            var shade = new Color32((byte)(light * 255f), (byte)(light * 255f), (byte)(light * 255f), 255);
            for (int corner = 0; corner < 4; corner++) colors.Add(shade);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }
    }

    private void OnDestroy()
    {
        if (tint != null) Destroy(tint);
    }
}

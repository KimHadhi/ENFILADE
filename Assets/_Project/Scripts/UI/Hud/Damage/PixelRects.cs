using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Solid rectangles in screen pixels, one mesh, drawn in the order they were added: Source's surface.DrawRect, for the
/// enemy health bar that reproduces a Garry's Mod addon rectangle by rectangle (EnemyHealthBarHUD). Positions run as
/// Source's do, from the screen's top left with y down. The graphic sits at the top left corner of a canvas of one unit
/// per pixel, so a rectangle given in whole pixels fills exactly the pixels the addon fills, and the bar's dozen pieces
/// are one mesh rebuilt each frame instead of a dozen images moved about.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PixelRects : MaskableGraphic
{
    private struct Quad
    {
        public float Left, Top, Right, Bottom;
        public Color32 Colour;
    }

    private readonly List<Quad> quads = new List<Quad>();

    /// <summary>Starts the next picture: nothing shows until rectangles are added again.</summary>
    public void Clear()
    {
        if (quads.Count == 0) return;
        quads.Clear();
        SetVerticesDirty();
    }

    /// <summary>A rectangle from (left, top) to (right, bottom), screen pixels with y down. An empty or clear one adds nothing.</summary>
    public void Add(float left, float top, float right, float bottom, Color32 colour)
    {
        if (right <= left || bottom <= top || colour.a == 0) return;
        quads.Add(new Quad { Left = left, Top = top, Right = right, Bottom = bottom, Colour = colour });
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        foreach (var quad in quads)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector3(quad.Left, -quad.Top), quad.Colour, Vector4.zero);
            mesh.AddVert(new Vector3(quad.Right, -quad.Top), quad.Colour, Vector4.zero);
            mesh.AddVert(new Vector3(quad.Right, -quad.Bottom), quad.Colour, Vector4.zero);
            mesh.AddVert(new Vector3(quad.Left, -quad.Bottom), quad.Colour, Vector4.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first + 2, first + 3, first);
        }
    }
}

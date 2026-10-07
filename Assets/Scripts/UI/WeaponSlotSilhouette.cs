using UnityEngine;
using UnityEngine.UI;

/// <summary>Simple vector placeholders until WeaponData.HudIcon is assigned.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WeaponSlotSilhouette : MaskableGraphic
{
    [SerializeField] private int kind;
    public int Kind { get => kind; set { if(kind == value) return; kind=value; SetVerticesDirty(); } }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (kind == 0) { Quad(mesh,.40f,.47f,.60f,.50f); return; }
        if (kind == 1)
        {
            Quad(mesh,.23f,.46f,.66f,.67f); Quad(mesh,.63f,.53f,.95f,.60f);
            Quad(mesh,.87f,.49f,.94f,.64f); Quad(mesh,.34f,.67f,.55f,.71f);
            Poly(mesh,new Vector2(.04f,.63f),new Vector2(.25f,.60f),new Vector2(.25f,.48f),new Vector2(.04f,.36f));
            Poly(mesh,new Vector2(.38f,.48f),new Vector2(.47f,.48f),new Vector2(.50f,.17f),new Vector2(.40f,.15f));
            Poly(mesh,new Vector2(.28f,.48f),new Vector2(.34f,.48f),new Vector2(.29f,.25f),new Vector2(.23f,.25f));
            Quad(mesh,.74f,.60f,.76f,.69f);
        }
        else
        {
            Quad(mesh,.28f,.51f,.75f,.71f); Quad(mesh,.30f,.47f,.67f,.55f);
            Poly(mesh,new Vector2(.30f,.54f),new Vector2(.43f,.54f),new Vector2(.36f,.14f),new Vector2(.23f,.14f));
            Quad(mesh,.44f,.31f,.54f,.35f); Quad(mesh,.52f,.33f,.55f,.51f);
            Quad(mesh,.30f,.71f,.34f,.75f); Quad(mesh,.69f,.71f,.72f,.75f);
        }
    }
    private void Quad(VertexHelper mesh,float x,float y,float right,float top) => Poly(mesh,new Vector2(x,y),new Vector2(right,y),new Vector2(right,top),new Vector2(x,top));
    private void Poly(VertexHelper mesh,params Vector2[] points)
    {
        int start=mesh.currentVertCount; Rect rect=rectTransform.rect;
        foreach(var p in points) mesh.AddVert(new Vector3(rect.xMin+p.x*rect.width,rect.yMin+p.y*rect.height),color,Vector2.zero);
        mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
    }
}

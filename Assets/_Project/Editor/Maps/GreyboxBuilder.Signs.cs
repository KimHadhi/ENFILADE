using TMPro;
using UnityEngine;

// GreyboxBuilder: words in the world. A hint that names a place ("마스터키는 303호에") is only a hint when the place
// says its name: a flat's number beside its door, a floor's number by its stairs (the user, 2026-11-18: "마스터키 힌트
// 불일치"). One part of the class; the rest is in GreyboxBuilder.cs and the files beside it.
public sealed partial class GreyboxBuilder
{
    // How far a plate stands off its wall and its words off the plate (m): enough that neither flickers into the other.
    private const float PlateThick = .02f, WordsProud = .006f;
    // TextMeshPro's world text: one unit of font size is a tenth of a metre of letter height.
    private const float FontPerMetre = 10f;
    private static readonly Color PlateWords = new Color(.93f, .92f, .86f);

    private Transform signs;

    /// <summary>
    /// A plate on a wall with words on it: its middle at <paramref name="middle"/> on the wall's face, facing
    /// <paramref name="face"/> (degrees about Y: the way out of the wall, toward whoever reads it), <paramref name="width"/>
    /// by <paramref name="height"/> m, the letters <paramref name="letters"/> m tall. Seen only: no collider, and it
    /// stays out of the static batches (its words are drawn by TextMeshPro, in the HUD's label face). The words are the
    /// caller's: digits and marks that need no translating, or a Texts row's words.
    /// </summary>
    public void Plate(string name, Vector3 middle, float face, float width, float height, string words, float letters)
    {
        if (signs == null) signs = Root("◤━━ 표지 (호수판, 층 표시)");
        var turn = Quaternion.Euler(0f, face, 0f);
        Vector3 out_ = turn * Vector3.forward;
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.name = name;
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.transform.SetParent(signs, false);
        plate.transform.SetPositionAndRotation(middle + out_ * (PlateThick * .5f), turn);
        plate.transform.localScale = new Vector3(width, height, PlateThick);
        plate.GetComponent<Renderer>().sharedMaterial = Metal;
        plate.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var holder = new GameObject(name + " 글자", typeof(RectTransform));
        holder.transform.SetParent(signs, false);
        // TextMeshPro's words are read by someone looking along its forward: into the wall.
        holder.transform.SetPositionAndRotation(middle + out_ * (PlateThick + WordsProud), Quaternion.LookRotation(-out_, Vector3.up));
        var text = holder.AddComponent<TextMeshPro>();
        FontRoles.Apply(text, FontRoles.Label);
        text.rectTransform.sizeDelta = new Vector2(width, height);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontSize = letters * FontPerMetre;
        text.color = PlateWords;
        text.text = words;
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stage 6A: the money mark drawn beside money numbers instead of a currency sign, the user's pick (option C): a banknote,
/// LAYERLAB Game Picto Pack's Currency_Banknote copied to Resources/UI/MoneyIcon, white, tinted green here.
/// </summary>
public static class MoneyIcon
{
    public static readonly Color Tint = new Color(.49f, .86f, .54f);
    private static Texture2D texture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => texture = null;

    public static Texture2D Texture => texture != null ? texture : texture = Resources.Load<Texture2D>("UI/MoneyIcon");

    /// <summary>A square icon of <paramref name="size"/> (px) under <paramref name="parent"/>, anchored at its middle.</summary>
    public static RawImage Make(Transform parent, float size)
    {
        var rect = FeelUI.Rect("Money Icon", parent);
        rect.sizeDelta = new Vector2(size, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = Texture;
        image.color = Tint;
        image.raycastTarget = false;
        image.enabled = image.texture != null;
        return image;
    }
}

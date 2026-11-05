using UnityEngine;
using UnityEngine.UI;

/// <summary>One look's card picture from <see cref="SkinPortraits"/>; it fills in when the picture is taken.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(RawImage))]
public sealed class PortraitImage : MonoBehaviour
{
    private RawImage image;
    private PlayerBodyProfile.Skin skin;
    private SkinVariant variant;

    /// <summary>Puts the picture of <paramref name="skin"/> in <paramref name="variant"/> (null: as imported) on a rect.</summary>
    public static PortraitImage Add(RectTransform rect, PlayerBodyProfile.Skin skin, SkinVariant variant)
    {
        var image = rect.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        var portrait = rect.gameObject.AddComponent<PortraitImage>();
        portrait.image = image;
        portrait.skin = skin;
        portrait.variant = variant;
        portrait.Refresh();
        return portrait;
    }

    private void OnEnable()
    {
        SkinPortraits.Taken += Refresh;
        Refresh();
    }

    private void OnDisable() => SkinPortraits.Taken -= Refresh;

    private void Refresh()
    {
        if (image == null) return;
        var picture = SkinPortraits.Of(skin, variant);
        image.texture = picture;
        image.enabled = picture != null;
    }
}

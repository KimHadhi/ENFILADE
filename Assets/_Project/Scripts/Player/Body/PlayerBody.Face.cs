/// <summary>
/// The body's face (CharacterFace): put on the model once it is dressed, so its moods, blinks and talking ride the
/// character's own shape keys and those of anything worn that follows them. PlayerFace, on the player, works out which
/// mood it shows from what is happening to them; a body made again (another skin or outfit) gets a face again.
/// </summary>
public sealed partial class PlayerBody
{
    private CharacterFace bodyFace;

    /// <summary>The body's face, or null while there is no body.</summary>
    internal CharacterFace Face => bodyFace;

    // From Build, right after dressing.
    private void WearFace()
    {
        bodyFace = CharacterFace.Wear(body.gameObject, loadedSkin != null ? loadedSkin.Id : null);
        if (!TryGetComponent(out PlayerFace _)) gameObject.AddComponent<PlayerFace>();
    }
}

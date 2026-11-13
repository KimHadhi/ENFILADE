/// <summary>
/// The body's face (CharacterFace): put on the model once it is dressed, so its moods, blinks and talking ride the
/// character's own shape keys and those of anything worn that follows them, and the callouts' motion (CalloutGesture).
/// PlayerFace, on the player, works out which mood it shows from what is happening to them; a body made again (another
/// skin or outfit) gets a face again.
/// </summary>
public sealed partial class PlayerBody
{
    private CharacterFace bodyFace;

    // From Build, right after dressing.
    private void WearFace()
    {
        bodyFace = CharacterFace.Wear(body.gameObject, loadedSkin != null ? loadedSkin.Id : null);
        // The callouts' motion (a nod, a point, a wave) on the same body.
        if (!body.TryGetComponent(out CalloutGesture _)) body.gameObject.AddComponent<CalloutGesture>();
        if (!TryGetComponent(out PlayerFace _)) gameObject.AddComponent<PlayerFace>();
    }
}

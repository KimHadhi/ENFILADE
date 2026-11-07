using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// The body's side of a vault carried by its clip's hands (WarpedTraversal): the movement asks, before the vault starts,
/// which clip it will play and how that clip's hands meet the obstacle on this body (ObstacleClipContacts); the body then
/// plays that very clip, at the clip time the movement drives (ObstaclePose). The vault clips are measured when the body
/// is made, so the first vault doesn't stall. Numbers come from Tables/Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody : IObstacleMotionSource
{
    [Header("Hands over obstacles (measured from the clips)")]
    [SerializeField, Range(0f, 1f), Tooltip("A hand counts as put down on the obstacle when it reaches at least this share of an arm "
        + "ahead of the hips, below its shoulder.")]
    private float vaultHandAhead = .25f;
    [SerializeField, Range(0f, 2f), Tooltip("A planted hand leaves once the hips have gone this share of an arm past it.")]
    private float vaultHandPass = .6f;
    [SerializeField, Range(0f, 1f), Tooltip("...or once it has lifted this share of an arm off its lowest.")]
    private float vaultHandLift = .15f;
    [SerializeField, Range(.1f, 1f), Tooltip("A hand is put down within this first share of the clip.")]
    private float vaultPlantWindow = .65f;

    // The clip the movement was told the vault plays, kept for the body's Enter a frame later at most.
    private string pickedObstacleClip;
    private int pickedAtFrame = -1000;
    private const int PickedFrames = 2;

    private ObstacleClipContacts.Rules ContactRules => new(vaultHandAhead, vaultHandPass, vaultHandLift, vaultPlantWindow);

    public bool TryObstacleMotion(VaultKind kind, out IObstacleMotion motion)
    {
        motion = null;
        if (animator == null || !animator.isHuman || dead || limp || loadedModel == null) return false;
        pickedObstacleClip = ObstacleClip();
        pickedAtFrame = Time.frameCount;
        var channel = Find(pickedObstacleClip);
        var clip = channel != null && channel.Playable.IsValid() ? channel.Playable.GetAnimationClip() : null;
        var contacts = ObstacleClipContacts.For(loadedModel, clip, animator, ContactRules);
        if (contacts == null) return false;
        motion = new ClipHands(this, contacts);
        return true;
    }

    // Climbing off a ladder's top: the clip by the hand that went up last, the one Enter plays (LadderExitClip).
    public bool TryLadderExitMotion(bool leftHand, out IObstacleMotion motion)
    {
        motion = null;
        if (animator == null || !animator.isHuman || dead || limp || loadedModel == null) return false;
        var channel = Find(leftHand ? ladderExitLeft : ladderExitRight);
        var clip = channel != null && channel.Playable.IsValid() ? channel.Playable.GetAnimationClip() : null;
        var contacts = ObstacleClipContacts.For(loadedModel, clip, animator, ContactRules);
        if (contacts == null) return false;
        motion = new ClipHands(this, contacts);
        return true;
    }

    // The clip picked for the movement this frame or the last, else a fresh pick.
    private string PickObstacleClip()
    {
        if (pickedObstacleClip != null && Time.frameCount - pickedAtFrame <= PickedFrames)
        {
            string picked = pickedObstacleClip;
            pickedObstacleClip = null;
            return picked;
        }
        return ObstacleClip();
    }

    // Measured as the body is made: the three vaults, plain and with a gun.
    private void MeasureObstacleClips()
    {
        if (animator == null || !animator.isHuman || loadedModel == null) return;
        foreach (string name in new[] { vault, vaultKong, vaultHigh })
            foreach (string variant in new[] { name, name + rifleSuffix, name + pistolSuffix })
                if (!string.IsNullOrEmpty(variant) && byName.TryGetValue(variant, out var channel) && channel.Playable.IsValid())
                    ObstacleClipContacts.For(loadedModel, channel.Playable.GetAnimationClip(), animator, ContactRules);
        // The climbs off a ladder's top, warped onto the floor up there the same way (TryLadderExitMotion).
        foreach (string name in new[] { ladderExitLeft, ladderExitRight })
            foreach (string variant in new[] { name, name + rifleSuffix, name + pistolSuffix })
                if (!string.IsNullOrEmpty(variant) && byName.TryGetValue(variant, out var channel) && channel.Playable.IsValid())
                    ObstacleClipContacts.For(loadedModel, channel.Playable.GetAnimationClip(), animator, ContactRules);
    }

    // The clip's hands on this body as it is posed now: the Animator's place, turn and fit scale.
    private sealed class ClipHands : IObstacleMotion
    {
        private readonly PlayerBody body;
        private readonly ObstacleClipContacts.Contacts contacts;

        public ClipHands(PlayerBody body, ObstacleClipContacts.Contacts contacts)
        {
            this.body = body;
            this.contacts = contacts;
        }

        public float Length => contacts.Length;
        public float PlantTime => contacts.PlantTime;
        public float ReleaseTime => contacts.ReleaseTime;

        public Vector3 HandsFromFeet(float clipTime)
            => body.animator != null ? body.animator.transform.TransformPoint(contacts.HandsAt(clipTime)) - body.transform.position : Vector3.zero;

        public float WristAboveTop => body.hands != null ? body.hands.PalmDepth : 0f;
    }
}

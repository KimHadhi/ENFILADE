using UnityEngine;

public sealed partial class PlayerBodyHands
{
    [SerializeField, Range(0f, .75f), Tooltip("How far inside the edge the palm centre rests, as a share of palm length.")]
    private float ledgePalmInset = .15f;
    private PlayerBodyProfile.Skin gripSkin;
    private readonly struct LedgeContact
    {
        public readonly Collider Surface;
        public readonly Vector3 Palm, Fingers, Normal, Side;
        public LedgeContact(Collider surface, Vector3 palm, Vector3 fingers, Vector3 normal, Vector3 side)
        { Surface = surface; Palm = palm; Fingers = fingers; Normal = normal; Side = side; }
    }

    private bool FindLedgeContact(ArmIK arm, bool isLeft, out LedgeContact contact)
    {
        contact = default;
        var ledge = player.Abilities.Traversal.Ledge;
        if (ledge.Surface == null || arm == null) return false;
        Transform support = ledge.Surface.transform;
        Vector3 outward = Vector3.ProjectOnPlane(support.TransformDirection(ledge.LocalNormal), Vector3.up).normalized;
        Vector3 along = Vector3.Cross(Vector3.up, -outward), edge = support.TransformPoint(ledge.LocalEdge);
        // Climbing, the edge point is already the one in front of the body, and the hands must not slide with it.
        Vector3 centre = player.CurrentState != PlayerMovement.MovementState.LedgeHang ? edge
            : edge + along * Vector3.Dot(transform.position - edge, along);
        Vector3 side = along * (isLeft ? -1f : 1f);
        float across = ledgeHandSpacing * shoulderSpan * (gripSkin?.LedgeHandSpacingScale ?? 1f) * .5f;
        // The palm rests just inside the edge; the wrist is placed from the measured palm centre, not guessed.
        float inset = arm.PalmLength * ledgePalmInset * (gripSkin?.LedgePalmInsetScale ?? 1f);
        for (int attempt = 0; attempt < 5; attempt++, across *= .7f)
        {
            Vector3 probe = centre + side * across - outward * inset + Vector3.up * .25f;
            if (!ledge.Surface.Raycast(new Ray(probe, Vector3.down), out RaycastHit top, .5f) || top.normal.y < .3f) continue;
            Vector3 fingers = Vector3.ProjectOnPlane(-outward, top.normal).normalized;
            contact = new LedgeContact(ledge.Surface, top.point + top.normal * (arm.PalmDepth + fingerSurfaceClearance),
                fingers, top.normal, side);
            return true;
        }
        return false;
    }

    private bool LedgeHandTarget(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        position = hint = Vector3.zero;
        rotation = Quaternion.identity;
        if (!FindLedgeContact(arm, isLeft, out var contact)) return false;
        rotation = arm.HandRotation(contact.Fingers, -contact.Normal);
        position = arm.WristAtPalm(contact.Palm, rotation);
        // Looking away, the look clip already has the elbow somewhere an arm can go from that way round.
        hint = Vector3.Lerp(arm.Upper.position + contact.Side * .3f - contact.Normal * .1f, arm.Elbow, body.HangTurn);
        return true;
    }

    // The body's ledge alignment uses the same contacts as the final hand IK, before the eye camera is placed. A hand that
    // has let go no longer counts: the body hangs from the other. centre is where the wrists belong, held where they are.
    internal bool TryLedgeWrists(out Vector3 centre, out Vector3 held)
    {
        centre = held = Vector3.zero;
        if (left == null || right == null
            || !LedgeHandTarget(left, true, out Vector3 l, out _, out _)
            || !LedgeHandTarget(right, false, out Vector3 r, out _, out _)) return false;
        float leftShare = Mathf.Max(edgeShares[0], .001f), rightShare = Mathf.Max(edgeShares[1], .001f), total = leftShare + rightShare;
        centre = (l * leftShare + r * rightShare) / total;
        held = (left.Hand.position * leftShare + right.Hand.position * rightShare) / total;
        return true;
    }

    private void CurlLedgeFingers(ArmIK arm, bool isLeft)
    {
        float weight = weights[(int)Hold.Ledge] * edgeContacts[isLeft ? 0 : 1];
        if (weight <= 0f || !FindLedgeContact(arm, isLeft, out var contact)) return;
        arm.GripLedge(contact.Surface, contact.Fingers, -contact.Normal, contact.Palm, fingerSurfaceClearance, weight);
    }
}

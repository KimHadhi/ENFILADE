using UnityEngine;

public sealed partial class PlayerBodyHands
{
    private readonly Vector3[] skirtPushes = new Vector3[2];
    // How much of each hand a hold has this frame (0 left, 1 right), which a skirt leaves alone.
    private readonly float[] heldShares = new float[2];
    private PlayerBodyCloth cloth;

    // The clips were made for bodies without skirts, so an empty hand can hang inside one, where it would push the cloth
    // out like a fist in a bag. It rests on the skirt instead, moved straight out from the body with its elbow where the
    // clip has it, as a hand rests on an obstacle's top. The shell is the skirt's rest shape and thickness, the same on
    // every PC; the cloth itself only brushes the hand as it swings.
    private void RestOnSkirt(float dt)
    {
        if (cloth == null) cloth = GetComponent<PlayerBodyCloth>();
        if (cloth == null) return;
        Rest(left, 0);
        Rest(right, 1);

        void Rest(ArmIK arm, int hand)
        {
            float free = 1f - heldShares[hand];
            Vector3 wanted = free > 0f ? cloth.OutOfSkirts(arm.PalmPosition, arm.PalmDepth) : Vector3.zero;
            skirtPushes[hand] = Vector3.MoveTowards(skirtPushes[hand], wanted, RestLiftSpeed * arm.Reach * dt);
            if (skirtPushes[hand] != Vector3.zero)
                arm.Solve(arm.Hand.position + skirtPushes[hand], arm.Hand.rotation, arm.Elbow, free, 0f, shoulderReach);
        }
    }
}

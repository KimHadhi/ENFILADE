using UnityEngine;

// ResultsShow: The camera and its lights. One part of the class; the rest is in ResultsShow.cs and the files beside it.
public sealed partial class ResultsShow
{
    // One player's shot (the Warzone victory video): very close on the hand they strike with, gliding over the chest to
    // the face, then back and a little low to the waist up, with them on the right third and room for their titles on
    // the left, drifting in until the cut.
    private void Shoot(Seat seat, float time)
    {
        var pose = seat.Pose;
        var root = pose.transform;
        Vector3 hand = root.TransformPoint(pose.Hand), chest = root.TransformPoint(pose.Chest), eyes = root.TransformPoint(pose.Eyes);
        float sweep = Mathf.Max(HandHold + .1f, HubLobby.Number("showSweep", .9f));
        float pull = Mathf.Max(.1f, HubLobby.Number("showPull", .5f));
        // Clear of a hand thrust at the camera (the pointing finger).
        float close = Mathf.Max(.2f, HubLobby.Number("showClose", .55f)) + pose.Reach * .8f;
        float far = Mathf.Max(close, HubLobby.Number("showFar", 1.7f));
        // Hand to face, on a curve bent through the chest when the hand is below it.
        float glide = Ease.Smooth((time - HandHold) / (sweep - HandHold));
        Vector3 bend = hand.y < chest.y - .05f ? chest : (hand + eyes) * .5f;
        Vector3 along = Bezier(hand, bend, eyes, glide);
        float back = Ease.Smooth((time - sweep) / pull);
        float settle = Mathf.Clamp01((time - sweep - pull) / Mathf.Max(.5f, seat.ShotLength - sweep - pull));
        float distance = Mathf.Lerp(close, far, back) * (1f - .06f * settle);
        // Round to one side and from a little below: wider while it glides, closing in as it settles.
        float yaw = seat.Side * Mathf.Lerp(Mathf.Lerp(24f, 12f, glide), Mathf.Lerp(9f, 5f, settle), back);
        float pitch = Mathf.Lerp(-10f, -7f, back) * Mathf.Deg2Rad;
        Vector3 facing = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        Vector3 level = Quaternion.AngleAxis(yaw, Vector3.up) * facing;
        Vector3 position = along + (level * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)) * distance;
        // Centred on what it glides over; then the face on the right third and a little high.
        float tanV = Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad), tanH = tanV * view.aspect;
        Vector3 right = Vector3.Cross(Vector3.up, -level).normalized;
        Vector3 framed = eyes - right * (distance * tanH / 3f) - Vector3.up * (distance * tanV * .35f);
        Vector3 look = Vector3.Lerp(along, framed, back);
        view.transform.position = position;
        view.transform.rotation = Quaternion.LookRotation(look - position, Vector3.up);
        LightUp(Vector3.Lerp(chest, eyes, .6f), false);
    }

    // The show's two lights on whom it frames: a soft one riding with the camera, and one rimming them from behind and
    // above (Hub.csv showLight, showRim; 0 turns one off).
    private void LightUp(Vector3 subject, bool wide)
    {
        if (key == null) key = MakeLight("Results Key", new Color(1f, .96f, .9f));
        if (rim == null) rim = MakeLight("Results Rim", new Color(.84f, .9f, 1f));
        Vector3 facing = Quaternion.Euler(0f, stage.Yaw, 0f) * Vector3.forward;
        key.transform.position = view.transform.position + Vector3.up * .25f;
        key.transform.rotation = Quaternion.LookRotation(subject - key.transform.position, Vector3.up);
        key.spotAngle = wide ? 70f : 46f;
        key.intensity = Mathf.Max(0f, HubLobby.Number("showLight", 2f));
        key.enabled = key.intensity > 0f;
        rim.transform.position = subject - facing * (wide ? 2.4f : 1.4f) + Vector3.up * (wide ? 2.4f : 1.5f);
        rim.transform.rotation = Quaternion.LookRotation(subject - rim.transform.position, Vector3.up);
        rim.spotAngle = wide ? 100f : 60f;
        rim.intensity = Mathf.Max(0f, HubLobby.Number("showRim", 3f));
        rim.enabled = rim.intensity > 0f;
    }

    private static Light MakeLight(string name, Color color)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = color;
        light.range = 10f;
        light.innerSpotAngle = 20f;
        light.shadows = LightShadows.None;
        return light;
    }

    private static Vector3 Bezier(Vector3 from, Vector3 bend, Vector3 to, float t)
    {
        float u = 1f - t;
        return u * u * from + 2f * u * t * bend + t * t * to;
    }
}

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Stage G&amp;B-2: a runner's leap, shown on every PC (EnemyCombatAI.IsPouncing, which copies time themselves). In the
/// air it plays ZombieLooks.leap (top input 18) and the body rises and falls over the leap's windup, as high as
/// ZombieRunner's leapHeight; landing on nothing it plays ZombieLooks.leapLand once (input 19). Landing on someone, its
/// pin (ZombieGrip) takes over: kneeling over them, eating.
/// </summary>
public sealed partial class ZombieBody
{
    private const int LeapInput = 18, LandInput = 19;
    // How fast it goes into the leap's pose, and the landing's in and out (s).
    private const float LeapIn = .08f, LandBlend = .12f;

    private AnimationClipPlayable leap, land;
    private ZombieRunner runner;
    private float leapLength, landLength, leapTime, landTime, leapWeight, landWeight, modelFeet;
    // How the body sits in the enemy as dressed (a body of its own keeps its maker's turn): the leap and the grab clips'
    // turn go on top of it.
    private Quaternion modelTurn = Quaternion.identity;
    private bool wasPouncing, landing;

    private void BuildLeap(ZombieLooks looks, AnimationClip rest)
    {
        leap = Clip(Or(looks.leap, Or(looks.run, rest)), top, LeapInput, out leapLength);
        land = Clip(Or(looks.leapLand, rest), top, LandInput, out landLength);
        modelFeet = Model != null ? Model.transform.localPosition.y : 0f;
        modelTurn = Model != null ? Model.transform.localRotation : Quaternion.identity;
    }

    /// <summary>From Update, after ArmsTick: the leap's pose and height, and the landing.</summary>
    private void LeapTick(float dt)
    {
        bool pouncing = combat != null && combat.IsPouncing && !down;
        if (pouncing)
        {
            if (!wasPouncing) leapTime = 0f;
            leapTime += dt;
            landing = false;
        }
        else if (wasPouncing && !down && (grip == null || !grip.IsHolding))
        {
            // Down on nothing: it lands where it leapt to.
            landing = true;
            landTime = 0f;
        }
        wasPouncing = pouncing;
        if (landing && ((landTime += dt) >= landLength || down || (grip != null && grip.IsHolding))) landing = false;
        leapWeight = Mathf.MoveTowards(leapWeight, pouncing ? 1f : 0f, dt / LeapIn);
        landWeight = Mathf.MoveTowards(landWeight, landing ? 1f : 0f, dt / LandBlend);
        leap.SetTime(Mathf.Repeat(leapTime, leapLength));
        land.SetTime(Mathf.Min(landTime, landLength));
        Rise(pouncing);
    }

    // Up off the ground and back down over the leap's time in the air.
    private void Rise(bool pouncing)
    {
        if (Model == null) return;
        float height = 0f;
        if (pouncing)
        {
            if (runner == null) runner = GetComponent<ZombieRunner>();
            float share = Mathf.Clamp01(combat.WindupElapsed / Mathf.Max(.05f, combat.Windup));
            height = Mathf.Sin(share * Mathf.PI) * (runner != null ? runner.LeapHeight : .45f);
        }
        var place = Model.transform.localPosition;
        if (Mathf.Approximately(place.y, modelFeet + height)) return;
        Model.transform.localPosition = new Vector3(place.x, modelFeet + height, place.z);
    }
}

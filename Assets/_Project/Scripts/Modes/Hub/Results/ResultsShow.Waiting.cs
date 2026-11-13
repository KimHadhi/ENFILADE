using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ResultsShow: Waiting behind the curtain until the hub is really up on this PC; the seats, the camera, the posed copies. One part of the class; the rest is in ResultsShow.cs and the files beside it.
public sealed partial class ResultsShow
{
    private void Wait()
    {
        float now = Time.realtimeSinceStartup;
        // A long frame (a hitch while the hub loads, a shader compiling) starts the quiet stretch over.
        if (Time.unscaledDeltaTime > HubLobby.Number("showSettleFrame", .1f)) smoothSince = now;
        bool late = now - createdAt >= HubLobby.Number("showWait", 20f);
        mine = PlayerRoster.Local;
        if (!prepared && (mine != null && mine.IsInitialized || late)) Prepare();
        if (prepared && stage != null) HideBodies();
        foreach (var seat in seats)
            if (seat.Pose != null) seat.Pose.Tick(0f);
        if (waitCaption != null) waitCaption.alpha = .4f + .25f * Mathf.Sin(now * 4f);
        if (late || Ready(now)) StartShow();
    }

    private bool Ready(float now)
    {
        if (!prepared) return false;
        // The hub's lobby is up (its scene active and its Start run); elsewhere (/results in a chapter) there is none to wait for.
        if (HubFlow.InHub && !HubLobby.IsActive) return false;
        if (HoldBack != null && HoldBack()) return false;
        // The camera has drawn the posed copies a few times, and no frame has run long since they stood there.
        if (view != null && framesSeen < Mathf.Max(1, Mathf.RoundToInt(HubLobby.Number("showFrames", 3f)))) return false;
        return now - Mathf.Max(smoothSince, preparedAt) >= HubLobby.Number("showSettle", .5f);
    }

    // The results' camera has drawn a frame.
    private void Drawn(ScriptableRenderContext context, Camera drawn)
    {
        if (drawn != null && drawn == view) framesSeen++;
    }

    // The hub is up: the show from its beginning, the curtain lifting off it.
    private void StartShow()
    {
        if (!prepared) Prepare();
        showing = true;
        age = prevAge = 0f;
        if (waitCaption != null) waitCaption.gameObject.SetActive(false);
        if (stage == null || seats.Count == 0)
        {
            // No stage in this hub: straight to the sheet.
            OpenSheet();
            return;
        }
        BuildLineup();
        // Over everything the lineup put up, so it lifts off all of it.
        curtain.transform.SetAsLastSibling();
        KitSound.Play(KitSound.Cue.Escape, .8f);
    }

    // Behind the curtain: the seats, the camera (drawing the stage from now on, so what it sees is compiled and uploaded
    // before the curtain lifts), the posed copies on their marks, and the players' own bodies out of sight.
    private void Prepare()
    {
        prepared = true;
        preparedAt = Time.realtimeSinceStartup;
        var ranked = ResultsAwards.Ranked(MatchResults.Entries, won);
        int count = ranked.Count;
        // The best in the middle, then out to either side by turns.
        int middle = (count - 1) / 2;
        for (int rank = 0; rank < count; rank++)
        {
            int offset = (rank + 1) / 2 * (rank % 2 == 1 ? 1 : -1);
            seats.Add(new Seat { Entry = ranked[rank], Index = Mathf.Clamp(middle + offset, 0, count - 1) });
        }
        // Guard against two in one place (a short row's rounding): seats in rank order take the next free place.
        var taken = new bool[count];
        foreach (var seat in seats)
        {
            int at = seat.Index;
            while (taken[at]) at = (at + 1) % count;
            taken[at] = true;
            seat.Index = at;
        }
        if (stage == null) return;
        foreach (var seat in seats) seat.Feet = stage.Slot(seat.Index, count);
        MakeCamera();
        MakePoses();
        HideBodies();
        HoldInput(true);
    }

    private void MakeCamera()
    {
        var host = new GameObject("Results Camera");
        view = host.AddComponent<Camera>();
        var main = Camera.main;
        // Drawn over the player's own camera (and the HUD it carries), which goes on underneath as it is.
        view.depth = (main != null ? main.depth : 0f) + 10f;
        view.fieldOfView = stage.FieldOfView;
        view.nearClipPlane = .05f;
        view.farClipPlane = 500f;
        view.clearFlags = CameraClearFlags.Skybox;
        var data = view.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = true;
        view.transform.position = stage.CameraPoint(seats.Count, view.aspect);
        view.transform.rotation = Quaternion.LookRotation(stage.Focus - view.transform.position, Vector3.up);
    }

    // Each seat's posed copy on its mark, turned to the stage's camera: the fist for the best who won a title, the others
    // by turns (or everyone the pose the console asked for).
    private void MakePoses()
    {
        var facing = Quaternion.Euler(0f, stage.Yaw, 0f);
        var forced = ResultsMannequin.Named(forcedPose);
        forcedPose = null;
        int turn = 0;
        for (int rank = 0; rank < seats.Count; rank++)
        {
            var seat = seats[rank];
            bool best = rank == 0 && ResultsAwards.Of(won, seat.Entry).Count > 0;
            var clip = forced != null ? forced : ResultsMannequin.PoseFor(best ? 0 : turn++, best);
            seat.Pose = ResultsMannequin.Make(seat.Entry.Look, seat.Feet, facing, 1f, clip);
        }
    }

    // The players' own bodies (and what they hold) stay out of sight while the posed copies stand for them.
    private void HideBodies()
    {
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null) continue;
            someone.GetComponentsInChildren(true, found);
            foreach (var part in found)
            {
                if (part == null || !hiddenSet.Add(part)) continue;
                hidden.Add((part, part.forceRenderingOff));
                part.forceRenderingOff = true;
            }
        }
        found.Clear();
    }

    private void ShowBodies()
    {
        foreach (var (part, wasOff) in hidden)
            if (part != null) part.forceRenderingOff = wasOff;
        hidden.Clear();
        hiddenSet.Clear();
    }

    private void HoldInput(bool hold)
    {
        var actions = InputSystem.actions;
        if (actions == null) return;
        var moves = actions.FindActionMap("Player", false);
        var combat = actions.FindActionMap("Combat", false);
        if (hold)
        {
            if (mapsHeld) return;
            mapsHeld = true;
            playerMapWas = moves != null && moves.enabled;
            combatMapWas = combat != null && combat.enabled;
            moves?.Disable();
            combat?.Disable();
            return;
        }
        if (!mapsHeld) return;
        mapsHeld = false;
        if (playerMapWas) moves?.Enable();
        if (combatMapWas) combat?.Enable();
    }

    // The player has the stage, the camera and the controls back.
    private void Release()
    {
        HoldInput(false);
        ClearStage();
        if (view != null) view.enabled = false;
    }

    // The posed copies and the show's lights go, and the players' own bodies show again.
    private void ClearStage()
    {
        foreach (var seat in seats)
        {
            if (seat.Pose != null) Destroy(seat.Pose.gameObject);
            seat.Pose = null;
        }
        if (key != null) Destroy(key.gameObject);
        if (rim != null) Destroy(rim.gameObject);
        key = rim = null;
        ShowBodies();
    }

    private static bool Skipped()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        return keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            || mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private void OpenSheet()
    {
        if (sheet != null) return;
        if (lineup != null) lineup.alpha = 0f;
        if (view != null) view.enabled = false;
        ClearStage();
        // The controls stay held under the sheet; they come back when it closes (OnDestroy).
        sheet = ResultsSheet.Open(won);
        if (curtain != null) curtain.color = Color.clear;
    }
}

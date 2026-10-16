using System;
using UnityEditor;
using UnityEngine;

public static class MissionHUDChecks
{
    [MenuItem("Tools/HUD/Run Mission Checks")]
    public static void Run()
    {
        var root = new GameObject("Mission model check");
        try
        {
            var tracker = root.AddComponent<MissionTracker>();
            int alerts = 0;
            tracker.Changed += (_, announce) => { if (announce) alerts++; };
            tracker.SetObjective(new MissionObjective("test", "처치", "모든 적을 처치하세요.", MissionProgressDisplay.Count, 0, 3));
            tracker.SetProgress("test", 2, 3);
            Require(alerts == 1 && tracker.Current.ProgressText == "2 / 3", "Progress must not replay alerts.");
            tracker.SetProgress("stale", 3, 3);
            Require(tracker.Current.Current == 2, "Stale mission progress must not overwrite the current mission.");
            tracker.SetProgress("test", 7, 3);
            Require(tracker.Current.Progress == 1 && tracker.Current.Current == 3, "Clamp progress.");
            tracker.SetObjective(new MissionObjective("travel", "통제실로 이동", "표시된 통제실 입구로 이동하세요."));
            Require(tracker.Current.Display == MissionProgressDisplay.None && alerts == 2, "Non-numeric objective.");
            tracker.SetObjective(new MissionObjective("hack", "보안 해제", "단말기를 보호하세요.", MissionProgressDisplay.Percent, 45, 100));
            Require(tracker.Current.ProgressText == "45%", "Percentage objective.");
            tracker.SetObjective(new MissionObjective("empty", "대기", "", MissionProgressDisplay.Count, 1, 0));
            Require(tracker.Current.Display == MissionProgressDisplay.None, "Zero target hides counters.");
            tracker.ClearObjective(); Require(tracker.Current == null, "Clear mission.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Debug.Log("MISSION_HUD_CHECKS_PASSED");
    }
    public static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    // Exercise the actual view on an isolated copy, leaving the user's scene and mission untouched.
    public static void CheckLayout(MissionHUD source)
    {
        var copy = UnityEngine.Object.Instantiate(source.gameObject);
        try
        {
            var hud = copy.GetComponent<MissionHUD>();
            var so = new SerializedObject(hud);
            var bar = (RectTransform)so.FindProperty("missionBar").objectReferenceValue;
            var counter = ((GameObject)so.FindProperty("counterRoot").objectReferenceValue).GetComponent<RectTransform>();
            var fill = (UnityEngine.UI.Image)so.FindProperty("progress").objectReferenceValue;
            var refresh = typeof(MissionHUD).GetMethod("Refresh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var apply = typeof(MissionHUD).GetMethod("ApplyProgress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            float left = bar.anchoredPosition.x + bar.rect.xMin;
            foreach (string title in new[] { "처치", "추가로 투입되는 적을 모두 처치하세요" })
            {
                refresh.Invoke(hud, new object[] { new MissionObjective("check", title, "", MissionProgressDisplay.Count, 1, 3), false });
                Require(Mathf.Abs(bar.anchoredPosition.x + bar.rect.xMin - left) < .01f, "Title changes must keep the left edge fixed.");
                Require(Mathf.Abs(counter.rect.height - bar.rect.height) < .01f, "Counter must match bar height.");
                Require(Mathf.Abs(counter.offsetMax.x) < .01f, "Counter must meet the right edge.");
                var track = (RectTransform)fill.transform.parent;
                foreach (float amount in new[] { 0f, .5f, 1f })
                {
                    apply.Invoke(hud, new object[] { amount });
                    Require(Mathf.Abs(fill.rectTransform.rect.width - track.rect.width * amount) < .01f, "Fill width must match numeric progress.");
                    Require(Mathf.Abs(fill.rectTransform.rect.height - track.rect.height) < .01f, "Fill must match track height.");
                }
            }
            Require(Mathf.Abs(hud.AlertDuration - 2.75f) < .01f, "Slower alert duration.");
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }
}

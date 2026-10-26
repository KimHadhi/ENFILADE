using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Only runs when explicitly requested. Play tests use the actual scene and discard test state on exit.
[InitializeOnLoad]
public static class PlayerConsoleChecks
{
    private const string Request = "Library/PlayerConsoleChecks.request";
    private const string Result = "Library/PlayerConsoleChecks.result.txt";
    private const string Running = "PlayerConsoleChecks.Running";
    private static int stage, nextFrame;
    private static PlayerMovement player;
    private static SinglePlayerChatManager chat;
    private static AdaptiveHealthHUD hp;
    private static UltimateHUD ult;
    private static UltimateFireEffect fire;
    private static GameObject prefab;

    static PlayerConsoleChecks() { EditorApplication.update += Tick; }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Development/CombatFoundation.unity");
        File.WriteAllText(Request, "Run player stat, console and live HUD checks.");
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request))
            {
                File.Delete(Request);
                Install();
                UltimateHUDChecks.Run();
                UltimateFireSetup.Check();
                SessionState.SetBool(Running, true);
                stage = 0;
                EditorApplication.EnterPlaymode();
            }
            if (!SessionState.GetBool(Running, false) || !Application.isPlaying || Time.timeSinceLevelLoad < 1f) return;
            if (Time.frameCount < nextFrame) return;
            RunStep();
            nextFrame = Time.frameCount + 2;
        }
        catch (Exception e)
        {
            File.WriteAllText(Result, "FAILED: " + e);
            Debug.LogException(e);
            Finish();
        }
    }

    [MenuItem("Tools/HUD/Connect Player Ultimate")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var view = Object.FindFirstObjectByType<AdaptiveHealthHUD>();
        Require(view != null, "Scene HP HUD");
        var serialized = new SerializedObject(view);
        var target = serialized.FindProperty("player").objectReferenceValue as PlayerMovement;
        Require(target != null, "Explicit scene player");
        if (target.GetComponent<UltimateCharge>() == null)
        {
            var authored = view.GetComponentInChildren<UltimateCharge>(true);
            var charge = Undo.AddComponent<UltimateCharge>(target.gameObject);
            if (authored != null) charge.SetValues(authored.CurrentCharge, authored.MaximumCharge, authored.RequiredCharge);
            EditorUtility.SetDirty(charge);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            Require(EditorSceneManager.SaveScene(target.gameObject.scene), "Save player resource");
        }
    }

    private static void RunStep()
    {
        switch (stage++)
        {
            case 0:
                player = Object.FindFirstObjectByType<PlayerMovement>();
                chat = Object.FindFirstObjectByType<SinglePlayerChatManager>();
                hp = Object.FindFirstObjectByType<AdaptiveHealthHUD>();
                ult = hp.GetComponentInChildren<UltimateHUD>();
                fire = hp.GetComponentInChildren<UltimateFireEffect>();
                Require(player != null && chat != null && player.Ultimate != null, "Initialized player and chat");
                CheckShieldPresentation();
                player.DamageReceiver.SetMaximumHealth(100, true);
                player.Ultimate.SetValues(0,120,100);
                int count = chat.History.Count;
                chat.SubmitText("/shield 30");
                Require(chat.History.Count >= count + 2, "Command echo and response retained");
                Near(player.DamageReceiver.Shield,30,"Chat submission adds shield");
                Run("/hurt 20"); Near(player.DamageReceiver.Health,100,"Shield protects HP"); Near(player.DamageReceiver.Shield,10,"Shield consumed first");
                Run("/hurt 25"); Near(player.DamageReceiver.Health,85,"Overflow damages HP"); Near(player.DamageReceiver.Shield,0,"Shield depleted");
                Run("/heal 5"); Near(player.DamageReceiver.Health,90,"Partial heal");
                CheckMaximumCommands(); Run("/maxhp 300"); Near(player.DamageReceiver.MaximumHealth,300,"Set maximum"); Near(player.DamageReceiver.Health,90,"Preserve current");
                Run("/shield 500"); Run("/heal"); Near(player.DamageReceiver.Health,300,"Full heal"); Near(player.DamageReceiver.Shield,500,"Heal preserves shield");
                foreach (string bad in new[] { "-1", "NaN", "Infinity", "1e99", "1 2", "abc", "" })
                    foreach (string command in new[] { "hurt", "shield", "ult", "ultset", "ultmax", "ultcost" })
                        Require(Run("/"+command+" "+bad).StartsWith("사용법:"),"Invalid arguments rejected: "+command+" "+bad);
                Near(player.DamageReceiver.Health,300,"Invalid damage preserves HP"); Near(player.DamageReceiver.Shield,500,"Invalid shield preserves value");
                Run("/ult 100"); Require(player.Ultimate.IsReady,"Ready at cost");
                break;
            case 1:
                Near(Field<float>(hp,"currentHealth"),300,"Live HP HUD"); Near(Field<float>(hp,"extraShield"),500,"Live shield HUD");
                CheckLabelSeparation();
                Require(UltimateCharge.ResolveHUDSource(ult, null) == player.Ultimate,"HUD uses player resource");
                Near(Field<Image>(ult,"fill").rectTransform.anchorMax.x,100f/120f,"Live ultimate fill");
                CheckUltimateStatsAndLayout();
                CheckAmmoCommand();
                Require(fire.CurrentPhase == UltimateFireEffect.Phase.Starting || fire.CurrentPhase == UltimateFireEffect.Phase.Looping,"Ready fire starts");
                fire.RefreshNow(20); Require(fire.CurrentPhase == UltimateFireEffect.Phase.Looping,"Fire loops");
                Run("/ult 999"); Near(player.Ultimate.CurrentCharge,120,"Charge capacity clamp");
                Run("/ultuse"); Near(player.Ultimate.CurrentCharge,20,"Use preserves overflow");
                fire.RefreshNow(0); Require(fire.CurrentPhase == UltimateFireEffect.Phase.Ending,"Use ends fire");
                fire.RefreshNow(20); Require(fire.CurrentPhase == UltimateFireEffect.Phase.Hidden,"Fire hides after end");
                Run("/ultuse"); Near(player.Ultimate.CurrentCharge,20,"Insufficient charge not consumed");
                Run("/ultmax 200"); Near(player.Ultimate.MaximumCharge,200,"Set absolute ultimate capacity");
                Run("/ultcost 70"); Run("/ultset 70"); Require(player.Ultimate.IsReady,"Lower cost readiness");
                prefab = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/UI/HUD/PlayerHealthHUD.prefab"));
                break;
            case 2:
                Near(Field<Image>(ult,"fill").rectTransform.anchorMax.x,.35f,"Capacity reflected in fill");
                var copied = prefab.GetComponentInChildren<UltimateHUD>();
                Require(UltimateCharge.ResolveHUDSource(copied,null)==player.Ultimate,"Reusable prefab finds resource");
                Near(Field<Image>(copied,"fill").rectTransform.anchorMax.x,.35f,"Prefab renders live charge");
                var glow = hp.GetComponentInChildren<UltimatePortraitGlow>();
                glow.RefreshNow(1); Near(Field<float>(glow,"displayedProgress"),1,"Portrait follows readiness");
                var so = new SerializedObject(player.Ultimate);
                so.FindProperty("currentCharge").floatValue = 35;
                so.ApplyModifiedPropertiesWithoutUndo();
                break;
            case 3:
                Near(Field<Image>(ult,"fill").rectTransform.anchorMax.x,.175f,"Inspector changes update live HUD");
                Run("/hurt 1000"); Require(!player.DamageReceiver.IsAlive,"Lethal damage");
                Run("/heal"); Near(player.DamageReceiver.Health,0,"Healing does not revive");
                Run("/revive"); Near(player.DamageReceiver.Health,300,"Revive full HP"); Near(player.DamageReceiver.Shield,0,"Revive clears shield");
                Require(Run("/stats").Contains("궁극기 35%/200%"),"Stats reflect gameplay state");
                File.WriteAllText(Result,"PASS: native Play Mode; shield minimum 100 scale/width/percentage and non-overlapping label meshes; console submission/history, shield-first damage/overflow, healing, absolute/relative max HP with minimum 1, hurt rename, invalid arguments, death/revive, ultimate capacity/cost/consumption, live HP/ultimate HUD, Inspector edits, fire start/loop/end, portrait readiness and reusable HUD prefab. Editor HUD/fire regressions passed.");
                Debug.Log("PLAYER_CONSOLE_CHECKS_PASSED");
                Finish(true);
                break;
        }
    }

    private static void CheckShieldPresentation()
    {
        player.DamageReceiver.SetMaximumHealth(100,true);
        player.DamageReceiver.SetShield(160);
        hp.RefreshNow(); CheckLabelSeparation();
        var shield = Field<SlantedHealthGraphic>(hp,"shieldGraphic");
        float normalWidth = shield.rectTransform.rect.width;
        var label = (TMPro.TMP_Text)Field<Graphic>(hp,"shieldText");
        Require(label.text.Contains("160%"),"Shield overflow percentage at 100 HP");
        foreach (float max in new[] { 1f, 50f, 100f })
        {
            player.DamageReceiver.SetMaximumHealth(max,true);
            player.DamageReceiver.SetShield(100); hp.RefreshNow();
            Near(shield.rectTransform.rect.width,normalWidth,"Shield width has 100 HP floor");
            Near(Field<float>(shield,"maximum"),100,"Shield fill scale floor");
            Near(Field<float>(shield,"value"),100,"100 shield fills minimum bar");
            Require(label.text == "+100","No misleading 10000 percent");
            player.DamageReceiver.SetShield(50); hp.RefreshNow();
            Near(Field<float>(shield,"value") / Field<float>(shield,"maximum"),.5f,"50 shield is half minimum bar");
            CheckLabelSeparation();
        }
        foreach (float max in new[] { 200f, 300f, 5000f })
        {
            player.DamageReceiver.SetMaximumHealth(max,true);
            player.DamageReceiver.SetShield(max*2); hp.RefreshNow();
            Near(shield.rectTransform.rect.width,Field<SlantedHealthGraphic>(hp,"healthGraphic").rectTransform.rect.width,"Shield follows HP width above floor");
            Require(label.text.Contains("200%"),"Overflow uses actual maximum above floor");
            CheckLabelSeparation();
        }
        player.DamageReceiver.SetShield(0); hp.RefreshNow(); Require(!label.gameObject.activeSelf,"Zero shield hides label");
        player.DamageReceiver.SetShield(100000); hp.RefreshNow(); CheckLabelSeparation();
    }

    private static void CheckLabelSeparation()
    {
        // Test both the immediate update and the final Canvas pass: font glyphs must not overlap.
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1) Canvas.ForceUpdateCanvases();
            var max = (TMPro.TMP_Text)Field<Graphic>(hp,"maxHPText");
            var shield = (TMPro.TMP_Text)Field<Graphic>(hp,"shieldText");
            max.ForceMeshUpdate(); shield.ForceMeshUpdate();
            Transform space = max.transform.parent;
            float right = space.InverseTransformPoint(max.transform.TransformPoint(max.textBounds.max)).x;
            float left = space.InverseTransformPoint(shield.transform.TransformPoint(shield.textBounds.min)).x;
            Require(left > right + 5f,"Shield glyphs stay to right of maximum HP: "+left+" <= "+right);
        }
    }

    private static void CheckMaximumCommands()
    {
        Require(Run("/damage 10").StartsWith("알 수 없는 명령어"), "Old damage name removed");
        Require(chat.Commands.Help().Contains("/hurt") && !chat.Commands.Help().Contains("/damage"), "Help uses hurt");
        Run("/maxhp 200"); Near(player.DamageReceiver.MaximumHealth,200,"Absolute maximum");
        Run("/maxhp 200"); Near(player.DamageReceiver.MaximumHealth,200,"Repeated assignment is not addition");
        Run("/maxhp +10"); Near(player.DamageReceiver.MaximumHealth,210,"Relative increase");
        Run("/maxhp -10"); Near(player.DamageReceiver.MaximumHealth,200,"Relative decrease");
        Near(player.DamageReceiver.Health,90,"Changing maximum does not heal");
        foreach (string bad in new[] { "NaN", "Infinity", "-Infinity", "1e99", "1 2", "abc", "", "+", "-", "--10", "+-10" })
            Require(Run("/maxhp "+bad).StartsWith("사용법:"), "Reject invalid maximum: "+bad);
        Near(player.DamageReceiver.MaximumHealth,200,"Invalid maximum preserves state");
        Run("/shield 7");
        Require(Run("/maxhp 0").StartsWith("최대 HP의 최솟값"),"Minimum feedback");
        Near(player.DamageReceiver.MaximumHealth,1,"Zero maximum clamps to one");
        Near(player.DamageReceiver.Health,1,"Current HP clamps to reduced maximum");
        Near(player.DamageReceiver.Shield,7,"Maximum changes preserve shield");
        Run("/maxhp 5"); Run("/maxhp -10"); Near(player.DamageReceiver.MaximumHealth,1,"Negative result clamps to one");
        Run("/maxhp 0.5"); Near(player.DamageReceiver.MaximumHealth,1,"Fraction below minimum");
        Run("/maxhp 100.5"); Run("/maxhp +0.5"); Near(player.DamageReceiver.MaximumHealth,101,"Fractional delta");
        Run("/maxhp 3e38"); Run("/maxhp +3e38"); Require(player.DamageReceiver.MaximumHealth == float.MaxValue,"Finite addition overflow capped");
        Run("/maxhp 1"); Run("/hurt 1"); Run("/hurt 10");
        Require(!player.DamageReceiver.IsAlive,"Dead fixture after shield and HP consumed");
        Run("/maxhp +10"); Near(player.DamageReceiver.Health,0,"Maximum changes do not revive");
        player.DamageReceiver.SetMaximumHealth(100,true);
        Run("/hurt 10");
    }

    private static string Run(string command)
    {
        string output = chat.Commands.Execute(command);
        Require(output != null && !output.StartsWith("명령어 실행에 실패"), command);
        return output;
    }
    private static void CheckUltimateStatsAndLayout()
    {
        Require(player.Stats != null && player.Stats == player.Ultimate.Stats, "Player stats are the gameplay stat source");
        var bar = Field<Image>(ult, "bar").rectTransform;
        var marker = Field<RectTransform>(ult, "readyThresholdMarker");
        Require(marker.name == "UltReadyThresholdIcon", "Scene threshold icon has its descriptive name");
        float initialWidth = bar.rect.width;
        float leftEdge = bar.localPosition.x + bar.rect.xMin;
        Run("/ultcost 70"); ult.RefreshNow();
        Near(player.Stats.UltimateCostPercent, 70f, "Console edits the cost stat");
        Near(bar.rect.width, initialWidth, "Cost command preserves the 120% bar width");
        Vector3 threshold = marker.parent.InverseTransformPoint(bar.TransformPoint(new Vector3(Mathf.Lerp(bar.rect.xMin, bar.rect.xMax, 70f / 120f), 0f)));
        Near(marker.localPosition.x, threshold.x, "Scene threshold marks 70/120 of the bar");
        Run("/ultset 120"); Run("/ultuse");
        Near(player.Ultimate.CurrentCharge, 50f, "70% cast leaves 50% reserve");
        Run("/ultmax 1000"); ult.RefreshNow();
        Near(player.Stats.UltimateCapacityPercent, 1000f, "ultmax assigns its argument as the absolute capacity");
        Near(bar.rect.width, Field<float>(ult, "maximumBarWidth"), "Actual HUD obeys its maximum width");
        Near(bar.localPosition.x + bar.rect.xMin, leftEdge, "Actual HUD's left edge stays fixed");
        Near(Field<Image>(ult, "fill").rectTransform.anchorMax.x, 50f / 1000f, "Actual HUD uses capacity ratios above the width cap");
        var serialized = new SerializedObject(player.Stats);
        serialized.FindProperty("ultimateCostPercent").floatValue = 40f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Require(player.Ultimate.IsReady, "Inspector cost stat changes gameplay readiness");
        Run("/ultmax 20"); ult.RefreshNow();
        Near(player.Ultimate.MaximumCharge, 20f, "ultmax 20 sets the maximum to 20 rather than adding 20");
        Near(player.Ultimate.CurrentCharge, 20f, "Lowering the maximum clamps excess stored charge");
        Near(player.Ultimate.RequiredCharge, 40f, "Capacity command preserves the independent cast cost");
        Require(!player.Ultimate.IsReady && !marker.gameObject.activeSelf, "Capacity below the cost disables readiness and hides its marker");
        Run("/ultmax 120");
        Near(player.Ultimate.CurrentCharge, 20f, "Increasing the maximum does not grant charge");
        player.Ultimate.SetValues(100f, 120f, 100f); ult.RefreshNow();
    }

    private static void CheckAmmoCommand()
    {
        var slots = player.GetComponentInChildren<PlayerWeaponSlots>(true);
        Require(slots != null, "Actual scene has a weapon inventory");
        var first = slots.GetRuntime(0);
        var second = slots.GetRuntime(1);
        Require(first != null && second != null, "Actual inventory has two guns");
        int selection = slots.SelectedSlot;
        while (first.Ammo > 0) first.Tick(1f, new WeaponInputFrame(true, true));
        first.Tick(.01f, new WeaponInputFrame(false, false, true));
        Require(first.IsReloading, "Ammo command fixture is reloading");
        second.Tick(1f, new WeaponInputFrame(true, true));
        second.AddReserve(999);
        int bonusReserve = second.ReserveAmmo;
        Require(Run("/ammo 10").StartsWith("사용법:"), "Ammo rejects unsupported arguments");
        Require(first.Ammo == 0 && first.IsReloading, "Invalid ammo command leaves ammunition and reload unchanged");
        Require(Run("/ammo").Contains("총기 2정"), "Ammo replenishes every carried gun even while holstered");
        Require(first.Ammo == first.Data.MagazineSize && first.ReserveAmmo == first.Data.InitialReserveAmmo, "Ammo restores magazine and starting reserve");
        Require(second.Ammo == second.Data.MagazineSize && second.ReserveAmmo == bonusReserve, "Ammo preserves reserve above the starting amount");
        Require(!first.IsReloading && slots.SelectedSlot == selection, "Ammo cancels reload without changing selection");
        first.Tick(10f, default);
        Require(first.Ammo == first.Data.MagazineSize && first.ReserveAmmo == first.Data.InitialReserveAmmo, "Cancelled reload cannot transfer ammunition after replenishing");
    }

    private static void Finish(bool success = false)
    {
        SessionState.SetBool(Running,false);
        if (prefab != null) Object.Destroy(prefab);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }
    private static T Field<T>(object target,string name) => (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void Require(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(float actual,float expected,string message) => Require(Mathf.Abs(actual-expected)<.001f,message+": "+actual+" != "+expected);
}

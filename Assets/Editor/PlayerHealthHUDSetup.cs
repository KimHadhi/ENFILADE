using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PlayerHealthHUDSetup
{
    private const string Request = "Library/PlayerHealthHudInstall.request";
    private const string PlayRequest = "Library/PlayerHealthHudPlayCheck.request";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool editReady = !Application.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
    private static int stage = -1, nextFrame;
    private static AdaptiveHealthHUD hud;
    private static PlayerDamageReceiver receiver;
    private static float oldMaximum, oldHealth, blockWidth;
    private static DamageHit testHit;
    private const string PrefabPath = "Assets/Prefabs/UI/HUD/PlayerHealthHUD.prefab";
    private static GameObject prefabInstance;
    private static float oldShield;

    static PlayerHealthHUDSetup()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            editReady = state == PlayModeStateChange.EnteredEditMode;
            if (editReady) stage = -1;
        };
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (editReady && !Application.isPlaying && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request))
        {
            File.Delete(Request);
            try
            {
                Check(); Install(); ExportPrefab();
                File.WriteAllText("Library/PlayerHealthHudInstall.result.txt", "PASS: health API, damage/dodge regression, editor HUD checks, player link and scene save.");
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Library/PlayerHealthHudInstall.result.txt", "FAILED: " + e); }
        }
        if (!Application.isPlaying || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < 1f) return;
        try
        {
            if (stage < 0 && File.Exists(PlayRequest))
            {
                File.Delete(PlayRequest);
                hud = Find<AdaptiveHealthHUD>(SceneManager.GetSceneByPath("Assets/Scenes/CombatFoundation.unity"));
                var player = Get<PlayerMovement>(hud, "player");
                Require(player != null && player.DamageReceiver != null, "Runtime player receiver exists");
                receiver = player.DamageReceiver;
                oldMaximum = receiver.MaximumHealth; oldHealth = receiver.Health; oldShield = receiver.Shield;
                receiver.SetMaximumHealth(100f, true);
                stage = 0; nextFrame = Time.frameCount + 2;
            }
            if (stage >= 0 && Time.frameCount >= nextFrame) PlayStep();
        }
        catch (Exception e)
        {
            RestorePlayer(); stage = -1;
            if (prefabInstance != null) Object.Destroy(prefabInstance);
            Debug.LogException(e); File.WriteAllText("Library/PlayerHealthHudPlayCheck.result.txt", "FAILED: " + e);
        }
    }


    [MenuItem("Tools/HUD/Export Reusable Player Health HUD")]
    public static void ExportPrefab()
    {
        if (!editReady || Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CombatFoundation.unity");
        var view = Find<AdaptiveHealthHUD>(scene);
        Transform section = view.transform;
        while (section != null && section.name != "HPSection") section = section.parent;
        Require(section != null, "HPSection root");
        var sourceCanvas = section.GetComponentInParent<Canvas>();
        Require(sourceCanvas != null, "Source Canvas");
        UltimateHUDPreview.StopAll();
        var root = new GameObject("PlayerHealthHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.layer = sourceCanvas.gameObject.layer;
        try
        {
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = sourceCanvas.sortingOrder;
            var scaler = sourceCanvas.GetComponent<CanvasScaler>();
            if (scaler != null) EditorUtility.CopySerialized(scaler, root.GetComponent<CanvasScaler>());
            var copy = Object.Instantiate(section.gameObject, root.transform, false); copy.name = "HPSection";
            var copiedHUD = copy.GetComponentInChildren<AdaptiveHealthHUD>(true);
            copiedHUD.BindPlayer(null);
            var so = new SerializedObject(copiedHUD); so.FindProperty("autoFindPlayer").boolValue = true; so.ApplyModifiedProperties();
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI/HUD")) AssetDatabase.CreateFolder("Assets/Prefabs/UI", "HUD");
            var asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Require(asset != null && asset.GetComponentInChildren<UltimateFireEffect>(true) != null, "Complete HUD prefab saved");
            Require(Get<PlayerMovement>(asset.GetComponentInChildren<AdaptiveHealthHUD>(true),"player") == null, "Prefab has no scene-player reference");
            Require(File.ReadAllText(PrefabPath).IndexOf("Adaptive Visuals (Runtime)",StringComparison.Ordinal) < 0, "No transient visuals saved");
            Debug.Log("PLAYER_HEALTH_HUD_PREFAB_SAVED: " + PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [MenuItem("Tools/HUD/Connect Player Health")]
    public static void Install()
    {
        if (!editReady || Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/CombatFoundation.unity");
        var view = Find<AdaptiveHealthHUD>(scene);
        var players = new List<PlayerMovement>();
        foreach (var root in scene.GetRootGameObjects()) players.AddRange(root.GetComponentsInChildren<PlayerMovement>(true));
        Require(view != null && players.Count == 1, "CombatFoundation must contain one player and an HP HUD");
        var so = new SerializedObject(view); so.FindProperty("player").objectReferenceValue = players[0]; so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        Require(EditorSceneManager.SaveScene(scene), "Save connected scene");
        Debug.Log("PLAYER_HEALTH_HUD_CONNECTED: " + players[0].name + " -> " + view.name);
    }

    [MenuItem("Tools/HUD/Run Player Health Checks")]
    public static void Check()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        Scene previous = SceneManager.GetActiveScene();
        Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(test);
        var root = new GameObject("Health checks");
        try
        {
            var health = root.AddComponent<PlayerDamageReceiver>(); health.RestoreHealth();
            Near(health.Health, 100f, "Initial full HP");
            var hit = new DamageHit(DamageHit.NewAttackId(), 25f, DamageKind.Fall, Vector3.zero, Vector3.down);
            Require(health.ReceiveHit(hit) == DamageResult.Damaged, "Damage accepted"); Near(health.Health, 75f, "Damage changes HP");
            Require(health.ReceiveHit(hit) == DamageResult.Duplicate, "Duplicate rejected"); Near(health.Health, 75f, "Duplicate preserves HP");
            health.Heal(10f); Near(health.Health, 85f, "Partial recovery");
            health.Heal(float.NaN); health.Heal(-10f); Near(health.Health, 85f, "Invalid healing ignored");
            health.SetMaximumHealth(300f); Near(health.Health, 85f, "Capacity increase preserves HP");
            health.Heal(999f); Near(health.Health, 300f, "Healing clamps to capacity");
            health.SetMaximumHealth(50f); Near(health.Health, 50f, "Capacity decrease clamps HP");
            int deaths = 0; health.Died += () => deaths++;
            health.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), 100f, DamageKind.Fall, Vector3.zero, Vector3.down));
            Near(health.Health, 0f, "Lethal hit clamps at zero"); Require(deaths == 1, "One death event");
            health.Heal(20f); Near(health.Health, 0f, "Ordinary heal does not respawn");
            health.RestoreHealth(); Near(health.Health, 50f, "Respawn uses real capacity");
            health.SetMaximumHealth(float.NaN); Near(health.MaximumHealth, 100f, "Invalid capacity normalized");
            health.RestoreHealth(); health.SetShield(30f);
            var shieldHit = new DamageHit(DamageHit.NewAttackId(),20f,DamageKind.Attack,Vector3.zero,Vector3.forward);
            health.ReceiveHit(shieldHit); Near(health.Health,100f,"Shield protects HP"); Near(health.Shield,10f,"Shield takes damage first");
            Require(health.ReceiveHit(shieldHit)==DamageResult.Duplicate,"Shield hit duplicate"); Near(health.Shield,10f,"Duplicate preserves shield");
            health.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),25f,DamageKind.Fall,Vector3.zero,Vector3.down));
            Near(health.Shield,0f,"Shield depleted"); Near(health.Health,85f,"Only overflow hurts HP");
            health.AddShield(5000f); Near(health.Shield,5000f,"Shield can exceed maximum HP");
            health.SetShield(float.NaN); Near(health.Shield,0f,"Invalid shield normalized");
            health.AddShield(25f); health.RestoreHealth(); Near(health.Shield,0f,"Respawn clears temporary shield");
            foreach (string name in new[] { "MicroDodge", "DamageRules" })
                typeof(MovementAbilityChecks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }
        finally
        {
            Object.DestroyImmediate(root);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(test, true);
        }
        UltimateHUDChecks.Run();
        Debug.Log("PLAYER_HEALTH_CHECKS_PASSED: damage, duplicate, heal, capacity, death, respawn and existing dodge rules.");
    }

    private static void PlayStep()
    {
        switch (stage++)
        {
            case 0:
                Expect(100,100); testHit = new DamageHit(DamageHit.NewAttackId(),25f,DamageKind.Fall,Vector3.zero,Vector3.down);
                receiver.ReceiveHit(testHit); break;
            case 1:
                Expect(75,100); Require(receiver.ReceiveHit(testHit) == DamageResult.Duplicate, "Duplicate ignored in scene"); receiver.Heal(10); break;
            case 2: Expect(85,100); receiver.SetMaximumHealth(300); break;
            case 3: Expect(85,300); blockWidth = Get<RectTransform>(hud,"visualRoot").rect.width; receiver.RestoreHealth(); break;
            case 4: Expect(300,300); receiver.SetMaximumHealth(5000,true); break;
            case 5:
                Expect(5000,5000); Near(Get<RectTransform>(hud,"visualRoot").rect.width,blockWidth,"5000 HP keeps 300 HP width");
                receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),6000f,DamageKind.Fall,Vector3.zero,Vector3.down)); break;
            case 6: Expect(0,5000); receiver.SetMaximumHealth(100,true); break;
            case 7: Expect(100,100); hud.enabled = false; receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),10f,DamageKind.Fall,Vector3.zero,Vector3.down)); break;
            case 8: hud.enabled = true; break;
            case 9:
                Expect(90,100);
                var so = new SerializedObject(receiver); so.FindProperty("maximumHealth").floatValue = 80; so.ApplyModifiedProperties(); break;
            case 10: Expect(80,80); receiver.SetMaximumHealth(100,true); receiver.SetShield(30); break;
            case 11:
                Expect(100,100); ExpectShield(30);
                testHit = new DamageHit(DamageHit.NewAttackId(),20,DamageKind.Fall,Vector3.zero,Vector3.down); receiver.ReceiveHit(testHit); break;
            case 12:
                Expect(100,100); ExpectShield(10); Require(receiver.ReceiveHit(testHit)==DamageResult.Duplicate,"Shield duplicate in Play");
                receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),25,DamageKind.Fall,Vector3.zero,Vector3.down)); break;
            case 13:
                Expect(85,100); ExpectShield(0);
                prefabInstance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)); break;
            case 14:
                var copy = prefabInstance.GetComponentInChildren<AdaptiveHealthHUD>();
                Require(Get<PlayerMovement>(copy,"player") == Get<PlayerMovement>(hud,"player"),"Prefab automatically found player");
                Near(Get<float>(copy,"currentHealth"),85,"Prefab displays real HP");
                receiver.SetShield(200); break;
            case 15:
                ExpectShield(200);
                Near(Get<float>(prefabInstance.GetComponentInChildren<AdaptiveHealthHUD>(),"extraShield"),200,"Prefab displays real shield");
                Object.Destroy(prefabInstance); prefabInstance = null; RestorePlayer(); break;
            default:
                Expect(oldHealth,oldMaximum); stage = -1;
                File.WriteAllText("Library/PlayerHealthHudPlayCheck.result.txt", "PASS: actual LateUpdate sync, labels, damage, duplicate, healing, max HP, compact width, death, respawn, re-enable, Inspector capacity, shield absorption/overflow and automatic prefab binding.");
                Debug.Log("PLAYER_HEALTH_HUD_PLAY_CHECKS_PASSED"); break;
        }
        nextFrame = Time.frameCount + 2;
    }

    private static void RestorePlayer()
    {
        if (hud != null) hud.enabled = true;
        if (receiver == null) return;
        receiver.SetMaximumHealth(oldMaximum,true);
        if (oldHealth < oldMaximum) receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(),oldMaximum-oldHealth,DamageKind.Fall,Vector3.zero,Vector3.down));
        receiver.SetShield(oldShield);
    }
    private static void ExpectShield(float amount)
    {
        Near(receiver.Shield,amount,"Actual shield"); Near(Get<float>(hud,"extraShield"),amount,"HUD shield");
        Require(Get<Graphic>(hud,"shieldText").gameObject.activeSelf == (amount>0),"Shield text visibility");
    }
    private static void Expect(float current, float maximum)
    {
        Near(receiver.Health,current,"Real HP"); Near(Get<float>(hud,"currentHealth"),current,"HUD HP"); Near(Get<float>(hud,"maxHealth"),maximum,"HUD maximum");
        Require(Label(Get<Graphic>(hud,"hpText")) == current.ToString("N0"),"HP text");
        Require(Label(Get<Graphic>(hud,"maxHPText")) == maximum.ToString("N0"),"Maximum text");
    }
    private static string Label(Graphic graphic) => graphic is TMP_Text tmp ? tmp.text : ((Text)graphic).text;
    private static T Get<T>(object target,string name) => (T)target.GetType().GetField(name,Fields).GetValue(target);
    private static void Near(float actual,float expected,string message) => Require(Mathf.Abs(actual-expected)<.01f,message+": "+actual+" != "+expected);
    private static void Require(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
    private static T Find<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects()) { var item=root.GetComponentInChildren<T>(true); if(item!=null)return item; }
        return null;
    }
}

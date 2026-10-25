using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WeaponSlotsSetup
{
    private const string Request = "Library/WeaponSlotsInstall.request";
    static WeaponSlotsSetup() { EditorApplication.update += Tick; }
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        try { Install(); WeaponSlotsMeleeChecks.Run(); File.WriteAllText("Library/WeaponSlotsChecks.result.txt", "PASS: four typed slots and fist combat checks."); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("Library/WeaponSlotsChecks.result.txt", "FAILED: " + e); }
    }

    [MenuItem("Tools/HUD/Install Weapon Slots")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var chat = Object.FindFirstObjectByType<SinglePlayerChatManager>();
        if (player == null || chat == null) throw new InvalidOperationException("Player and chat canvas required.");
        var loadout = player.GetComponent<PlayerWeaponSlots>();
        if (loadout == null) loadout = Undo.AddComponent<PlayerWeaponSlots>(player.gameObject);
        if (player.GetComponent<PlayerMeleeCombat>() == null) Undo.AddComponent<PlayerMeleeCombat>(player.gameObject);
        if (player.GetComponent<FistsView>() == null) Undo.AddComponent<FistsView>(player.gameObject);
        var canvas = chat.GetComponentInParent<Canvas>();
        var view = canvas.GetComponentInChildren<WeaponSlotsHUD>(true);
        if (view == null)
        {
            var root = Rect("WeaponSlotsHUD", canvas.transform, new Vector2(1,.5f), new Vector2(-36,14), new Vector2(220,390));
            var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = group.interactable = false;
            view = root.gameObject.AddComponent<WeaponSlotsHUD>();
            var settings = new SerializedObject(view);
            settings.FindProperty("source").objectReferenceValue = loadout;
            settings.FindProperty("group").objectReferenceValue = group;
            var rows = settings.FindProperty("slots"); rows.arraySize = PlayerWeaponSlots.SlotCount;
            var chatData = new SerializedObject(chat);
            var field = (TMP_InputField)chatData.FindProperty("chatInput").objectReferenceValue;
            for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) BuildRow(rows.GetArrayElementAtIndex(i), root, field.textComponent.font, i);
            settings.ApplyModifiedProperties();
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Create weapon slots HUD");
        }
        else view.UpgradeToFourSlots();
        view.Refresh(0f);
        EditorUtility.SetDirty(view);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        if (!EditorSceneManager.SaveScene(player.gameObject.scene)) throw new InvalidOperationException("Could not save scene.");
        var copy = Object.Instantiate(view.gameObject);
        try
        {
            copy.name = "WeaponSlotsHUD";
            var data = new SerializedObject(copy.GetComponent<WeaponSlotsHUD>());
            data.FindProperty("source").objectReferenceValue = null; data.ApplyModifiedPropertiesWithoutUndo();
            ProjectAssetPaths.EnsureFolder("Assets/_Project/Prefabs/UI/HUD");
            PrefabUtility.SaveAsPrefabAsset(copy, "Assets/_Project/Prefabs/UI/HUD/WeaponSlotsHUD.prefab");
        }
        finally { Object.DestroyImmediate(copy); }
        Debug.Log("WEAPON_SLOTS_INSTALLED: ranged / ranged / melee / special; empty slots use fists.");
    }
    private static void BuildRow(SerializedProperty entry, RectTransform root, TMP_FontAsset font, int index)
    {
        var row = Rect("Slot " + (index+1), root, new Vector2(1,.5f), new Vector2(0,(1.5f-index)*98), new Vector2(220,88));
        var bg = row.gameObject.AddComponent<Image>(); bg.raycastTarget = false;
        var outline = row.gameObject.AddComponent<Outline>(); outline.effectDistance = new Vector2(1,-1);
        var accent = Rect("Selection accent", row, new Vector2(1,.5f), new Vector2(-2,0), new Vector2(3,80)).gameObject.AddComponent<Image>();
        accent.raycastTarget = false;
        var number = Label("Slot number",row,font,new Vector2(1,1),new Vector2(-12,-7),new Vector2(28,24),18);
        number.text = (index+1).ToString();
        var icon = Rect("Weapon image",row,new Vector2(.5f,.5f),new Vector2(-9,9),new Vector2(156,49)).gameObject.AddComponent<Image>();
        icon.preserveAspect = true; icon.raycastTarget = false;
        var silhouette = Rect("Fallback silhouette",row,new Vector2(.5f,.5f),new Vector2(-9,9),new Vector2(156,49)).gameObject.AddComponent<WeaponSlotSilhouette>();
        silhouette.Kind = 0; silhouette.raycastTarget = false;
        var name = Label("Weapon name",row,font,new Vector2(1,0),new Vector2(-16,9),new Vector2(192,24),15);
        name.alignment = TextAlignmentOptions.BottomRight;
        Assign(entry,"rect",row); Assign(entry,"background",bg); Assign(entry,"accent",accent); Assign(entry,"outline",outline);
        Assign(entry,"icon",icon); Assign(entry,"silhouette",silhouette); Assign(entry,"number",number); Assign(entry,"weaponName",name);
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
        rect.gameObject.layer = parent.gameObject.layer; rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, Vector2 anchor, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect(name,parent,anchor,position,size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.TopRight;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false; return text;
    }
    private static void Assign(SerializedProperty entry, string name, Object value) => entry.FindPropertyRelative(name).objectReferenceValue = value;

    // Explicit batch migration: only the known combat scene and reusable slots HUD.
    public static void UpgradeProjectAssets()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Development/CombatFoundation.unity", OpenSceneMode.Single);
            Install();
            AssetDatabase.SaveAssets();
        }
        finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
    }
}

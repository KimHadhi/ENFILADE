using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MissionInputChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static MissionInputChecks() { EditorApplication.update += ProcessRequest; }
    private static void ProcessRequest()
    {
        const string request = "Library/MissionInputCheck.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !Application.isPlaying || Time.timeSinceLevelLoad < 1f || !Application.isFocused) return;
        File.Delete(request);
        try { Run(); File.WriteAllText("Library/MissionInputCheck.result.txt", "PASS: UI TAB routing, repeated chat/channel/close/board cycle, stale selection, map restoration and shortcut lifecycle."); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("Library/MissionInputCheck.result.txt", "FAILED: " + e); }
    }

    [MenuItem("Tools/HUD/Check Mission and Chat Input")]
    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode before running input checks; Unity suppresses gameplay actions in Edit mode.");
        var asset = Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Actions/Player.inputactions"));
        var root = new GameObject("Mission input checks"); root.SetActive(false);
        var previousEvents = EventSystem.current;
        var previousSelected = previousEvents != null ? previousEvents.currentSelectedGameObject : null;
        var oldLock = Cursor.lockState; bool oldVisible = Cursor.visible;
        Keyboard previousKeyboard = Keyboard.current;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            asset.devices = new UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice>(new InputDevice[] { keyboard });
            var player = root.AddComponent<PlayerInput>(); player.actions = asset;
            var chat = root.AddComponent<SinglePlayerChatManager>();
            var hud = root.AddComponent<MissionHUD>();
            var inputObject = new GameObject("ChatInput", typeof(RectTransform), typeof(TMP_InputField));
            inputObject.transform.SetParent(root.transform);
            var input = inputObject.GetComponent<TMP_InputField>();
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(inputObject.transform);
            input.textComponent = textObject.GetComponent<TextMeshProUGUI>();
            input.textViewport = textObject.GetComponent<RectTransform>();
            Set(chat, "playerInput", player); Set(chat, "chatInput", input);
            Set(hud, "playerInput", player); Set(hud, "chat", chat);
            // Keep the test hierarchy inactive: no real gameplay or scene callbacks can run.
            Call(chat, "OnEnable"); Call(hud, "OnEnable");
            var actions = player.actions;
            Require(actions.FindAction("Player/OpenObjectiveBoard", false) == null, "Board action must leave Player map.");
            var boardAction = actions.FindAction("UI/OpenObjectiveBoard", true);
            var channelAction = actions.FindAction("UI/CycleChatChannel", true);
            Require(boardAction.enabled && channelAction.enabled, "UI shortcuts enabled independently.");
            Require(boardAction.id.ToString() == "2db3f5b1-4f42-4207-8db5-24d5d164b696", "Preserve existing board action ID.");
            var playerMap = actions.FindActionMap("Player", true);
            var combatMap = actions.FindActionMap("Combat", true);
            playerMap.Enable(); combatMap.Enable();
            // An inactive selected TMP field reproduces the stuck-board guard from the reported bug.
            if (previousEvents != null) previousEvents.SetSelectedGameObject(inputObject);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Set(chat, "lastClosedFrame", -1); // Represent the next frame after CloseChat.
                PressTab(keyboard); Call(hud, "ProcessBoardInput");
                Require(hud.IsBoardOpen, $"TAB must open board, including with an inactive selected input. focused={Application.isFocused}, scale={Time.timeScale}, action={boardAction.enabled}/{boardAction.WasPressedThisFrame()}, controls={boardAction.controls.Count}, key={keyboard.tabKey.isPressed}");
                chat.OpenChat(); Call(hud, "ProcessBoardInput");
                Require(!hud.IsBoardOpen && chat.IsChatOpen, "Chat dismisses board.");
                Require(!playerMap.enabled && !combatMap.enabled && boardAction.enabled && channelAction.enabled, "Chat pauses gameplay without disabling UI shortcuts.");
                var before = (ChatChannel)Get(chat, "inputChannel");
                PressTab(keyboard); Call(chat, "Update"); Call(hud, "ProcessBoardInput");
                Require((ChatChannel)Get(chat, "inputChannel") != before && !hud.IsBoardOpen, "TAB only switches chat channel while typing.");
                // Reactivation on a channel switch must not leave a selected hidden chat input.
                if (previousEvents != null) previousEvents.SetSelectedGameObject(inputObject);
                chat.CloseChat(false);
                Require(previousEvents == null || previousEvents.currentSelectedGameObject != inputObject, "Close clears chat selection.");
                Require(playerMap.enabled && combatMap.enabled, "Close restores gameplay maps.");
                Call(hud, "ProcessBoardInput");
                Require(!hud.IsBoardOpen && chat.BlocksMissionInputThisFrame, "A chat TAB cannot also open board on the closing frame.");
            }
            Set(chat, "lastClosedFrame", -1);
            playerMap.Disable(); combatMap.Disable();
            PressTab(keyboard); Call(hud, "ProcessBoardInput");
            Require(hud.IsBoardOpen, "UI board remains usable independently of gameplay maps.");
            chat.OpenChat(); chat.CloseChat(false);
            Require(!playerMap.enabled && !combatMap.enabled, "Do not enable gameplay maps that were already disabled.");
            Call(chat, "OnDisable"); Call(hud, "OnDisable");
            Require(!boardAction.enabled && !channelAction.enabled, "Owned shortcuts release on disable.");
            Call(chat, "OnEnable"); Call(hud, "OnEnable");
            Set(chat, "lastClosedFrame", -1);
            PressTab(keyboard); Call(hud, "ProcessBoardInput");
            Require(hud.IsBoardOpen, "Shortcuts recover after disable/enable.");
            Call(chat, "OnDisable"); Call(hud, "OnDisable");
            Debug.Log("MISSION_INPUT_CHECKS_PASSED");
        }
        finally
        {
            Object.DestroyImmediate(root);
            asset.Disable(); Object.DestroyImmediate(asset);
            InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            Cursor.lockState = oldLock; Cursor.visible = oldVisible;
            if (previousEvents != null) previousEvents.SetSelectedGameObject(previousSelected);
        }
    }
    private static void PressTab(Keyboard keyboard)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Tab)); InputSystem.Update();
    }
    private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

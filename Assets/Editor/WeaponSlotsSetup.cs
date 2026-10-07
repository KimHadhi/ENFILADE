using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WeaponSlotsSetup
{
    private const string Request = "Library/WeaponSlotsInstall.request";
    private const string Result = "Library/WeaponSlotsChecks.result.txt";
    private const string Running = "WeaponSlotsChecks.Running";
    private static int stage, nextFrame;
    private static PlayerWeaponSlots slots;
    private static WeaponSlotsHUD hud;
    private static int ammo;
    private static double previewUntil;
    static WeaponSlotsSetup() { EditorApplication.update += Tick; }
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request))
            {
                File.Delete(Request);
                Install();
                WeaponFoundationChecks.Run();
                SessionState.SetBool(Running,true); stage=0; nextFrame=0;
                EditorApplication.EnterPlaymode();
            }
            if (!SessionState.GetBool(Running,false) || !Application.isPlaying || Time.timeSinceLevelLoad<1f || Time.frameCount<nextFrame) return;
            CheckStep(); nextFrame=Time.frameCount+2;
        }
        catch (Exception e)
        {
            Debug.LogException(e); File.WriteAllText(Result,"FAILED: "+e); Finish();
        }
    }

    [MenuItem("Tools/HUD/Install Weapon Slots")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var chat=Object.FindFirstObjectByType<SinglePlayerChatManager>();
        var player=Object.FindFirstObjectByType<PlayerMovement>();
        Require(chat!=null && player!=null,"Player and chat exist");
        var canvas=chat.GetComponentInParent<Canvas>();
        var loadout=player.GetComponent<PlayerWeaponSlots>();
        if(loadout==null)
        {
            loadout=Undo.AddComponent<PlayerWeaponSlots>(player.gameObject);
            var data=new SerializedObject(loadout);
            data.FindProperty("slot1").objectReferenceValue=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Combat/Weapons/FoundationRifle.asset");
            data.FindProperty("slot2").objectReferenceValue=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Combat/Weapons/FoundationPistol.asset");
            data.ApplyModifiedProperties();
        }
        var view=canvas.GetComponentInChildren<WeaponSlotsHUD>(true);
        if(view==null)
        {
            var chatData=new SerializedObject(chat);
            var input=(TMP_InputField)chatData.FindProperty("chatInput").objectReferenceValue;
            var scroll=(ScrollRect)chatData.FindProperty("scrollRect").objectReferenceValue;
            float height=((RectTransform)scroll.transform).rect.height+((RectTransform)input.transform).rect.height+4f;
            var root=Rect("WeaponSlotsHUD",canvas.transform,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-36,14),new Vector2(220,height));
            var group=root.gameObject.AddComponent<CanvasGroup>(); group.alpha=1; group.blocksRaycasts=false; group.interactable=false;
            view=root.gameObject.AddComponent<WeaponSlotsHUD>();
            var settings=new SerializedObject(view);
            settings.FindProperty("source").objectReferenceValue=loadout;
            settings.FindProperty("group").objectReferenceValue=group;
            float rowHeight=(height-20)/3.12f;
            settings.FindProperty("rowHeight").floatValue=rowHeight;
            var rows=settings.FindProperty("slots"); rows.arraySize=3;
            for(int i=0;i<3;i++)
            {
                var row=Rect("Slot "+(i+1),root,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(0,(1-i)*(rowHeight+10)),new Vector2(220,rowHeight));
                var bg=row.gameObject.AddComponent<Image>(); bg.color=new Color(.035f,.06f,.095f,.58f); bg.raycastTarget=false;
                var outline=row.gameObject.AddComponent<Outline>(); outline.effectDistance=new Vector2(1,-1); outline.effectColor=Color.clear;
                var accent=Rect("Selection accent",row,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(-2,0),new Vector2(3,rowHeight-8)).gameObject.AddComponent<Image>();
                accent.color=Color.clear; accent.raycastTarget=false;
                var number=Label("Slot number",row,input.textComponent.font,new Vector2(1,1),new Vector2(1,1),new Vector2(-12,-7),new Vector2(28,24),18,TextAlignmentOptions.TopRight);
                number.text=(i+1).ToString(); number.color=new Color(.7f,.77f,.8f,1);
                var iconRect=Rect("Weapon image",row,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(-9,9),new Vector2(156,49));
                var icon=iconRect.gameObject.AddComponent<Image>(); icon.preserveAspect=true; icon.raycastTarget=false; icon.enabled=false;
                var placeholder=Rect("Fallback silhouette",row,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(-9,9),new Vector2(156,49)).gameObject.AddComponent<WeaponSlotSilhouette>();
                placeholder.Kind=i==0?1:i==1?2:0; placeholder.color=new Color(.72f,.79f,.83f,i==2?.3f:1); placeholder.raycastTarget=false;
                var name=Label("Weapon name",row,input.textComponent.font,new Vector2(1,0),new Vector2(1,0),new Vector2(-16,9),new Vector2(192,24),15,TextAlignmentOptions.BottomRight);
                name.text=""; name.gameObject.SetActive(false);
                var entry=rows.GetArrayElementAtIndex(i);
                Assign(entry,"rect",row); Assign(entry,"background",bg); Assign(entry,"accent",accent); Assign(entry,"outline",outline);
                Assign(entry,"icon",icon); Assign(entry,"silhouette",placeholder); Assign(entry,"number",number); Assign(entry,"weaponName",name);
            }
            settings.ApplyModifiedProperties();
            Undo.RegisterCreatedObjectUndo(root.gameObject,"Create weapon slots HUD");
        }
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Require(EditorSceneManager.SaveScene(player.gameObject.scene),"Save weapon slots scene");
        if(!AssetDatabase.IsValidFolder("Assets/Prefabs/UI/HUD")) AssetDatabase.CreateFolder("Assets/Prefabs/UI","HUD");
        var copy=Object.Instantiate(view.gameObject);
        try
        {
            copy.name="WeaponSlotsHUD";
            var data=new SerializedObject(copy.GetComponent<WeaponSlotsHUD>()); data.FindProperty("source").objectReferenceValue=null; data.ApplyModifiedPropertiesWithoutUndo();
            Require(PrefabUtility.SaveAsPrefabAsset(copy,"Assets/Prefabs/UI/HUD/WeaponSlotsHUD.prefab")!=null,"Save reusable slots prefab");
        }
        finally { Object.DestroyImmediate(copy); }
        Debug.Log("WEAPON_SLOTS_INSTALLED: rifle/pistol/empty, initial unarmed, right side aligned with chat.");
    }

    private static void CheckStep()
    {
        switch(stage++)
        {
            case 0:
                slots=Object.FindFirstObjectByType<PlayerWeaponSlots>(); hud=Object.FindFirstObjectByType<WeaponSlotsHUD>();
                Require(slots!=null&&hud!=null,"Scene installation");
                Require(slots.SelectedSlot==-1 && slots.GetComponent<WeaponController>().Runtime==null,"Starts unarmed");
                hud.Refresh(1); Near(Field<CanvasGroup>(hud,"group").alpha,0,"Initially hidden");
                CheckKeyboardInput();
                slots.ToggleSlot(0); Require(slots.SelectedSlot==0,"Equip slot 1");
                Require(slots.GetComponent<WeaponController>().EquippedData==slots.GetWeapon(0),"Controller equips actual weapon");
                var runtime=slots.GetRuntime(0); runtime.Tick(.01f,new WeaponInputFrame(true,true)); ammo=runtime.Ammo;
                runtime.Tick(.01f,new WeaponInputFrame(false,false,true)); Require(runtime.IsReloading,"Reload fixture");
                slots.ToggleSlot(1); Require(!runtime.IsReloading,"Switch interrupts reload");
                Require(slots.GetComponent<WeaponController>().EquippedData==slots.GetWeapon(1),"Slot 2 actual data");
                break;
            case 1:
                hud.Refresh(1); var rows=Field<WeaponSlotsHUD.SlotView[]>(hud,"slots");
                Near(Field<CanvasGroup>(hud,"group").alpha,1,"Input reveals HUD");
                Require(rows[1].rect.localScale.x>1.1f && rows[0].rect.localScale.x<1.01f,"Selected enlarges");
                Require(rows[1].weaponName.gameObject.activeSelf&&!rows[0].weaponName.gameObject.activeSelf&&!rows[2].weaponName.gameObject.activeSelf,"Only selected name visible");
                for(int i=0;i<2;i++) Require(rows[i].rect.anchoredPosition.y-rows[i+1].rect.anchoredPosition.y>
                    (rows[i].rect.rect.height*rows[i].rect.localScale.y+rows[i+1].rect.rect.height*rows[i+1].rect.localScale.y)/2f,"Expanded rows do not overlap");
                slots.ToggleSlot(1); Require(slots.SelectedSlot==-1&&slots.GetComponent<WeaponController>().Runtime==null,"Repeat key unequips");
                slots.ToggleSlot(0); Require(slots.GetRuntime(0).Ammo==ammo,"Switch preserves ammo");
                slots.ToggleSlot(2); Require(slots.SelectedSlot==-1,"Empty slot returns to unarmed");
                var data=new SerializedObject(slots); data.FindProperty("slot3").objectReferenceValue=slots.GetWeapon(1); data.ApplyModifiedPropertiesWithoutUndo();
                slots.ToggleSlot(2); Require(slots.SelectedSlot==2,"Third slot equips when assigned");
                Require(slots.GetRuntime(2)!=slots.GetRuntime(1),"Duplicate weapon data has independent magazines");
                slots.ToggleSlot(2); Require(slots.SelectedSlot==-1,"Third slot toggles off");
                var chat=Object.FindFirstObjectByType<SinglePlayerChatManager>(); chat.OpenChat();
                Require(!slots.CanReadInput&&!slots.HandleSlotInput(0)&&slots.SelectedSlot==-1,"Typing numbers in chat cannot equip"); chat.CloseChat(false);
                slots.ToggleSlot(0); Set(hud,"lastActivity",Time.unscaledTime-3.5f); hud.Refresh(1);
                Near(Field<CanvasGroup>(hud,"group").alpha,.5f,"Idle fades gradually");
                Set(hud,"lastActivity",Time.unscaledTime-5f); hud.Refresh(1); Near(Field<CanvasGroup>(hud,"group").alpha,0,"Idle fully hides");
                slots.ToggleSlot(1); hud.Refresh(1); Near(Field<CanvasGroup>(hud,"group").alpha,1,"Input during hidden state reveals again");
                Require(!Field<CanvasGroup>(hud,"group").blocksRaycasts,"HUD does not block gameplay pointer");
                File.WriteAllText(Result,"PASS: existing weapon regressions; Input System keyboard 1/2/3 and numpad selection/toggle, three slots, repeated selection returns to unarmed, actual controller data, magazine isolation/preservation, reload interruption, empty slot, chat input suppression, initial hidden/idle fade/reveal, selected-only name, enlargement and non-overlapping neighbours.");
                Debug.Log("WEAPON_SLOTS_CHECKS_PASSED"); previewUntil=EditorApplication.timeSinceStartup+15;
                break;
            case 2:
                if(EditorApplication.timeSinceStartup<previewUntil) { hud.Reveal(); stage--; } else Finish();
                break;
        }
    }
    private static void CheckKeyboardInput()
    {
        var previous=Cursor.lockState;
        var keyboard=Keyboard.current;
        Require(keyboard!=null,"Keyboard available");
        try
        {
            Cursor.lockState=CursorLockMode.Locked;
            Require(slots.CanReadInput,"Gameplay input gate: focused="+Application.isFocused+", movement="+slots.GetComponent<PlayerMovement>().InputAvailable+", map="+slots.GetComponent<PlayerInput>().actions.FindActionMap("Player").enabled);
            var update=typeof(PlayerWeaponSlots).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
            foreach(var key in new[] {Key.Digit1,Key.Digit1,Key.Digit2,Key.Digit2,Key.Digit3,Key.Numpad1,Key.Numpad1})
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update();
                int before=slots.SelectedSlot;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(key)); InputSystem.Update(); update.Invoke(slots,null);
                int index=key==Key.Digit2?1:key==Key.Digit3?2:0;
                int expected=slots.GetWeapon(index)==null||before==index?-1:index;
                Require(slots.SelectedSlot==expected,"Keyboard selection: "+key);
            }
            Require(slots.SelectedSlot==-1,"Keyboard checks end unarmed");
        }
        finally { InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update(); Cursor.lockState=previous; }
    }
    private static void Finish() { SessionState.SetBool(Running,false); if(EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); }
    private static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false); rect.gameObject.layer=parent.gameObject.layer;
        rect.anchorMin=rect.anchorMax=anchor; rect.pivot=pivot; rect.anchoredPosition=position; rect.sizeDelta=size; return rect;
    }
    private static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size,float fontSize,TextAlignmentOptions alignment)
    {
        var text=Rect(name,parent,anchor,pivot,position,size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font=font; text.fontSize=fontSize; text.alignment=alignment; text.textWrappingMode=TextWrappingModes.NoWrap; text.overflowMode=TextOverflowModes.Ellipsis;
        text.color=new Color(.78f,.96f,1); text.raycastTarget=false; return text;
    }
    private static void Assign(SerializedProperty entry,string name,Object value) => entry.FindPropertyRelative(name).objectReferenceValue=value;
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    private static void Near(float actual,float expected,string message)=>Require(Mathf.Abs(actual-expected)<.01f,message+": "+actual);
    private static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}

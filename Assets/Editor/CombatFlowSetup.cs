using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class CombatFlowSetup
{
    private const string Request="Library/CombatFlowSetup.request", Result="Library/CombatFlowChecks.result.txt", Running="CombatFlowChecks.Running";
    private static int stage;
    private static double nextTime, deadline;
    private static WaveManager wave;
    private static PlayerMovement player;
    private static SinglePlayerChatManager chat;
    private static float frozenDelay, frozenCharge;
    private static EnemyHealth pausedEnemy, unrelatedEnemy;
    private static Vector3 frozenPosition;
    static CombatFlowSetup() { EditorApplication.update+=Tick; }
    private static void Tick()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating) return;
        try
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request))
            {
                File.Delete(Request); Install(); SessionState.SetBool(Running,true); stage=0;
                EditorApplication.EnterPlaymode();
            }
            if(!SessionState.GetBool(Running,false)||!Application.isPlaying||Time.timeSinceLevelLoad<1f||EditorApplication.timeSinceStartup<nextTime) return;
            CheckStep();
        }
        catch(Exception e) { Debug.LogException(e); File.WriteAllText(Result,"FAILED: "+e); Finish(); }
    }

    [MenuItem("Tools/Combat/Install Chat Test Stations")]
    public static void Install()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Require(scene.name=="CombatFoundation","Open CombatFoundation first");
        var p=Object.FindFirstObjectByType<PlayerMovement>(); var c=Object.FindFirstObjectByType<SinglePlayerChatManager>();
        Require(p!=null&&c!=null,"Player/chat found");
        foreach(var w in Object.FindObjectsByType<WaveManager>(FindObjectsSortMode.None))
        { var so=new SerializedObject(w); so.FindProperty("autoStart").boolValue=false; so.ApplyModifiedProperties(); }
        var interaction=p.GetComponent<PlayerTestInteraction>(); if(interaction==null) interaction=Undo.AddComponent<PlayerTestInteraction>(p.gameObject);
        var canvas=c.GetComponentInParent<Canvas>();
        var chatSO=new SerializedObject(c); var input=(TMP_InputField)chatSO.FindProperty("chatInput").objectReferenceValue;
        var existing=canvas.transform.Find("InteractionPrompt");
        TMP_Text prompt;
        if(existing!=null) prompt=existing.GetComponent<TMP_Text>();
        else
        {
            var go=new GameObject("InteractionPrompt",typeof(RectTransform)); go.transform.SetParent(canvas.transform,false); go.layer=canvas.gameObject.layer;
            var rect=(RectTransform)go.transform; rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f); rect.anchoredPosition=new Vector2(0,-100); rect.sizeDelta=new Vector2(520,44);
            prompt=go.AddComponent<TextMeshProUGUI>(); prompt.font=input.textComponent.font; prompt.fontSize=24;
            prompt.alignment=TextAlignmentOptions.Center; prompt.color=new Color(.6f,1, .9f); prompt.raycastTarget=false; prompt.text="[F] 상호작용";
            go.SetActive(false); Undo.RegisterCreatedObjectUndo(go,"Create interaction prompt");
        }
        var settings=new SerializedObject(interaction); settings.FindProperty("view").objectReferenceValue=p.GetComponentInChildren<Camera>();
        settings.FindProperty("chat").objectReferenceValue=c; settings.FindProperty("prompt").objectReferenceValue=prompt; settings.ApplyModifiedProperties();
        if(!scene.GetRootGameObjects().Any(go=>go.name=="Chat Test Stations"))
        {
            var root=new GameObject("Chat Test Stations"); Undo.RegisterCreatedObjectUndo(root,"Create chat test stations");
            var channels=new[]{ChatChannel.Dialogue,ChatChannel.Broadcast,ChatChannel.Team,ChatChannel.All};
            for(int i=0;i<channels.Length;i++)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=ChatStyle.Label(channels[i])+" Test Button"; go.transform.SetParent(root.transform);
                go.transform.position=p.transform.position+p.transform.forward*4+p.transform.right*((i-1.5f)*1.6f)+Vector3.up*.6f;
                go.transform.rotation=p.transform.rotation; go.transform.localScale=new Vector3(1.1f,1.2f,.35f);
                var station=go.AddComponent<ChatTestStation>(); var so=new SerializedObject(station); so.FindProperty("channel").enumValueIndex=(int)channels[i]; so.ApplyModifiedProperties();
                string materialPath="Assets/Materials/ChatTest"+channels[i]+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if(material==null)
                {
                    material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard")); material.color=ChatStyle.ColorFor(channels[i]);
                    AssetDatabase.CreateAsset(material,materialPath);
                }
                go.GetComponent<Renderer>().sharedMaterial=material;
                var labelGO=new GameObject("Station label"); labelGO.transform.SetParent(root.transform);
                labelGO.transform.position=go.transform.position+Vector3.up*1.05f; labelGO.transform.rotation=p.transform.rotation;
                var label=labelGO.AddComponent<TextMeshPro>(); label.font=input.textComponent.font; label.fontSize=2.5f; label.alignment=TextAlignmentOptions.Center;
                label.rectTransform.sizeDelta=new Vector2(1.5f,.9f); label.text="[F]\n"+ChatStyle.Label(channels[i])+" 테스트"; label.color=ChatStyle.ColorFor(channels[i]);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene),"Save updated CombatFoundation");
        AssetDatabase.SaveAssets();
        Debug.Log("COMBAT_FLOW_INSTALLED: manual waves, Interact/F and four chat test stations.");
    }

    private static void CheckStep()
    {
        nextTime=EditorApplication.timeSinceStartup+.2;
        switch(stage++)
        {
            case 0:
                wave=Object.FindFirstObjectByType<WaveManager>(); player=Object.FindFirstObjectByType<PlayerMovement>(); chat=Object.FindFirstObjectByType<SinglePlayerChatManager>();
                Require(wave!=null&&player!=null&&chat!=null,"Scene dependencies");
                Require(!wave.IsRunning&&wave.AliveCount==0,"No automatic wave on game start");
                CheckHitCharge(); CheckChatAndInteraction();
                Set(wave,"timeBetweenWaves",.3f); Set(wave,"spawnInterval",.15f);
                Run("/wave start"); Require(wave.IsRunning&&wave.CurrentWave==0,"Start begins countdown");
                float delay=wave.RemainingDelay; Run("/wave start"); Near(wave.RemainingDelay,delay,"Duplicate start preserves countdown");
                Run("/wave stop"); Require(wave.IsPaused,"Stop pauses countdown"); frozenDelay=wave.RemainingDelay;
                break;
            case 1:
                Near(wave.RemainingDelay,frozenDelay,"Paused countdown does not advance"); Require(wave.AliveCount==0,"No paused spawns");
                Run("/wave resume"); Require(!wave.IsPaused,"Resume countdown"); deadline=EditorApplication.timeSinceStartup+5;
                break;
            case 2:
                if(wave.AliveCount==0) { Require(EditorApplication.timeSinceStartup<deadline,"Real NavMesh spawn: "+wave.LastError); stage--; break; }
                Require(wave.CurrentWave==1,"First wave index");
                Run("/wave pause"); pausedEnemy=OwnedEnemies().First(); Require(pausedEnemy.WavePaused,"Wave enemy frozen");
                frozenDelay=wave.RemainingDelay; frozenPosition=pausedEnemy.transform.position; frozenCharge=player.Ultimate.CurrentCharge;
                Require(pausedEnemy.ReceiveHit(Hit(10,player.gameObject))==DamageResult.Ignored,"Paused enemy ignores damage");
                break;
            case 3:
                Near(wave.RemainingDelay,frozenDelay,"Spawn timer paused"); Near(Vector3.Distance(frozenPosition,pausedEnemy.transform.position),0,"Enemy movement paused");
                Near(player.Ultimate.CurrentCharge,frozenCharge,"No charge for paused enemy");
                Run("/wave resume"); Require(!pausedEnemy.WavePaused,"Enemy resumes"); deadline=EditorApplication.timeSinceStartup+5;
                break;
            case 4:
                if(wave.Phase!=WaveManager.WavePhase.Combat) { Require(EditorApplication.timeSinceStartup<deadline,"Spawn completes"); stage--; break; }
                foreach(var enemy in OwnedEnemies().ToArray()) enemy.ReceiveHit(Hit(10000,null));
                break;
            case 5:
                Require(wave.Phase==WaveManager.WavePhase.Countdown,"Clear schedules next wave");
                unrelatedEnemy=new GameObject("Unrelated enemy fixture").AddComponent<EnemyHealth>();
                Run("/wave stop"); Run("/wave abort");
                Require(!wave.IsRunning&&!wave.IsPaused&&wave.AliveCount==0&&wave.CurrentWave==0,"Abort resets paused wave");
                Require(unrelatedEnemy!=null&&unrelatedEnemy.IsAlive,"Abort affects owned enemies only");
                Run("/wave start"); deadline=EditorApplication.timeSinceStartup+5; break;
            case 6:
                if(wave.AliveCount==0) { Require(EditorApplication.timeSinceStartup<deadline,"Restart spawns"); stage--; break; }
                Require(wave.CurrentWave==1,"Restart from wave one");
                var spawned=OwnedEnemies().ToArray(); Run("/wave abort"); Require(spawned.All(e=>e==null||!e.gameObject.activeSelf),"Abort immediately disables owned enemies");
                Require(Run("/wave invalid").StartsWith("사용법:"),"Invalid command rejected");
                File.WriteAllText(Result,"PASS: actual Play, idle startup, valid/duplicate/lethal/invalid/non-player hit charge, HUD source, all message types, real F InputAction/raycast and chat blocking, countdown and live-enemy pause/resume, full wave clear, abort cleanup/ownership, restart and command validation.");
                Debug.Log("COMBAT_FLOW_CHECKS_PASSED"); Finish(); break;
        }
    }
    private static void CheckHitCharge()
    {
        var enemy=new GameObject("Hit charge fixture").AddComponent<EnemyHealth>();
        try
        {
            player.Ultimate.SetValues(0,120,100); float gain=Field<float>(player.Ultimate,"chargePerEnemyHit");
            var hit=Hit(10,player.gameObject); Require(enemy.ReceiveHit(hit)==DamageResult.Damaged,"Player enemy hit accepted"); Near(player.Ultimate.CurrentCharge,gain,"Hit grants charge");
            Require(enemy.ReceiveHit(hit)==DamageResult.Duplicate,"Duplicate rejected"); Near(player.Ultimate.CurrentCharge,gain,"Duplicate grants nothing");
            enemy.ReceiveHit(Hit(float.NaN,player.gameObject)); enemy.ReceiveHit(Hit(1,null)); Near(player.Ultimate.CurrentCharge,gain,"Invalid/environment grants nothing");
            enemy.ReceiveHit(Hit(10000,player.gameObject)); Near(player.Ultimate.CurrentCharge,gain*2,"Lethal hit grants once");
            enemy.ReceiveHit(Hit(10,player.gameObject)); Near(player.Ultimate.CurrentCharge,gain*2,"Dead target grants nothing");
            var hud=Object.FindFirstObjectByType<UltimateHUD>(); Require(UltimateCharge.ResolveHUDSource(hud,null)==player.Ultimate,"HUD shares awarded resource");
        }
        finally { if(enemy!=null) Object.DestroyImmediate(enemy.gameObject); }
    }
    private static void CheckChatAndInteraction()
    {
        int count=chat.History.Count; Run("/chattest mixed"); Require(chat.History.Count==count+5,"Five sample messages");
        foreach(var channel in new[]{ChatChannel.Dialogue,ChatChannel.Broadcast,ChatChannel.Team,ChatChannel.All})
            Require(chat.History.Skip(count).Any(entry=>entry.Channel==channel),"Message type: "+channel);
        var interaction=player.GetComponent<PlayerTestInteraction>(); var camera=player.GetComponentInChildren<Camera>();
        var station=Object.FindObjectsByType<ChatTestStation>(FindObjectsSortMode.None).First();
        Vector3 oldPosition=camera.transform.position; Quaternion oldRotation=camera.transform.rotation; var oldLock=Cursor.lockState;
        var keyboard=Keyboard.current; var update=typeof(PlayerTestInteraction).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
        try
        {
            Cursor.lockState=CursorLockMode.Locked;
            camera.transform.position=station.transform.position-station.transform.forward*2;
            camera.transform.LookAt(station.transform.position); Physics.SyncTransforms();
            var action=player.GetComponent<PlayerInput>().actions.FindAction("Player/Interact");
            Require(action!=null&&action.enabled&&action.bindings.Any(b=>b.path=="<Keyboard>/f"),"Interact/F action enabled");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update();
            count=chat.History.Count; InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F)); InputSystem.Update(); update.Invoke(interaction,null);
            Require(interaction.Target==station&&chat.History.Count>count,"F raycast activates station");
            chat.OpenChat(); count=chat.History.Count;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F)); InputSystem.Update(); update.Invoke(interaction,null);
            Require(interaction.Target==null&&chat.History.Count==count,"Typing F in chat cannot interact"); chat.CloseChat(false);
        }
        finally { InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update(); camera.transform.SetPositionAndRotation(oldPosition,oldRotation); Cursor.lockState=oldLock; }
    }
    private static System.Collections.Generic.HashSet<EnemyHealth> OwnedEnemies()=>Field<System.Collections.Generic.HashSet<EnemyHealth>>(wave,"aliveEnemies");
    private static DamageHit Hit(float damage,GameObject source)=>new DamageHit(DamageHit.NewAttackId(),damage,DamageKind.Attack,Vector3.zero,Vector3.forward,source);
    private static string Run(string command) { string output=chat.Commands.Execute(command); Require(output==null||!output.StartsWith("명령어 실행에 실패"),command); return output; }
    private static void Finish() { SessionState.SetBool(Running,false); if(unrelatedEnemy!=null) Object.Destroy(unrelatedEnemy.gameObject); if(EditorApplication.isPlaying) EditorApplication.ExitPlaymode(); }
    private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    private static void Near(float actual,float expected,string message)=>Require(Mathf.Abs(actual-expected)<.001f,message+": "+actual+" != "+expected);
    private static void Require(bool value,string message) { if(!value) throw new InvalidOperationException(message); }
}

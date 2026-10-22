using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Native NavMeshAgents only activate in Play Mode; run in an isolated batch project.</summary>
[InitializeOnLoad]
public static class ExecutionCrowdPlayChecks
{
    private const string Pending = "Morphang.ExecutionCrowdChecks";
    private static GameObject root, wall;
    private static PlayerExecution execution;
    private static MeleeCombatTarget target;
    private static NavMeshAgent agent;
    private static NavMeshData data;
    private static NavMeshDataInstance nav;
    private static double started;
    private static int stage;
    private static Vector3 origin = new Vector3(1000,0,1000);
    static ExecutionCrowdPlayChecks() => EditorApplication.playModeStateChanged += StateChanged;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this check in an isolated Unity batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Pending,true); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false)) return;
        try { Setup(); EditorApplication.update += Tick; }
        catch(Exception e) { Finish(e); }
    }
    private static GameObject Box(string name, Vector3 offset, Vector3 size)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform); go.transform.position = origin + offset;
        go.AddComponent<BoxCollider>().size = size; return go;
    }
    private static void Setup()
    {
        root = new GameObject("Execution crowd check");
        Box("Floor",new Vector3(0,-.1f,0),new Vector3(20,.2f,20));
        var source = new NavMeshBuildSource {shape=NavMeshBuildSourceShape.Box,
            transform=Matrix4x4.TRS(origin+Vector3.down*.1f,Quaternion.identity,Vector3.one), size=new Vector3(20,.2f,20)};
        data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0),new List<NavMeshBuildSource>{source},
            new Bounds(Vector3.zero,new Vector3(22,10,22)),origin,Quaternion.identity);
        nav = NavMesh.AddNavMeshData(data);
        var playerObject = new GameObject("Player"); playerObject.transform.SetParent(root.transform); playerObject.transform.position=origin+Vector3.up;
        playerObject.AddComponent<CharacterController>();
        var camera = new GameObject("Camera"); camera.transform.SetParent(playerObject.transform,false); camera.AddComponent<Camera>();
        var player = playerObject.AddComponent<PlayerMovement>(); execution=player.Execution;
        // The interaction transaction is covered by Edit Mode checks. Hold its crowd phase here without synthetic focus/input.
        execution.enabled=false;
        typeof(PlayerExecution).GetField("<IsExecuting>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(execution,true);
        typeof(PlayerExecution).GetField("<ActiveExecution>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,execution);
        var enemy=Box("Bystander",new Vector3(1.1f,0,0),new Vector3(.6f,1.8f,.6f));
        enemy.GetComponent<BoxCollider>().center=Vector3.up*.9f;
        enemy.AddComponent<EnemyHealth>(); target=enemy.GetComponent<MeleeCombatTarget>();
        agent=enemy.AddComponent<NavMeshAgent>(); agent.radius=.3f;
        Require(agent.Warp(origin+new Vector3(1.1f,0,0)),"Agent must activate on the runtime NavMesh");
        wall=Box("Unbaked wall",new Vector3(1.9f,1,0),new Vector3(.2f,2f,3f));
        Physics.SyncTransforms(); started=EditorApplication.timeSinceStartup; stage=0;
    }
    private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private static void Tick()
    {
        try
        {
            double elapsed=EditorApplication.timeSinceStartup-started;
            if(elapsed>10) throw new TimeoutException("Crowd simulation timed out");
            if(stage==0 && elapsed>.6)
            {
                Require(agent.isOnNavMesh && agent.isStopped,"Crowd navigation remains stopped");
                Require(agent.transform.position.x>origin.x+1.13f && agent.transform.position.x<origin.x+1.8f,"Close enemy retreats but cannot pass physical cover");
                Object.DestroyImmediate(wall); Physics.SyncTransforms(); stage=1; started=EditorApplication.timeSinceStartup;
            }
            else if(stage==1 && elapsed>1.2)
            {
                float distance=Vector3.ProjectOnPlane(agent.transform.position-execution.transform.position,Vector3.up).magnitude;
                Require(distance>=2.55f && distance<=2.65f,"Enemy backs away to the clearance radius and stops");
                target.ApplyPostureDamage(100f);
                agent.Warp(origin+new Vector3(1.1f,0,0));
                target.Tick(.1f);
                Require(agent.transform.position.x<origin.x+1.15f,"Staggered enemy cannot perform a fear retreat");
                target.ResetPosture(); execution.Cancel(); target.Tick(.01f);
                Require(!PlayerExecution.Suppresses(target) && !agent.isStopped,"Completion restores navigation");
                Debug.Log("EXECUTION_CROWD_PLAY_CHECKS_PASSED: retreat, physical cover, clearance, stagger and cleanup");
                CheckShoveMotion(); Finish(null);
            }
        }
        catch(Exception e) { Finish(e); }
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update-=Tick; SessionState.EraseBool(Pending); execution?.Cancel();
        if(root!=null) Object.DestroyImmediate(root); nav.Remove(); if(data!=null) Object.DestroyImmediate(data);
        if(error!=null) Debug.LogException(error);
        EditorApplication.Exit(error==null?0:1);
    }
    private static void CheckShoveMotion()
    {
        foreach (int hz in new[]{30,60,120})
        {
            target.ResetPosture(); agent.isStopped=false;
            Require(agent.Warp(origin+new Vector3(1.1f,0,0)),"Shove fixture warp"); Physics.SyncTransforms();
            float start=agent.nextPosition.x;
            target.Interrupt(.85f); target.ReceiveShove(Vector3.right*8f,agent.transform.position);
            for(int i=0;i<hz/2;i++) target.Tick(1f/hz);
            Require(Mathf.Abs(agent.nextPosition.x-start-1.28f)<.06f,"Decelerating knockback covers 1.28m at "+hz+"Hz: "+(agent.nextPosition.x-start));
            Require(target.IsStunned && agent.isStopped,"Enemy keeps reeling after physical knockback ends");
            target.Tick(.4f); Require(!target.IsStunned && !agent.isStopped,"Enemy recovers after the reeling window");
        }
        target.ResetPosture(); agent.Warp(origin+new Vector3(1.1f,0,0));
        wall=Box("Shove wall",new Vector3(1.9f,1,0),new Vector3(.2f,2f,3f)); Physics.SyncTransforms();
        target.Interrupt(.85f); target.ReceiveShove(Vector3.right*8f,agent.transform.position);
        for(int i=0;i<24;i++) target.Tick(1f/60f);
        Require(agent.nextPosition.x>origin.x+1.13f && agent.nextPosition.x<origin.x+1.51f,"Strong shove cannot push an enemy through an unbaked wall");
        Debug.Log("SHOVE_MOTION_PLAY_CHECKS_PASSED: 30/60/120Hz displacement, reeling recovery and wall collision");
    }
}

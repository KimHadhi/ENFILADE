using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Run only in an isolated batch project; checks actual prefab spawning on a native NavMesh.</summary>
[InitializeOnLoad]
public static class ConsoleSpawnPlayChecks
{
    private const string Pending = "Morphang.ConsoleSpawnChecks";
    private static NavMeshData data;
    private static NavMeshDataInstance nav;
    static ConsoleSpawnPlayChecks() => EditorApplication.playModeStateChanged += StateChanged;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        try { Check(); Debug.Log("CONSOLE_SPAWN_PLAY_CHECKS_PASSED"); Finish(0); }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name); go.transform.position = position;
        go.AddComponent<BoxCollider>().size = size; return go;
    }
    private static void Check()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Enemy_Test.prefab").GetComponent<EnemyHealth>();
        Vector3 origin = new Vector3(1000, 0, 1000);
        Box("Floor", origin + Vector3.down * .1f, new Vector3(20, .2f, 20));
        var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.TRS(origin + Vector3.down * .1f, Quaternion.identity, Vector3.one), size = new Vector3(20, .2f, 20) };
        data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(prefab.GetComponent<NavMeshAgent>().agentTypeID),
            new List<NavMeshBuildSource> { source }, new Bounds(Vector3.zero, new Vector3(22, 10, 22)), origin, Quaternion.identity);
        nav = NavMesh.AddNavMeshData(data);
        var go = new GameObject("Player"); go.transform.position = origin + Vector3.up;
        go.AddComponent<CharacterController>();
        var camera = new GameObject("Camera"); camera.transform.SetParent(go.transform, false); camera.AddComponent<Camera>();
        var player = go.AddComponent<PlayerMovement>();
        var wave = new GameObject("Wave").AddComponent<WaveManager>();
        typeof(WaveManager).GetField("enemyPrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(wave, prefab);
        var commands = new ChatCommandRouter(); PlayerConsoleCommands.Register(commands, () => player);
        Require(commands.Help().Contains("/spawn"), "Spawn appears in console help");
        Require(commands.Execute("/spawn 2").StartsWith("사용법:"), "One-enemy command rejects arguments");
        Physics.SyncTransforms();
        string result = commands.Execute("/spawn");
        Require(result.StartsWith("적 한 마리를 소환했습니다"), "First spawn succeeds: " + result);
        var enemies = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        Require(enemies.Length == 1, "Exactly one actual enemy is spawned");
        var first = enemies[0];
        Require(first.IsAlive && first.GetComponent<EnemyCombatAI>() != null && first.GetComponent<EnemyChaseAI>() != null
            && first.GetComponent<NavMeshAgent>().isOnNavMesh, "Spawned prefab has initialized health, AI and active navigation");
        Require(first.transform.parent.name == "Console Enemies" && first.gameObject.scene == player.gameObject.scene, "Spawn group belongs to the player's scene");
        Require(first.GetComponent<Collider>().bounds.min.y > -.1f, "Enemy body is placed above the floor");
        var hud = first.GetComponent<EnemyVitalsHUD>(); hud.Refresh(camera.GetComponent<Camera>());
        Require(first.transform.Find("Combat UI") != null, "Spawned enemy owns its UI hierarchy");
        Physics.SyncTransforms(); result = commands.Execute("spawn");
        enemies = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        Require(result.StartsWith("적 한 마리를 소환했습니다") && enemies.Length == 2, "Second command adds exactly one enemy in another free spot");
        Require(Vector3.Distance(enemies[0].transform.position, enemies[1].transform.position) > .99f, "Repeated spawns do not overlap");
        Require(wave.AliveCount == 0 && wave.TotalEnemiesThisWave == 0 && !wave.IsRunning, "Manual enemies do not alter wave counts or start a wave");
        Box("Blocking wall", origin + new Vector3(0, 2, 1.5f), new Vector3(20, 4, .2f)); Physics.SyncTransforms();
        Require(!commands.Execute("/spawn").StartsWith("적 한 마리를 소환했습니다"), "Cannot spawn beyond an unbaked wall");
        Require(Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Length == 2, "Failed spawn creates no enemy");
        NavMesh.RemoveNavMeshData(nav);
        Require(!commands.Execute("/spawn").StartsWith("적 한 마리를 소환했습니다"), "Missing NavMesh produces a friendly failure");
    }
    private static void Finish(int code)
    {
        if (nav.valid) NavMesh.RemoveNavMeshData(nav);
        if (data != null) Object.Destroy(data);
        EditorApplication.Exit(code);
    }
}

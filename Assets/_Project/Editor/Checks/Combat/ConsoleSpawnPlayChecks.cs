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
        var legacyFolder = new GameObject("Console Enemies");
        Require(commands.Help().Contains("/spawn"), "Spawn appears in console help");
        foreach (string bad in new[] { "0", "-1", "1.5", "NaN", "2147483648", "1 2", "abc" })
            Require(commands.Execute("/spawn " + bad).StartsWith("사용법:"), "Spawn rejects an invalid count: " + bad);
        Require(Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Length == 0, "Invalid counts do not spawn enemies");
        Physics.SyncTransforms();
        string result = commands.Execute("/spawn");
        Require(result.StartsWith("적 한 마리를 소환했습니다"), "First spawn succeeds: " + result);
        var enemies = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        Require(enemies.Length == 1, "Exactly one actual enemy is spawned");
        var first = enemies[0];
        Require(first.IsAlive && first.GetComponent<EnemyCombatAI>() != null && first.GetComponent<EnemyChaseAI>() != null
            && first.GetComponent<NavMeshAgent>().isOnNavMesh, "Spawned prefab has initialized health, AI and active navigation");
        var enemyCategory = first.transform.parent.parent;
        Require(first.transform.parent.name == "Console" && enemyCategory != null && enemyCategory.name.Contains("ENEMIES")
            && enemyCategory != legacyFolder.transform && first.gameObject.scene == player.gameObject.scene,
            "Console enemies use their scene's ENEMIES/Console category, not a similarly named legacy folder");
        Object.Destroy(legacyFolder);
        Require(first.GetComponent<Collider>().bounds.min.y > -.1f, "Enemy body is placed above the floor");
        var hud = first.GetComponent<EnemyVitalsHUD>(); hud.Refresh(camera.GetComponent<Camera>());
        Require(first.transform.Find("Combat UI") != null, "Spawned enemy owns its UI hierarchy");
        Physics.SyncTransforms(); result = commands.Execute("spawn");
        enemies = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        Require(result.StartsWith("적 한 마리를 소환했습니다") && enemies.Length == 2, "Second command adds exactly one enemy in another free spot");
        Require(Vector3.Distance(enemies[0].transform.position, enemies[1].transform.position) > .99f, "Repeated spawns do not overlap");
        result = commands.Execute("/spawn 3");
        enemies = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
        Require(result.StartsWith("적 3마리를 소환했습니다") && enemies.Length == 5, "Explicit count spawns exactly three additional enemies");
        for (int i = 0; i < enemies.Length; i++)
            for (int j = i + 1; j < enemies.Length; j++)
                Require(Vector3.Distance(enemies[i].transform.position, enemies[j].transform.position) > .99f, "Enemies in the same command do not overlap");
        result = commands.Execute("/spawn 100000");
        int countBeforeWall = Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Length;
        Require(countBeforeWall >= 5 && countBeforeWall < 100000, "A crowded spawn request stops when no safe space remains");
        Require(countBeforeWall == 5 ? !result.StartsWith("적 ") : result.StartsWith($"적 {countBeforeWall - 5}/100000마리"), "Partial spawn reports the actual count");
        Require(wave.AliveCount == 0 && wave.TotalEnemiesThisWave == 0 && !wave.IsRunning, "Manual enemies do not alter wave counts or start a wave");
        Box("Blocking wall", origin + new Vector3(0, 2, 1.5f), new Vector3(20, 4, .2f)); Physics.SyncTransforms();
        Require(!commands.Execute("/spawn").StartsWith("적 한 마리를 소환했습니다"), "Cannot spawn beyond an unbaked wall");
        Require(Object.FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None).Length == countBeforeWall, "Failed spawn creates no enemy");
        CheckWaveCategory(wave, first, origin, enemyCategory);
        NavMesh.RemoveNavMeshData(nav);
        Require(!commands.Execute("/spawn").StartsWith("적 한 마리를 소환했습니다"), "Missing NavMesh produces a friendly failure");
    }

    private static void CheckWaveCategory(WaveManager wave, EnemyHealth consoleEnemy, Vector3 origin, Transform category)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
        var point = new GameObject("Wave hierarchy spawn point").transform;
        point.position = origin + new Vector3(-4f, 0f, -3f);
        typeof(WaveManager).GetField("spawnPoints", any).SetValue(wave, new[] { point });
        Require(wave.StartWaves(), "Wave starts with its configured prefab and spawn point");
        var spawn = typeof(WaveManager).GetMethod("SpawnEnemy", any);
        Require((bool)spawn.Invoke(wave, null) && (bool)spawn.Invoke(wave, null), "Wave spawns two real enemies on the native NavMesh");
        var tracked = (HashSet<EnemyHealth>)typeof(WaveManager).GetField("aliveEnemies", any).GetValue(wave);
        var spawned = new List<EnemyHealth>(tracked);
        Require(wave.AliveCount == 2, "Only wave enemies enter wave tracking");
        foreach (var enemy in spawned)
            Require(enemy.transform.parent.name == "Waves" && enemy.transform.parent.parent == category
                && enemy.gameObject.scene == wave.gameObject.scene && enemy.GetComponent<NavMeshAgent>().isOnNavMesh,
                "Wave enemies share ENEMIES/Waves in the spawning scene with working navigation");
        Require(wave.PauseWaves(), "Grouped wave can pause");
        foreach (var enemy in spawned) Require(enemy.WavePaused, "Pause reaches every wave enemy");
        Require(!consoleEnemy.WavePaused, "Console enemy is not paused with a wave");
        Require(wave.ResumeWaves(), "Grouped wave can resume");
        foreach (var enemy in spawned) Require(!enemy.WavePaused, "Resume reaches every wave enemy");
        wave.AbortWaves();
        Require(wave.AliveCount == 0 && !wave.IsRunning, "Abort clears wave tracking");
        foreach (var enemy in spawned) Require(!enemy.gameObject.activeSelf, "Abort cleans up only its tracked enemies");
        Require(consoleEnemy.IsAlive && consoleEnemy.gameObject.activeSelf, "Abort leaves console enemies intact under the shared category");
        Debug.Log("ENEMY_HIERARCHY_CHECKS_PASSED");
    }
    private static void Finish(int code)
    {
        if (nav.valid) NavMesh.RemoveNavMeshData(nav);
        if (data != null) Object.Destroy(data);
        EditorApplication.Exit(code);
    }
}

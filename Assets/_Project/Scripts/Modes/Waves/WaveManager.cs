using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class WaveManager : MonoBehaviour
{
    /// <summary>Data/Resources/Tables/Modes/Waves.csv: enemy counts and timing per wave set.</summary>
    public const string TableName = "Waves";

    [Tooltip("Waves.csv의 행 id. 맵마다 다른 웨이브 구성을 쓰려면 행을 추가하고 여기에 적는다.")]
    [SerializeField] private string tableId = "Default";
    [Header("Enemy")]
    [SerializeField] private EnemyHealth enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [Header("Wave Settings")]
    [SerializeField] private bool autoStart;
    [SerializeField, Min(1)] private int initialEnemies = 3;
    [SerializeField, Min(0)] private int extraEnemiesPerWave = 2;
    [SerializeField, Min(.1f)] private float timeBetweenWaves = 5f;
    [SerializeField, Min(0)] private float spawnInterval = .5f;
    [Header("Last enemies")]
    [Tooltip("웨이브 중 남은 적이 이 수 이하가 되면 벽 너머로 보인다(EnemyReveal). 0이면 끈다.")]
    [SerializeField, Min(0)] private int revealRemaining = 5;
    [Tooltip("벽 너머로 보이는 적의 색. 알파가 진하기다.")]
    [SerializeField] private Color revealColor = new Color(1f, .2f, .15f, .63f);

    public enum WavePhase { Idle, Countdown, Spawning, Combat }
    /// <summary>What the screen shows about the waves, which the host sends to the other PCs (WaveSync).</summary>
    public struct Status : Unity.Netcode.INetworkSerializeByMemcpy, System.IEquatable<Status>
    {
        public WavePhase Phase;
        public bool Paused;
        public int Wave, Defeated, Total, SecondsLeft;
        public bool Equals(Status other) => Phase == other.Phase && Paused == other.Paused && Wave == other.Wave
            && Defeated == other.Defeated && Total == other.Total && SecondsLeft == other.SecondsLeft;
    }
    public WavePhase Phase { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsRunning => Phase != WavePhase.Idle;
    public int CurrentWave => currentWave;
    public int AliveCount => aliveEnemies.Count;
    public EnemyHealth EnemyPrefab => enemyPrefab;
    public float RemainingDelay => remainingDelay;
    public string LastError { get; private set; }
    public int TotalEnemiesThisWave => totalEnemiesThisWave;
    public int DefeatedEnemies => defeatedEnemies;
    public Status CurrentStatus => new Status { Phase = Phase, Paused = IsPaused, Wave = currentWave, Defeated = defeatedEnemies,
        Total = totalEnemiesThisWave, SecondsLeft = Mathf.CeilToInt(remainingDelay) };
    /// <summary>A wave is under way with few enough enemies left to show them through walls (EnemyReveal).</summary>
    public bool RevealsRemaining
    {
        get
        {
            int left = totalEnemiesThisWave - defeatedEnemies;
            return (Phase == WavePhase.Spawning || Phase == WavePhase.Combat) && !IsPaused && left > 0 && left <= revealRemaining;
        }
    }
    public Color RevealColor => revealColor;
    public event System.Action ObjectiveChanged;
    /// <summary>A new wave began, here or on the host. The dead come back with it.</summary>
    public event System.Action WaveStarted;
    private readonly HashSet<EnemyHealth> aliveEnemies = new HashSet<EnemyHealth>();
    private int currentWave, totalEnemiesThisWave, defeatedEnemies, spawned;
    private float remainingDelay;

    private void Awake() => GameTables.Bind(TableName, tableId, this);

    private void Start()
    {
        if (!IsRunning) NotifyObjective();
        if (GameSession.IsOnline) gameObject.AddComponent<WaveSync>();
        gameObject.AddComponent<EnemyReveal>();
        if (autoStart) StartWaves();
    }

    /// <summary>In a room, the host's waves as the other PCs see them.</summary>
    internal void ShowHostStatus(in Status status)
    {
        bool newWave = status.Wave > currentWave;
        Phase = status.Phase; IsPaused = status.Paused; currentWave = status.Wave;
        if (newWave) WaveStarted?.Invoke();
        defeatedEnemies = status.Defeated; totalEnemiesThisWave = status.Total; remainingDelay = status.SecondsLeft;
        NotifyObjective();
    }

    public bool StartWaves()
    {
        if (!isActiveAndEnabled || IsRunning) return false;
        LastError = null;
        if (GameSession.IsGuest) { LastError = "웨이브는 호스트만 시작할 수 있습니다."; return false; }
        if (enemyPrefab == null || spawnPoints == null || System.Array.Find(spawnPoints, p => p != null) == null)
        { LastError = "Enemy Prefab과 Spawn Points 연결을 확인하세요."; return false; }
        currentWave = totalEnemiesThisWave = defeatedEnemies = spawned = 0;
        IsPaused = false;
        BeginCountdown(); NotifyObjective();
        return true;
    }

    public bool PauseWaves()
    {
        if (!IsRunning || IsPaused) return false;
        IsPaused = true;
        foreach (var enemy in aliveEnemies) if (enemy != null) enemy.SetWavePaused(true);
        return true;
    }

    public bool ResumeWaves()
    {
        if (!IsRunning || !IsPaused) return false;
        IsPaused = false;
        foreach (var enemy in aliveEnemies) if (enemy != null) enemy.SetWavePaused(false);
        return true;
    }

    public void AbortWaves()
    {
        Phase = WavePhase.Idle; IsPaused = false; remainingDelay = 0;
        foreach (var enemy in aliveEnemies)
        {
            if (enemy == null) continue;
            enemy.Died -= OnEnemyDied;
            enemy.gameObject.SetActive(false);
            Destroy(enemy.gameObject);
        }
        aliveEnemies.Clear();
        currentWave = totalEnemiesThisWave = defeatedEnemies = spawned = 0;
        NotifyObjective();
    }

    /// <summary>After a wipe: the enemies go, and it all starts again from the first wave.</summary>
    public void RestartFromFirst()
    {
        AbortWaves();
        StartWaves();
    }

    private void Update()
    {
        if (IsPaused) return;
        if (!IsRunning || Time.deltaTime <= 0 || GameSession.IsGuest) return;
        if (Phase == WavePhase.Countdown)
        {
            remainingDelay = Mathf.Max(0,remainingDelay-Time.deltaTime);
            if (remainingDelay > 0) return;
            currentWave++; defeatedEnemies = spawned = 0;
            totalEnemiesThisWave = (int)System.Math.Min(10000L,System.Math.Max(1,initialEnemies)+(long)(currentWave-1)*System.Math.Max(0,extraEnemiesPerWave));
            Phase = WavePhase.Spawning; remainingDelay = 0; NotifyObjective();
            WaveStarted?.Invoke();
        }
        if (Phase == WavePhase.Spawning)
        {
            remainingDelay -= Time.deltaTime;
            if (remainingDelay <= 0)
            {
                if (!SpawnEnemy())
                {
                    AbortWaves(); LastError = "스폰 지점 주변 NavMesh를 찾지 못했습니다.";
                    Debug.LogWarning("[WaveManager] " + LastError,this); return;
                }
                spawned++; remainingDelay = Mathf.Max(0,spawnInterval);
                if (spawned >= totalEnemiesThisWave) Phase = WavePhase.Combat;
            }
        }
        // External destruction must not leave a completed wave stuck forever.
        int removed = aliveEnemies.RemoveWhere(enemy => enemy == null);
        if (removed > 0) { defeatedEnemies += removed; NotifyObjective(); }
        if (Phase == WavePhase.Combat && aliveEnemies.Count == 0) BeginCountdown();
    }

    private void BeginCountdown()
    { Phase=WavePhase.Countdown; remainingDelay=Mathf.Max(0,timeBetweenWaves); NotifyObjective(); }
    private bool SpawnEnemy()
    {
        int offset=Random.Range(0,spawnPoints.Length);
        for (int i=0;i<spawnPoints.Length;i++)
        {
            var point=spawnPoints[(i+offset)%spawnPoints.Length];
            if(point==null || !NavMesh.SamplePosition(point.position,out NavMeshHit navHit,2f,NavMesh.AllAreas)) continue;
            // A Support's searchlight keeps its ground safe: nothing comes up within its reach (Barricade.Shelters).
            if(Barricade.Shelters(navHit.position)) continue;
            var container = RuntimeSceneHierarchy.GetGroup(gameObject.scene, RuntimeSceneHierarchy.Enemies, "Waves");
            var enemy=Instantiate(enemyPrefab,navHit.position,point.rotation,container);
            EnemyNetwork.SpawnInRoom(enemy);
            aliveEnemies.Add(enemy); enemy.Died+=OnEnemyDied; return true;
        }
        return false;
    }
    private void OnEnemyDied(EnemyHealth enemy)
    {
        if(!aliveEnemies.Remove(enemy)) return;
        enemy.Died-=OnEnemyDied; defeatedEnemies++; NotifyObjective();
    }
    // The mission HUD (WaveMissionSource) and the other PCs (WaveSync) read the numbers from here when told.
    private void NotifyObjective() => ObjectiveChanged?.Invoke();
    private void OnDisable() { if(Application.isPlaying) AbortWaves(); }
}

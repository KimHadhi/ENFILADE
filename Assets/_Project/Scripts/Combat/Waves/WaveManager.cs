using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public sealed class WaveManager : MonoBehaviour
{
    [Header("Enemy")]
    [SerializeField] private EnemyHealth enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [Header("Wave Settings")]
    [SerializeField] private bool autoStart;
    [SerializeField, Min(1)] private int initialEnemies = 3;
    [SerializeField, Min(0)] private int extraEnemiesPerWave = 2;
    [SerializeField, Min(.1f)] private float timeBetweenWaves = 5f;
    [SerializeField, Min(0)] private float spawnInterval = .5f;
    [Header("Objective UI")]
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private TMP_Text enemyCountText;
    [SerializeField] private Image progressBar;
    [Header("Assault Status UI")]
    [SerializeField] private TMP_Text assaultStatusText;
    [SerializeField] private string activeMessage = "ASSAULT IN PROGRESS";
    [SerializeField] private string preparingMessage = "NEXT ASSAULT IN";
    [Header("Progress Bar Animation")]
    [SerializeField, Min(.1f)] private float fillSpeed = 1.5f;
    [Header("Objective")]
    [SerializeField] private string objectiveMessage = "Eliminate the enemy";

    public enum WavePhase { Idle, Countdown, Spawning, Combat }
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
    public event System.Action ObjectiveChanged;
    private readonly HashSet<EnemyHealth> aliveEnemies = new HashSet<EnemyHealth>();
    private int currentWave, totalEnemiesThisWave, defeatedEnemies, spawned;
    private float targetFillAmount, remainingDelay;

    private void Start()
    {
        if (!IsRunning) { UpdateObjectiveUI(); UpdateAssaultStatus("ASSAULT STANDBY"); }
        if (autoStart) StartWaves();
    }

    public bool StartWaves()
    {
        if (!isActiveAndEnabled || IsRunning) return false;
        LastError = null;
        if (enemyPrefab == null || spawnPoints == null || System.Array.Find(spawnPoints, p => p != null) == null)
        { LastError = "Enemy Prefab과 Spawn Points 연결을 확인하세요."; return false; }
        currentWave = totalEnemiesThisWave = defeatedEnemies = spawned = 0;
        IsPaused = false;
        BeginCountdown(); UpdateObjectiveUI();
        return true;
    }

    public bool PauseWaves()
    {
        if (!IsRunning || IsPaused) return false;
        IsPaused = true;
        foreach (var enemy in aliveEnemies) if (enemy != null) enemy.SetWavePaused(true);
        UpdateAssaultStatus("ASSAULT PAUSED");
        return true;
    }

    public bool ResumeWaves()
    {
        if (!IsRunning || !IsPaused) return false;
        IsPaused = false;
        foreach (var enemy in aliveEnemies) if (enemy != null) enemy.SetWavePaused(false);
        RefreshStatus(); return true;
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
        UpdateObjectiveUI();
        if (progressBar != null) progressBar.fillAmount = 0;
        UpdateAssaultStatus("ASSAULT STOPPED");
    }

    // Preserve the existing public stop entry point as a complete stop.
    public void StopWaves() => AbortWaves();

    private void Update()
    {
        if (IsPaused) return;
        if (progressBar != null) progressBar.fillAmount = Mathf.MoveTowards(progressBar.fillAmount,targetFillAmount,fillSpeed*Time.deltaTime);
        if (!IsRunning || Time.deltaTime <= 0) return;
        if (Phase == WavePhase.Countdown)
        {
            remainingDelay = Mathf.Max(0,remainingDelay-Time.deltaTime); RefreshStatus();
            if (remainingDelay > 0) return;
            currentWave++; defeatedEnemies = spawned = 0;
            totalEnemiesThisWave = (int)System.Math.Min(10000L,System.Math.Max(1,initialEnemies)+(long)(currentWave-1)*System.Math.Max(0,extraEnemiesPerWave));
            Phase = WavePhase.Spawning; remainingDelay = 0; UpdateObjectiveUI(); RefreshStatus();
        }
        if (Phase == WavePhase.Spawning)
        {
            remainingDelay -= Time.deltaTime;
            if (remainingDelay <= 0)
            {
                if (!SpawnEnemy())
                {
                    AbortWaves(); LastError = "스폰 지점 주변 NavMesh를 찾지 못했습니다.";
                    UpdateAssaultStatus("SPAWN FAILED"); Debug.LogWarning("[WaveManager] " + LastError,this); return;
                }
                spawned++; remainingDelay = Mathf.Max(0,spawnInterval);
                if (spawned >= totalEnemiesThisWave) Phase = WavePhase.Combat;
            }
        }
        // External destruction must not leave a completed wave stuck forever.
        int removed = aliveEnemies.RemoveWhere(enemy => enemy == null);
        if (removed > 0) { defeatedEnemies += removed; UpdateObjectiveUI(); }
        if (Phase == WavePhase.Combat && aliveEnemies.Count == 0) BeginCountdown();
    }

    private void BeginCountdown()
    { Phase=WavePhase.Countdown; remainingDelay=Mathf.Max(0,timeBetweenWaves); RefreshStatus(); UpdateObjectiveUI(); }
    private void RefreshStatus() => UpdateAssaultStatus(IsPaused ? "ASSAULT PAUSED" : Phase==WavePhase.Countdown
        ? $"{preparingMessage} {Mathf.CeilToInt(remainingDelay)}..." : activeMessage);
    private bool SpawnEnemy()
    {
        int offset=Random.Range(0,spawnPoints.Length);
        for (int i=0;i<spawnPoints.Length;i++)
        {
            var point=spawnPoints[(i+offset)%spawnPoints.Length];
            if(point==null || !NavMesh.SamplePosition(point.position,out NavMeshHit navHit,2f,NavMesh.AllAreas)) continue;
            var enemy=Instantiate(enemyPrefab,navHit.position,point.rotation);
            aliveEnemies.Add(enemy); enemy.Died+=OnEnemyDied; return true;
        }
        return false;
    }
    private void OnEnemyDied(EnemyHealth enemy)
    {
        if(!aliveEnemies.Remove(enemy)) return;
        enemy.Died-=OnEnemyDied; defeatedEnemies++; UpdateObjectiveUI();
    }
    private void UpdateObjectiveUI()
    {
        if(objectiveText!=null) objectiveText.text=objectiveMessage;
        if(enemyCountText!=null) enemyCountText.text=$"{defeatedEnemies} / {totalEnemiesThisWave}";
        targetFillAmount=totalEnemiesThisWave>0 ? (float)defeatedEnemies/totalEnemiesThisWave : 0;
        ObjectiveChanged?.Invoke();
    }
    private void UpdateAssaultStatus(string message) { if(assaultStatusText!=null) assaultStatusText.text=message; }
    private void OnDisable() { if(Application.isPlaying) AbortWaves(); }
}

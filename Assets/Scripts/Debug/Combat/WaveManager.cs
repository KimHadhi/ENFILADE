
using System.Collections;
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
    [SerializeField, Min(1)] private int initialEnemies = 3;
    [SerializeField, Min(0)] private int extraEnemiesPerWave = 2;
    [SerializeField, Min(0.1f)] private float timeBetweenWaves = 5f;
    [SerializeField, Min(0f)] private float spawnInterval = 0.5f;

    [Header("Objective UI")]
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private TMP_Text enemyCountText;
    [SerializeField] private Image progressBar;

    [Header("Assault Status UI")]
    [SerializeField] private TMP_Text assaultStatusText;
    [SerializeField] private string activeMessage = "ASSAULT IN PROGRESS";
    [SerializeField] private string preparingMessage = "NEXT ASSAULT IN";

    [Header("Progress Bar Animation")]
    [SerializeField, Min(0.1f)] private float fillSpeed = 1.5f;

    [Header("Objective")]
    [SerializeField] private string objectiveMessage = "Eliminate the enemy";

    private readonly HashSet<EnemyHealth> aliveEnemies =
        new HashSet<EnemyHealth>();

    private int currentWave;
    private int totalEnemiesThisWave;
    private int defeatedEnemies;

    private float targetFillAmount;
    private bool startingWave;
    private bool gameOver;

    private void Start()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("[WaveManager] Enemy Prefab is missing.", this);
            enabled = false;
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[WaveManager] Spawn Points are missing.", this);
            enabled = false;
            return;
        }

        if (objectiveText != null)
            objectiveText.text = objectiveMessage;

        targetFillAmount = 0f;

        if (progressBar != null)
            progressBar.fillAmount = 0f;

        StartCoroutine(StartNextWave());
    }

    private void Update()
    {
        if (progressBar == null)
            return;

        progressBar.fillAmount = Mathf.MoveTowards(
            progressBar.fillAmount,
            targetFillAmount,
            fillSpeed * Time.deltaTime
        );
    }


    private IEnumerator StartNextWave()
    {
        if (startingWave || gameOver)
            yield break;

        startingWave = true;

        // 첫 웨이브를 포함해 매번 준비 카운트다운 표시
        int countdown = Mathf.CeilToInt(timeBetweenWaves);

        for (int seconds = countdown; seconds > 0; seconds--)
        {
            UpdateAssaultStatus($"{preparingMessage} {seconds}...");

            yield return new WaitForSeconds(1f);

            if (gameOver)
            {
                startingWave = false;
                yield break;
            }
        }

        currentWave++;
        defeatedEnemies = 0;

        totalEnemiesThisWave =
            initialEnemies + (currentWave - 1) * extraEnemiesPerWave;

        targetFillAmount = 0f;
        UpdateObjectiveUI();
        UpdateAssaultStatus(activeMessage);

        Debug.Log($"[WaveManager] WAVE {currentWave} started.");

        for (int i = 0; i < totalEnemiesThisWave; i++)
        {
            if (gameOver)
                break;

            SpawnEnemy();

            if (spawnInterval > 0f && i < totalEnemiesThisWave - 1)
                yield return new WaitForSeconds(spawnInterval);
        }

        startingWave = false;

        if (!gameOver && aliveEnemies.Count == 0)
            StartCoroutine(StartNextWave());
    }


    private void SpawnEnemy()
    {
        Transform point = spawnPoints[
            Random.Range(0, spawnPoints.Length)
        ];

        if (point == null)
        {
            Debug.LogWarning("[WaveManager] A spawn point is missing.", this);
            return;
        }

        if (!NavMesh.SamplePosition(
            point.position, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
        {
            Debug.LogWarning(
                $"[WaveManager] No NavMesh near {point.name}.", point);
            return;
        }

        EnemyHealth enemy = Instantiate(
            enemyPrefab, navHit.position, point.rotation
        );

        aliveEnemies.Add(enemy);
        enemy.Died += OnEnemyDied;
    }

    private void OnEnemyDied(EnemyHealth enemy)
    {
        if (!aliveEnemies.Remove(enemy))
            return;

        enemy.Died -= OnEnemyDied;
        defeatedEnemies++;

        UpdateObjectiveUI();

        if (defeatedEnemies >= totalEnemiesThisWave &&
            !startingWave && !gameOver)
        {
            StartCoroutine(StartNextWave());
        }
    }

    private void UpdateObjectiveUI()
    {
        if (objectiveText != null)
            objectiveText.text = objectiveMessage;

        if (enemyCountText != null)
        {
            enemyCountText.text =
                $"{defeatedEnemies} / {totalEnemiesThisWave}";
        }

        targetFillAmount = totalEnemiesThisWave > 0
            ? (float)defeatedEnemies / totalEnemiesThisWave
            : 0f;
    }

    private void UpdateAssaultStatus(string message)
    {
        if (assaultStatusText != null)
            assaultStatusText.text = message;
    }

    public void StopWaves()
    {
        gameOver = true;
        StopAllCoroutines();
        startingWave = false;
        UpdateAssaultStatus("ASSAULT STOPPED");
    }

    private void OnDestroy()
    {
        foreach (EnemyHealth enemy in aliveEnemies)
        {
            if (enemy != null)
                enemy.Died -= OnEnemyDied;
        }

        aliveEnemies.Clear();
    }
}

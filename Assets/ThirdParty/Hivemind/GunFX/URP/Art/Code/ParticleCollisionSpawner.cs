using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class RotationRange
{
    public Vector2 x = new Vector2(0, 0);
    public Vector2 y = new Vector2(0, 0);
    public Vector2 z = new Vector2(0, 0);
}

public class ParticleCollisionSpawner : MonoBehaviour
{
    [Header("Prefab Settings")]
    public GameObject prefabToSpawn;
    public ParticleSystem ps;

    [Header("Pooling Settings")]
    public int poolSize = 50;
    private Queue<GameObject> objectPool;

    [Header("Position Offset")]
    public Vector3 positionOffset = Vector3.zero;

    [Header("Random Rotation")]
    public RotationRange randomRotationRange;

    [Header("Random Scale")]
    public Vector2 sizeRange = new Vector2(1f, 1f);

    [Header("Destruction Delay (Seconds)")]
    public Vector2 destroyDelayRange = new Vector2(2f, 5f);

    [Header("Decal Color Over Lifetime")]
    public Gradient decalColorOverLifetime;

    private List<ParticleCollisionEvent> collisionEvents;

    void Start()
    {
        if (ps == null)
            ps = GetComponent<ParticleSystem>();

        collisionEvents = new List<ParticleCollisionEvent>();
        InitializePool();
    }

    void InitializePool()
    {
        objectPool = new Queue<GameObject>();

        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(prefabToSpawn);
            obj.transform.SetParent(ps.transform);
            obj.SetActive(false);
            objectPool.Enqueue(obj);
        }
    }

    GameObject GetPooledObject()
    {
        if (objectPool.Count > 0)
        {
            return objectPool.Dequeue();
        }
        else
        {
            GameObject obj = Instantiate(prefabToSpawn);
            obj.transform.SetParent(ps.transform);
            obj.SetActive(false);
            return obj;
        }
    }

    void ReturnToPool(GameObject obj)
    {
        obj.SetActive(false);
        objectPool.Enqueue(obj);
    }

    void OnParticleCollision(GameObject other)
    {
        int numCollisionEvents = ps.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < numCollisionEvents; i++)
        {
            Vector3 collisionPos = collisionEvents[i].intersection;
            Vector3 collisionNormal = collisionEvents[i].normal;

            Vector3 spawnPos =
                collisionPos +
                collisionNormal * positionOffset.z +
                Vector3.right * positionOffset.x +
                Vector3.up * positionOffset.y;

            float rotX = Random.Range(randomRotationRange.x.x, randomRotationRange.x.y);
            float rotY = Random.Range(randomRotationRange.y.x, randomRotationRange.y.y);
            float rotZ = Random.Range(randomRotationRange.z.x, randomRotationRange.z.y);

            Quaternion baseRotation = Quaternion.LookRotation(collisionNormal);
            Quaternion randomRotation = Quaternion.Euler(rotX, rotY, rotZ);
            Quaternion finalRotation = baseRotation * randomRotation;

            GameObject spawned = GetPooledObject();
            spawned.transform.position = spawnPos;
            spawned.transform.rotation = finalRotation;

            float randomScale = Random.Range(sizeRange.x, sizeRange.y);
            spawned.transform.localScale = Vector3.one * randomScale;

            spawned.SetActive(true);

            float lifetime = Random.Range(destroyDelayRange.x, destroyDelayRange.y);

            // Decal Projector setup
            var projector = spawned.GetComponentInChildren<DecalProjector>();
            if (projector != null)
            {
                Material instancedMat = new Material(projector.material);
                instancedMat.SetColor("_DecalColor", decalColorOverLifetime.Evaluate(0f));
                projector.material = instancedMat;

                StartCoroutine(LerpDecalProjectorColor(instancedMat, lifetime));
            }

            StartCoroutine(DisableAfterDelay(spawned, lifetime));
        }
    }

    IEnumerator LerpDecalProjectorColor(Material mat, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            float normalizedTime = t / duration;
            Color lerped = decalColorOverLifetime.Evaluate(normalizedTime);
            mat.SetColor("_DecalColor", lerped);

            t += Time.deltaTime;
            yield return null;
        }

        mat.SetColor("_DecalColor", decalColorOverLifetime.Evaluate(1f));
    }

    IEnumerator DisableAfterDelay(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        ReturnToPool(obj);
    }
}
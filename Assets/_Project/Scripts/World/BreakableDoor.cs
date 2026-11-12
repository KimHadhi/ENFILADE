using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A door a body breaks by running, sliding or diving into it fast enough. It breaks for every player: whoever breaks it
/// tells the others (DoorBreaks). Standing, it is a carving NavMeshObstacle, which the walk mesh's bake leaves out, so the
/// mesh runs through the doorway: enemies go round a closed door and through a broken one (ENFILADE > 맵 > 문 길찾기 다시
/// 굽기 bakes the scene that way).
/// </summary>
[RequireComponent(typeof(Collider))]
public class BreakableDoor : MonoBehaviour, IBreakable
{
    [Header("Effects")]
    [SerializeField] private GameObject fracturePrefab; // 산산조각 나는 문 파편 프리팹 (선택)
    [SerializeField] private AudioClip breakSound;       // 파괴 효과음 (선택)

    private static readonly List<BreakableDoor> all = new List<BreakableDoor>();

    /// <summary>Every door in the open scenes, broken ones too.</summary>
    internal static IReadOnlyList<BreakableDoor> All => all;
    internal bool IsBroken { get; private set; }

    private void Awake()
    {
        all.Add(this);
        if (!TryGetComponent(out NavMeshObstacle obstacle)) obstacle = gameObject.AddComponent<NavMeshObstacle>();
        ShapeObstacle(obstacle);
    }

    private void Start() => DoorBreaks.Listen();

    private void OnDestroy() => all.Remove(this);

    public void Break(Vector3 hitPoint, Vector3 hitNormal, float impactSpeed)
    {
        if (IsBroken) return;
        Shatter(hitPoint, impactSpeed, false);
        DoorBreaks.Tell(this, hitPoint, impactSpeed);
    }

    /// <summary>Breaks it on this PC only; quiet for a door that was down before this PC joined.</summary>
    internal void Shatter(Vector3 hitPoint, float impactSpeed, bool quiet)
    {
        if (IsBroken) return;
        IsBroken = true;
        if (!quiet)
        {
            if (breakSound != null) GameAudio.PlayAt(breakSound, hitPoint, AudioChannel.Combat);
            if (fracturePrefab != null) Instantiate(fracturePrefab, transform.position, transform.rotation);
        }
        // Its collider and its carve go with it, so players and enemies go straight through. It is switched off rather
        // than destroyed, so the host can tell anyone who joins later which doors are down.
        gameObject.SetActive(false);
    }

    /// <summary>A box over the door that cuts the walk mesh while it stands.</summary>
    public void ShapeObstacle(NavMeshObstacle obstacle)
    {
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        if (TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
        {
            obstacle.center = filter.sharedMesh.bounds.center;
            obstacle.size = filter.sharedMesh.bounds.size;
        }
        else if (TryGetComponent(out BoxCollider box))
        {
            obstacle.center = box.center;
            obstacle.size = box.size;
        }
    }
}

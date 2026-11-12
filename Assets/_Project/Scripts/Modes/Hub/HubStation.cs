using UnityEngine;

/// <summary>
/// A place in the hub where something is laid out or done (memo 46). The builder (Editor/Maps/HubBuilder) stands one on
/// each piece of furniture; at play HubLobby finds them and fills them from the tables, so a new chapter, class or gun
/// shows up without rebuilding the scene: the chapters' cards on the map table, a locker door per class on the class
/// wall, a gun per FPSBody gun on the rack, the mirror's glass and dressing spot. <see cref="Size"/> is the area to lay
/// things out in, on the station's own axes: x across, y up, z out of the face (a table's top faces up its local y, a
/// wall's face looks along its local z).
/// </summary>
[DisallowMultipleComponent]
public sealed class HubStation : MonoBehaviour
{
    public enum Kind
    {
        /// <summary>The map table: a card per chapter lies on its top; [F] on one picks where the party goes.</summary>
        MapTable,
        /// <summary>The ready button: [F] says this player is ready (or not).</summary>
        Ready,
        /// <summary>The class wall: a locker door per class; [F] on one plays it.</summary>
        ClassWall,
        /// <summary>The gun rack: every gun hung on it; [F] takes one into the hands.</summary>
        GunRack,
        /// <summary>The ammo crate: [F] fills every gun.</summary>
        Ammo,
        /// <summary>The board over the way out: the party, who is ready, the chapter and the count.</summary>
        ReadyBoard,
        /// <summary>The way out's shutter: it rises as the count runs down.</summary>
        DepartDoor,
        /// <summary>The sign over the map table: the chapter the party will go to.</summary>
        ChapterSign,
        /// <summary>The board over the firing line: the last hit on a range target, and how many it took to drop one.</summary>
        RangeBoard,
        /// <summary>The stage's mirror (HubMirror on the same object, its size the glass's): [F] at it opens the dressing
        /// spot in front of it (HubDressing).</summary>
        Mirror
    }

    [SerializeField] private Kind kind;
    [SerializeField, Tooltip("The area things are laid out in, on this object's own axes (m).")]
    private Vector3 size = Vector3.one;

    public Kind Station => kind;
    public Vector3 Size => size;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, .31f, .47f, .8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, size);
    }
}

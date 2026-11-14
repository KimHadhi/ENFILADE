using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The room each face leaves the blink, measured on the character's own face mesh (FaceRoom) the first time the face is
/// made on it: a face that has shut the eyes, or put drawn eyes in front of them, blinks no more, whatever its row in
/// Faces.csv says and with no row at all for a creator's expression or a modder's avatar. Kept for the run per mesh,
/// character and face; a table read again asks again.
/// </summary>
public sealed partial class CharacterFace
{
    // A face mesh's eyes as a character's blink has them, and the room of each face asked about so far.
    private sealed class Eyes
    {
        public FaceRoom Room;
        // How far the face being asked about moves each vertex, and one key's moves as read.
        public Vector3[] Face, Key;
        public readonly Dictionary<string, float> Rooms = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Dictionary<Mesh, Dictionary<string, Eyes>> eyesOf = new Dictionary<Mesh, Dictionary<string, Eyes>>();

    private bool roomsAsked;

    private static void ForgetRooms() => eyesOf.Clear();

    // The share of its blink the slot's face has room for (FaceRoom), asked once.
    private float RoomOf(Slot slot)
    {
        if (slot.Room >= 0f) return slot.Room;
        // Whatever goes wrong reading a mesh nobody here has seen, the face blinks as its row says.
        slot.Room = 1f;
        try
        {
            slot.Room = Measure(slot);
        }
        catch (Exception error)
        {
            Warn($"{Skin} {slot.Mood.Id}: 깜박일 여유를 재지 못했습니다({error.GetType().Name}: {error.Message}).");
        }
        return slot.Room;
    }

    // One face not asked about yet, a frame: by the time a mood is first made in a fight its room is known, and a model
    // made again (a wardrobe preview) finds them all kept.
    private void AskAhead()
    {
        if (roomsAsked) return;
        foreach (var slot in slots)
        {
            if (slot.Room >= 0f || !slot.Keys.Any) continue;
            RoomOf(slot);
            return;
        }
        roomsAsked = true;
    }

    private float Measure(Slot slot)
    {
        if (!blink.Any || !slot.Keys.Any || FaceTables.Number("Room.on", 1f) <= 0f) return 1f;
        var on = BlinkMesh();
        var mesh = on != null ? on.sharedMesh : null;
        if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) return 1f;
        var eyes = EyesOn(on, mesh);
        if (eyes == null) return 1f;
        if (eyes.Rooms.TryGetValue(slot.Mood.Id, out float room)) return room;
        Array.Clear(eyes.Face, 0, eyes.Face.Length);
        room = Moves(slot.Keys, on, mesh, eyes.Face, eyes.Key) ? eyes.Room.Measure(eyes.Face).Room : 1f;
        eyes.Rooms[slot.Mood.Id] = room;
        return room;
    }

    // The mesh the blink is judged on: of those with its key, the one with the most keys (the face, not a mask or a
    // fringe made to follow one of its keys).
    private SkinnedMeshRenderer BlinkMesh()
    {
        SkinnedMeshRenderer best = null;
        int most = -1;
        foreach (var mesh in channels[blink.Channels[0]].Meshes)
        {
            var shared = mesh != null ? mesh.sharedMesh : null;
            if (shared == null || shared.blendShapeCount <= most) continue;
            most = shared.blendShapeCount;
            best = mesh;
        }
        return best;
    }

    private Eyes EyesOn(SkinnedMeshRenderer on, Mesh mesh)
    {
        if (!eyesOf.TryGetValue(mesh, out var bySkin))
        {
            // Meshes made at run time (a copy cut under a mask) go with their outfit: forget the gone ones.
            if (eyesOf.Count >= 8)
            {
                var gone = new List<Mesh>();
                foreach (var known in eyesOf.Keys)
                    if (known == null) gone.Add(known);
                foreach (var known in gone) eyesOf.Remove(known);
            }
            eyesOf[mesh] = bySkin = new Dictionary<string, Eyes>(StringComparer.OrdinalIgnoreCase);
        }
        if (bySkin.TryGetValue(Skin, out var eyes)) return eyes;
        bySkin[Skin] = null;
        var rest = mesh.vertices;
        var closing = new Vector3[rest.Length];
        var key = new Vector3[rest.Length];
        if (!Moves(blink, on, mesh, closing, key)) return null;
        // The rules are in metres; the mesh may not be.
        float unit = 1f / Mathf.Max(1e-4f, Scale(on, mesh));
        var rules = new FaceRoom.Rules
        {
            Moved = FaceTables.Number("Room.moved", .0005f) * unit,
            Reach = FaceTables.Number("Room.reach", .03f) * unit,
            Margin = FaceTables.Number("Room.margin", .003f) * unit,
            HiddenFrom = FaceTables.Number("Room.hiddenFrom", .15f),
            HiddenTo = FaceTables.Number("Room.hiddenTo", .35f),
            Least = Mathf.Max(1, Mathf.RoundToInt(FaceTables.Number("Room.least", 6f)))
        };
        eyes = new Eyes { Room = new FaceRoom(rest, mesh.triangles, closing, rules), Face = new Vector3[rest.Length], Key = key };
        bySkin[Skin] = eyes;
        return eyes;
    }

    // Adds what a set's keys on this mesh move, each at its weight, to <paramref name="into"/>; false with none of them on it.
    private bool Moves(in Set set, SkinnedMeshRenderer on, Mesh mesh, Vector3[] into, Vector3[] key)
    {
        bool any = false;
        for (int i = 0; i < set.Channels.Length; i++)
        {
            var channel = channels[set.Channels[i]];
            int at = Array.IndexOf(channel.Meshes, on);
            if (at < 0) continue;
            int shape = channel.Indices[at];
            int frame = mesh.GetBlendShapeFrameCount(shape) - 1;
            if (frame < 0) continue;
            float full = mesh.GetBlendShapeFrameWeight(shape, frame);
            if (!(full > 0f)) continue;
            mesh.GetBlendShapeFrameVertices(shape, frame, key, null, null);
            float share = set.Weights[i] / full;
            for (int v = 0; v < into.Length; v++) into[v] += key[v] * share;
            any = true;
        }
        return any;
    }

    // How long one of the mesh's units is in the world, as its first bone places it (a mesh kept in centimetres under a
    // skeleton a hundred times smaller).
    private static float Scale(SkinnedMeshRenderer on, Mesh mesh)
    {
        var bones = on.bones;
        var poses = mesh.bindposes;
        Vector3 scale = bones.Length > 0 && poses.Length > 0 && bones[0] != null
            ? (bones[0].localToWorldMatrix * poses[0]).lossyScale
            : on.transform.lossyScale;
        return (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// dev-text: file (the record's lines are for whoever reads Logs/placement.txt; nothing here is shown in the game)

// FieldPickups: Where a pickup lies once something is set down (the placement rule's side of it). One part of the class;
// the rest is in FieldPickups.cs and the files beside it.
//
// A pickup has no body. Its keeper (the host, or the PC alone) holds it as a point on the floor, and its look is made
// with every collider taken off (PickupProps), so nothing a player sets down ever meets one: a barricade, an ammo crate
// or a searchlight goes up over a medkit as over bare floor (the user, 2026-11-18: "바닥에 떨어진 체력킷, 쉴드쉘이
// 서포트의 구조물 설치 판정을 막는다는 뜻"; read in the code that day: no rule of placement can meet a pickup, and what it
// did meet where one lay was the corpse of the zombie that dropped it, which gives way since the same day).
// Nothing pushes a pickup out from under what goes up either, as physics pushes a corpse. Left there it would lie
// inside the crate with only its beam showing, sharing [F] with the crate. So a pickup makes way: when something solid
// is set down over it (Deployables.Stood), and when one is laid into something already standing (a gun put down at an
// ammo crate), it comes to lie on the free floor right beside that body, on its builder's side first. Where, and what
// is free floor, is the placement rule's to say (Placement.Lie); this part only asks, on the PC that keeps the
// pickups, and the others are told as of any other change. With no free floor on any side it stays, and is taken by
// reach and look as before. Barbed wire is walked through: nothing makes way for it.
public sealed partial class FieldPickups
{
    [SerializeField, Min(.05f), Tooltip("How far off a structure's face a pickup comes to lie when it makes way for the structure, to its middle (m).")]
    private float aside = .3f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void HearStructures()
    {
        Deployables.Stood -= OnStood;
        Deployables.Stood += OnStood;
    }

    private static void OnStood(IPlaced placed)
    {
        if (instance != null && instance.IsHost) instance.MakeWay(placed);
    }

    // Something was set down: the pickups its body holds make way, to its builder's side first.
    private void MakeWay(IPlaced placed)
    {
        if (!(placed is Component structure) || structure == null) return;
        var builder = placed.Owner;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            Vector3 toward = builder != null ? builder.transform.position : item.Position;
            Vector3 lies = Placement.Lie(item.Position, aside, toward, out Collider body, structure);
            // One still under an older structure, with nowhere to go then or now, is not this one's to tell of.
            if (body == null || !body.transform.IsChildOf(structure.transform)) continue;
            NoteWay(item, lies, body);
            if (lies == item.Position) continue;
            item.Position = lies;
            items[i] = item;
            changed = true;
        }
    }

    // Where a thing just laid comes to lie (Add): where it was put, or beside the structure it was put into.
    private Vector3 LieClear(in Item item)
    {
        Vector3 lies = Placement.Lie(item.Position, aside, item.Position, out Collider body);
        if (body != null) NoteWay(item, lies, body);
        return lies;
    }

    private void NoteWay(in Item item, Vector3 lies, Collider body)
    {
        if (!PlayRecord.On) return;
        var row = RowOf(item);
        string name = row != null ? row.Name : "?", under = PlayRecord.PathOf(body.transform);
        Placement.Note(lies != item.Position
            ? $"ASIDE   pickup {name} {PlayRecord.At(item.Position)} -> {PlayRecord.At(lies)} ({Vector3.Distance(lies, item.Position):0.00} m) beside \"{under}\""
            : $"STAYS   pickup {name} at {PlayRecord.At(item.Position)} under \"{under}\": no free floor beside it (taken by reach and look as before)");
    }

    /// <summary>
    /// The console's /lay (CardCommands): a pickup of this kind (gun, ammo, heal, shield, money: the first row of that
    /// kind in Tables/Pickups) is laid on the floor in front of this PC's player, where a Support's nearest structure
    /// would stand (Support.barricadeAhead), to try what is set down over one without waiting for a zombie to drop
    /// it. Only on the PC that keeps the pickups (the host, or alone).
    /// </summary>
    public static string LayForTrial(string kind)
    {
        if (instance == null) return "줍기가 없는 맵이다";
        if (!instance.IsHost) return "줍기 물건은 방장 PC가 놓는다";
        var mine = PlayerRoster.Local;
        if (mine == null) return "플레이어가 없다";
        string word = (kind ?? "").Trim();
        if (word.Equals("medkit", StringComparison.OrdinalIgnoreCase)) word = nameof(Kind.Heal);
        if (!Enum.TryParse(word, true, out Kind wanted) || !Enum.IsDefined(typeof(Kind), wanted)) return "gun · ammo · heal · shield · money 중 하나를 적는다";
        int row = instance.rows.FindIndex(candidate => candidate.Kind == wanted);
        if (row < 0) return "Pickups.csv에 그 종류의 줄이 없다";
        float ahead = PlayerClass.Ability("Support", "barricadeAhead", 1.8f);
        Vector3 at = Ground(mine.transform.position + mine.transform.forward * ahead);
        instance.Add(row, at, 0f);
        return $"{instance.rows[row].Name}: 내 앞 {ahead:0.0}m 바닥 {PlayRecord.At(at)}에 놓았다. 그 위에 설치물을 세우면 옆으로 비켜난다 (기록: Logs/placement.txt)";
    }

    /// <summary>
    /// What of the pickups lies where something of <paramref name="size"/> would stand with its feet at
    /// <paramref name="at"/>, turned <paramref name="yaw"/>, as this PC has them, in the placement record's words:
    /// "구급상자 ×1, 실드 셀 ×1 (colliders on them: 0)". The count of colliders is the proof, in the play itself, that
    /// a pickup has no body for the placement rule to meet; anything but 0 there is a pickup's look that kept one.
    /// Empty when nothing lies there. Asked only when a line of the record is written.
    /// </summary>
    public static string Under(Vector3 at, float yaw, Vector3 size)
    {
        if (instance == null || !PlayRecord.On) return "";
        var back = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int colliders = 0;
        foreach (var item in instance.items)
        {
            Vector3 local = back * (item.Position - at);
            if (Mathf.Abs(local.x) > size.x * .5f || Mathf.Abs(local.z) > size.z * .5f) continue;
            if (local.y < -Placement.FloorSpan || local.y > size.y) continue;
            var row = instance.RowOf(item);
            string name = row != null ? row.Name : "?";
            counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
            if (instance.views.TryGetValue(item.Id, out var view) && view != null)
                colliders += view.GetComponentsInChildren<Collider>(true).Length;
        }
        if (counts.Count == 0) return "";
        var text = new StringBuilder();
        foreach (var pair in counts)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(pair.Key).Append(" ×").Append(pair.Value);
        }
        return text.Append(" (colliders on them: ").Append(colliders).Append(')').ToString();
    }
}

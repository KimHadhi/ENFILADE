using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The board over the hub range's firing line (memo 46): what this PC's last hit on a range target did, big (the damage,
/// head or body, how far), how many hits it took to drop the last target that went over (a zombie dummy has a normal
/// zombie's health, so that is how many it takes to kill one with this gun), and the farthest headshot so far. Every
/// PC's board shows its own player's shots.
/// </summary>
[DisallowMultipleComponent]
public sealed class RangeBoard : MonoBehaviour
{
    private readonly Dictionary<RangeTarget, int> hitsOn = new Dictionary<RangeTarget, int>();
    private TMP_Text damage, part, detail, dropped, farthest;
    private float best;

    /// <summary>A board filling the station's face (HubLobby).</summary>
    public static RangeBoard Make(HubStation station)
    {
        var board = station.gameObject.AddComponent<RangeBoard>();
        board.Build(new Vector2(station.Size.x, station.Size.y));
        return board;
    }

    private void Build(Vector2 size)
    {
        var face = HubDisplays.Board(transform, "Range board", size, Vector3.zero, Quaternion.LookRotation(Vector3.back, Vector3.up));
        Vector2 px = face.sizeDelta;
        HubDisplays.Block(face, "Back", Vector2.zero, px, KitStyle.Panel, true);
        HubDisplays.Block(face, "Rail", Vector2.zero, new Vector2(px.x, 12f), KitStyle.Accent);
        HubDisplays.Words(face, Texts.Get("hub.range.title"), 72f, KitStyle.Text, TextAlignmentOptions.MidlineLeft, new Vector2(50f, 30f), new Vector2(px.x * .5f, 90f));
        HubDisplays.Words(face, Texts.Get("hub.range.last"), 44f, KitStyle.Soft, TextAlignmentOptions.MidlineLeft, new Vector2(50f, 140f), new Vector2(500f, 50f));
        damage = HubDisplays.Words(face, "-", 220f, KitStyle.Ice, TextAlignmentOptions.MidlineLeft, new Vector2(50f, 185f), new Vector2(600f, 240f));
        part = HubDisplays.Words(face, "", 80f, KitStyle.Accent, TextAlignmentOptions.MidlineLeft, new Vector2(640f, 200f), new Vector2(px.x - 690f, 90f));
        detail = HubDisplays.Words(face, Texts.Get("hub.range.hint"), 56f, KitStyle.Soft, TextAlignmentOptions.MidlineLeft, new Vector2(640f, 300f), new Vector2(px.x - 690f, 70f));
        dropped = HubDisplays.Words(face, "", 60f, KitStyle.Text, TextAlignmentOptions.MidlineLeft, new Vector2(50f, px.y - 200f), new Vector2(px.x - 100f, 80f));
        farthest = HubDisplays.Words(face, "", 48f, KitStyle.Soft, TextAlignmentOptions.MidlineLeft, new Vector2(50f, px.y - 110f), new Vector2(px.x - 100f, 70f));
        RangeTarget.Struck += OnStruck;
    }

    private void OnDestroy() => RangeTarget.Struck -= OnStruck;

    private void OnStruck(RangeTarget.Shot shot)
    {
        if (shot.Target == null) return;
        damage.text = Mathf.RoundToInt(shot.Damage).ToString();
        part.text = Texts.Get(shot.Headshot ? "hub.range.head" : "hub.range.body");
        part.color = shot.Headshot ? KitStyle.Accent : KitStyle.Text;
        detail.text = Texts.Format("hub.range.detail", shot.Target.DisplayName, shot.Distance.ToString("0.0"));
        hitsOn.TryGetValue(shot.Target, out int hits);
        hitsOn[shot.Target] = ++hits;
        if (shot.Downed)
        {
            dropped.text = Texts.Format("hub.range.dropped", shot.Target.DisplayName, hits);
            hitsOn[shot.Target] = 0;
        }
        if (shot.Headshot && shot.Distance > best)
        {
            best = shot.Distance;
            farthest.text = Texts.Format("hub.range.farthest", best.ToString("0.0"));
        }
    }
}

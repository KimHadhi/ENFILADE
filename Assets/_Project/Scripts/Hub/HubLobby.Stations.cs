using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hub's stations filled from the tables at play, and [F] on them (memo 46): the chapters' cards on the map table
/// (Tables/Maps, the campaign's chapters first), a locker door per class on the class wall (Tables/Classes, with each
/// class's rank insignia), every FPSBody gun on the rack, the ammo crate, the ready button, the board over the way out,
/// the sign over the table and the range board. What [F] works is what the view points at within reach (Tables/Hub
/// reach, aimAngle): the nearest to the middle of the view.
/// </summary>
public sealed partial class HubLobby
{
    private enum Act { Chapter, Ready, Class, Gun, Ammo }

    // One thing [F] can work, where it is, and how it shows being looked at or chosen.
    private sealed class Pick
    {
        public Act Act;
        public string Id = "";
        public Transform At;
        public TMP_Text Label;
        public Graphic Frame;
        public Transform Lift;
        public WeaponData Gun;
        public Color LabelColor = KitStyle.Text;
    }

    private readonly List<Pick> picks = new List<Pick>();
    private Pick aimed;
    private Transform door;
    private Vector3 doorShut;
    private float doorRise;
    private TMP_Text signNumber, signName, boardTitle, boardList, boardCount, readyLabel;
    private RawImage signPicture;
    private Image readyLight;
    private string shownChapter, shownBoard;

    private void BuildStations()
    {
        foreach (var station in FindObjectsByType<HubStation>(FindObjectsSortMode.None))
        {
            switch (station.Station)
            {
                case HubStation.Kind.MapTable: BuildTable(station); break;
                case HubStation.Kind.ClassWall: BuildClasses(station); break;
                case HubStation.Kind.GunRack: BuildGuns(station); break;
                case HubStation.Kind.Ammo: Single(station, Act.Ammo, Texts.Get("hub.sign.ammo"), out _); break;
                case HubStation.Kind.Ready: readyLabel = Single(station, Act.Ready, Texts.Get("hub.sign.ready"), out readyLight); break;
                case HubStation.Kind.ReadyBoard: BuildBoard(station); break;
                case HubStation.Kind.ChapterSign: BuildSign(station); break;
                case HubStation.Kind.DepartDoor:
                    door = station.transform;
                    doorShut = door.localPosition;
                    // The builder's shutter is a box scaled to its size: its own height is its scale's.
                    doorRise = station.Size.y * station.transform.lossyScale.y;
                    break;
                case HubStation.Kind.RangeBoard: RangeBoard.Make(station); break;
            }
        }
    }

    // ---- The map table: a card per chapter lying on its top ----

    // The maps there are to set off to, the campaign's chapters first in their order, then any other map.
    private static List<string> Chapters()
    {
        var list = new List<string>();
        foreach (string id in GameTables.RowIds("CampaignChapters"))
            if (id != HubFlow.SceneName && Application.CanStreamedLevelBeLoaded(id) && !list.Contains(id)) list.Add(id);
        foreach (string id in MapTable.Playable()) if (!list.Contains(id)) list.Add(id);
        return list;
    }

    private void BuildTable(HubStation table)
    {
        var maps = Chapters();
        if (maps.Count == 0) return;
        Vector3 top = table.Size;
        int perRow = Mathf.Min(maps.Count, 4), rows = Mathf.CeilToInt(maps.Count / (float)perRow);
        float width = Mathf.Min(.95f, top.x / perRow - .08f), height = Mathf.Min(.66f, top.z / rows - .08f);
        // Lying face up, the card's top toward the table's far side: read from the near side.
        var lying = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        for (int i = 0; i < maps.Count; i++)
        {
            int row = i / perRow, column = i % perRow, inRow = Mathf.Min(perRow, maps.Count - row * perRow);
            float x = (column - (inRow - 1) * .5f) * (width + .08f);
            float z = ((rows - 1) * .5f - row) * (height + .08f);
            var lift = new GameObject("Chapter " + maps[i]).transform;
            lift.SetParent(table.transform, false);
            lift.localPosition = new Vector3(x, top.y * .5f + .006f, z);
            var card = HubDisplays.Board(lift, "Card", new Vector2(width, height), Vector3.zero, lying);
            Vector2 px = card.sizeDelta;
            var frame = HubDisplays.Block(card, "Chosen frame", new Vector2(-10f, -10f), px + new Vector2(20f, 20f), KitStyle.Accent, true);
            HubDisplays.Block(card, "Card", Vector2.zero, px, KitStyle.Panel, true);
            HubDisplays.Picture(card, "Picture", new Vector2(8f, 8f), new Vector2(px.x - 16f, px.y - 70f), MapTable.Picture(maps[i]));
            var name = HubDisplays.Words(card, MapTable.DisplayName(maps[i]), 30f, KitStyle.Text, TextAlignmentOptions.MidlineLeft,
                new Vector2(16f, px.y - 58f), new Vector2(px.x - 32f, 50f));
            picks.Add(new Pick { Act = Act.Chapter, Id = maps[i], At = lift, Label = name, Frame = frame, Lift = card });
        }
        shownChapter = null;
    }

    // ---- The class wall: a locker door per class ----

    private void BuildClasses(HubStation wall)
    {
        var classes = PlayerClass.Catalog;
        if (classes.Count == 0) return;
        Vector3 size = wall.Size;
        float width = Mathf.Min(1.1f, size.x / classes.Count - .06f), height = Mathf.Min(2.1f, size.y);
        // The wall's face looks along its forward into the room; a canvas reads from in front looking along its forward.
        var facing = Quaternion.LookRotation(Vector3.back, Vector3.up);
        string current = ClassGrowth.CurrentId;
        for (int i = 0; i < classes.Count; i++)
        {
            var info = classes[i];
            // A wall station's x runs to the room's left (its forward faces the room), so the table's order reads left to right.
            float x = ((classes.Count - 1) * .5f - i) * (width + .06f);
            var at = new GameObject("Class " + info.Id).transform;
            at.SetParent(wall.transform, false);
            at.localPosition = new Vector3(x, 0f, size.z * .5f + .01f);
            var door = HubDisplays.Board(at, "Door", new Vector2(width, height), new Vector3(0f, -size.y * .5f + height * .5f, 0f), facing);
            Vector2 px = door.sizeDelta;
            var frame = HubDisplays.Block(door, "Chosen frame", new Vector2(-10f, -10f), px + new Vector2(20f, 20f), KitStyle.Accent, true);
            HubDisplays.Block(door, "Door", Vector2.zero, px, KitStyle.Panel, true);
            HubDisplays.Block(door, "Edge", Vector2.zero, new Vector2(px.x, 8f), KitStyle.With(KitStyle.Ice, .25f));
            var icon = HubDisplays.Picture(door, "Icon", new Vector2(px.x * .5f - 75f, 50f), new Vector2(150f, 150f),
                Resources.Load<Texture2D>("UI/Icons/Class_" + info.Id));
            icon.color = icon.texture != null ? KitStyle.Ice : Color.clear;
            var name = HubDisplays.Words(door, info.Title, 52f, KitStyle.Text, TextAlignmentOptions.Center, new Vector2(10f, 215f), new Vector2(px.x - 20f, 64f));
            var role = HubDisplays.Words(door, info.Role, 24f, KitStyle.Soft, TextAlignmentOptions.Top, new Vector2(24f, 290f), new Vector2(px.x - 48f, 220f));
            role.textWrappingMode = TextWrappingModes.Normal;
            role.fontStyle = FontStyles.Normal;
            // The class's rank insignia (stage 6E): growth shows as the insignia alone, no numbers.
            int rank = ClassGrowth.RankNow(info.Id);
            var insignia = HubDisplays.Picture(door, "Insignia", new Vector2(px.x * .5f - 85f, px.y - 270f), new Vector2(170f, 170f), ClassGrowth.Insignia(rank));
            if (insignia.texture == null) insignia.color = Color.clear;
            if (!info.Ready)
                HubDisplays.Words(door, Texts.Get("classes.later"), 28f, KitStyle.Soft, TextAlignmentOptions.Center, new Vector2(10f, px.y - 80f), new Vector2(px.x - 20f, 50f));
            picks.Add(new Pick { Act = Act.Class, Id = info.Id, At = at, Label = name, Frame = frame });
            frame.enabled = string.Equals(info.Id, current, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- The gun rack: every gun the body can hold, hung along it ----

    private void BuildGuns(HubStation rack)
    {
        var assets = FPSBodyAssets.Instance;
        if (assets == null) return;
        var guns = new List<FPSBodyAssets.Gun>();
        foreach (var entry in assets.guns)
            if (entry.weapon != null && entry.prefab != null && entry.weapon.Category == WeaponCategory.Ranged && !guns.Exists(g => g.weapon == entry.weapon))
                guns.Add(entry);
        if (guns.Count == 0) return;
        Vector3 size = rack.Size;
        int perRow = Mathf.CeilToInt(guns.Count / 2f), rows = guns.Count > perRow ? 2 : 1;
        float slot = size.x / perRow, rowHeight = size.y / rows;
        var facing = Quaternion.LookRotation(Vector3.back, Vector3.up);
        for (int i = 0; i < guns.Count; i++)
        {
            var gun = guns[i];
            int row = i / perRow, column = i % perRow;
            var at = new GameObject("Gun " + gun.weapon.name).transform;
            at.SetParent(rack.transform, false);
            at.localPosition = new Vector3(((perRow - 1) * .5f - column) * slot, size.y * .5f - (row + .45f) * rowHeight, size.z * .5f + .12f);
            // Hung level along the rack, the barrel to the right as seen from the room.
            var piece = HubDisplays.Showpiece(this, gun.prefab, at, shown => Fit(shown, at, slot * .86f));
            piece.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            var tag = HubDisplays.Board(at, "Tag", new Vector2(slot - .06f, .16f), new Vector3(0f, -rowHeight * .38f, -.1f), facing);
            HubDisplays.Block(tag, "Back", Vector2.zero, tag.sizeDelta, KitStyle.With(KitStyle.Night, .85f), true);
            var name = HubDisplays.Words(tag, gun.weapon.DisplayName, 30f, KitStyle.Text, TextAlignmentOptions.Center);
            picks.Add(new Pick { Act = Act.Gun, Id = gun.weapon.name, At = at, Label = name, Gun = gun.weapon });
        }
    }

    // A gun bigger than its place on the rack is shrunk to fit; centred on its place.
    private static void Fit(Transform piece, Transform place, float longest)
    {
        var bounds = HubDisplays.Measure(piece);
        float length = Mathf.Max(bounds.size.x, bounds.size.z);
        if (length > longest && length > .01f) piece.localScale *= longest / length;
        bounds = HubDisplays.Measure(piece);
        piece.position += place.position - bounds.center;
    }

    // ---- One-of stations: the ammo crate and the ready button ----

    private TMP_Text Single(HubStation station, Act act, string words, out Image light)
    {
        Vector3 size = station.Size;
        var at = new GameObject(act.ToString()).transform;
        at.SetParent(station.transform, false);
        at.localPosition = new Vector3(0f, size.y * .5f, 0f);
        // A tag standing over it, turned to face the room (the station's forward).
        var tag = HubDisplays.Board(at, "Tag", new Vector2(1.1f, .32f), new Vector3(0f, .45f, 0f), Quaternion.LookRotation(Vector3.back, Vector3.up));
        light = HubDisplays.Block(tag, "Light", Vector2.zero, tag.sizeDelta, KitStyle.With(KitStyle.Night, .9f), true);
        var label = HubDisplays.Words(tag, words, 60f, KitStyle.Text, TextAlignmentOptions.Center);
        picks.Add(new Pick { Act = act, At = at, Label = label });
        return label;
    }

    // ---- The board over the way out and the sign over the table ----

    private void BuildBoard(HubStation station)
    {
        Vector2 size = new Vector2(station.Size.x, station.Size.y);
        var board = HubDisplays.Board(station.transform, "Ready board", size, Vector3.zero, Quaternion.LookRotation(Vector3.back, Vector3.up));
        Vector2 px = board.sizeDelta;
        HubDisplays.Block(board, "Back", Vector2.zero, px, KitStyle.Panel, true);
        HubDisplays.Block(board, "Rail", Vector2.zero, new Vector2(px.x, 12f), KitStyle.Accent);
        boardTitle = HubDisplays.Words(board, "", 80f, KitStyle.Text, TextAlignmentOptions.MidlineLeft, new Vector2(50f, 30f), new Vector2(px.x * .7f, 100f));
        boardCount = HubDisplays.Words(board, "", 220f, KitStyle.Accent, TextAlignmentOptions.MidlineRight, new Vector2(px.x * .6f, 10f), new Vector2(px.x * .4f - 50f, 200f));
        boardList = HubDisplays.Words(board, "", 56f, KitStyle.Text, TextAlignmentOptions.TopLeft, new Vector2(50f, 160f), new Vector2(px.x - 100f, px.y - 190f));
        boardList.textWrappingMode = TextWrappingModes.Normal;
        boardList.overflowMode = TextOverflowModes.Truncate;
        shownBoard = null;
    }

    private void BuildSign(HubStation station)
    {
        Vector2 size = new Vector2(station.Size.x, station.Size.y);
        var sign = HubDisplays.Board(station.transform, "Chapter sign", size, Vector3.zero, Quaternion.LookRotation(Vector3.back, Vector3.up));
        Vector2 px = sign.sizeDelta;
        HubDisplays.Block(sign, "Back", Vector2.zero, px, KitStyle.Panel, true);
        signPicture = HubDisplays.Picture(sign, "Picture", new Vector2(16f, 16f), new Vector2(px.x - 32f, px.y - 200f), null);
        HubDisplays.Words(sign, Texts.Get("hub.sign.next"), 40f, KitStyle.Soft, TextAlignmentOptions.MidlineLeft, new Vector2(40f, px.y - 175f), new Vector2(500f, 50f));
        signNumber = HubDisplays.Words(sign, "", 110f, KitStyle.Accent, TextAlignmentOptions.MidlineLeft, new Vector2(40f, px.y - 130f), new Vector2(260f, 120f));
        signName = HubDisplays.Words(sign, "", 72f, KitStyle.Text, TextAlignmentOptions.MidlineLeft, new Vector2(290f, px.y - 120f), new Vector2(px.x - 330f, 100f));
        shownChapter = null;
    }

    // ---- Every frame: what shows the state ----

    private void ShowStations()
    {
        if (shownChapter != chapter)
        {
            shownChapter = chapter;
            foreach (var pick in picks)
            {
                if (pick.Act != Act.Chapter) continue;
                bool chosen = pick.Id == chapter;
                if (pick.Frame != null) pick.Frame.enabled = chosen;
                if (pick.Lift != null) pick.Lift.localPosition = new Vector3(0f, chosen ? .05f : 0f, 0f);
            }
            if (signName != null)
            {
                HubDisplays.SplitChapter(MapTable.DisplayName(chapter), out string number, out string name);
                signNumber.text = number;
                signName.text = name;
                signPicture.texture = MapTable.Picture(chapter);
                signPicture.color = signPicture.texture != null ? Color.white : KitStyle.With(KitStyle.Night, .9f);
            }
            RenameObjective();
        }
        ShowBoard();
        if (readyLabel != null)
        {
            var mine = PlayerRoster.Local;
            bool set = mine != null && IsReady(mine);
            readyLabel.text = set ? Texts.Get("hub.sign.ready.set") : Texts.Get("hub.sign.ready");
            if (readyLight != null) readyLight.color = set ? KitStyle.With(KitStyle.Green, .9f) : KitStyle.With(KitStyle.Night, .9f);
        }
        // The way out's shutter rises as the count runs down, and drops back if it stops.
        if (door != null)
        {
            float open = countdown >= 0f ? 1f - Mathf.Clamp01(countdown / Mathf.Max(.01f, countdownFrom)) : 0f;
            door.localPosition = Vector3.MoveTowards(door.localPosition, doorShut + Vector3.up * (doorRise * .85f * open), Time.unscaledDeltaTime * 4f);
        }
    }

    private void ShowBoard()
    {
        if (boardList == null) return;
        var text = new System.Text.StringBuilder();
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !(someone.IsLocal || someone.IsNetworkDriven) || !someone.TryGetComponent(out PlayerTally tally)) continue;
            bool set = IsReady(someone);
            text.Append(set ? "<color=#4CDB85>■</color>  " : "<color=#FFFFFF55>□</color>  ")
                .Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(tally.Color)).Append('>').Append(tally.DisplayName).Append("</color>")
                .Append("  <size=70%><color=#FFFFFF88>").Append(ClassGrowth.TitleOf(tally.ClassId)).Append("</color></size>")
                .Append(set ? "  <size=70%><color=#4CDB85>" + Texts.Get("hub.board.ready") + "</color></size>" : "").Append('\n');
        }
        string count = countdown >= 0f ? Mathf.CeilToInt(countdown).ToString() : "";
        string all = MapTable.DisplayName(chapter) + "|" + text + "|" + count;
        if (all == shownBoard) return;
        shownBoard = all;
        boardTitle.text = Texts.Format("hub.board.title", MapTable.DisplayName(chapter));
        boardList.text = text.ToString();
        boardCount.text = count;
    }

    // ---- What the view points at, and [F] ----

    private void Aim()
    {
        var mine = PlayerRoster.Local;
        Pick found = null;
        if (mine != null && mine.IsInitialized && !mine.IsDead && !mine.IsDowned && mine.TryGetComponent(out PlayerBody body) && body.View != null)
        {
            Vector3 eye = body.View.position, forward = body.View.forward;
            float reach = Number("reach", 2.6f), cone = Number("aimAngle", 14f), best = cone;
            foreach (var pick in picks)
            {
                if (pick.At == null) continue;
                Vector3 to = pick.At.position - eye;
                if (to.sqrMagnitude > reach * reach) continue;
                float angle = Vector3.Angle(forward, to);
                if (angle > best) continue;
                best = angle;
                found = pick;
            }
        }
        if (found != aimed)
        {
            if (aimed != null && aimed.Label != null) aimed.Label.color = aimed.LabelColor;
            aimed = found;
            if (aimed != null && aimed.Label != null) { aimed.LabelColor = aimed.Label.color; aimed.Label.color = KitStyle.Accent; }
        }
        string text = aimed != null ? PromptFor(aimed) : null;
        screen.Prompt(ResultsShow.IsPlaying ? null : text);
        if (aimed == null || interact == null || !interact.WasPressedThisFrame() || !TakesInput()) return;
        Work(aimed, mine);
    }

    private string PromptFor(Pick pick)
    {
        switch (pick.Act)
        {
            case Act.Chapter:
                string name = MapTable.DisplayName(pick.Id);
                if (pick.Id == chapter) return Texts.Format("hub.prompt.chapter.chosen", name);
                return IsHost ? Texts.Format("hub.prompt.chapter", name) : Texts.Format("hub.prompt.chapter.host", name);
            case Act.Class:
                string title = ClassGrowth.TitleOf(pick.Id);
                return string.Equals(pick.Id, ClassGrowth.CurrentId, System.StringComparison.OrdinalIgnoreCase)
                    ? Texts.Format("hub.prompt.class.chosen", title) : Texts.Format("hub.prompt.class", title);
            case Act.Gun:
                return Texts.Format("hub.prompt.gun", pick.Gun != null ? pick.Gun.DisplayName : pick.Id);
            case Act.Ammo:
                return Texts.Get("hub.prompt.ammo");
            case Act.Ready:
                var mine = PlayerRoster.Local;
                return mine != null && IsReady(mine) ? Texts.Get("hub.prompt.unready") : Texts.Get("hub.prompt.ready");
        }
        return null;
    }

    private void Work(Pick pick, PlayerMovement mine)
    {
        switch (pick.Act)
        {
            case Act.Chapter:
                if (!IsHost || pick.Id == chapter) { KitSound.Play(KitSound.Cue.Refuse, .4f); return; }
                chapter = pick.Id;
                HubFlow.Chosen = chapter;
                stateChanged = true;
                KitSound.Play(KitSound.Cue.Ping, .8f);
                break;
            case Act.Class:
                if (string.Equals(pick.Id, ClassGrowth.CurrentId, System.StringComparison.OrdinalIgnoreCase)) return;
                ClassGrowth.Choose(pick.Id);
                ClassKit.Local?.Rekit();
                foreach (var other in picks)
                    if (other.Act == Act.Class && other.Frame != null) other.Frame.enabled = other == pick;
                KitSound.Play(KitSound.Cue.Buy, .8f);
                AlertHUD.Self(Texts.Format("hub.alert.class", ClassGrowth.TitleOf(pick.Id)));
                break;
            case Act.Gun:
                if (!TakeGun(mine, pick.Gun)) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
                KitSound.Play(KitSound.Cue.PickGun, .8f);
                break;
            case Act.Ammo:
                var slots = mine != null ? mine.GetComponent<PlayerWeaponSlots>() : null;
                if (slots == null || slots.RefillAmmo() <= 0) { KitSound.Play(KitSound.Cue.Refuse, .4f); return; }
                KitSound.Play(KitSound.Cue.PickAmmo, .8f);
                break;
            case Act.Ready:
                ToggleReady();
                break;
        }
    }

    // A gun off the rack goes in the gun slot in hand, else an empty gun slot, else the first, full and drawn (as the
    // shop hands one over).
    private static bool TakeGun(PlayerMovement mine, WeaponData gun)
    {
        var slots = mine != null ? mine.GetComponent<PlayerWeaponSlots>() : null;
        if (slots == null || gun == null || mine.CombatInputLocked) return false;
        int slot = -1, held = slots.SelectedSlot;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++) if (slots.GetWeapon(i) == gun) slot = i;
        if (slot < 0 && held >= 0 && PlayerWeaponSlots.GetSlotCategory(held) == WeaponCategory.Ranged) slot = held;
        for (int i = 0; slot < 0 && i < PlayerWeaponSlots.SlotCount; i++)
            if (PlayerWeaponSlots.GetSlotCategory(i) == WeaponCategory.Ranged && slots.GetWeapon(i) == null) slot = i;
        if (slot < 0) slot = 0;
        if (slots.GetWeapon(slot) != gun && !slots.TrySetWeapon(slot, gun)) return false;
        slots.RefillAmmo();
        if (!slots.HandsFull) slots.SelectSlot(slot);
        return true;
    }
}

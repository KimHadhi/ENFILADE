using UnityEngine;

// FieldPickups: In the world. One part of the class; the rest is in FieldPickups.cs and the files beside it.
public sealed partial class FieldPickups
{
    private void LateUpdate()
    {
        seen.Clear();
        float time = Time.unscaledTime;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var row = rows[item.Row];
            seen.Add(item.Id);
            if (!views.TryGetValue(item.Id, out var view) || view == null)
                views[item.Id] = view = PickupView.Make(transform, row.Name, row.Kind, row.Weapon, ColorOf(row.Kind));
            view.transform.position = item.Position;
            view.Show(i == targeted, eye, time);
        }
        if (views.Count == seen.Count && asked.Count == 0) return;
        // Gone (taken, or its time ran out): it plays its take and goes.
        gone.Clear();
        foreach (var pair in views) if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
        foreach (ushort id in gone)
        {
            if (views[id] != null) views[id].Take();
            views.Remove(id);
        }
        gone.Clear();
        foreach (var pair in asked) if (!seen.Contains(pair.Key)) gone.Add(pair.Key);
        foreach (ushort id in gone) asked.Remove(id);
    }

    private static Color ColorOf(Kind kind) => kind switch
    {
        Kind.Gun => KitStyle.Text,
        Kind.Ammo => new Color(1f, .8f, .3f),
        Kind.Heal => KitStyle.Red,
        Kind.Shield => new Color(.45f, .7f, 1f),
        _ => new Color(.49f, .86f, .54f),
    };
}

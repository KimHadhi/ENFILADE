using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bundle 3, after GMod Horde's weighted enemy list (sh_enemy.lua: every enemy has a weight from its wave on, elites
/// among them, and boss-class ones from wave 5): what a campaign step's horde is made of. A row of Tables/CampaignMix
/// weighs the kinds against each other (shambler, runner and each elite), caps how many of a kind may be about at once
/// (alone; a cap grows with the players as the horde does, CampaignDirector.HordeGrowth), and names the kind that fills
/// the step's boss slot (CampaignSteps.peakBoss: at a hold's song highlight, halfway through a hold with no song, and
/// every few waves of the endless finale). A step names its row (CampaignSteps.mix) or keeps the one before it, so the
/// elites come in as the campaign goes on. A chapter that names none leaves each zombie to its own roll by
/// Tables/ZombieKinds' shares, as before. The host picks (CampaignHorde), before the zombie goes out to the room.
/// </summary>
internal sealed class CampaignMix
{
    public const string TableName = "CampaignMix";
    // The kinds a mix weighs: a column each for its weight (the kind's name) and its cap ("<name>Most", empty for none).
    private static readonly ZombieKind[] Kinds = { ZombieKind.Shambler, ZombieKind.Runner, ZombieKind.Exploder, ZombieKind.Charger };
    private static readonly Dictionary<string, CampaignMix> mixes = new Dictionary<string, CampaignMix>(StringComparer.OrdinalIgnoreCase);

    private readonly float[] weights = new float[Kinds.Length], open = new float[Kinds.Length];
    // At most this many about at once alone; below 0, no cap.
    private readonly int[] caps = new int[Kinds.Length];

    /// <summary>Its row's id, for the director's report.</summary>
    public string Id { get; private set; } = "";
    /// <summary>What fills the step's boss slot (a brute unless the row says otherwise).</summary>
    public ZombieKind Boss { get; private set; } = ZombieKind.Brute;

    /// <summary>The row a step names, read once (and again after a table edit); null for none or a row the table lacks.</summary>
    public static CampaignMix Of(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (mixes.TryGetValue(id, out var known)) return known;
        CampaignMix mix = null;
        if (GameTables.TryGetRow(TableName, id, out var row))
        {
            mix = new CampaignMix { Id = id };
            for (int i = 0; i < Kinds.Length; i++)
            {
                string column = Column(Kinds[i]);
                mix.weights[i] = Mathf.Max(0f, CampaignStep.Number(row, column, 0f));
                mix.caps[i] = Mathf.RoundToInt(CampaignStep.Number(row, column + "Most", -1f));
            }
            if (Enum.TryParse(CampaignStep.Text(row, "boss"), true, out ZombieKind boss) && Enum.IsDefined(typeof(ZombieKind), boss))
                mix.Boss = boss;
        }
        else Debug.LogWarning($"[Campaign] Tables/{TableName} has no mix '{id}'; those zombies keep Tables/ZombieKinds' shares.");
        mixes[id] = mix;
        return mix;
    }

    /// <summary>
    /// A kind for a new zombie by the weights, leaving out a kind that already has its cap about (<paramref name="alive"/>
    /// counts them; <paramref name="capGrowth"/> grows the caps for the players). With every weighed kind capped, a
    /// shambler. False when the row weighs nothing at all: the zombie's own roll stands.
    /// </summary>
    public bool TryPick(Func<ZombieKind, int> alive, float capGrowth, out ZombieKind kind)
    {
        kind = ZombieKind.Shambler;
        float total = 0f;
        bool any = false;
        for (int i = 0; i < Kinds.Length; i++)
        {
            float weight = weights[i];
            any |= weight > 0f;
            if (weight > 0f && caps[i] >= 0 && alive(Kinds[i]) >= Mathf.RoundToInt(caps[i] * Mathf.Max(1f, capGrowth))) weight = 0f;
            open[i] = weight;
            total += weight;
        }
        if (total <= 0f) return any;
        float roll = UnityEngine.Random.value * total;
        for (int i = 0; i < Kinds.Length; i++)
        {
            if (open[i] <= 0f) continue;
            kind = Kinds[i];
            if (roll < open[i]) break;
            roll -= open[i];
        }
        return true;
    }

    // The column a kind's weight is in: its name, lower case first (shambler, runner, exploder, charger).
    private static string Column(ZombieKind kind)
    {
        string name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    // Live table edits and a new Play session read the rows again.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        mixes.Clear();
        GameTables.Reloaded -= mixes.Clear;
        GameTables.Reloaded += mixes.Clear;
    }
}

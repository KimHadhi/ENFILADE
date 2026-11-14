/// <summary>
/// Which bones move together closely enough that what follows one can hide, or cut, what follows the other. A layer
/// fitted against a garment is fitted standing still, and the fit has to hold when the body moves: a shoe and the sock
/// in it follow the same foot, so the shoe hides the sock whatever the foot does; a skirt hangs from bones of its own
/// and swings clear of the thighs, so it hides nothing of them for good, however near it hangs at rest.
/// A garment's triangle counts for a point of the layer under it when its bone is
/// - the point's own bone,
/// - or the body bone next to the point's along the body (thigh and shin, hand and finger),
/// - or the body bone the point's own extra bone hangs from (a bust's bone under the chest, a lock of hair under the head).
/// A garment's own extra bone (a skirt's, a coat tail's, a ribbon's) never counts for anything but itself.
/// Bones are numbers here (the dressed model's, whoever numbers them), so the rule runs outside the game too.
/// </summary>
internal sealed class FitBones
{
    private readonly int[] parent, bodyAbove;
    private readonly bool[] body;
    private readonly bool[][] rows;

    /// <param name="parent">Each bone's parent, -1 for none.</param>
    /// <param name="body">Whether each is one of the body's own (humanoid) bones.</param>
    public FitBones(int[] parent, bool[] body)
    {
        this.parent = parent;
        this.body = body;
        rows = new bool[parent.Length][];
        bodyAbove = new int[parent.Length];
        for (int bone = 0; bone < parent.Length; bone++)
        {
            int above = parent[bone], steps = 0;
            while (above >= 0 && !body[above] && steps++ < parent.Length) above = parent[above];
            bodyAbove[bone] = above;
        }
    }

    /// <summary>By a layer's bone, the garment bones that move with it (indexed by garment bone); rows not asked for stay null.</summary>
    public bool[][] Rows => rows;

    /// <summary>Makes ready the rows of every bone in <paramref name="tags"/> (FitTrim and FitFollow read them from many threads).</summary>
    public void Ready(int[] tags)
    {
        foreach (int tag in tags)
        {
            if (tag < 0 || tag >= rows.Length || rows[tag] != null) continue;
            var row = new bool[rows.Length];
            row[tag] = true;
            int above = bodyAbove[tag];
            if (above >= 0) row[above] = true;
            if (body[tag])
                for (int other = 0; other < rows.Length; other++)
                    if (body[other] && bodyAbove[other] == tag) row[other] = true;
            rows[tag] = row;
        }
    }
}

using UnityEngine;

/// <summary>Reading a transform tree by name. Every script that reads a rig or a model carried its own copy of these.</summary>
public static class Hierarchy
{
    /// <summary>The first transform called so at or under the root, depth first; null when there is none.</summary>
    public static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = Find(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>A part's path of names down from the root ("Arm/Hand/Index"), the root's own name left out.</summary>
    public static string PathOf(Transform part, Transform root)
    {
        string path = part.name;
        for (var at = part.parent; at != null && at != root; at = at.parent) path = at.name + "/" + path;
        return path;
    }
}

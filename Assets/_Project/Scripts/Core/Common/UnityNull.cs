using UnityEngine;

/// <summary>
/// Unity's objects are "null" in two ways. A reference can be truly empty, or it can still point at a husk whose object
/// Unity has destroyed (a scene that was left, a zombie cleared away) or that was never set in the Inspector. Unity's own
/// == and != see both, but C#'s ?. , ?? and ??= only see the first: on a husk they go straight on and call into a dead
/// object. OrNull turns a husk into a true null, so those operators are safe on Unity objects:
/// <c>target.OrNull()?.Stop()</c>, <c>found.OrNull() ?? fallback</c>. Tools/Check-Code.ps1 audit lists the places that
/// forget it.
/// </summary>
public static class UnityNull
{
    /// <summary>The object, or a true null when it is gone or was never there.</summary>
    public static T OrNull<T>(this T thing) where T : Object => thing != null ? thing : null;
}

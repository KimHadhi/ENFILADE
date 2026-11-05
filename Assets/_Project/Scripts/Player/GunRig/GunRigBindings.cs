using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.IK;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.PropertyBindings.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Runtime copies of KINEMATION's CAS settings whose bindings read our player. KINEMATION's procedural settings bind their
/// inputs by type name to its demo player (CasFPSPlayerController, CharacterExampleController, CharacterCamera); the copies
/// bind the same paths to GunRigInputs, which has the same member names. Three more fixes ride along:
/// - the recoil binding names RecoilAnimation in Assembly-CSharp, where the integration was made; here it lives in its own
///   assembly (RecoilAnimation.Runtime), so the name alone would find nothing;
/// - the leg modifiers (stride, steps, foot IK) are left out: the arms rig is never seen, and the body has its own legs;
/// - the bindings are resolved on the rig when a copy is made, the step KINEMATION takes on its prefab at load (Resolve).
/// The assets themselves stay as KINEMATION shipped them. One copy is made per settings asset and shared by every rig,
/// and the copies go when the game stops (Release).
/// </summary>
internal static class GunRigBindings
{
    private static readonly Dictionary<CharacterAnimationSettings, CharacterAnimationSettings> Settings = new();
    private static readonly Dictionary<ProceduralAnimationSettings, ProceduralAnimationSettings> Procedural = new();
    // Every copy made (settings, procedural settings and their modifiers), for Release.
    private static readonly List<Object> Made = new();
    private static readonly HashSet<string> Warned = new();
    // KINEMATION's demo types its settings bind to; GunRigInputs answers for them.
    private static readonly string[] DemoTypes =
        { "CasFPSPlayerController", "CharacterExampleController", "FPSExampleController", "CharacterCamera" };
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    // Settings nest a few levels (modifier, weight override list, struct, property); deeper means a cycle.
    private const int MaxDepth = 8;

    /// <summary>
    /// The copy of <paramref name="source"/> that reads GunRigInputs, its bindings resolved on <paramref name="rig"/>:
    /// an awake arms rig that already carries GunRigInputs and RecoilAnimation.
    /// </summary>
    internal static CharacterAnimationSettings For(CharacterAnimationSettings source, GameObject rig)
    {
        if (source == null) return null;
        if (Settings.TryGetValue(source, out var copy) && copy != null) return copy;
        copy = Copy(source);
        copy.proceduralSettings = For(source.proceduralSettings, rig);
        Settings[source] = copy;
        return copy;
    }

    private static ProceduralAnimationSettings For(ProceduralAnimationSettings source, GameObject rig)
    {
        if (source == null) return null;
        if (Procedural.TryGetValue(source, out var copy) && copy != null) return copy;
        copy = Copy(source);
        copy.modifiers = new List<AnimationModifierSettings>();
        foreach (var modifier in source.modifiers)
        {
            if (modifier == null || MovesLegsOnly(modifier)) continue;
            var clone = Copy(modifier);
            Rebind(clone, 0);
            Resolve(clone, rig);
            copy.modifiers.Add(clone);
        }
        Procedural[source] = copy;
        return copy;
    }

    // KINEMATION resolves every binding once, at load, against the settings' character prefab
    // (ProceduralAnimationSettings.InitializeOnLoad). Each job then points the resolved binding at its own rig. The ADS
    // job relies on that: it copies the sight binding and only re-points it (AdsModifierJob.Initialize). A binding that
    // was never resolved stays empty, and aiming then lines up the weapon bone instead of the sight. A copy is made after
    // loading, so KINEMATION's own step runs here with the rig as the character. Every rig's jobs re-point the result to
    // themselves, so the first rig serves them all.
    private static void Resolve(AnimationModifierSettings modifier, GameObject rig)
    {
        if (rig == null) return;
        var prefab = modifier.characterPrefab;
        modifier.characterPrefab = rig;
        modifier.InitializeOnLoad();
        modifier.characterPrefab = prefab;
    }

    // Instantiate copies only serialized fields, so the copies' bindings start unresolved until Resolve.
    private static T Copy<T>(T source) where T : ScriptableObject
    {
        var copy = Object.Instantiate(source);
        copy.name = source.name;
        copy.hideFlags = HideFlags.DontSave;
        if (Made.Count == 0) Application.quitting += Release;
        Made.Add(copy);
        return copy;
    }

    // DontSave keeps the copies through scene loads, and nothing else removes them, so in the editor they would outlive
    // Play. When the next Play starts, KINEMATION binds every procedural settings still in memory to its character prefab
    // (ProceduralAnimationSettings.InitializeOnLoad). That prefab is KINEMATION's demo player, which has no GunRigInputs,
    // so a leftover copy warned once per binding. The copies in use are resolved on the rig (Resolve).
    // Exiting Play raises Application.quitting too.
    private static void Release()
    {
        Application.quitting -= Release;
        foreach (var copy in Made) if (copy != null) Object.DestroyImmediate(copy);
        Made.Clear();
        Settings.Clear();
        Procedural.Clear();
    }

    private static bool MovesLegsOnly(AnimationModifierSettings modifier)
        => modifier is StrideModifierSettings || modifier is StepModifierSettings || modifier is FootIkSettings
           || modifier.name.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0;

    // Walks the serialized fields (lists and nested structs included) to every BindableProperty.
    private static void Rebind(object target, int depth)
    {
        if (target == null || depth > MaxDepth) return;
        for (var type = target.GetType(); type != null && type != typeof(ScriptableObject) && type != typeof(object)
             && type != typeof(ValueType); type = type.BaseType)
        {
            foreach (var field in type.GetFields(Fields))
            {
                if (field.IsNotSerialized || (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)) continue;
                var fieldType = field.FieldType;
                if (fieldType.IsPrimitive || fieldType.IsEnum || fieldType == typeof(string)
                    || typeof(Object).IsAssignableFrom(fieldType)) continue;
                object value = field.GetValue(target);
                if (value == null) continue;
                if (IsBindable(fieldType)) { Point(value); continue; }
                if (value is IList list)
                {
                    foreach (var item in list)
                    {
                        if (item == null || item is Object) continue;
                        if (IsBindable(item.GetType())) Point(item);
                        else Rebind(item, depth + 1);
                    }
                    continue;
                }
                if (fieldType.Namespace != null && fieldType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                Rebind(value, depth + 1);
            }
        }
    }

    private static bool IsBindable(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BindableProperty<>);

    private static void Point(object property)
    {
        var type = property.GetType();
        if (!(bool)type.GetProperty(nameof(BindableProperty<float>.IsBound)).GetValue(property)) return;
        string objectType = type.GetField("objectType", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(property) as string;
        string path = (string)type.GetProperty(nameof(BindableProperty<float>.PropertyPath)).GetValue(property);
        string shortName = ShortName(objectType);
        Type target = Array.IndexOf(DemoTypes, shortName) >= 0 ? typeof(GunRigInputs)
            : shortName == nameof(RecoilAnimation) ? typeof(RecoilAnimation) : null;
        if (target == null) return;
        if (target == typeof(GunRigInputs) && !Answers(path))
        {
            if (Warned.Add(shortName + "." + path))
                Debug.LogWarning($"GunRig: KINEMATION's settings read {shortName}.{path}, which GunRigInputs does not have; it stays at its default.");
            return;
        }
        type.GetMethod(nameof(BindableProperty<float>.Bind), new[] { typeof(Type), typeof(string) })
            .Invoke(property, new object[] { target, path });
    }

    // "KINEMATION.X.CasFPSPlayerController, Assembly-CSharp, Version=..." -> "CasFPSPlayerController".
    private static string ShortName(string objectType)
    {
        if (string.IsNullOrEmpty(objectType)) return string.Empty;
        string full = objectType.Split(',')[0].Trim();
        int dot = full.LastIndexOf('.');
        return dot >= 0 ? full.Substring(dot + 1) : full;
    }

    private static bool Answers(string path)
    {
        string first = path.Split('.')[0];
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var type = typeof(GunRigInputs);
        return type.GetProperty(first, flags) != null || type.GetField(first, flags) != null || type.GetMethod(first, flags, null, Type.EmptyTypes, null) != null;
    }
}

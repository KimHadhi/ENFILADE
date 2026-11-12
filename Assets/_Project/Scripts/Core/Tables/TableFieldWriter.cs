using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

internal enum TableWrite { NotFound, Written, Invalid }

/// <summary>
/// Writes one table cell into a field by name: a serialized (public or [SerializeField]) field of the object or its base
/// classes, otherwise a public settable property (NavMeshAgent.speed). Lists use ';' or '|': "0.35;0.2;0.1".
/// Vectors also accept "(10, 28)". Numbers use '.' as decimal point; a trailing '%' is ignored.
/// </summary>
internal static class TableFieldWriter
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<(Type, string), MemberInfo> members = new Dictionary<(Type, string), MemberInfo>();
    private static readonly char[] ListSeparators = { ';', '|' };
    private static readonly char[] VectorSeparators = { ';', '|', ',', ' ', '\t' };

    public static TableWrite TryWrite(object target, string name, string raw, out string error)
    {
        error = null;
        var member = Find(target.GetType(), name);
        if (member == null) return TableWrite.NotFound;
        var field = member as FieldInfo;
        Type type = field != null ? field.FieldType : ((PropertyInfo)member).PropertyType;
        if (!TryConvert(raw, type, out object value))
        {
            error = $"{TypeName(type)} 값으로 읽을 수 없습니다";
            return TableWrite.Invalid;
        }
        if (field != null) field.SetValue(target, value);
        else ((PropertyInfo)member).SetValue(target, value);
        return TableWrite.Written;
    }

    private static MemberInfo Find(Type type, string name)
    {
        if (members.TryGetValue((type, name), out var cached)) return cached;
        MemberInfo found = null;
        for (var t = type; t != null && found == null; t = t.BaseType)
        {
            var field = t.GetField(name, Declared);
            if (field != null && !field.IsInitOnly && !field.IsLiteral
                && (field.IsPublic || field.IsDefined(typeof(SerializeField), false)))
                found = field;
        }
        if (found == null)
        {
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.GetSetMethod() != null && property.GetIndexParameters().Length == 0) found = property;
        }
        members[(type, name)] = found;
        return found;
    }

    private static bool TryConvert(string raw, Type type, out object value)
    {
        value = null;
        raw = raw.Trim();
        if (type == typeof(string)) { value = raw; return true; }
        if (type == typeof(float))
        {
            if (!TryFloat(raw, out float number)) return false;
            value = number; return true;
        }
        if (type == typeof(int))
        {
            if (!TryFloat(raw, out float number) || Mathf.Round(number) != number || Math.Abs(number) > int.MaxValue) return false;
            value = (int)number; return true;
        }
        if (type == typeof(bool))
        {
            switch (raw.ToLowerInvariant())
            {
                case "true": case "1": case "yes": case "y": case "o": value = true; return true;
                case "false": case "0": case "no": case "n": case "x": value = false; return true;
                default: return false;
            }
        }
        if (type.IsEnum)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                value = Enum.ToObject(type, index);
                return Enum.IsDefined(type, value);
            }
            foreach (string option in Enum.GetNames(type))
                if (string.Equals(option, raw, StringComparison.OrdinalIgnoreCase)) { value = Enum.Parse(type, option); return true; }
            return false;
        }
        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4))
        {
            int size = type == typeof(Vector2) ? 2 : type == typeof(Vector3) ? 3 : 4;
            var parts = raw.Trim('(', ')', '[', ']').Split(VectorSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != size) return false;
            var v = Vector4.zero;
            for (int i = 0; i < size; i++)
            {
                if (!TryFloat(parts[i], out float component)) return false;
                v[i] = component;
            }
            value = size == 2 ? (object)(Vector2)v : size == 3 ? (object)(Vector3)v : v;
            return true;
        }
        if (type == typeof(Color))
        {
            if (!ColorUtility.TryParseHtmlString(raw.StartsWith("#") ? raw : "#" + raw, out var color)) return false;
            value = color; return true;
        }
        if (type == typeof(LayerMask))
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bits)) return false;
            value = (LayerMask)bits; return true;
        }
        if (type.IsArray && type.GetArrayRank() == 1)
        {
            var element = type.GetElementType();
            var parts = raw.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries);
            var array = Array.CreateInstance(element, parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!TryConvert(parts[i], element, out object item)) return false;
                array.SetValue(item, i);
            }
            value = array; return true;
        }
        return false;
    }

    private static bool TryFloat(string raw, out float value)
    {
        raw = raw.Trim().TrimEnd('%').Trim();
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string TypeName(Type type)
    {
        if (type == typeof(float)) return "숫자";
        if (type == typeof(int)) return "정수";
        if (type == typeof(bool)) return "true/false";
        if (type.IsEnum) return string.Join("/", Enum.GetNames(type)) + " 중 하나의";
        if (type.IsArray) return "';'로 구분한 " + TypeName(type.GetElementType()) + " 목록";
        return type.Name;
    }
}

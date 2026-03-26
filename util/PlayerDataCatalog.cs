using System;
using System.Collections.Generic;
using System.Reflection;

namespace Patchwork.Util;

/// <summary>
/// Lazy reflection catalog of all bool/int/float/string fields and properties on
/// <see cref="PlayerData"/>.  Used to power the generic PlayerData condition type.
/// Built once on first access; individual member accessors are cached per-name.
/// </summary>
public static class PlayerDataCatalog
{
    public readonly struct Entry
    {
        public readonly string Name;
        public readonly Type   FieldType;
        /// <summary>Short type tag shown in the search list: "bool", "int", "float", "str".</summary>
        public readonly string TypeLabel;

        public Entry(string name, Type type)
        {
            Name      = name;
            FieldType = type;
            TypeLabel = type == typeof(bool)   ? "bool" :
                        type == typeof(int)    ? "int"  :
                        type == typeof(float)  ? "float":
                        type == typeof(string) ? "str"  : type.Name;
        }
    }

    private static List<Entry>                        _catalog;
    private static readonly Dictionary<string, MemberInfo> _memberCache =
        new(StringComparer.Ordinal);

    // ----------------------------------------------------------------
    //  Public API
    // ----------------------------------------------------------------

    public static IReadOnlyList<Entry> All    { get { EnsureBuilt(); return _catalog; } }
    public static int                  Count  { get { EnsureBuilt(); return _catalog.Count; } }

    /// <summary>
    /// Returns entries whose name contains <paramref name="query"/> (case-insensitive).
    /// Pass null or empty to get all entries.
    /// </summary>
    public static IEnumerable<Entry> Search(string query)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(query))
            return _catalog;
        return SearchFiltered(query);
    }

    private static IEnumerable<Entry> SearchFiltered(string query)
    {
        foreach (var e in _catalog)
            if (e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                yield return e;
    }

    /// <summary>
    /// Reads the named field/property from <paramref name="pd"/>.
    /// Returns null if the name is not found or <paramref name="pd"/> is null.
    /// </summary>
    public static object GetValue(PlayerData pd, string name)
    {
        if (pd == null || string.IsNullOrEmpty(name)) return null;
        if (!_memberCache.TryGetValue(name, out var member))
        {
            // Cache miss — look up once and store (null means unknown)
            var t = typeof(PlayerData);
            member = (MemberInfo)t.GetField(name, BindingFlags.Public | BindingFlags.Instance)
                  ?? t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            _memberCache[name] = member; // store null sentinel too — avoids re-scan
        }
        if (member == null) return null;
        return member is FieldInfo fi ? fi.GetValue(pd) :
               member is PropertyInfo pi ? pi.GetValue(pd) : null;
    }

    // ----------------------------------------------------------------
    //  Build
    // ----------------------------------------------------------------

    private static void EnsureBuilt()
    {
        if (_catalog != null) return;
        _catalog = Build();
    }

    private static List<Entry> Build()
    {
        var list    = new List<Entry>(512);
        var seen    = new HashSet<string>(StringComparer.Ordinal);
        var type    = typeof(PlayerData);
        var allowed = new HashSet<Type> { typeof(bool), typeof(int), typeof(float), typeof(string) };

        foreach (var fi in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!allowed.Contains(fi.FieldType)) continue;
            if (!seen.Add(fi.Name)) continue;
            list.Add(new Entry(fi.Name, fi.FieldType));
        }

        foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!pi.CanRead) continue;
            if (!allowed.Contains(pi.PropertyType)) continue;
            if (!seen.Add(pi.Name)) continue;
            list.Add(new Entry(pi.Name, pi.PropertyType));
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}

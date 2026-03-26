using System.Collections.Generic;
using System.Linq;
using Patchwork.Util;

namespace Patchwork.Packs;

public enum ConditionType
{
    Scene,          // active scene name exactly matches value
    SceneContains,  // active scene name contains value
    PackActive,     // another pack (by display name) is enabled
    CrestEquipped,  // player currently has a specific crest equipped
    NailUpgrade,    // player's nail upgrade level (0–4); value supports >=/<=/>/< prefix
    PlayerData,     // generic PlayerData field check; value: "fieldName" (bool) or "fieldName >= target"
}

/// <summary>How a condition joins with the next one in the list (per-pair, not global).</summary>
public enum LogicJoin     { Or, And }
public enum ReloadTrigger { OnSceneTransition, HotReload }

/// <summary>
/// A single condition row in a pack's condition list.
/// Conditions are user-defined per-installation (not authored in pack.json).
/// </summary>
public class PackCondition
{
    public ConditionType Type      { get; set; } = ConditionType.Scene;
    public string        Value     { get; set; } = "";
    public bool          Negate    { get; set; } = false;
    /// <summary>How this condition joins with the next one (OR = new clause, AND = same clause).</summary>
    public LogicJoin     JoinNext  { get; set; } = LogicJoin.Or;

    public bool Evaluate(string sceneName, IReadOnlyList<PackInfo> allPacks)
    {
        bool result = Type switch
        {
            ConditionType.Scene =>
                string.Equals(sceneName, Value, System.StringComparison.OrdinalIgnoreCase),
            ConditionType.SceneContains =>
                sceneName.IndexOf(Value, System.StringComparison.OrdinalIgnoreCase) >= 0,
            ConditionType.PackActive =>
                allPacks.Any(p => p.IsEnabled &&
                    string.Equals(p.Name, Value, System.StringComparison.OrdinalIgnoreCase)),
            ConditionType.CrestEquipped =>
                CrestMatches(HeroController.instance?.playerData?.CurrentCrestID, Value),
            ConditionType.NailUpgrade =>
                NailUpgradeMatches(HeroController.instance?.playerData?.nailUpgrades ?? 0, Value),
            ConditionType.PlayerData =>
                EvaluatePlayerData(HeroController.instance?.playerData, Value),
            _ => true
        };
        return Negate ? !result : result;
    }

    /// <summary>
    /// Returns true if the player's nail upgrade level satisfies the value expression.
    /// Value format: [operator]level, where operator is optional (default ==).
    /// Supported operators: == >= <= > &lt;
    /// Level can be numeric (0–4) or named: old sharpened channelled coiled pure
    /// Examples: "pure", ">=channelled", ">1", "<=coiled"
    /// </summary>
    private static bool NailUpgradeMatches(int actual, string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        string rest = value.Trim();
        string op   = "==";
        if      (rest.StartsWith(">=")) { op = ">="; rest = rest.Substring(2); }
        else if (rest.StartsWith("<=")) { op = "<="; rest = rest.Substring(2); }
        else if (rest.StartsWith(">"))  { op = ">";  rest = rest.Substring(1); }
        else if (rest.StartsWith("<"))  { op = "<";  rest = rest.Substring(1); }
        else if (rest.StartsWith("==")) { op = "=="; rest = rest.Substring(2); }
        int target = ParseNailLevel(rest.Trim());
        if (target < 0) return false;
        return op switch
        {
            ">=" => actual >= target,
            "<=" => actual <= target,
            ">"  => actual >  target,
            "<"  => actual <  target,
            _    => actual == target,
        };
    }

    // ================================================================
    //  PlayerData generic condition
    // ================================================================

    /// <summary>
    /// Evaluates a generic PlayerData expression.
    /// Format: "fieldName" (bool — true if field is true/non-zero/non-empty)
    ///      or "fieldName [op] target" where op ∈ == != >= <= > &lt;
    /// </summary>
    private static bool EvaluatePlayerData(PlayerData pd, string expr)
    {
        if (pd == null || string.IsNullOrEmpty(expr)) return false;

        if (!TryParseFieldExpr(expr, out string fieldName, out string op, out string target))
            return false;

        object val = PlayerDataCatalog.GetValue(pd, fieldName);
        if (val == null) return false;

        // bool shorthand: no operator → true if truthy
        if (op == null)
        {
            if (val is bool b)  return b;
            if (val is int  i)  return i != 0;
            if (val is float f) return f != 0f;
            if (val is string s) return !string.IsNullOrEmpty(s);
            return false;
        }

        return CompareValue(val, op, target);
    }

    /// <summary>Splits "fieldName op target" into its parts. Returns false if expr is just a field name (bool shorthand).</summary>
    private static bool TryParseFieldExpr(string expr, out string fieldName, out string op, out string target)
    {
        fieldName = expr.Trim();
        op        = null;
        target    = null;

        // Try operators longest-first to avoid prefix collisions (>= before >)
        string[] ops = { ">=", "<=", "!=", ">", "<", "==" };
        foreach (var o in ops)
        {
            int idx = expr.IndexOf(o, StringComparison.Ordinal);
            if (idx < 0) continue;
            fieldName = expr.Substring(0, idx).Trim();
            op        = o;
            target    = expr.Substring(idx + o.Length).Trim();
            return true;
        }
        return false; // bool shorthand
    }

    private static bool CompareValue(object val, string op, string target)
    {
        if (val is bool b)
        {
            bool t = target.Equals("true", System.StringComparison.OrdinalIgnoreCase) || target == "1";
            return op switch { "==" => b == t, "!=" => b != t, _ => b == t };
        }
        if (val is int i)
        {
            if (!int.TryParse(target, out int t)) return false;
            return op switch
            {
                ">=" => i >= t, "<=" => i <= t, "!=" => i != t,
                ">"  => i >  t, "<"  => i <  t, _    => i == t,
            };
        }
        if (val is float f)
        {
            if (!float.TryParse(target, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float t)) return false;
            return op switch
            {
                ">=" => f >= t, "<=" => f <= t, "!=" => f != t,
                ">"  => f >  t, "<"  => f <  t, _    => f == t,
            };
        }
        if (val is string s)
        {
            int cmp = string.Compare(s, target, System.StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                ">=" => cmp >= 0, "<=" => cmp <= 0, "!=" => cmp != 0,
                ">"  => cmp >  0, "<"  => cmp <  0, _    => cmp == 0,
            };
        }
        return false;
    }

    private static int ParseNailLevel(string s) => s.ToLowerInvariant() switch
    {
        "0" or "old"         => 0,
        "1" or "sharpened"   => 1,
        "2" or "channelled"  => 2,
        "3" or "coiled"      => 3,
        "4" or "pure"        => 4,
        _ => int.TryParse(s, out int n) && n >= 0 ? n : -1,
    };

    /// <summary>
    /// Returns true if <paramref name="currentID"/> matches <paramref name="value"/>.
    /// An exact case-insensitive match always passes.
    /// A base name without a variant suffix also matches all variants of that crest:
    /// e.g. "Hunter" matches "Hunter", "Hunter_v2", "Hunter_v3".
    /// Specifying the full variant (e.g. "Hunter_v2") still works as a precise filter.
    /// </summary>
    private static bool CrestMatches(string currentID, string value)
    {
        if (currentID == null || string.IsNullOrEmpty(value)) return false;
        if (string.Equals(currentID, value, System.StringComparison.OrdinalIgnoreCase))
            return true;
        // "Hunter" should match "Hunter_v2", "Hunter_v3", etc.
        return currentID.StartsWith(value + "_", System.StringComparison.OrdinalIgnoreCase);
    }

    public PackCondition Clone() => new() { Type = Type, Value = Value, Negate = Negate, JoinNext = JoinNext };

    // ================================================================
    //  GUI label helpers
    // ================================================================

    /// <summary>Known crest IDs with their in-game display names.
    /// Internal ID is what's stored in playerData; display name is shown in the Pack Manager UI.
    /// Cursed and Cloakless are context-specific crests active in particular story segments.
    /// </summary>
    public static readonly (string Id, string DisplayName)[] KnownCrests =
    {
        ("Hunter",     "Hunter"),
        ("Reaper",     "Reaper"),
        ("Wanderer",   "Wanderer"),
        ("Spell",      "Shaman"),
        ("Toolmaster", "Architect"),
        ("Warrior",    "Beast"),
        ("Witch",      "Witch"),
        ("Cursed",     "Cursed"),
        ("Cloakless",  "Cloakless"),
    };

    /// <summary>Returns the display name for a known crest ID, or the raw ID if unknown.</summary>
    public static string CrestDisplayName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "—";
        foreach (var (crestId, name) in KnownCrests)
            if (string.Equals(crestId, id, System.StringComparison.OrdinalIgnoreCase)) return name;
        return id;
    }

    /// <summary>
    /// Preset nail upgrade picker entries. Value is stored as-is in the condition;
    /// DisplayName is shown in the Pack Manager picker dropdown.
    /// </summary>
    public static readonly (string Value, string DisplayName)[] KnownNailLevels =
    {
        ("pure",         "Pure Nail"),
        ("coiled",       "Coiled Nail"),
        ("channelled",   "Channelled Nail"),
        ("sharpened",    "Sharpened Nail"),
        ("old",          "Old Nail"),
        (">=pure",       "≥ Pure Nail"),
        (">=coiled",     "≥ Coiled Nail"),
        (">=channelled", "≥ Channelled Nail"),
        (">=sharpened",  "≥ Sharpened Nail"),
    };

    /// <summary>Returns the display name for a known nail value, or the raw value if custom.</summary>
    public static string NailDisplayValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        foreach (var (val, name) in KnownNailLevels)
            if (string.Equals(val, value, System.StringComparison.OrdinalIgnoreCase)) return name;
        return value;
    }

    /// <summary>Maps a <see cref="ConditionType"/> to its short display label.</summary>
    public static string LabelFor(ConditionType type) => type switch
    {
        ConditionType.Scene         => "scene",
        ConditionType.SceneContains => "scene~",
        ConditionType.PackActive    => "pack",
        ConditionType.CrestEquipped => "crest",
        ConditionType.NailUpgrade   => "nail",
        ConditionType.PlayerData    => "pd",
        _                           => type.ToString()
    };

    public string TypeLabel => LabelFor(Type);

    // ================================================================
    //  Serialization  (pipe-separated within a tab-delimited line)
    //  format: type|negate|join|value
    //  (old format: type|negate|value — parsed with backward compat)
    // ================================================================

    public string Serialize()
    {
        string join = JoinNext == LogicJoin.And ? "and" : "or";
        return $"{TypeLabel}|{(Negate ? 1 : 0)}|{join}|{Value}";
    }

    public static PackCondition TryDeserialize(string token)
    {
        var parts = token.Split('|');
        if (parts.Length < 3) return null;

        ConditionType type = parts[0] switch
        {
            "scene~" => ConditionType.SceneContains,
            "pack"   => ConditionType.PackActive,
            "crest"  => ConditionType.CrestEquipped,
            "nail"   => ConditionType.NailUpgrade,
            "pd"     => ConditionType.PlayerData,
            _        => ConditionType.Scene,
        };
        bool negate = parts[1] == "1";

        // Detect format: new = type|negate|join|value, old = type|negate|value
        LogicJoin join = LogicJoin.Or;
        string value;
        if (parts.Length >= 4 && (parts[2] == "or" || parts[2] == "and"))
        {
            join  = parts[2] == "and" ? LogicJoin.And : LogicJoin.Or;
            value = string.Join("|", parts, 3, parts.Length - 3);
        }
        else
        {
            value = string.Join("|", parts, 2, parts.Length - 2); // value may contain |
        }

        return new PackCondition { Type = type, Negate = negate, JoinNext = join, Value = value };
    }
}

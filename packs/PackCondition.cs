using System.Collections.Generic;
using System.Linq;

namespace Patchwork.Packs;

public enum ConditionType
{
    Scene,         // active scene name exactly matches value
    SceneContains, // active scene name contains value
    PackActive,    // another pack (by display name) is enabled
}

public enum LogicMode    { Or, And }
public enum ReloadTrigger { OnSceneTransition, HotReload }

/// <summary>
/// A single condition row in a pack's condition list.
/// Conditions are user-defined per-installation (not authored in pack.json).
/// </summary>
public class PackCondition
{
    public ConditionType Type   { get; set; } = ConditionType.Scene;
    public string        Value  { get; set; } = "";
    public bool          Negate { get; set; } = false;

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
            _ => true
        };
        return Negate ? !result : result;
    }

    public PackCondition Clone() => new() { Type = Type, Value = Value, Negate = Negate };

    // ================================================================
    //  GUI helpers — cycle through enum values on button click
    // ================================================================

    public string TypeLabel => Type switch
    {
        ConditionType.Scene        => "scene",
        ConditionType.SceneContains => "scene~",
        ConditionType.PackActive   => "pack",
        _                          => "?"
    };

    public void CycleType() =>
        Type = Type switch
        {
            ConditionType.Scene        => ConditionType.SceneContains,
            ConditionType.SceneContains => ConditionType.PackActive,
            _                          => ConditionType.Scene,
        };

    // ================================================================
    //  Serialization  (pipe-separated within a tab-delimited line)
    //  format: type|negate|value
    // ================================================================

    public string Serialize() => $"{TypeLabel}|{(Negate ? 1 : 0)}|{Value}";

    public static PackCondition TryDeserialize(string token)
    {
        var parts = token.Split('|');
        if (parts.Length < 3) return null;
        ConditionType type = parts[0] switch
        {
            "scene~" => ConditionType.SceneContains,
            "pack"   => ConditionType.PackActive,
            _        => ConditionType.Scene,
        };
        string value = string.Join("|", parts, 2, parts.Length - 2); // value may contain |
        return new PackCondition { Type = type, Negate = parts[1] == "1", Value = value };
    }
}

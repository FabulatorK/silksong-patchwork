using System.Collections.Generic;
using System.Linq;

namespace Patchwork.Packs;

/// <summary>
/// Represents a single resource pack discovered on disk.
/// </summary>
public class PackInfo
{
    public string Path        { get; }
    public string Name        { get; }
    public string Author      { get; }
    public string Version     { get; }
    public string Description { get; }
    public bool   IsEnabled   { get; set; } = true;

    /// <summary>True when the pack lives under Patchwork/Packs/ (user-managed).
    /// False when discovered as a Thunderstore plugin pack.</summary>
    public bool IsLocal { get; }

    /// <summary>Cached asset-file counts, populated by PackManager after each Apply()
    /// and persisted to packs-stats.txt across sessions.</summary>
    public PackStats Stats { get; set; }

    // ── Conditions ──────────────────────────────────────────────────
    /// <summary>User-defined activation conditions. Empty = always active (when IsEnabled).</summary>
    public List<PackCondition> Conditions     { get; set; } = new();
    /// <summary>How multiple conditions are combined.</summary>
    public LogicMode           ConditionLogic { get; set; } = LogicMode.Or;
    /// <summary>Whether a condition change triggers an immediate hot reload or waits for
    /// the next scene transition.</summary>
    public ReloadTrigger       ReloadTrigger  { get; set; } = ReloadTrigger.OnSceneTransition;

    public bool HasConditions => Conditions.Count > 0;

    /// <summary>Returns true if this pack's conditions are satisfied in the current context.
    /// Always true when <see cref="HasConditions"/> is false.</summary>
    public bool EvaluateConditions(string sceneName, IReadOnlyList<PackInfo> allPacks)
    {
        if (!HasConditions) return true;
        return ConditionLogic == LogicMode.Or
            ? Conditions.Any(c => c.Evaluate(sceneName, allPacks))
            : Conditions.All(c => c.Evaluate(sceneName, allPacks));
    }

    public PackInfo(string path, string name, bool isLocal,
                    string author = null, string version = null, string description = null)
    {
        Path        = path;
        Name        = name;
        IsLocal     = isLocal;
        Author      = author;
        Version     = version;
        Description = description;
    }

    public PackInfo Clone() => new(Path, Name, IsLocal, Author, Version, Description)
    {
        IsEnabled      = IsEnabled,
        Stats          = Stats,
        Conditions     = Conditions.Select(c => c.Clone()).ToList(),
        ConditionLogic = ConditionLogic,
        ReloadTrigger  = ReloadTrigger,
    };
}

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
    public List<PackCondition> Conditions    { get; set; } = new();
    /// <summary>Whether a condition change triggers an immediate hot reload or waits for
    /// the next scene transition.</summary>
    public ReloadTrigger       ReloadTrigger { get; set; } = ReloadTrigger.OnSceneTransition;

    public bool HasConditions => Conditions.Count > 0;

    /// <summary>
    /// Evaluates conditions using Disjunctive Normal Form (DNF).
    /// Each condition's <see cref="PackCondition.JoinNext"/> controls how it connects to the
    /// next condition: AND keeps them in the same clause; OR starts a new clause.
    /// The pack is active if any clause evaluates to true.
    /// Always returns true when there are no conditions.
    /// </summary>
    public bool EvaluateConditions(string sceneName, IReadOnlyList<PackInfo> allPacks)
    {
        if (!HasConditions) return true;

        bool clauseResult  = true;
        bool anyClauseTrue = false;

        for (int i = 0; i < Conditions.Count; i++)
        {
            clauseResult &= Conditions[i].Evaluate(sceneName, allPacks);

            bool endOfClause = i == Conditions.Count - 1 ||
                               Conditions[i].JoinNext == LogicJoin.Or;
            if (endOfClause)
            {
                if (clauseResult) anyClauseTrue = true;
                clauseResult = true; // reset for the next AND-group
            }
        }

        return anyClauseTrue;
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
        IsEnabled     = IsEnabled,
        Stats         = Stats,
        Conditions    = Conditions.Select(c => c.Clone()).ToList(),
        ReloadTrigger = ReloadTrigger,
    };
}

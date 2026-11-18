using BattleNusantara.Core;

namespace BattleNusantara.UI;

/// <summary>Per-match UI groups. Commands still pass through authoritative Match validation.</summary>
public sealed class TacticalGroups
{
    private readonly Dictionary<int, int[]> groups = new();

    public static int[] Filter(IEnumerable<int> ids, MatchView? state, int owner)
    {
        if (state?.Mode != GameMode.Rts) return [];
        var allowed = state.Units.Where(u => u.Owner == owner && u.Team == 0 && u.Health > 0 && u.Kind is "hero" or "soldier" or "worker")
            .Select(u => u.Id).ToHashSet();
        return ids.Where(allowed.Contains).Distinct().Order().Take(Match.MaxSelection).ToArray();
    }

    public bool Save(int slot, IEnumerable<int> ids, MatchView? state, int owner)
    {
        if (slot is < 4 or > 6) return false;
        var selected = Filter(ids, state, owner);
        if (selected.Length == 0) return false;
        groups[slot] = selected;
        return true;
    }

    public int[] Recall(int slot, MatchView? state, int owner)
    {
        if (slot is < 4 or > 6) return [];
        var selected = Filter(groups.GetValueOrDefault(slot) ?? [], state, owner);
        if (selected.Length == 0) groups.Remove(slot); else groups[slot] = selected;
        return selected.ToArray();
    }

    public int Count(int slot) => groups.GetValueOrDefault(slot)?.Length ?? 0;
    public void Prune(MatchView? state, int owner)
    {
        foreach (var slot in groups.Keys.ToArray()) Recall(slot, state, owner);
    }
    public void Clear() => groups.Clear();
}

using BattleNusantara.Core;
using BattleNusantara.UI;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class TacticalGroupsTests
{
    private static (Match Match, int Hero, int[] Soldiers) Skirmish(int soldiers = 2)
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        for (var i = 0; i < soldiers; i++) Assert.True(match.Execute(0, new GameCommand(CommandKind.Recruit)));
        var units = match.Snapshot().Units;
        return (match, units.Single(u => u.Kind == "hero" && u.Owner == 0).Id,
            units.Where(u => u.Kind == "soldier" && u.Owner == 0).Select(u => u.Id).ToArray());
    }

    [Fact]
    public void Filter_MixedIds_KeepsOwnedLiveCommandableUnitsSortedAndDistinct()
    {
        var (match, hero, soldiers) = Skirmish();
        var state = match.Snapshot(fog: false);
        var enemy = state.Units.First(u => u.Team == 1).Id;
        var result = TacticalGroups.Filter([soldiers[1], enemy, hero, soldiers[1], 9999, soldiers[0]], state, 0);
        Assert.Equal(new[] { hero, soldiers[0], soldiers[1] }.Order(), result);
    }

    [Fact]
    public void Filter_OtherOwnerOrBuilding_ExcludesUnits()
    {
        var (match, hero, soldiers) = Skirmish(1);
        Assert.True(match.Execute(0, new GameCommand(CommandKind.Build, 30, 34)));
        var state = match.Snapshot();
        var outpost = state.Units.Single(u => u.Kind == "outpost").Id;
        Assert.Equal([hero, soldiers[0]], TacticalGroups.Filter([hero, soldiers[0], outpost], state, 0));
        Assert.Empty(TacticalGroups.Filter([hero, soldiers[0]], state, 1));
    }

    [Fact]
    public void Filter_NonRtsOrMissingState_ReturnsEmpty()
    {
        var moba = new Match(GameMode.Moba, "gajah-mada").Snapshot();
        Assert.Empty(TacticalGroups.Filter(moba.Units.Select(u => u.Id), moba, 0));
        Assert.Empty(TacticalGroups.Filter([1, 2], null, 0));
    }

    [Fact]
    public void Filter_MoreThanSelectionLimit_TruncatesToMaxSelection()
    {
        var units = Enumerable.Range(1, 30).Select(id => new UnitView(id, 0, "soldier", 0, 20, 20, 10, 10, "#fff", UnitOrder.Auto, 20, 20, 0)).ToArray();
        var state = new Match(GameMode.Rts, "gajah-mada").Snapshot() with { Units = units };
        var result = TacticalGroups.Filter(units.Select(u => u.Id).Reverse(), state, 0);
        Assert.Equal(Enumerable.Range(1, Match.MaxSelection), result);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(0)]
    public void Save_SlotOutsideFourToSix_IsRejected(int slot)
    {
        var (match, hero, _) = Skirmish(0);
        var groups = new TacticalGroups();
        Assert.False(groups.Save(slot, [hero], match.Snapshot(), 0));
        Assert.Empty(groups.Recall(slot, match.Snapshot(), 0));
        Assert.Equal(0, groups.Count(slot));
    }

    [Fact]
    public void Save_EmptyOrForeignSelection_KeepsPreviousGroup()
    {
        var (match, hero, soldiers) = Skirmish();
        var state = match.Snapshot(fog: false);
        var groups = new TacticalGroups();
        Assert.True(groups.Save(4, soldiers, state, 0));
        Assert.False(groups.Save(4, [], state, 0));
        Assert.False(groups.Save(4, [state.Units.First(u => u.Team == 1).Id], state, 0));
        Assert.Equal(soldiers, groups.Recall(4, state, 0));
        Assert.Equal(0, groups.Count(5));
        Assert.True(groups.Save(5, [hero], state, 0));
        Assert.Equal([hero], groups.Recall(5, state, 0));
    }

    [Fact]
    public void Recall_AfterUnitLoss_DropsMissingUnitsAndEmptiesDeadGroups()
    {
        var (match, hero, soldiers) = Skirmish();
        var state = match.Snapshot();
        var groups = new TacticalGroups();
        Assert.True(groups.Save(4, soldiers, state, 0));
        Assert.True(groups.Save(6, [soldiers[0]], state, 0));
        var afterLoss = state with { Units = state.Units.Where(u => u.Id != soldiers[0]).ToArray() };
        groups.Prune(afterLoss, 0);
        Assert.Equal(1, groups.Count(4));
        Assert.Equal(0, groups.Count(6));
        Assert.Equal([soldiers[1]], groups.Recall(4, afterLoss, 0));
        // A pruned unit does not return even if a later snapshot lists the same ID again.
        Assert.Equal([soldiers[1]], groups.Recall(4, state, 0));
        Assert.Empty(groups.Recall(6, state, 0));
        Assert.Contains(hero, state.Units.Select(u => u.Id));
    }

    [Fact]
    public void Recall_ReturnedArray_DoesNotExposeStoredGroup()
    {
        var (match, _, soldiers) = Skirmish();
        var state = match.Snapshot();
        var groups = new TacticalGroups();
        Assert.True(groups.Save(4, soldiers, state, 0));
        groups.Recall(4, state, 0)[0] = -1;
        Assert.Equal(soldiers, groups.Recall(4, state, 0));
    }

    [Fact]
    public void Clear_RemovesAllGroups()
    {
        var (match, hero, _) = Skirmish(0);
        var groups = new TacticalGroups();
        Assert.True(groups.Save(4, [hero], match.Snapshot(), 0));
        groups.Clear();
        Assert.Equal(0, groups.Count(4));
        Assert.Empty(groups.Recall(4, match.Snapshot(), 0));
    }
}

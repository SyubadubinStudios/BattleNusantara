using BattleNusantara.Core;
using Xunit;

namespace BattleNusantara.Tests;

/// <summary>Whole matches played by fixed policies. They must reach victory, and identically on every run.</summary>
public sealed class FullMatchTests
{
    private static double Distance(UnitView a, double x, double z) => Math.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z));

    /// <summary>RTS: mine, raise an army, upgrade, then attack-move on the enemy gate. The hero retreats to heal.</summary>
    internal static void PlayRts(Match match, long maxTicks)
    {
        var healing = false;
        while (match.Status == MatchStatus.Playing && match.Tick < maxTicks)
        {
            if (match.Tick % 10 == 0)
            {
                var view = match.Snapshot();
                var hero = view.Units.FirstOrDefault(u => u.Kind == "hero");
                var workers = view.Units.Where(u => u.Kind == "worker").ToArray();
                var soldiers = view.Units.Where(u => u.Kind == "soldier" && u.Owner == 0).Select(u => u.Id).ToArray();
                if (workers.Length < 3) match.Execute(0, new(CommandKind.RecruitWorker));
                var idle = workers.Where(w => w.Order == UnitOrder.Hold).Select(w => w.Id).ToArray();
                var mine = view.Resources.Where(r => r.Remaining > 0).OrderBy(r => r.Id).FirstOrDefault();
                if (idle.Length > 0 && mine is not null) match.Execute(0, new(CommandKind.Gather, UnitIds: idle, TargetId: mine.Id));
                if (workers.Length >= 3)
                {
                    if (view.Level < 4 && view.Gold >= 200) match.Execute(0, new(CommandKind.Upgrade));
                    match.Execute(0, new(CommandKind.Recruit));
                }
                if (hero is not null)
                {
                    if (hero.Health < 90) healing = true; else if (hero.Health > hero.MaxHealth - 20) healing = false;
                    if (healing) match.Execute(0, new(CommandKind.Move, 14, 14, [hero.Id]));
                    else if (soldiers.Length >= 5) match.Execute(0, new(CommandKind.AttackMove, 82, 82, [hero.Id]));
                    if (view.UltimateCooldown == 0 && (Distance(hero, 87, 87) < 22 || view.Units.Count(u => u.Team == 1 && Distance(u, hero.X, hero.Z) < 18) >= 2)) match.Execute(0, new(CommandKind.Ultimate));
                    if (view.GuardCooldown == 0 && hero.Health < 120) match.Execute(0, new(CommandKind.Guard));
                }
                if (soldiers.Length >= 5) match.Execute(0, new(CommandKind.AttackMove, 82, 82, soldiers));
            }
            match.Advance();
        }
    }

    /// <summary>MOBA: push the weakest enemy tower with the lane, buy items, retreat to heal, then take the gate.</summary>
    internal static void PlayMoba(Match match, long maxTicks)
    {
        var healing = false;
        while (match.Status == MatchStatus.Playing && match.Tick < maxTicks)
        {
            if (match.Tick % 10 == 0)
            {
                var view = match.Snapshot();
                for (var item = 0; item < Catalog.Items.Count; item++) match.Execute(0, new(CommandKind.BuyItem, TargetId: item));
                var hero = view.Units.FirstOrDefault(u => u.Kind == "hero");
                if (hero is not null)
                {
                    if (hero.Health < hero.MaxHealth * .35) healing = true; else if (hero.Health > hero.MaxHealth * .9) healing = false;
                    var tower = view.Units.Where(u => u.Kind == "tower" && u.Team == 1).OrderBy(u => u.Health).ThenBy(u => u.Id).FirstOrDefault();
                    var exposed = view.Units.Count(u => u.Kind == "tower" && u.Team == 1) < Match.TowersPerTeam;
                    if (healing) match.Execute(0, new(CommandKind.Move, 14, 14));
                    else if (exposed) match.Execute(0, new(CommandKind.Move, 80, 80));
                    else if (tower is not null) match.Execute(0, new(CommandKind.Move, tower.X - 8, tower.Z - 8));
                    if (view.UltimateCooldown == 0 && view.Units.Any(u => u.Team == 1 && Distance(u, hero.X, hero.Z) < 18)) match.Execute(0, new(CommandKind.Ultimate));
                    if (view.GuardCooldown == 0 && hero.Health < hero.MaxHealth * .5) match.Execute(0, new(CommandKind.Guard));
                }
            }
            match.Advance();
        }
    }

    public static readonly TheoryData<GameMode, string> Matches = new()
    {
        { GameMode.Rts, "gajah-mada" }, { GameMode.Rts, "nyi-roro-kidul" },
        { GameMode.Moba, "gajah-mada" }, { GameMode.Moba, "cut-nyak-dien" },
        { GameMode.Adventure, "gajah-mada" }, { GameMode.Adventure, "republik" }
    };

    internal static void Play(Match match, long maxTicks)
    {
        if (match.Mode == GameMode.Rts) PlayRts(match, maxTicks);
        else if (match.Mode == GameMode.Moba) PlayMoba(match, maxTicks);
        else AdventureTests.Play(match, maxTicks);
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void FullMatch_ScriptedPlayer_ReachesVictoryReproducibly(GameMode mode, string hero)
    {
        // Thirty simulated minutes is the ceiling; a policy that stalls is a progression blocker.
        var first = new Match(mode, hero); Play(first, 18000);
        Assert.Equal(MatchStatus.Victory, first.Status);
        Assert.InRange(first.Snapshot(fog: false).Units.Length, 1, Match.MaxUnits);
        var second = new Match(mode, hero); Play(second, 18000);
        Assert.Equal(first.Tick, second.Tick);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first.Snapshot(fog: false)), System.Text.Json.JsonSerializer.Serialize(second.Snapshot(fog: false)));
    }

    [Theory]
    [InlineData(GameMode.Rts)]
    [InlineData(GameMode.Moba)]
    public void IdleMatch_NoPlayerInput_NeverThrowsOrExceedsUnitCap(GameMode mode)
    {
        var match = new Match(mode, "gajah-mada");
        for (var i = 0; i < 12000 && match.Status == MatchStatus.Playing; i++)
        {
            match.Advance();
            if (i % 200 == 0) Assert.InRange(match.Snapshot(fog: false).Units.Length, 1, Match.MaxUnits);
        }
    }
}

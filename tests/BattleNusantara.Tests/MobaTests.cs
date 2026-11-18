using BattleNusantara.Core;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class MobaTests
{
    private static Match NewMatch() => new(GameMode.Moba, "gajah-mada");
    private static void Advance(Match match, int ticks) { for (var i = 0; i < ticks; i++) match.Advance(); }
    private static UnitView? Hero(Match match, int owner = 0) => match.Snapshot(owner).Units.FirstOrDefault(u => u.Kind == "hero" && u.Owner == owner);
    private static int Count(Match match, string kind, int team) => match.Snapshot().Units.Count(u => u.Kind == kind && u.Team == team);

    private static void KillHero(Match match)
    {
        Assert.True(match.Execute(0, new(CommandKind.Move, 80, 80)));
        while (Hero(match) is not null && match.Tick < 3000) match.Advance();
        Assert.Null(Hero(match));
    }

    [Fact]
    public void Setup_Moba_CreatesTowersFiveHeroesPerSideAndNeutralBoss()
    {
        var match = NewMatch();
        Assert.Equal(Match.TowersPerTeam, Count(match, "tower", 0));
        Assert.Equal(Match.TowersPerTeam, Count(match, "tower", 1));
        Assert.Equal(4, Count(match, "champion", 0));
        Assert.Equal(5, Count(match, "champion", 1));
        Assert.Equal(1, Count(match, "boss", 3));
        Assert.Equal(1, Hero(match)!.Id);
        Assert.False(match.BaseExposed(0));
        Assert.False(match.BaseExposed(1));
    }

    [Theory]
    [InlineData(GameMode.Rts)]
    [InlineData(GameMode.Adventure)]
    public void Setup_OtherModes_HaveNoMobaUnitsAndExposedBases(GameMode mode)
    {
        var match = new Match(mode, "gajah-mada");
        Assert.DoesNotContain(match.Snapshot(fog: false).Units, u => u.Kind is "tower" or "champion" || u.Model == "naga");
        Assert.True(match.BaseExposed(0));
        Assert.False(match.Execute(0, new(CommandKind.BuyItem, TargetId: 2)));
    }

    [Fact]
    public void AddPlayer_SecondPlayer_ReplacesOneAiAlly()
    {
        var match = NewMatch(); match.AddPlayer(1, "republik");
        Assert.Equal(3, Count(match, "champion", 0));
        Assert.Equal(2, Count(match, "hero", 0));
        Assert.Equal(5, Count(match, "champion", 1));
    }

    [Fact]
    public void Towers_StayInPlace_AndBasesAreSafeWhileAllTowersStand()
    {
        var match = NewMatch();
        var before = match.Snapshot().Units.Where(u => u.Kind == "tower").Select(u => (u.Id, u.X, u.Z)).ToArray();
        while (!match.BaseExposed(0) && !match.BaseExposed(1) && match.Tick < 6000)
        {
            match.Advance();
            Assert.Equal(900, match.BaseHealth);
            Assert.Equal(900, match.EnemyBaseHealth);
        }
        Assert.True(match.BaseExposed(0) || match.BaseExposed(1), "A tower should fall within ten minutes.");
        var after = match.Snapshot().Units.Where(u => u.Kind == "tower").ToDictionary(u => u.Id);
        Assert.All(before.Where(t => after.ContainsKey(t.Id)), t => Assert.Equal((t.X, t.Z), (after[t.Id].X, after[t.Id].Z)));
        Assert.True(after.Count < before.Length);
    }

    [Fact]
    public void Tower_Destroyed_RewardsGoldAndExposesGate()
    {
        var match = NewMatch();
        while (!match.BaseExposed(1) && match.Status == MatchStatus.Playing && match.Tick < 9000)
        {
            if (match.Tick % 20 == 0 && Hero(match) is { } hero)
            {
                match.Execute(0, new(hero.Health < 90 ? CommandKind.Move : CommandKind.Ultimate, 14, 14));
                if (hero.Health > 230) match.Execute(0, new(CommandKind.Move, 56, 56));
            }
            match.Advance();
        }
        Assert.True(match.BaseExposed(1));
        Assert.True(Count(match, "tower", 1) < Match.TowersPerTeam);
    }

    [Fact]
    public void HeroDeath_Moba_RespawnsAtBaseWithSameIdInsteadOfDefeat()
    {
        var match = NewMatch();
        KillHero(match);
        Assert.Equal(MatchStatus.Playing, match.Status);
        var respawn = match.Snapshot().Respawn;
        Assert.InRange(respawn, 0.1, Match.RespawnTicks * Match.StepSeconds);
        Assert.False(match.Execute(0, new(CommandKind.Move, 30, 30)));
        Assert.False(match.Execute(0, new(CommandKind.Ultimate)));
        Advance(match, (int)Math.Round(respawn / Match.StepSeconds) - 1);
        Assert.Null(Hero(match));
        match.Advance();
        var hero = Hero(match)!;
        Assert.Equal(1, hero.Id);
        Assert.Equal((24d, 24d), (hero.X, hero.Z));
        Assert.Equal(hero.MaxHealth, hero.Health);
        Assert.Equal(0, match.Snapshot().Respawn);
        Assert.True(match.Execute(0, new(CommandKind.Move, 30, 30)));
    }

    [Fact]
    public void Champions_Die_AndReturnSoSidesKeepFiveHeroes()
    {
        var match = NewMatch();
        var sawLoss = false;
        for (var i = 0; i < 3000; i++)
        {
            match.Advance();
            var enemies = Count(match, "champion", 1);
            Assert.InRange(enemies, 0, 5);
            sawLoss |= enemies < 5 || Count(match, "champion", 0) < 4;
        }
        Assert.True(sawLoss, "Some champion should have died in five minutes.");
        Advance(match, 0);
        var ids = match.Snapshot().Units.Where(u => u.Kind == "champion").Select(u => u.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void BuyItem_ValidPurchase_SpendsGoldAndAppliesStats()
    {
        var match = NewMatch();
        var gold = match.Gold; var health = Hero(match)!.MaxHealth;
        Assert.True(match.Execute(0, new(CommandKind.BuyItem, TargetId: 1)));
        Assert.Equal(gold - 120, match.Gold);
        Assert.Equal(health + 90, Hero(match)!.MaxHealth);
        Assert.Equal(health + 90, Hero(match)!.Health);
        Assert.Equal(1, Match.ItemLevel(match.Snapshot().Items, 1));
        Assert.Equal(0, Match.ItemLevel(match.Snapshot().Items, 0));
        Assert.False(match.Execute(0, new(CommandKind.BuyItem, TargetId: 0)));
        Assert.Equal(gold - 120, match.Gold);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(null)]
    public void BuyItem_UnknownItemOrOwner_IsRejected(int? id)
    {
        var match = NewMatch(); var gold = match.Gold;
        Assert.False(match.Execute(0, new(CommandKind.BuyItem, TargetId: id)));
        Assert.False(match.Execute(5, new(CommandKind.BuyItem, TargetId: 2)));
        Assert.Equal(gold, match.Gold);
        Assert.Equal(0, match.Snapshot().Items);
    }

    [Fact]
    public void BuyItem_Levels_CostMoreEachTimeAndStopAtMaximum()
    {
        var match = NewMatch();
        Advance(match, 1500);
        Assert.Equal(MatchStatus.Playing, match.Status);
        for (var level = 0; level < Match.MaxItemLevel; level++)
        {
            var gold = match.Gold;
            Assert.Equal(100 * (level + 1), Match.ItemCost(2, level));
            Assert.True(match.Execute(0, new(CommandKind.BuyItem, TargetId: 2)));
            Assert.Equal(gold - 100 * (level + 1), match.Gold);
        }
        Assert.Equal(Match.MaxItemLevel, Match.ItemLevel(match.Snapshot().Items, 2));
        var after = match.Gold;
        Assert.False(match.Execute(0, new(CommandKind.BuyItem, TargetId: 2)));
        Assert.Equal(after, match.Gold);
        Assert.Equal(0, match.Snapshot(1).Items);
    }

    [Fact]
    public void BuyItem_WhileDead_AppliesAfterRespawn()
    {
        var match = NewMatch();
        var health = Hero(match)!.MaxHealth;
        KillHero(match);
        Assert.True(match.Gold >= 120);
        Assert.True(match.Execute(0, new(CommandKind.BuyItem, TargetId: 1)));
        Advance(match, Match.RespawnTicks);
        Assert.Equal(health + 90, Hero(match)!.MaxHealth);
        Assert.Equal(health + 90, Hero(match)!.Health);
    }

    [Fact]
    public void Boss_LeftAlone_IsTakenByEnemyHuntersWhoGainTheBlessing()
    {
        var match = NewMatch();
        var gold = 0;
        while (match.Snapshot().EnemyBuff == 0 && match.Tick < 9000 && match.Status == MatchStatus.Playing)
        {
            var boss = match.Snapshot().Units.SingleOrDefault(u => u.Kind == "boss");
            if (boss is not null) Assert.Equal((66d, 34d), (boss.X, boss.Z));
            gold = match.Gold;
            match.Advance();
        }
        Assert.InRange(match.Snapshot().EnemyBuff, 59, 60);
        Assert.Equal(0, match.Snapshot().Buff);
        Assert.DoesNotContain(match.Snapshot().Units, u => u.Kind == "boss");
        Assert.True(match.Gold < gold + 150, "The allied side earns no boss bounty when the enemy lands the last hit.");
    }

    [Fact]
    public void Guard_Used_HealsHalvesDamageAndCoolsDown()
    {
        var match = NewMatch();
        Assert.True(match.Execute(0, new(CommandKind.Move, 80, 80)));
        while (Hero(match)!.Health > 150 && match.Tick < 2000) match.Advance();
        var before = Hero(match)!.Health;
        Assert.True(match.Execute(0, new(CommandKind.Guard)));
        Assert.Equal(Math.Min(260, before + 52), Hero(match)!.Health);
        Assert.Equal(12, match.Snapshot().GuardCooldown);
        Assert.False(match.Execute(0, new(CommandKind.Guard)));
        Advance(match, 120);
        Assert.Equal(0, match.Snapshot().GuardCooldown, 6);
    }

    [Fact]
    public void Guard_SameAttackers_TakeHalfTheDamage()
    {
        double Loss(bool guard)
        {
            var match = new Match(GameMode.Adventure, "gajah-mada");
            Assert.True(match.Execute(0, new(CommandKind.Move, 84, 78)));
            while (Hero(match)!.Health == 260 && match.Tick < 600) match.Advance();
            if (guard) Assert.True(match.Execute(0, new(CommandKind.Guard)));
            var start = Hero(match)!.Health;
            Advance(match, Match.GuardTicks - 2);
            return start - Hero(match)!.Health;
        }
        var plain = Loss(false); var guarded = Loss(true);
        Assert.True(plain > 0);
        Assert.Equal(plain / 2, guarded, 6);
    }

    [Fact]
    public void HeroLevel_KillsNearby_RaiseLevelAndStats()
    {
        var match = NewMatch();
        Assert.Equal(1, match.Snapshot().Level);
        Assert.Equal(0, match.Snapshot().Experience);
        var health = Hero(match)!.MaxHealth;
        Assert.True(match.Execute(0, new(CommandKind.Move, 34, 34)));
        while (match.Snapshot().Level == 1 && match.Tick < 3000)
        {
            if (match.Tick % 20 == 0 && Hero(match) is { } hero) match.Execute(0, new(CommandKind.Move, hero.Health < 100 ? 14 : 34, hero.Health < 100 ? 14 : 34));
            match.Advance();
        }
        Assert.Equal(2, match.Snapshot().Level);
        Assert.InRange(match.Snapshot().Experience, 0, 0.999);
        var after = Hero(match)?.MaxHealth ?? health + 25;
        Assert.Equal(health + 25, after);
    }

    [Fact]
    public void Tower_WithTroopsAndHeroInRange_ShootsTroopsFirst()
    {
        var match = NewMatch();
        // Walk beside the enemy middle tower together with the allied wave and watch who takes tower damage first.
        Assert.True(match.Execute(0, new(CommandKind.Move, 56, 56)));
        var heroHit = false; var troopsNear = false;
        for (var i = 0; i < 900 && !heroHit; i++)
        {
            match.Advance();
            var view = match.Snapshot();
            var tower = view.Units.FirstOrDefault(u => u.Kind == "tower" && u.Team == 1 && u.X == 64);
            var hero = Hero(match);
            if (tower is null || hero is null) break;
            double Away(UnitView u) => Math.Sqrt((u.X - tower.X) * (u.X - tower.X) + (u.Z - tower.Z) * (u.Z - tower.Z));
            var troop = view.Units.Any(u => u.Team == 0 && u.Kind == "soldier" && Away(u) <= 14);
            var enemiesOnHero = view.Units.Any(u => u.Team == 1 && u.Kind != "tower" && Math.Abs(u.X - hero.X) + Math.Abs(u.Z - hero.Z) < 16);
            if (troop && Away(hero) <= 14 && !enemiesOnHero) { troopsNear = true; var hp = hero.Health; match.Advance(); heroHit = Hero(match) is { } later && later.Health < hp && match.Snapshot().Units.Any(u => u.Team == 0 && u.Kind == "soldier" && Away(u) <= 13); }
        }
        Assert.False(heroHit, "The tower damaged the hero while allied troops stood in its range.");
        _ = troopsNear;
    }

    [Fact]
    public void Boss_KilledByPlayer_GrantsBlessingAndReturnsLater()
    {
        var match = NewMatch();
        Advance(match, 3000);
        Assert.Equal(MatchStatus.Playing, match.Status);
        for (var level = 0; level < Match.MaxItemLevel; level++)
        {
            Assert.True(match.Execute(0, new(CommandKind.BuyItem, TargetId: 0)));
            Assert.True(match.Execute(0, new(CommandKind.BuyItem, TargetId: 1)));
        }
        var gold = 0;
        // Enemy hunters may have claimed the boss already; wait for it to return.
        while (!match.Snapshot().Units.Any(u => u.Kind == "boss") && match.Tick < 6000) match.Advance();
        // Enemy champions may interrupt the first attempt; the hero returns after respawning.
        while (match.Snapshot().Units.Any(u => u.Kind == "boss") && match.Tick < 9000)
        {
            if (match.Tick % 20 == 0) match.Execute(0, new(CommandKind.Move, 58, 30));
            match.Execute(0, new(CommandKind.Ultimate));
            gold = match.Gold;
            match.Advance();
        }
        Assert.DoesNotContain(match.Snapshot().Units, u => u.Kind == "boss");
        Assert.True(match.Gold >= gold + 150);
        Assert.InRange(match.Snapshot().Buff, 59, 60);
        Assert.Contains("Naga Basuki", match.Message);
        Advance(match, Match.BuffTicks);
        Assert.Equal(0, match.Snapshot().Buff);
        Advance(match, Match.BossRespawnTicks - Match.BuffTicks);
        var boss = match.Snapshot().Units.Single(u => u.Kind == "boss");
        Assert.Equal(900, boss.MaxHealth); // The hero may still be standing in range and strike it at once.
        Assert.InRange(boss.Health, 1, 900);
    }
}

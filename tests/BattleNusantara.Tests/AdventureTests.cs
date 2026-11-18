using BattleNusantara.Core;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class AdventureTests
{
    private static Match NewMatch() => new(GameMode.Adventure, "gajah-mada");
    private static void Advance(Match match, int ticks) { for (var i = 0; i < ticks; i++) match.Advance(); }
    private static QuestState State(Match match, string quest) => match.Snapshot().Quests!.Single(q => q.Id == quest).State;
    private static double Distance(UnitView a, double x, double z) => Math.Sqrt((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z));

    /// <summary>Plays Adventure with a fixed policy: follow the quest graph, retreat to the shrine when hurt.</summary>
    internal static void Play(Match match, long maxTicks, Func<Match, bool>? stop = null)
    {
        var healing = false;
        while (match.Status == MatchStatus.Playing && match.Tick < maxTicks && stop?.Invoke(match) != true)
        {
            if (match.Tick % 5 == 0)
            {
                var view = match.Snapshot();
                var hero = view.Units.FirstOrDefault(u => u.Kind == "hero");
                if (hero is not null)
                {
                    var quests = view.Quests!.ToDictionary(q => q.Id, q => q.State);
                    if (hero.Health < 110) healing = true; else if (hero.Health > hero.MaxHealth - 10) healing = false;
                    UnitView? Nearest(string kind) => view.Units.Where(u => u.Kind == kind).OrderBy(u => Distance(u, hero.X, hero.Z)).ThenBy(u => u.Id).FirstOrDefault();
                    var target = quests[Story.Keeper] != QuestState.Done ? Nearest("npc")
                        : quests[Story.Guardians] != QuestState.Done ? Nearest("guardian")
                        : quests[Story.Inscriptions] != QuestState.Done ? Nearest("relic")
                        : quests[Story.Heirloom] != QuestState.Done ? Nearest("artifact")
                        : Nearest("boss");
                    if (healing) match.Execute(0, new(CommandKind.Move, 13, 13));
                    else if (target is not null) match.Execute(0, new(CommandKind.Move, target.X, target.Z));
                    if (view.UltimateCooldown == 0 && view.Units.Any(u => u.Team == 1 && Distance(u, hero.X, hero.Z) < 18)) match.Execute(0, new(CommandKind.Ultimate));
                    if (view.GuardCooldown == 0 && hero.Health < 150) match.Execute(0, new(CommandKind.Guard));
                }
            }
            match.Advance();
        }
    }

    [Fact]
    public void Story_QuestGraph_IsOrderedAcyclicAndFullyAuthored()
    {
        var seen = new HashSet<string>();
        foreach (var quest in Story.Quests)
        {
            Assert.All(quest.Requires, required => Assert.Contains(required, seen));
            Assert.True(seen.Add(quest.Id), $"Duplicate quest {quest.Id}");
            Assert.False(string.IsNullOrWhiteSpace(quest.Title));
            Assert.False(string.IsNullOrWhiteSpace(quest.TitleEn));
        }
        Assert.Equal(Story.Kalatidha, Story.Quests[^1].Id);
        Assert.All(Story.Dialogues, entry =>
        {
            Assert.NotEmpty(entry.Value);
            Assert.All(entry.Value, line => Assert.False(string.IsNullOrWhiteSpace(line.Speaker) || string.IsNullOrWhiteSpace(line.Text) || string.IsNullOrWhiteSpace(line.TextEn)));
        });
        Assert.All(new[] { "intro", "gua-terbuka", "akhir", Story.Keeper, Story.Inscriptions, Story.Heirloom, Story.Kalatidha }, key => Assert.True(Story.Dialogues.ContainsKey(key)));
    }

    [Fact]
    public void Quests_AtStart_OnlyRootsAreActive()
    {
        var match = NewMatch();
        Assert.Equal(QuestState.Active, State(match, Story.Keeper));
        Assert.Equal(QuestState.Active, State(match, Story.Inscriptions));
        Assert.Equal(QuestState.Locked, State(match, Story.Guardians));
        Assert.Equal(QuestState.Locked, State(match, Story.Heirloom));
        Assert.Equal(QuestState.Locked, State(match, Story.Kalatidha));
        var relics = match.Snapshot().Quests!.Single(q => q.Id == Story.Inscriptions);
        Assert.Equal((0, 3), (relics.Progress, relics.Goal));
        Assert.Null(new Match(GameMode.Rts, "gajah-mada").Snapshot().Quests);
        Assert.Equal("", new Match(GameMode.Moba, "gajah-mada").Snapshot().Dialogue);
    }

    [Fact]
    public void Dialogue_Intro_AdvancesOnCommandAndOnItsOwnWithoutPausingPlay()
    {
        var match = NewMatch();
        Assert.Equal("intro:0", match.Snapshot().Dialogue);
        Assert.True(match.Execute(0, new(CommandKind.AdvanceDialogue)));
        Assert.Equal("intro:1", match.Snapshot().Dialogue);
        Assert.False(match.Execute(7, new(CommandKind.AdvanceDialogue)));
        Advance(match, Match.DialogueLineTicks - 1);
        Assert.Equal("intro:1", match.Snapshot().Dialogue);
        match.Advance();
        Assert.Equal("", match.Snapshot().Dialogue);
        Assert.Equal(Match.DialogueLineTicks, match.Tick);
        Assert.False(match.Execute(0, new(CommandKind.AdvanceDialogue)));
        Assert.False(new Match(GameMode.Rts, "gajah-mada").Execute(0, new(CommandKind.AdvanceDialogue)));
    }

    [Fact]
    public void Keeper_Reached_CompletesQuestUnlocksGuardiansAndSpeaks()
    {
        var match = NewMatch();
        Assert.True(match.Execute(0, new(CommandKind.AdvanceDialogue)));
        Assert.True(match.Execute(0, new(CommandKind.AdvanceDialogue)));
        var gold = match.Gold;
        Assert.True(match.Execute(0, new(CommandKind.Move, 30, 21)));
        Advance(match, 10);
        Assert.Equal(QuestState.Done, State(match, Story.Keeper));
        Assert.Equal(QuestState.Active, State(match, Story.Guardians));
        Assert.Equal(QuestState.Locked, State(match, Story.Heirloom));
        Assert.StartsWith(Story.Keeper + ":", match.Snapshot().Dialogue);
        Assert.Equal(gold + 60 + 3, match.Gold); // Quest reward plus one second of passive income.
    }

    [Fact]
    public void Cave_BeforeGuardiansFall_IsSealedAndBossCannotBeHurt()
    {
        var match = NewMatch();
        Assert.Contains(match.Snapshot().Obstacles, o => o is { X: 84, Z: 30 });
        Assert.False(match.Execute(0, new(CommandKind.Move, 88, 12)));
        Assert.True(match.Execute(0, new(CommandKind.Move, 84, 72)));
        for (var i = 0; i < 400 && match.Status == MatchStatus.Playing; i++)
        {
            match.Execute(0, new(CommandKind.Ultimate));
            match.Advance();
            var boss = match.Snapshot().Units.Single(u => u.Kind == "boss");
            Assert.Equal(700, boss.Health);
        }
        Assert.Equal(QuestState.Locked, State(match, Story.Kalatidha));
    }

    [Fact]
    public void FullRun_ScriptedPlayer_CompletesEveryQuestInGraphOrderAndWins()
    {
        var match = NewMatch();
        var order = new List<string>();
        Play(match, 12000, m =>
        {
            foreach (var quest in m.Snapshot().Quests!) if (quest.State == QuestState.Done && !order.Contains(quest.Id)) order.Add(quest.Id);
            return false;
        });
        Assert.Equal(MatchStatus.Victory, match.Status);
        // The side quest is optional and was not taken; every main quest is done.
        Assert.All(match.Snapshot().Quests!.Where(q => Story.Quests.Any(main => main.Id == q.Id)), q => Assert.Equal(QuestState.Done, q.State));
        Assert.Equal(Story.Quests.Count, match.Snapshot().Quests!.Count(q => q.State == QuestState.Done));
        Assert.Equal(3, match.Relics);
        // Each quest finished only after its prerequisites.
        foreach (var quest in Story.Quests.Where(q => order.Contains(q.Id)))
            Assert.All(quest.Requires, required => Assert.True(order.IndexOf(required) < order.IndexOf(quest.Id)));
        Assert.DoesNotContain(match.Snapshot().Obstacles, o => o is { X: 84, Z: 30 });
        var tick = match.Tick;
        match.Advance();
        Assert.Equal(tick, match.Tick);
        Assert.False(match.Execute(0, new(CommandKind.Move, 10, 10)));

        var again = NewMatch();
        Play(again, 12000);
        Assert.Equal(tick, again.Tick);
    }

    [Fact]
    public void SaveLoad_MidRun_RestoresStateAndContinuesIdentically()
    {
        var match = NewMatch();
        Play(match, 12000, m => m.Snapshot().Quests!.Count(q => q.State == QuestState.Done) >= 2);
        Assert.Equal(MatchStatus.Playing, match.Status);
        var json = match.Save();
        Assert.NotNull(json);
        var loaded = Match.Load(json);
        Assert.NotNull(loaded);
        string Json(Match m) => System.Text.Json.JsonSerializer.Serialize(m.Snapshot());
        Assert.Equal(Json(match), Json(loaded!));
        Assert.Equal(json, loaded!.Save());
        Play(match, match.Tick + 600);
        Play(loaded, loaded.Tick + 600);
        Assert.Equal(Json(match), Json(loaded));
        Play(loaded, 12000);
        Assert.Equal(MatchStatus.Victory, loaded.Status);
    }

    [Fact]
    public void Save_AfterCaveOpens_KeepsDoorOpenWhenLoaded()
    {
        var match = NewMatch();
        Play(match, 12000, m => State(m, Story.Guardians) == QuestState.Done);
        var loaded = Match.Load(match.Save())!;
        Assert.DoesNotContain(loaded.Snapshot().Obstacles, o => o is { X: 84, Z: 30 });
        Assert.True(loaded.Execute(0, new(CommandKind.Move, 88, 12)));
    }

    [Theory]
    [InlineData(GameMode.Rts)]
    [InlineData(GameMode.Moba)]
    public void Save_OtherModesFinishedOrCoop_ReturnsNull(GameMode mode)
    {
        Assert.Null(new Match(mode, "gajah-mada").Save());
        var coop = NewMatch(); coop.AddPlayer(1, "republik");
        Assert.Null(coop.Save());
        var done = NewMatch();
        Play(done, 12000);
        Assert.Null(done.Save());
    }

    [Fact]
    public void Load_MalformedOrImplausibleData_IsRejected()
    {
        var json = NewMatch().Save()!;
        Assert.NotNull(Match.Load(json));
        Assert.Null(Match.Load(null));
        Assert.Null(Match.Load(""));
        Assert.Null(Match.Load("not json"));
        Assert.Null(Match.Load("{}"));
        Assert.Null(Match.Load("[1,2,3]"));
        Assert.Null(Match.Load(new string(' ', Match.MaxSaveLength + 1)));
        Assert.Null(Match.Load(json.Replace("\"Version\":3", "\"Version\":4")));
        Assert.Null(Match.Load(json.Replace("\"Model\":\"tuyul\"", "\"Model\":\"gundala\"")));
        Assert.Null(Match.Load(json.Replace("\"Kind\":\"creep\"", "\"Kind\":\"guardian\"")));
        Assert.Null(Match.Load(json.Replace("\"Model\":\"captain\"", "\"Model\":\"gundala\"")));
        Assert.Null(Match.Load(json.Replace("\"SideStarted\":false", "\"SideStarted\":false,\"SideDone\":true").Replace("\"SideDone\":false", "\"Spare\":0")));
        Assert.Null(Match.Load(json.Replace("\"Hero\":\"gajah-mada\"", "\"Hero\":\"nobody\"")));
        Assert.Null(Match.Load(json.Replace("\"Kind\":\"boss\"", "\"Kind\":\"tower\"")));
        Assert.Null(Match.Load(json.Replace("\"Kind\":\"hero\"", "\"Kind\":\"npc\"")));
        Assert.Null(Match.Load(json.Replace("\"MaxHealth\":700", "\"MaxHealth\":1e9")));
        Assert.Null(Match.Load(json.Replace("\"X\":84", "\"X\":840")));
        Assert.Null(Match.Load(json.Replace("\"Gold\":180", "\"Gold\":-5")));
        Assert.Null(Match.Load(json.Replace("\"QuestsDone\":[]", "\"QuestsDone\":[\"kalatidha\"]")));
        Assert.Null(Match.Load(json.Replace("\"QuestsDone\":[]", "\"QuestsDone\":[\"unknown\"]")));
        Assert.Null(Match.Load(json.Replace("\"DialogueKey\":\"intro\"", "\"DialogueKey\":\"missing\"")));
        Assert.Null(Match.Load(json.Replace("\"DialogueLine\":0", "\"DialogueLine\":9")));
        Assert.Null(Match.Load(json.Replace("\"Units\":[", "\"Units\":[null,")));
    }
}

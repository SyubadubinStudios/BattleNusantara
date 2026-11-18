using System.Diagnostics;
using System.Text.Json;
using BattleNusantara.Core;
using Xunit;
using Xunit.Abstractions;

namespace BattleNusantara.Tests;

/// <summary>
/// Measures the simulation against the budgets in docs/PERFORMANCE.md. The asserted limits are deliberately
/// several times looser than the measurements so the tests catch regressions, not machine noise.
/// </summary>
public sealed class PerformanceTests(ITestOutputHelper output)
{
    // Budgets: a 100 ms tick leaves the simulation at most 5 % of it; one snapshot must fit a single 32 KB SignalR message.
    public const double TickBudgetMs = 5, AllocationBudgetBytes = 256 * 1024;
    public const int SnapshotBudgetBytes = 32 * 1024;

    private sealed record Sample(double MeanTickMs, double WorstTickMs, double MeanSnapshotMs, long BytesPerTick, int WorstSnapshotBytes, int WorstUnits, long Ticks);

    private static Sample Measure(GameMode mode, bool scripted)
    {
        // Warm-up run so JIT compilation is not billed to the first ticks.
        var warm = new Match(mode, "gajah-mada"); for (var i = 0; i < 300; i++) { warm.Advance(); warm.Snapshot(); }
        var match = new Match(mode, "gajah-mada");
        double tickTotal = 0, tickWorst = 0, snapshotTotal = 0; long allocated = 0, ticks = 0; int bytesWorst = 0, unitsWorst = 0;
        var watch = new Stopwatch();
        void Step()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart(); match.Advance(); watch.Stop();
            var elapsed = watch.Elapsed.TotalMilliseconds; tickTotal += elapsed; tickWorst = Math.Max(tickWorst, elapsed);
            watch.Restart(); var view = match.Snapshot(); watch.Stop();
            snapshotTotal += watch.Elapsed.TotalMilliseconds;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            ticks++;
            if (ticks % 10 == 0)
            {
                bytesWorst = Math.Max(bytesWorst, JsonSerializer.SerializeToUtf8Bytes(view).Length);
                unitsWorst = Math.Max(unitsWorst, match.Snapshot(fog: false).Units.Length);
            }
        }
        if (scripted)
        {
            // The scripted policies advance the match themselves; interleave measurement by running them one second at a time.
            while (match.Status == MatchStatus.Playing && match.Tick < 6000)
            {
                var until = match.Tick + 10;
                FullMatchTests.Play(match, until - 1);
                if (match.Status == MatchStatus.Playing) Step();
            }
        }
        else for (var i = 0; i < 6000 && match.Status == MatchStatus.Playing; i++) Step();
        return new(tickTotal / ticks, tickWorst, snapshotTotal / ticks, allocated / ticks, bytesWorst, unitsWorst, match.Tick);
    }

    [Theory]
    [InlineData(GameMode.Rts, false)]
    [InlineData(GameMode.Rts, true)]
    [InlineData(GameMode.Moba, false)]
    [InlineData(GameMode.Moba, true)]
    [InlineData(GameMode.Adventure, false)]
    [InlineData(GameMode.Adventure, true)]
    public void Simulation_TenMinutesOrFullMatch_StaysInsideBudgets(GameMode mode, bool scripted)
    {
        var sample = Measure(mode, scripted);
        output.WriteLine($"PERF {mode} {(scripted ? "scripted" : "idle")}: ticks={sample.Ticks} tick mean={sample.MeanTickMs:0.000} ms worst={sample.WorstTickMs:0.000} ms, snapshot mean={sample.MeanSnapshotMs:0.000} ms, alloc={sample.BytesPerTick} B/tick, snapshot worst={sample.WorstSnapshotBytes} B ({sample.WorstSnapshotBytes * 10 / 1024.0:0.0} KB/s per client), units worst={sample.WorstUnits}");
        Assert.True(sample.MeanTickMs + sample.MeanSnapshotMs < TickBudgetMs, $"Mean tick + snapshot {sample.MeanTickMs + sample.MeanSnapshotMs:0.000} ms exceeds {TickBudgetMs} ms.");
        Assert.True(sample.BytesPerTick < AllocationBudgetBytes, $"{sample.BytesPerTick} B allocated per tick exceeds {AllocationBudgetBytes}.");
        Assert.True(sample.WorstSnapshotBytes < SnapshotBudgetBytes, $"Snapshot of {sample.WorstSnapshotBytes} B exceeds {SnapshotBudgetBytes}.");
        Assert.InRange(sample.WorstUnits, 1, Match.MaxUnits);
    }

    [Fact]
    public void Save_AdventureInProgress_FitsBrowserStorageLimit()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada");
        AdventureTests.Play(match, 12000, m => m.Snapshot().Quests!.Count(q => q.State == QuestState.Done) >= 3);
        var json = match.Save()!;
        output.WriteLine($"PERF save: {json.Length} chars");
        Assert.InRange(json.Length, 1, 12000); // world.js refuses larger saves: escaped, they must fit the 32 KB UI circuit message limit.
    }
}

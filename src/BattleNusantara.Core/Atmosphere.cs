namespace BattleNusantara.Core;

/// <summary>What the sky looks like at a tick. Weather is one of clear, mist, rain, storm; daylight runs from 0.25 (midnight) to 1 (noon).</summary>
public sealed record Sky(string Weather, double Daylight, string Phase);

/// <summary>
/// Weather and time of day, derived from the match tick alone. Every client of a room therefore sees the same sky
/// without anything new in the snapshot. Weather has one rule in the simulation: <see cref="SightFactor"/> shortens
/// how far units notice enemies and, in RTS, how far the team sees. Darkness has no effect on play.
/// </summary>
public static class Atmosphere
{
    /// <summary>One day lasts six minutes; a weather spell lasts 45 seconds.</summary>
    public const int DayTicks = 3600, WeatherTicks = 450;
    // Seven spells against an eight-spell day, so the weather drifts through the day instead of repeating with it.
    private static readonly string[] Pattern = ["clear", "clear", "mist", "clear", "rain", "storm", "clear"];

    public static Sky At(long tick)
    {
        if (tick < 0) tick = 0;
        var day = tick % DayTicks / (double)DayTicks; // 0 morning, .25 noon, .5 evening, .75 midnight.
        var daylight = Math.Round(.625 + .375 * Math.Cos(2 * Math.PI * (day - .25)), 3);
        var phase = daylight >= .8 ? "day" : daylight <= .45 ? "night" : day is >= .25 and < .75 ? "dusk" : "dawn";
        return new Sky(Pattern[tick / WeatherTicks % Pattern.Length], daylight, phase);
    }

    /// <summary>Multiplier on sight and vision radii: mist 0.8, storm 0.75, otherwise 1.</summary>
    public static double SightFactor(long tick) => At(tick).Weather switch { "mist" => .8, "storm" => .75, _ => 1 };
}

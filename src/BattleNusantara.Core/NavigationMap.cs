namespace BattleNusantara.Core;

public readonly record struct MapPoint(double X, double Z);
public sealed record ObstacleView(double X, double Z, double Width, double Depth);

/// <summary>Immutable deterministic visibility graph around axis-aligned obstacles; rebuild when buildings change.</summary>
public sealed class NavigationMap
{
    public const double Clearance = 2;
    public static readonly NavigationMap Open = new([]);
    public static readonly NavigationMap Rts = new([
        new(44, 40, 12, 8), new(62, 60, 10, 12), new(28, 62, 8, 10)
    ]);
    // Adventure: the Mystic Cave in the north-east corner. Its door is an obstacle until the guardians fall.
    private static readonly ObstacleView[] CaveWalls = [new(68, 17, 2, 26), new(74, 30, 12, 2), new(92, 30, 8, 2)];
    public static readonly NavigationMap AdventureOpen = new(CaveWalls);
    public static readonly NavigationMap AdventureClosed = new(CaveWalls.Append(new(84, 30, 8, 2)));
    private readonly ObstacleView[] obstacles;
    private readonly MapPoint[] corners;
    public IReadOnlyList<ObstacleView> Obstacles { get; }

    public NavigationMap(IEnumerable<ObstacleView> obstacles)
    {
        this.obstacles = obstacles.ToArray();
        Obstacles = Array.AsReadOnly(this.obstacles);
        corners = this.obstacles.SelectMany(o =>
        {
            var dx = o.Width / 2 + Clearance + .05;
            var dz = o.Depth / 2 + Clearance + .05;
            return new[] { new MapPoint(o.X - dx, o.Z - dz), new(o.X + dx, o.Z - dz), new(o.X + dx, o.Z + dz), new(o.X - dx, o.Z + dz) };
        }).Where(IsWalkable).ToArray();
    }

    public bool IsWalkable(MapPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Z)
        && point.X >= 4 && point.X <= 96 && point.Z >= 4 && point.Z <= 96
        && !obstacles.Any(o => Math.Abs(point.X - o.X) <= o.Width / 2 + Clearance && Math.Abs(point.Z - o.Z) <= o.Depth / 2 + Clearance);

    public bool ClearSegment(MapPoint from, MapPoint to, double padding = Clearance)
    {
        foreach (var obstacle in obstacles)
        {
            var minX = obstacle.X - obstacle.Width / 2 - padding;
            var maxX = obstacle.X + obstacle.Width / 2 + padding;
            var minZ = obstacle.Z - obstacle.Depth / 2 - padding;
            var maxZ = obstacle.Z + obstacle.Depth / 2 + padding;
            var enter = 0d; var exit = 1d;
            if (Clip(from.X, to.X - from.X, minX, maxX, ref enter, ref exit)
                && Clip(from.Z, to.Z - from.Z, minZ, maxZ, ref enter, ref exit)) return false;
        }
        return true;
    }

    public MapPoint[] FindPath(MapPoint start, MapPoint goal)
    {
        if (!IsWalkable(start) || !IsWalkable(goal)) return [];
        if (ClearSegment(start, goal)) return [goal];
        var nodes = new MapPoint[corners.Length + 2];
        nodes[0] = start; nodes[1] = goal; corners.CopyTo(nodes, 2);
        var costs = new double[nodes.Length]; Array.Fill(costs, double.PositiveInfinity); costs[0] = 0;
        var parents = new int[nodes.Length]; Array.Fill(parents, -1);
        var visited = new bool[nodes.Length];
        for (var step = 0; step < nodes.Length; step++)
        {
            var current = -1;
            for (var i = 0; i < nodes.Length; i++)
                if (!visited[i] && double.IsFinite(costs[i]) && (current < 0 || costs[i] < costs[current])) current = i;
            if (current < 0) return [];
            if (current == 1)
            {
                var route = new List<MapPoint>();
                for (var node = 1; node != 0; node = parents[node]) route.Add(nodes[node]);
                route.Reverse(); return route.ToArray();
            }
            visited[current] = true;
            for (var next = 0; next < nodes.Length; next++)
            {
                if (visited[next] || !ClearSegment(nodes[current], nodes[next])) continue;
                var dx = nodes[current].X - nodes[next].X; var dz = nodes[current].Z - nodes[next].Z;
                var cost = costs[current] + Math.Sqrt(dx * dx + dz * dz);
                if (cost < costs[next]) { costs[next] = cost; parents[next] = current; }
            }
        }
        return [];
    }

    private static bool Clip(double origin, double direction, double min, double max, ref double enter, ref double exit)
    {
        if (Math.Abs(direction) < 1e-9) return origin >= min && origin <= max;
        var a = (min - origin) / direction; var b = (max - origin) / direction;
        if (a > b) (a, b) = (b, a);
        enter = Math.Max(enter, a); exit = Math.Min(exit, b);
        return enter <= exit;
    }
}

namespace BattleNusantara.Core;

/// <summary>How hard a neutral camp is; its creeps, bounty, loot and respawn follow from it.</summary>
public enum CampTier { Lemah, Sedang, Kuat, Purba }

public sealed record ResourceDefinition(int Id, double X, double Z, int Amount);
public sealed record CampDefinition(int Id, double X, double Z, CampTier Tier);
/// <summary>A painted area of the landscape: subur (fertile, rice fields), hutan (forest), tandus (barren), gurun (sand sea),
/// sabana (savanna), rawa (swamp), pantai (beach) or vulkanik (volcanic ash). Presentation only; the simulation ignores it.</summary>
public sealed record BiomeRegion(string Biome, double X, double Z, double Radius);

/// <summary>
/// A playable map. Size is the side of the square arena; bases, spawns, lanes and towers are derived from it in
/// <see cref="MapLayout"/>, so a map only lists what makes it different: terrain obstacles, gold mines and camps.
/// Regions and Edges only describe the landscape for the renderer. Edges are the land beyond the north (low Z), east
/// (high X), south (high Z) and west (low X) sides: gunung (mountains), vulkan (a smoking volcano), laut (sea and
/// beach), hutan (forested hills) or gurun (dunes).
/// </summary>
public sealed record MapDefinition(string Id, GameMode Mode, double Size, string Name, string NameEn, string Description, string DescriptionEn,
    string Theme, ObstacleView[] Obstacles, ResourceDefinition[] Resources, CampDefinition[] Camps, BiomeRegion[] Regions, string[] Edges)
{
    public string SizeName => Size < 90 ? "kecil" : Size > 120 ? "besar" : "sedang";
    public string SizeNameEn => Size < 90 ? "small" : Size > 120 ? "large" : "medium";
}

/// <summary>Positions every map shares, scaled to its size. At size 100 they are the numbers the game always used.</summary>
public sealed class MapLayout(double size)
{
    public double Size { get; } = size;
    public const double Margin = 4;
    public double Max => Size - Margin;
    public double AllyBase => Size * .13;
    public double EnemyBase => Size * .87;
    /// <summary>Hero arrival for player <paramref name="owner"/>: two in front of the shrine, the rest in rows behind.</summary>
    public (double X, double Z) HeroSpawn(int owner) => owner <= 1
        ? (AllyBase + 11 + owner * 5, AllyBase + 11)
        : (AllyBase + 4 + (owner - 2) % 4 * 4, AllyBase + 15 + (owner - 2) / 4 * 4);
    public MapPoint RecruitSpawn => new(AllyBase + 4, AllyBase + 7);
    public MapPoint WorkerSpawn => new(AllyBase + 7, AllyBase + 5);
    /// <summary>Points outposts may not cover: recruitment, workers and the second player's arrival.</summary>
    public MapPoint[] ReservedSpawns => [RecruitSpawn, WorkerSpawn, new(AllyBase + 16, AllyBase + 11)];
    public (double X, double Z) WaveSpawn(int team, int lane) => team == 0 ? (AllyBase + 11, AllyBase + 11 + lane * 2) : (EnemyBase - 11, EnemyBase - 11 + lane * 2);
    /// <summary>MOBA lane waypoints: 0 runs up the west side, 1 is the middle, 2 runs along the south.</summary>
    public (double X, double Z) Lane(int lane) => lane switch { 0 => (Size * .2, Size * .8), 2 => (Size * .8, Size * .2), _ => (Size * .5, Size * .5) };
    public (double X, double Z)[] AllyTowers => [(Size * .2, Size * .48), (Size * .36, Size * .36), (Size * .48, Size * .2)];
    public (double X, double Z) ChampionSpawn(int team, int slot) => team == 0 ? (AllyBase + 3 + slot * 3, AllyBase + 7) : (EnemyBase - 3 - slot * 3, EnemyBase - 7);
    public (double X, double Z) Boss => (Size * .66, Size * .34);
    /// <summary>How far from the shrine outposts may be founded.</summary>
    public double OutpostReach => Size * .55;
    /// <summary>Explored-ground cells per side (2 units each).</summary>
    public int Cells => (int)Math.Ceiling(Size / 2);
}

/// <summary>The maps of the game. The first map of each mode is its default.</summary>
public static class Maps
{
    public const string LembahCandi = "lembah-candi", PesisirSelatan = "pesisir-selatan", HutanLarangan = "hutan-larangan";
    public const string ArenaJagat = "arena-jagat", GelanggangSabana = "gelanggang-sabana", JembatanNaga = "jembatan-naga";
    public const string PulauPrasasti = "pulau-prasasti";

    public static readonly IReadOnlyList<MapDefinition> All = Array.AsReadOnly(new[]
    {
        // ---------------------------------------------------------------- RTS
        new MapDefinition(LembahCandi, GameMode.Rts, 100, "Lembah Candi", "Temple Valley",
            "Lembah bertabur reruntuhan candi. Tiga tambang, empat kamp penunggu.", "A valley strewn with temple ruins. Three mines, four guardian camps.",
            "valley",
            [new(44, 40, 12, 8, "ruin"), new(62, 60, 10, 12, "ruin"), new(28, 62, 8, 10, "ruin")],
            [new(1, 20, 34, 120), new(2, 47, 22, 300), new(3, 76, 48, 400)],
            [new(1, 18, 76, CampTier.Lemah), new(2, 76, 16, CampTier.Lemah), new(3, 36, 88, CampTier.Sedang), new(4, 90, 30, CampTier.Kuat)],
            [new("subur", 18, 18, 28), new("hutan", 12, 64, 22), new("hutan", 62, 10, 16), new("sabana", 50, 50, 18), new("tandus", 84, 84, 24), new("rawa", 36, 88, 12)],
            ["vulkan", "hutan", "laut", "gunung"]),
        new MapDefinition(PesisirSelatan, GameMode.Rts, 80, "Pesisir Selatan", "Southern Shore",
            "Pantai sempit di antara dua laguna. Pertempuran cepat dan rapat.", "A narrow shore between two lagoons. Fast, close fighting.",
            "coast",
            [new(26, 46, 8, 14, "water"), new(54, 34, 8, 14, "water"), new(40, 40, 6, 6, "rock")],
            [new(1, 16, 30, 160), new(2, 34, 22, 260), new(3, 60, 50, 300)],
            [new(1, 16, 62, CampTier.Lemah), new(2, 62, 16, CampTier.Lemah), new(3, 40, 66, CampTier.Sedang)],
            [new("subur", 14, 14, 20), new("sabana", 40, 28, 16), new("rawa", 26, 46, 11), new("rawa", 54, 34, 11), new("pantai", 62, 70, 22), new("pantai", 74, 40, 16)],
            ["gunung", "laut", "laut", "hutan"]),
        new MapDefinition(HutanLarangan, GameMode.Rts, 140, "Hutan Larangan", "Forbidden Forest",
            "Hutan lebat penuh rumpun yang menutup pandangan. Kamp purba menjaga harta terbesar.", "Dense forest whose groves block sight. An ancient camp guards the richest treasure.",
            "forest",
            [new(48, 64, 12, 12, "grove"), new(64, 44, 12, 10, "grove"), new(80, 98, 10, 14, "grove"), new(100, 76, 14, 10, "grove"),
             new(40, 104, 10, 10, "grove"), new(104, 40, 10, 10, "grove"), new(72, 72, 8, 8, "rock")],
            [new(1, 30, 48, 200), new(2, 52, 26, 250), new(3, 76, 58, 400), new(4, 110, 96, 400)],
            [new(1, 30, 86, CampTier.Lemah), new(2, 86, 30, CampTier.Lemah), new(3, 56, 120, CampTier.Sedang), new(4, 120, 56, CampTier.Sedang),
             new(5, 22, 120, CampTier.Kuat), new(6, 120, 22, CampTier.Kuat), new(7, 96, 120, CampTier.Purba)],
            [new("subur", 22, 22, 24), new("hutan", 44, 44, 30), new("hutan", 100, 100, 28), new("hutan", 30, 108, 26), new("hutan", 108, 30, 26),
             new("rawa", 72, 72, 16), new("tandus", 96, 122, 18), new("vulkanik", 122, 122, 16)],
            ["vulkan", "hutan", "hutan", "gunung"]),
        // ---------------------------------------------------------------- MOBA
        new MapDefinition(ArenaJagat, GameMode.Moba, 100, "Arena Jagat", "Jagat Arena",
            "Tiga jalur, Naga Basuki di hutan selatan, kamp di kedua sisi hutan.", "Three lanes, Naga Basuki in the southern jungle, camps on both sides.",
            "valley",
            [],
            [],
            [new(1, 34, 60, CampTier.Lemah), new(2, 60, 78, CampTier.Sedang), new(3, 52, 28, CampTier.Lemah), new(4, 78, 58, CampTier.Sedang)],
            [new("subur", 16, 16, 26), new("hutan", 34, 62, 18), new("hutan", 66, 32, 18), new("sabana", 50, 50, 12), new("tandus", 84, 84, 24)],
            ["gunung", "laut", "laut", "hutan"]),
        new MapDefinition(GelanggangSabana, GameMode.Moba, 80, "Gelanggang Sabana", "Savanna Ring",
            "Sabana kecil dengan jalur pendek. Pertarungan dimulai lebih cepat.", "A small savanna with short lanes. Fights start sooner.",
            "savanna",
            [new(22, 54, 5, 5, "rock"), new(54, 22, 5, 5, "rock")],
            [],
            [new(1, 30, 48, CampTier.Lemah), new(2, 48, 62, CampTier.Sedang)],
            [new("sabana", 40, 40, 40), new("subur", 12, 12, 16), new("gurun", 66, 66, 22), new("gurun", 70, 18, 14), new("tandus", 20, 66, 12)],
            ["vulkan", "gurun", "gurun", "gunung"]),
        new MapDefinition(JembatanNaga, GameMode.Moba, 140, "Jembatan Naga", "Dragon Bridges",
            "Sungai membelah arena; tiga jembatan dan satu arungan naga. Hutan luas dengan kamp purba.", "A river splits the arena: three bridges and the dragon ford. A wide jungle with an ancient camp.",
            "river",
            River(140),
            [],
            [new(1, 44, 72, CampTier.Lemah), new(2, 72, 44, CampTier.Lemah), new(3, 100, 72, CampTier.Sedang), new(4, 72, 100, CampTier.Sedang),
             new(5, 96, 118, CampTier.Kuat), new(6, 118, 96, CampTier.Kuat), new(7, 12, 72, CampTier.Purba)],
            [new("subur", 24, 24, 30), new("rawa", 70, 70, 20), new("rawa", 30, 110, 16), new("rawa", 110, 30, 16), new("hutan", 44, 72, 18), new("hutan", 72, 44, 18),
             new("hutan", 100, 72, 18), new("hutan", 72, 100, 18), new("gurun", 118, 118, 26), new("tandus", 12, 72, 14)],
            ["gunung", "laut", "laut", "vulkan"]),
        // ---------------------------------------------------------------- Adventure
        new MapDefinition(PulauPrasasti, GameMode.Adventure, 100, "Pulau Prasasti", "Inscription Isle",
            "Pulau tempat prasasti pecah dan Gua Mistis menunggu.", "The isle where the inscription broke and the Mystic Cave waits.",
            "valley",
            [],
            [],
            [new(1, 50, 8, CampTier.Lemah), new(2, 92, 50, CampTier.Sedang)],
            [new("subur", 28, 22, 18), new("pantai", 8, 60, 14), new("hutan", 18, 86, 20), new("hutan", 50, 62, 12), new("gurun", 60, 92, 12),
             new("tandus", 86, 14, 16), new("vulkanik", 84, 84, 20)],
            ["vulkan", "laut", "laut", "laut"]),
    });

    /// <summary>River squares along the anti-diagonal, leaving bridges where the lanes cross and a ford for the dragon.</summary>
    private static ObstacleView[] River(double size)
    {
        var layout = new MapLayout(size);
        var gaps = new List<(double X, double Z)> { layout.Lane(0), layout.Lane(1), layout.Lane(2), layout.Boss };
        var squares = new List<ObstacleView>();
        var step = size / 16;
        for (var x = step; x < size - step / 2; x += step)
        {
            var z = size - x;
            if (x < MapLayout.Margin + 2 || z < MapLayout.Margin + 2) continue;
            if (gaps.Any(g => Math.Abs(g.X - x) < step * 1.2 && Math.Abs(g.Z - z) < step * 1.2)) continue;
            squares.Add(new ObstacleView(Math.Round(x, 2), Math.Round(z, 2), Math.Round(step * 1.35, 2), Math.Round(step * 1.35, 2), "water"));
        }
        return squares.ToArray();
    }

    public static MapDefinition? Find(string? id) => All.FirstOrDefault(m => m.Id == id);
    public static MapDefinition Default(GameMode mode) => All.First(m => m.Mode == mode);
    public static IEnumerable<MapDefinition> For(GameMode mode) => All.Where(m => m.Mode == mode);

    /// <summary>The map to play: the named one when it exists for this mode, otherwise null; no name means the default.</summary>
    public static MapDefinition? Resolve(GameMode mode, string? id) =>
        string.IsNullOrEmpty(id) ? Default(mode) : Find(id) is { } map && map.Mode == mode ? map : null;
}

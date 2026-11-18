namespace BattleNusantara.Core;

public enum GameMode { Rts, Moba, Adventure }
public enum MatchStatus { Playing, Victory, Defeat }
public enum CommandKind { Move, Ultimate, Recruit, Build, Upgrade, AttackMove, HoldPosition, RecruitWorker, Gather, ReturnCargo, Construct, CancelConstruction, PrioritizeConstruction, BuyItem, Guard, AdvanceDialogue }
public enum UnitOrder { Auto, Move, AttackMove, Hold, Gather, Construct }
/// <summary>How a hero's ultimate (Q) works. Burst: damage around the hero and a small heal. Chain: lightning that jumps
/// between up to six enemies, then a burst of speed. Leap: flight onto the nearest visible enemy, a shockwave and a guard.</summary>
public enum UltimateKind { Burst, Chain, Leap }
public sealed record HeroDefinition(string Id, string Name, string Role, string Faction, string Description, string Skill, int Health, int Damage, string Color, UltimateKind Ultimate = UltimateKind.Burst);

public sealed record ItemDefinition(int Id, string Name, string NameEn, int Cost, int Damage, int Health, double Speed);
/// <summary>A neutral creep of the jungle camps. Range above 6 attacks from a distance.</summary>
public sealed record CreepDefinition(string Model, string Name, string NameEn, int Health, int Damage, double Range, int Gold, int Experience);
public enum LootRarity { Minor, Major, Legendary, Treasure }
/// <summary>How an animal behaves. Timid flees, Calm grazes and fights back, Territorial charges whoever comes close,
/// Thief snatches gold from workers.</summary>
public enum Temperament { Timid, Calm, Territorial, Thief }
/// <summary>Wildlife of the maps, placed in regions of its biomes. Gold is paid when the player's side kills it.</summary>
public sealed record WildlifeDefinition(string Model, string Name, string NameEn, Temperament Temperament, int Health, int Damage, double Speed, int Gold, string[] Biomes);
/// <summary>Loot dropped by cleared camps. Picking it up changes the hero for the rest of the match.</summary>
public sealed record LootDefinition(string Id, string Name, string NameEn, LootRarity Rarity, int Damage, int Health, double Speed, int Gold, double Heal);

public static class Catalog
{
    // AI-controlled MOBA heroes. They are not selectable, so they stay out of the playable roster.
    public static readonly IReadOnlyList<HeroDefinition> AllyChampions = Array.AsReadOnly(new[]
    {
        new HeroDefinition("diponegoro", "Diponegoro", "Vanguard", "Nusantara", "", "", 230, 22, "#c9b06e"),
        new HeroDefinition("pattimura", "Pattimura", "Striker", "Nusantara", "", "", 210, 26, "#e0a07c"),
        new HeroDefinition("si-pitung", "Si Pitung", "Striker", "Nusantara", "", "", 190, 28, "#9fd0a4"),
        new HeroDefinition("barong", "Barong", "Guardian", "Mitologi", "", "", 260, 20, "#8fc4e0")
    });
    public static readonly IReadOnlyList<HeroDefinition> EnemyChampions = Array.AsReadOnly(new[]
    {
        new HeroDefinition("rangda", "Rangda", "Mystic", "Kalatidha", "", "", 220, 26, "#e06c8c"),
        new HeroDefinition("leak", "Leak", "Striker", "Kalatidha", "", "", 190, 28, "#d9719f"),
        new HeroDefinition("buto-ijo", "Buto Ijo", "Vanguard", "Kalatidha", "", "", 260, 20, "#c96a78"),
        new HeroDefinition("kuntilanak", "Kuntilanak", "Mystic", "Kalatidha", "", "", 200, 24, "#e58fb0"),
        new HeroDefinition("pocong", "Pocong", "Guardian", "Kalatidha", "", "", 230, 22, "#d98a8a")
    });
    public static readonly IReadOnlyList<ItemDefinition> Items = Array.AsReadOnly(new[]
    {
        new ItemDefinition(0, "Keris Pusaka", "Heirloom Keris", 120, 12, 0, 0),
        new ItemDefinition(1, "Perisai Rotan", "Rattan Shield", 120, 0, 90, 0),
        new ItemDefinition(2, "Terompah Angin", "Wind Sandals", 100, 0, 0, .2)
    });
    public static readonly IReadOnlyList<HeroDefinition> Heroes = Array.AsReadOnly(new[]
    {
        new HeroDefinition("gajah-mada", "Gajah Mada", "Vanguard", "Nusantara", "Sumpah yang menyatukan kepulauan. Pimpin barisan dan patahkan pertahanan musuh.", "Sumpah Palapa", 260, 24, "#d4b478"),
        new HeroDefinition("cut-nyak-dien", "Cut Nyak Dien", "Striker", "Nusantara", "Keberanian dari tanah Aceh. Bergerak cepat dan serang dari garis depan.", "Rencong Fajar", 200, 34, "#ecaa8e"),
        new HeroDefinition("nyi-roro-kidul", "Nyi Roro Kidul", "Mystic", "Mitologi", "Penjaga laut selatan. Gelombang mistis menyapu pasukan Sang Kalatidha.", "Gelombang Selatan", 180, 38, "#75d6c3"),
        new HeroDefinition("republik", "Penjaga Republik", "Sentinel", "Modern", "Pelindung prasasti dari tahun 2125, dipersenjatai energi quantum.", "Denyut Quantum", 220, 28, "#86b7ed"),
        // Original heroes for the superhero faction. The concept named licensed comic characters; these are our own,
        // with comparable powers (lightning and speed; strength, flight and toughness) but their own names and looks.
        new HeroDefinition("bledhek", "Satria Bledhek", "Striker", "Pahlawan Super", "Pendekar petir dari lereng Merapi. Sambarannya melompat dari musuh ke musuh, lalu ia melesat secepat kilat.", "Sambaran Bledhek", 200, 30, "#8fd3ff", UltimateKind.Chain),
        new HeroDefinition("rara-angkasa", "Rara Angkasa", "Guardian", "Pahlawan Super", "Penjaga langit berkekuatan raksasa. Ia terbang menerjang musuh dan menghantam tanah dengan gelombang kejut.", "Hentakan Angkasa", 280, 22, "#c7a6ff", UltimateKind.Leap)
    });
    public static HeroDefinition Hero(string id) => Heroes.FirstOrDefault(h => h.Id == id) ?? throw new ArgumentException("Unknown hero.", nameof(id));

    /// <summary>Neutral creeps, weakest first: Indonesian folklore spirits and siluman (were-creatures).</summary>
    public static readonly IReadOnlyList<CreepDefinition> Creeps = Array.AsReadOnly(new[]
    {
        new CreepDefinition("tuyul", "Tuyul", "Tuyul", 90, 6, 6, 20, 25),
        new CreepDefinition("siluman-kera", "Siluman Kera", "Monkey Siluman", 180, 11, 6, 35, 40),
        new CreepDefinition("siluman-macan", "Siluman Macan", "Tiger Siluman", 280, 16, 6, 50, 60),
        new CreepDefinition("siluman-buaya", "Siluman Buaya", "Crocodile Siluman", 340, 18, 7, 60, 70),
        new CreepDefinition("genderuwo", "Genderuwo", "Genderuwo", 650, 27, 7, 110, 120),
        new CreepDefinition("banaspati", "Banaspati", "Banaspati", 1500, 42, 10, 260, 250)
    });
    public static CreepDefinition? Creep(string model) => Creeps.FirstOrDefault(c => c.Model == model);

    /// <summary>Who guards a camp of each tier.</summary>
    public static string[] CampCreeps(CampTier tier) => tier switch
    {
        CampTier.Lemah => ["tuyul", "tuyul", "tuyul"],
        CampTier.Sedang => ["siluman-kera", "siluman-macan", "siluman-kera"],
        CampTier.Kuat => ["siluman-buaya", "genderuwo", "siluman-buaya"],
        _ => ["siluman-macan", "banaspati", "siluman-macan"]
    };
    /// <summary>Ticks before a cleared camp returns: one to four minutes.</summary>
    public static int CampRespawnTicks(CampTier tier) => tier switch { CampTier.Lemah => 600, CampTier.Sedang => 900, CampTier.Kuat => 1200, _ => 2400 };

    public static readonly IReadOnlyList<LootDefinition> Loot = Array.AsReadOnly(new[]
    {
        new LootDefinition("jamu", "Jamu Kunyit Asam", "Turmeric Tonic", LootRarity.Minor, 0, 20, 0, 0, .6),
        new LootDefinition("batu-asah", "Batu Asah", "Whetstone", LootRarity.Minor, 4, 0, 0, 0, 0),
        new LootDefinition("gelang-akar", "Gelang Akar Bahar", "Sea-Root Bracelet", LootRarity.Minor, 0, 40, 0, 0, 0),
        new LootDefinition("keris-luk", "Keris Luk Tujuh", "Seven-Wave Keris", LootRarity.Major, 10, 0, 0, 0, 0),
        new LootDefinition("zirah-sisik", "Zirah Sisik Naga", "Dragon-Scale Mail", LootRarity.Major, 0, 110, 0, 0, 0),
        new LootDefinition("terompah-kilat", "Terompah Kilat", "Lightning Sandals", LootRarity.Major, 0, 0, .15, 0, 0),
        new LootDefinition("mustika", "Mustika Banaspati", "Banaspati Gem", LootRarity.Legendary, 15, 150, .1, 0, 1),
        new LootDefinition("peti", "Peti Harta", "Treasure Chest", LootRarity.Treasure, 0, 0, 0, 200, 0)
    });
    public static LootDefinition? LootItem(string id) => Loot.FirstOrDefault(l => l.Id == id);

    public static readonly IReadOnlyList<WildlifeDefinition> Wildlife = Array.AsReadOnly(new[]
    {
        new WildlifeDefinition("kancil", "Kancil", "Mouse-deer", Temperament.Timid, 40, 0, 7, 5, ["subur", "sabana", "hutan"]),
        new WildlifeDefinition("kerbau", "Kerbau", "Water Buffalo", Temperament.Calm, 420, 14, 3, 15, ["subur", "rawa", "sabana"]),
        new WildlifeDefinition("babi-hutan", "Babi Hutan", "Wild Boar", Temperament.Territorial, 130, 9, 6, 15, ["hutan", "tandus", "rawa", "gurun"]),
        new WildlifeDefinition("kera-pencuri", "Kera Pencuri", "Thieving Macaque", Temperament.Thief, 70, 4, 8, 10, ["hutan", "pantai", "subur"])
    });
    public static WildlifeDefinition? Wild(string model) => Wildlife.FirstOrDefault(w => w.Model == model);
}

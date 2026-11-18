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
}

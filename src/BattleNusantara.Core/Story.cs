namespace BattleNusantara.Core;

public enum QuestState { Locked, Active, Done }

/// <summary>A node of the Adventure quest graph. A quest becomes active once every quest in <see cref="Requires"/> is done.</summary>
public sealed record QuestDefinition(string Id, string Title, string TitleEn, string[] Requires);
public sealed record QuestView(string Id, QuestState State, int Progress, int Goal);
public sealed record DialogueLine(string Speaker, string Text, string TextEn);
/// <summary>A question the player may put to an NPC, with the authored answer that is always available.
/// <see cref="Keywords"/> (lower case, both languages) route a typed question to this topic without a language model.</summary>
public sealed record NpcTopic(string Id, string Question, string QuestionEn, string Answer, string AnswerEn)
{
    public string Npc { get; init; } = Story.KeeperNpc;
    public string[] Keywords { get; init; } = [];
}
/// <summary>An NPC the player can question. <see cref="Unknown"/> is the authored reply to anything outside its topics.</summary>
public sealed record NpcDefinition(string Id, string Name, string Lore, string Unknown, string UnknownEn);
/// <summary>A quest generated from the match seed: what to do, where, and how many.</summary>
public sealed record SideQuestPlan(string Id, int Count, (double X, double Z)[] Points);

/// <summary>Authored Adventure content: the quest graph and its bilingual dialogue.</summary>
public static class Story
{
    public const string Keeper = "juru-kunci", Inscriptions = "prasasti", Guardians = "penjaga", Heirloom = "pusaka", Kalatidha = "kalatidha";

    // Listed in an order where every prerequisite precedes its dependants, so one pass per tick resolves the graph.
    public static readonly IReadOnlyList<QuestDefinition> Quests = Array.AsReadOnly(new[]
    {
        new QuestDefinition(Keeper, "Temui Juru Kunci di dekat candi", "Meet the Keeper beside the shrine", []),
        new QuestDefinition(Inscriptions, "Pulihkan tiga prasasti", "Recover three inscriptions", []),
        new QuestDefinition(Guardians, "Kalahkan lima penjaga jalan", "Defeat the five road guardians", [Keeper]),
        new QuestDefinition(Heirloom, "Ambil Keris Pusaka Jagat di Gua Mistis", "Take the Jagat Heirloom Keris from the Mystic Cave", [Guardians]),
        new QuestDefinition(Kalatidha, "Kalahkan Sang Kalatidha", "Defeat Sang Kalatidha", [Inscriptions, Heirloom])
    });

    public const string KeeperNpc = "keeper", CaptainNpc = "captain";
    public const string Captain = "nakhoda", Fragments = "serpihan", Spirits = "arwah";

    /// <summary>The optional side quest. "nakhoda" is shown until the captain is met; it then becomes one of the generated tasks.</summary>
    public static readonly IReadOnlyList<QuestDefinition> SideQuests = Array.AsReadOnly(new[]
    {
        new QuestDefinition(Captain, "Temui Nakhoda Tua di pantai barat", "Meet the Old Captain on the western shore", []),
        new QuestDefinition(Fragments, "Kumpulkan serpihan kapal Nakhoda", "Gather the Captain's ship fragments", [Captain]),
        new QuestDefinition(Spirits, "Usir arwah laut dari pantai", "Drive the sea spirits from the shore", [Captain])
    });

    public static QuestDefinition Quest(string id) => Quests.FirstOrDefault(q => q.Id == id) ?? SideQuests.First(q => q.Id == id);

    /// <summary>Open ground for generated objectives: inside the arena, clear of the cave walls and the main path.</summary>
    private static readonly (double X, double Z)[] SideSpots = [(10, 42), (14, 80), (24, 90), (40, 88), (52, 72), (34, 52), (46, 60), (20, 50), (60, 90), (8, 90), (30, 76), (16, 68)];

    /// <summary>
    /// Derives the side quest from a seed: fragments to collect or spirits to drive off, three or four of them, at
    /// distinct spots. Pure and deterministic, so a save only has to keep the seed.
    /// </summary>
    public static SideQuestPlan PlanSideQuest(int seed)
    {
        var state = (uint)seed ^ 0x9E3779B9u;
        uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        var id = Next() % 2 == 0 ? Fragments : Spirits;
        var count = 3 + (int)(Next() % 2);
        var spots = SideSpots.ToList(); var points = new (double X, double Z)[count];
        for (var i = 0; i < count; i++) { var pick = (int)(Next() % (uint)spots.Count); points[i] = spots[pick]; spots.RemoveAt(pick); }
        return new SideQuestPlan(id, count, points);
    }

    public static readonly IReadOnlyList<NpcDefinition> Npcs = Array.AsReadOnly(new[]
    {
        new NpcDefinition(KeeperNpc, "Juru Kunci", "Juru Kunci is the old keeper of a shrine. He waits beside the shrine and knows the way to the Mystic Cave.",
            "Hal itu di luar pengetahuanku, pengembara. Tanyakan tentang Sang Kalatidha, Gua Mistis, atau prasasti.",
            "That lies beyond what I know, traveller. Ask me of Sang Kalatidha, the Mystic Cave or the inscriptions."),
        new NpcDefinition(CaptainNpc, "Nakhoda Tua", "Nakhoda Tua is an old sea captain whose ship broke on the western reef when the ages collided. He camps on the western shore.",
            "Laut tak pernah bercerita soal itu padaku. Tanyakan tentang kapalku, badai, atau arwah laut.",
            "The sea never told me of that. Ask me about my ship, the storms or the sea spirits.")
    });

    public static NpcDefinition? Npc(string id) => Npcs.FirstOrDefault(n => n.Id == id);
    public static IEnumerable<NpcTopic> TopicsOf(string npc) => KeeperTopics.Concat(CaptainTopics).Where(t => t.Npc == npc);
    public static NpcTopic? Topic(string id) => KeeperTopics.Concat(CaptainTopics).FirstOrDefault(t => t.Id == id);

    /// <summary>Routes a typed question to the topic of <paramref name="npc"/> whose keywords it mentions most; null when none match.</summary>
    public static NpcTopic? MatchTopic(string npc, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLowerInvariant();
        NpcTopic? best = null; var score = 0;
        foreach (var topic in TopicsOf(npc))
        {
            var hits = topic.Keywords.Count(k => lower.Contains(k, StringComparison.Ordinal));
            if (hits > score) { best = topic; score = hits; }
        }
        return best;
    }

    /// <summary>What the Old Captain can be asked once he has been met.</summary>
    public static readonly IReadOnlyList<NpcTopic> CaptainTopics = Array.AsReadOnly(new[]
    {
        new NpcTopic("kapal", "Apa yang terjadi pada kapalmu?", "What happened to your ship?",
            "Saat zaman-zaman bertabrakan, laut berbalik dan kapalku pecah di karang barat. Serpihannya tersebar di sepanjang pantai.",
            "When the ages collided the sea turned on itself, and my ship broke on the western reef. Its fragments lie scattered along the shore.")
            { Npc = CaptainNpc, Keywords = ["kapal", "ship", "karang", "reef", "serpihan", "fragment", "pecah", "wreck"] },
        new NpcTopic("badai", "Mengapa badai datang silih berganti?", "Why do the storms keep coming?",
            "Retakan prasasti mengacaukan langit. Saat kabut atau badai turun, pandangan memendek; musuh pun tak melihatmu dari jauh.",
            "The cracked inscription unsettles the sky. When mist or storm comes down, sight grows short; the enemy cannot see you from afar either.")
            { Npc = CaptainNpc, Keywords = ["badai", "storm", "hujan", "rain", "kabut", "mist", "cuaca", "weather", "langit", "sky"] },
        new NpcTopic("arwah", "Siapakah arwah laut itu?", "Who are the sea spirits?",
            "Mereka awak kapal dari zaman lain yang tersesat di retakan waktu. Bila diusir, mereka kembali tenang ke laut.",
            "They are sailors from another age, lost in the rift of time. Drive them off and they return to the sea in peace.")
            { Npc = CaptainNpc, Keywords = ["arwah", "spirit", "hantu", "ghost", "awak", "sailor", "laut", "sea"] }
    });

    /// <summary>What the Keeper can be asked once he has been met. The answers are the authored fallback for the optional model voice.</summary>
    public static readonly IReadOnlyList<NpcTopic> KeeperTopics = Array.AsReadOnly(new[]
    {
        new NpcTopic("kalatidha", "Siapakah Sang Kalatidha?", "Who is Sang Kalatidha?",
            "Ia lahir dari retakan Prasasti Sang Hyang Jagat. Selama perisainya utuh, senjata biasa tidak dapat melukainya.",
            "He was born from the crack in the Sang Hyang Jagat inscription. While his ward holds, ordinary weapons cannot harm him.")
            { Keywords = ["kalatidha", "musuh", "enemy", "perisai", "ward", "kalahkan", "defeat", "lawan"] },
        new NpcTopic("gua", "Apa yang menunggu di Gua Mistis?", "What waits in the Mystic Cave?",
            "Keris Pusaka Jagat tersimpan di sana, di timur laut. Pintunya tersegel sampai lima penjaga jalan dikalahkan, dan para penunggu gua menjaga kerisnya.",
            "The Jagat Heirloom Keris lies there, to the north-east. Its door stays sealed until the five road guardians fall, and the wardens of the cave watch over the keris.")
            { Keywords = ["gua", "cave", "keris", "pusaka", "heirloom", "pintu", "door", "penjaga", "guardian", "timur", "east"] },
        new NpcTopic("prasasti", "Apa arti ketiga prasasti?", "What do the three inscriptions mean?",
            "Ketiganya adalah pecahan Prasasti Sang Hyang Jagat. Bila bersatu kembali, retakan antarzaman mulai menutup; bersama keris pusaka, itulah yang menumbangkan Sang Kalatidha.",
            "They are fragments of the Sang Hyang Jagat inscription. Made whole, they begin to close the rifts between the ages; together with the heirloom keris, that is what brings Sang Kalatidha down.")
            { Keywords = ["prasasti", "inscription", "pecahan", "retak", "crack", "jagat", "zaman", "ages"] }
    });

    public static NpcTopic? KeeperTopic(string id) => KeeperTopics.FirstOrDefault(t => t.Id == id);

    /// <summary>Background given to the optional model voice so that it stays inside the authored world.</summary>
    public const string KeeperLore = "The year is 2125. The Sang Hyang Jagat inscription has cracked and the ages of Nusantara collide. " +
        "Juru Kunci waits beside the shrine and knows the way to the Mystic Cave. Sang Kalatidha is the enemy.";

    public static readonly IReadOnlyDictionary<string, DialogueLine[]> Dialogues = new Dictionary<string, DialogueLine[]>
    {
        ["intro"] =
        [
            new("Narator", "Tahun 2125. Prasasti Sang Hyang Jagat retak, dan zaman-zaman Nusantara saling bertabrakan.", "The year 2125. The Sang Hyang Jagat inscription has cracked, and the ages of Nusantara collide."),
            new("Narator", "Seorang juru kunci menunggu di dekat candi. Ia tahu jalan menuju Gua Mistis.", "A keeper waits beside the shrine. He knows the way to the Mystic Cave.")
        ],
        [Keeper] =
        [
            new("Juru Kunci", "Akhirnya engkau datang. Sang Kalatidha tidak dapat dilukai oleh senjata biasa.", "At last you have come. Sang Kalatidha cannot be harmed by ordinary weapons."),
            new("Juru Kunci", "Keris Pusaka Jagat tersimpan di Gua Mistis di timur laut. Lima penjaga jalan menyegel pintunya.", "The Jagat Heirloom Keris lies in the Mystic Cave to the north-east. Five road guardians keep its door sealed."),
            new("Juru Kunci", "Kalahkan mereka, ambil kerisnya, dan kumpulkan ketiga prasasti. Baru setelah itu ia dapat ditumbangkan.", "Defeat them, take the keris, and gather the three inscriptions. Only then can he be brought down.")
        ],
        [Inscriptions] =
        [
            new("Narator", "Ketiga prasasti bersatu kembali. Retakan antarzaman mulai menutup.", "The three inscriptions are whole again. The rifts between the ages begin to close.")
        ],
        ["gua-terbuka"] =
        [
            new("Juru Kunci", "Segelnya runtuh. Pintu Gua Mistis terbuka; berhati-hatilah terhadap para penunggunya.", "The seal has fallen. The Mystic Cave stands open; beware those who dwell within.")
        ],
        [Heirloom] =
        [
            new("Narator", "Keris Pusaka Jagat menyala di tanganmu. Seranganmu kini menembus kegelapan.", "The Jagat Heirloom Keris flares in your hand. Your strikes now pierce the dark.")
        ],
        [Kalatidha] =
        [
            new("Sang Kalatidha", "Kalian menyatukan apa yang telah kupisahkan. Maka sejarah kalian akan kuhapus dengan tanganku sendiri.", "You have joined what I sundered. Then I shall erase your history with my own hands."),
            new("Juru Kunci", "Perisainya telah sirna. Sekarang!", "His ward is gone. Now!")
        ],
        [Captain] =
        [
            new("Nakhoda Tua", "Seorang pejuang! Kapalku pecah di karang ketika zaman-zaman bertabrakan.", "A fighter! My ship broke on the reef when the ages collided."),
            new("Nakhoda Tua", "Pantai ini kini tak aman. Bantulah aku, dan laut akan membalas kebaikanmu.", "This shore is no longer safe. Help me, and the sea will repay your kindness.")
        ],
        [Fragments] =
        [
            new("Nakhoda Tua", "Serpihan kapalku tersebar di daratan. Kumpulkan semuanya sebelum ombak menelannya.", "Pieces of my ship lie scattered inland. Gather them all before the tide takes them.")
        ],
        [Spirits] =
        [
            new("Nakhoda Tua", "Arwah awak kapal dari zaman lain berkeliaran di pantai. Usir mereka, tetapi berhati-hatilah.", "The spirits of sailors from another age roam the shore. Drive them off, but take care.")
        ],
        ["nakhoda-selesai"] =
        [
            new("Nakhoda Tua", "Terima kasih, pejuang. Ambillah azimat laut ini; ia akan menguatkan tubuhmu.", "Thank you, fighter. Take this sea charm; it will strengthen your body.")
        ],
        ["akhir"] =
        [
            new("Narator", "Sang Kalatidha tumbang di Borobudur Quantum. Jagat Nusantara bertahan.", "Sang Kalatidha falls at Borobudur Quantum. Jagat Nusantara endures.")
        ]
    };
}

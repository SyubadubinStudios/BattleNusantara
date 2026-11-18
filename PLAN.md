# Development roadmap / Roadmap pengembangan

## Phase 1 — Playable foundation / Fondasi permainan

- [x] .NET 10 solution with shared simulation, Razor UI, WPF desktop, ASP.NET Core host.
- [x] Four heroes, local RTS/MOBA/Adventure objectives, victory/defeat, ultimate and economy.
- [x] Offline Three.js rendering, original Blender shrine and core animation.
- [x] Artistic menu, options persistence, Indonesian/English UI, scrolling credits, attribution.
- [x] Two-player LAN room creation/join and server-owned simulation.
- [x] Unit and browser tests, bilingual guides, screenshots.

## Phase 2 — Gameplay depth / Kedalaman gameplay

- [x] RTS unit selection, formation movement, attack-move, hold position, and tactical map.
- [x] Fixed-ruin navigation and line-of-sight checks shared by solo and LAN.
- [x] Worker recruitment, finite mines, cargo/drop-off loops and return-cargo orders.
- [x] Player-positioned outposts with server-validated cost, footprint and build radius.
- [x] Dynamic outpost navigation, route invalidation, edge deposits and reopened paths after destruction.
- [x] Worker-built foundations, visible progress, interruption/resumption and completed-depot activation.
- [x] Automatic continuation across owned unstaffed foundations; cancellation with progress-based partial refunds.
- [x] Drag-box selection with additive Shift, and three saved control groups per match.
- [x] Server-filtered fog of war with shared team vision and explored-ground memory; move-to-front construction queue order.
- [x] Fog-aware AI, line-of-sight blocking by ruins, and vision-gated building placement.
- [x] MOBA 5v5 with AI heroes, lane towers guarding the bases, hero respawns, levelled items and a neutral boss.
- [x] MOBA depth: a second hero ability (Guard), hero levels, tower targeting priority, AI that contests the boss, and champions that grow over time.
- [x] Adventure quest graph, authored bilingual dialogue, the Mystic Cave dungeon and solo save/load.
- [x] Reconnect with seat tokens, room lifecycle, player departure handling and spectator support.
- [x] Profile allocations, tick duration, snapshot traffic and renderer draw cost; budgets proposed in `docs/PERFORMANCE.md` and enforced by tests.
- [x] Scripted full-match tests for RTS, MOBA and Adventure that reach victory identically on every run.
- [ ] Needs people and devices: MOBA balance from human playtests, and GPU/frame-rate measurements on agreed target hardware. / Memerlukan orang dan perangkat: keseimbangan MOBA dari playtest manusia, dan pengukuran GPU/laju frame pada perangkat target yang disepakati.

Exit: reproducible full-match tests, no known progression blockers, agreed performance budgets. / Selesai bila pengujian match penuh konsisten, progres tidak buntu, dan target performa terukur.

Status v0.10: full-match tests are reproducible and no progression blocker is known. The performance budgets are measured and proposed, but they still need the owner's agreement and confirmation on target hardware. / Status v0.10: tes pertandingan penuh konsisten dan tidak ada kebuntuan progres yang diketahui. Anggaran performa sudah diukur dan diusulkan, tetapi masih memerlukan persetujuan pemilik dan konfirmasi pada perangkat target.

## Phase 3 — Online platform / Platform online

- [x] Accounts with password login and revocable sessions; profile ownership taken from the authenticated connection.
- [x] SQL schema with versioned migrations and match-result storage, tested on embedded SQLite.
- [x] The same store against PostgreSQL 17 (`Database: postgres`): integration tests, the full server and a `pg_dump`/`pg_restore` drill, on one local server (v0.12). / Store yang sama terhadap PostgreSQL 17: tes integrasi, server penuh, dan latihan `pg_dump`/`pg_restore`, pada satu server lokal (v0.12).
- [x] Matchmaking queue with ticket expiry, decided by one Orleans lobby grain in a single-host silo.
- [x] Redis queue (`Queue: redis`): integration tests and the full server against Garnet 2.2, a Redis-protocol server; not yet run against Redis itself. / Antrean Redis: tes integrasi dan server penuh terhadap Garnet 2.2, server berprotokol Redis; belum dijalankan terhadap Redis itu sendiri.
- [ ] Orleans match grains and multi-host clustering; rooms still live in one process.
- [x] Co-op rooms expanded from 2 to 10 players (MOBA: the five allied hero slots).
- [ ] Player-versus-player teams, needed for a real 5v5; still not considering 128.
- [x] Single-host load test: 32 rooms × 10 players with tick, delivery and traffic results.
- [ ] Multi-host, real-network and failure tests.
- [x] Backup and restore for the embedded database, verified and scripted; `/api/status` for basic observability.
- [ ] Ranked seasons, guilds, tournaments, abuse controls and operational dashboards.
- [ ] Secure deployment (HTTPS, secrets), scheduled backups, recovery drills and capacity planning.

Exit: tested deployment, authenticated clients, reconnection and load results. / Selesai bila deployment, identitas, reconnect, dan kapasitas terbukti lewat tes.

Status v0.12: authenticated clients, reconnection, single-host load results and tested PostgreSQL and Redis-protocol paths exist. Deployment, HTTPS and more than one host are not tested, so Phase 3 is **not** complete. / Status v0.12: klien terotentikasi, reconnect, hasil beban satu host, serta jalur PostgreSQL dan protokol Redis yang teruji sudah ada. Deployment, HTTPS, dan lebih dari satu host belum diuji, sehingga Fase 3 **belum** selesai.

## Phase 4 — Art and world / Seni dan dunia

- [x] Blender characters: 24 low-poly characters on one 12-bone rig with Idle, Walk, Attack, Cast, Hit and Death (v0.13); Naga Basuki on its own serpent rig; props for towers, outposts, inscriptions, the keris, mines and ship fragments. Modelled procedurally in low poly by design, not sculpted. / 24 karakter low-poly pada satu rig 12 tulang dengan enam klip; Naga Basuki dengan rig sendiri; properti menara, pos, prasasti, keris, tambang, dan serpihan. Dimodelkan secara prosedural, bukan di-sculpt.
- [x] Original heroes replace the licensed comic characters of the concept: Satria Bledhek (lightning, speed) and Rara Angkasa (flight, strength) are playable with their own ultimates; eight more originals are design only (`solution-design.md`). / Hero orisinal menggantikan tokoh komik berlisensi.
- [x] Rodin/GPT Image Flare: six painted hero portraits, reviewed for consistency and for not resembling licensed characters; sources kept in `assets/concepts/`. / Enam potret hero.
- [x] Cinematics: an authored four-shot Adventure opening, an Adventure ending, one shot for RTS and MOBA, all skippable. Environmental storytelling props. / Sinematik dan properti cerita.
- [x] Audio: every dialogue line voiced in Indonesian and English (ElevenLabs, free plan: see `docs/ASSETS.md`); composed gamelan music, synthesized at runtime; adaptive rain, combat and thunder layers. / Semua dialog bersuara; musik gamelan digubah dan disintesis.
- [x] Weather and day/night; weather affects play (mist ×0.8 and storm ×0.75 sight). Ocean swell, shoreline foam, rain ripples, a water wave for Nyi Roro Kidul's ultimate. / Cuaca memengaruhi jarak pandang; ombak, buih, riak hujan.
- [x] WebGPU measured against WebGL2 (slower on the device measured; not adopted); ray tracing and destruction assessed and declined for now: `docs/RENDERING.md`. / WebGPU diukur; ray tracing dan penghancuran dinilai.
- [x] Semantic Kernel NPCs: the Keeper and the Old Captain answer fixed topics and typed questions, with authored fallbacks, timeouts, caches and a daily cap; a side quest generated from the match seed. / NPC Semantic Kernel dengan fallback; tugas sampingan prosedural.

Status v0.13: every Phase 4 item has a shipped, tested result. What these results do **not** include is listed under "Known limits" in `Progress.md` (for example: no motion capture or facial animation, music synthesized rather than recorded, voices on a non-commercial plan). / Status v0.13: setiap butir Fase 4 memiliki hasil yang dirilis dan diuji. Hal yang tidak tercakup dicatat di bagian "Batasan" pada `Progress.md`.

## Native acceleration / Akselerasi native

Rust may be introduced for measured bottlenecks, not as a mandatory dependency. First optimize spatial queries and reduce allocations in C#. Keep a deterministic reference implementation and benchmark any C ABI replacement.

Rust dapat ditambahkan untuk bottleneck yang terukur. Optimalkan pencarian spasial dan alokasi C# terlebih dahulu; bandingkan implementasi native dengan referensi deterministik.

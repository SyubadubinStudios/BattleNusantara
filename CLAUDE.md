# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Battle of Nusantara**: an Indonesian-legends RTS/MOBA/Adventure game. The code is a **playable prototype** (v0.14). `solution-design.md` is the long-term AAA concept, and most of it is not implemented. `Progress.md` records what actually exists and its known limits, and `PLAN.md` is the roadmap. Don't describe features from the design doc (Orleans, PostgreSQL, Redis, Semantic Kernel, 5v5 ranked, 128 players, WebGPU, ray tracing) as implemented.

Note: `AGENTS.md` predates the code and says no source exists. It is out of date on that point, but its style and documentation rules still apply (summarized below).

## Commands

Prerequisites: .NET SDK 10 (`global.json` pins 10.0.100, rollForward latestFeature), Windows + WebView2 for the desktop host, Node 22+ and Microsoft Edge for vendoring and browser tests.

```powershell
dotnet build BattleNusantara.slnx -c Release          # TreatWarningsAsErrors is on (Directory.Build.props)
dotnet test tests/BattleNusantara.Tests -c Release     # xUnit
dotnet test tests/BattleNusantara.Tests -c Release --filter "FullyQualifiedName~ConstructionQueueTests"
dotnet test tests/BattleNusantara.Tests -c Release --filter "Name=Move_OutOfBounds_ClampsDestination"

# Run
dotnet run --project src/BattleNusantara.Server -c Release --no-launch-profile --urls http://localhost:5080   # browser + LAN host
dotnet run --project src/BattleNusantara.Desktop -c Release                                                  # WPF, solo needs no server
dotnet run --project src/BattleNusantara.Photino -c Release                                                  # Photino host for macOS/Linux (also runs on Windows)
./scripts/run.ps1 -HostType Server -Lan                                                                      # binds 0.0.0.0:5080

# Browser (Playwright, Edge channel, tests/browser/). Starts the server on :5080 itself and reuses one already running outside CI
npm ci; npm run vendor; npm test
npx playwright test -g "RTS outposts block movement"   # single browser test by title
# Browser scenarios that click the arena disable the opening cinematic (the `calm` init script); pass `ready(page, true)` to keep it

# Windows installer (Inno Setup 6): publishes Desktop + Server self-contained, writes artifacts/installer/*.exe
./scripts/package.ps1
# Linux/macOS archives (Photino game + server, self-contained). macOS must be packaged on macOS (ad-hoc codesign);
# .github/workflows/release.yml does that on tag pushes and attaches them to the release
python scripts/package_unix.py --rids linux-x64,osx-arm64,osx-x64

# Native WPF smoke test: drives the built exe through WebView2 CDP on port 9223 (DESKTOP_SMOKE_PORT overrides it).
# The same script drives the Photino build on Windows (src/BattleNusantara.Photino/bin/Release/net10.0/BattleNusantara.exe)
node scripts/desktop-smoke.cjs [path\to\BattleNusantara.Desktop.exe]
# Photino smoke mode (CI on Linux/macOS): closes after N seconds, exit 0 only if the page and JS interop answered
NUSANTARA_SMOKE_SECONDS=40 ./BattleNusantara

# Music, ambience and sound effects: numpy + scipy synthesis, encoded with ffmpeg (libvorbis) into wwwroot/audio/
python scripts/compose_audio.py [--only music-explore,hit]
```

`npm run vendor` (`scripts/vendor.mjs`) copies Three.js 0.180.0, GLTFLoader, BufferGeometryUtils, SkeletonUtils and the post-processing passes and shaders (EffectComposer, UnrealBloomPass, OutputPass, FXAAShader and their dependencies) from `node_modules` into `src/BattleNusantara.UI/wwwroot/vendor/` and rewrites their imports to relative paths. The game must stay **offline: no CDN loads**. Re-run vendor after bumping `three`, and don't hand-edit the vendored files.

CI (`.github/workflows/build.yml`, windows-latest) runs: npm ci → vendor → build Release → `dotnet test --no-build` → Playwright msedge → `npm test`.

## Architecture

The solution has four projects plus tests (`BattleNusantara.slnx`). Dependencies point one way: **Desktop/Server → UI → Core**.

- **Core** (`net10.0`, no UI or networking deps): deterministic game rules. `Match.cs` holds the simulation: units, economy, workers, construction, combat, and objectives for each `GameMode`; `Match.Adventure.cs` (same partial class) holds quests, dialogue and save/load, and `Story.cs` the authored quest graph and bilingual dialogue. `Match.Neutrals.cs` holds jungle camps, wildlife and loot. `Maps.cs` holds the seven `MapDefinition`s and `MapLayout(size)`, which derives every shared position from the map size (at 100 it gives the numbers older tests use). `Catalog.cs` holds enums, heroes, items, creeps, loot, wildlife and `CommandKind`. `NavigationMap.cs` is a visibility graph around rectangular obstacles (each with a `Kind`; water blocks walking but not sight), using Dijkstra and line-of-sight checks. The Core project grants `InternalsVisibleTo` to the tests for a few hooks (`PlaceWildlife`, `LoadCargo`). All input arrives as a `GameCommand` record, and all output leaves as an immutable `MatchView`/`UnitView` snapshot.
- **UI** (Razor class library), shared by both hosts. `GameShell.razor` is the whole app UI: menus, HUD, options, credits, and Indonesian/English strings. `GameClient` runs either a **local `Match`** (solo) or a **SignalR connection** to the server (LAN), and exposes the same `View` either way. `TacticalGroups` holds the local-only control groups, and its `Filter` is the one rule for which unit IDs may be selected. Selection and groups never reach the simulation. `wwwroot/world.js` is the Three.js renderer, loaded via JS interop with `mount`/`setMap(definition)`/`update(state, owner, selection, attackMove, buildMode)`/`dispose`. It only renders snapshots and interpolates between them. It never owns game state. The shell calls `setMap` with the `MapDefinition` whenever the snapshot's `Map` changes (null for the title screen). `Atmosphere.At(tick)` (Core) gives weather and daylight; the shell passes it to `update` as a sixth argument. `world.js` uses four presentation modules: `terrain.js` (island landscape, sea depth texture, instanced nature, obstacle dressing), `effects.js` (GPU particles, loot beams, bloom/grade/FXAA on GPUs only), `life.js` (client-only villagers, chickens, birds, butterflies, fireflies) and `audio.js` (layered music, ambience and effects from `wwwroot/audio/`). Models come from `assets/blender/` scripts built on `nusantara_kit.py` and run through the Blender MCP: `quantum-shrine.glb` (`create_temple.py`), `characters.glb` + `naga.glb` (`create_characters.py`: 34 variants on one rig, including creeps and villagers, clips Idle/Walk/Attack/Cast/Hit/Death), `characters-lod.glb` (`create_lods.py`, the decimated set drawn on power saving), `props.glb` (`create_props.py`, including loot), `environment.glb` (`create_environment.py`) and `animals.glb` (`create_animals.py`). `UnitView.Model` names the variant for heroes, champions, NPCs, bosses, creeps and animals, and the item for loot; other kinds map by kind on the client. Portraits are in `wwwroot/portraits/`, voice clips in `wwwroot/voice/` (`{dialogueKey}-{line}-{id|en}.mp3`; regenerate when `Story.Dialogues` changes).
- **Server** (ASP.NET Core): Blazor interactive server hosting plus `/health`, `/api/account/*`, `/api/status` and the hub at `/hubs/battle`. `Platform/` holds accounts and sessions (`Accounts.cs`: one SQL store for SQLite and PostgreSQL), matchmaking (`Matchmaking.cs`: rules in `Matchmaker`, serialized by the Orleans `LobbyGrain`) and `ServerHost.Build` (`Platform.cs`), which tests call with `ui: false`. `Platform/Npc.cs` is the optional NPC voice (`/api/npc/ask` topics, `/api/npc/talk` typed questions; Semantic Kernel + Azure OpenAI, off by default, authored and keyword fallback). `Rooms.cs` contains `RoomRegistry` (room codes, membership, the ten-player cap (five in MOBA), command validation, rate limits and finished-match reporting), `BattleHub`, and `BattleLoop`. `BattleLoop` is a `BackgroundService` that ticks every room at 10 Hz and pushes snapshots.
- **Desktop** (WPF + `BlazorWebView`, `net10.0-windows10.0.19041.0`): hosts the same `GameShell` and registers `GameClient` in its own DI container.
- **Photino** (`net10.0`, assembly `BattleNusantara`): the macOS/Linux desktop host, `GameShell` in a Photino.Blazor window (WebKit on macOS, WebKitGTK 4.1 on Linux, WebView2 on Windows). Audio ships as Ogg and AAC because macOS WebKit does not reliably decode Vorbis.
- **Tests** reference the Server project, so they can reach Core and Server types. Test names follow `Method_Scenario_ExpectedResult`.

Invariants to preserve:
- **Fixed 100 ms tick.** Local matches serialize access with a semaphore, and shared rooms with the registry lock.
- **Server authority.** The player owner comes from the SignalR connection, never from a client-supplied ID. Coordinates must be finite and are clamped. The simulation or server checks mode, ownership, costs, cooldowns and paths *before* applying any part of a group order. Rejection is explicit and leaves the previous order intact.
- **Determinism.** Formation slots are sorted by unit ID, construction continuation follows creation order, and refunds use integer math (`75 * remainingTicks / 80`).
- **Fog of war is applied in `Match.Snapshot`**, never in the client. RTS snapshots omit enemies and neutrals (teams 1 and 3) outside team vision and carry the vision circles in `MatchView.Vision`. Tests that need to see hidden enemies call `Snapshot(fog: false)`.
- **Neutrals live in `Match`.** Camps (team 3, kind `creep`), wildlife (team 3, kind `wild`) and loot (team 2, kind `loot`) are simulated and saved; loot drops come from the pure `Match.CampDrops(seed, camp, clears, tier)`. AI armies only fight neutrals that `Engages` allows. Villagers, chickens, birds and other ambient life are client-only and must never enter `Match`.
- **Maps are validated.** A map id from a client goes through `Maps.Resolve(mode, id)`; unknown ids or another mode's map are refused (`HubException("Unknown map.")`).
- **MOBA heroes respawn with the same unit ID**, and only MOBA skips the defeat-on-hero-loss rule. AI heroes are kind `champion`, never `hero`: code looks up a player's unit by `Kind == "hero"` and owner.
- **Navigation rebuilds when outposts are created or destroyed**, and that clears cached routes.
- **Adventure story text never crosses the wire.** The snapshot carries quest states and a `"key:line"` dialogue reference; `Story.cs` holds the text and the client resolves it. Quests in `Story.Quests` must list prerequisites before dependants.
- **`Match.Load` treats saves as untrusted input** and returns null on any violation. Only a solo Adventure in progress can be saved, and a save must stay under 12,000 characters (it travels over the Blazor UI circuit, limit 32 KB; the game hub keeps a 4 KB limit). Saves are version 3; fields at their default are omitted to stay under the limit, and versions 1 and 2 still load.
- **Identity comes from the authenticated connection.** `BattleHub` reads the account from `Context.User`; never accept an account or owner from a client argument. Guests have a null account and are never written to match results.
- **`SqlAccountStore` must stay portable**: only SQL that both SQLite and PostgreSQL accept. The PostgreSQL path is tested against PostgreSQL 17 and the Redis queue against Garnet (Redis protocol, not Redis itself) by `ExternalStoreTests`; those tests and `NpcLiveTests` are skipped unless `~/.nusantara/test.env` or the environment configures them, so CI never runs them. Do not claim more than that. Schema changes are new entries appended to `Migrations`, never edits to old ones.
- **Weather has one rule:** `Atmosphere.SightFactor(tick)` (Core) shortens sight in mist and storm; everything else about weather, landscape, animation, audio, particles, cinematics and NPC replies is presentation and never enters `Match`, snapshots or saves. Character clips are inferred on the client. NPC typed questions reach the model only as the user message, never inside the system prompt, which holds authored text only; the API key comes from `Platform__Npc__ApiKey`, never from a file in the repo.
- **Original characters only.** The concept lists licensed comic heroes; do not model, name or portray them. The superhero faction uses originals (`solution-design.md`).
- **The session token is sent only to the server that issued it** (`GameClient.Build`). Passwords are never stored client-side.
- **LAN seats.** A dropped connection keeps its seat for `GraceTicks`; `Rejoin` needs the seat token. `Leave` or expiry calls `Match.RemovePlayer`. Spectators have owner -1 and can never issue commands.
- The **snapshot shape is the LAN protocol**. Changing `MatchView`/`UnitView`/`GameCommand` breaks clients on other versions (there's no backward compatibility), so bump the version in `Directory.Build.props`, `package.json` (and `package-lock.json`), `ServerHost.Version` (the `/health` response) and the edition label in `GameShell` together. 0.14 added `MatchView.Map`/`Size`/`Treasures`, `ObstacleView.Kind` and the map argument of the hub's `Create`.

`docs/ARCHITECTURE.md` has the detailed rules for economy, construction, navigation, MOBA growth, Adventure and rooms (exact numbers, HP, limits). Read it before changing `Match.cs` behavior. `docs/RENDERING.md` records the WebGPU measurement (WebGL2 stays) and the paper-only ray tracing and destruction assessments. `docs/PERFORMANCE.md` lists the budgets that `PerformanceTests` and one browser test enforce (the renderer budget is measured at power-saving quality in software rendering, where bloom and shadows are off); `FullMatchTests` play whole matches with scripted policies and must stay deterministic. `MapWorldTests` cover maps and neutrals.

## Conventions

- C#: four-space indentation (`.editorconfig`; JS/JSON/CSS use 2), LF line endings, nullable enabled. Keep allocations low in tick and gameplay loops.
- **Documentation is bilingual (English + Bahasa Indonesia)**, usually as paired paragraphs or `English / Indonesia` lines (see README, `docs/GUIDE-EN.md`, `docs/PANDUAN-ID.md`). Keep Indonesian character and faction names exactly as written.
- Each feature release updates `README.md` (a versioned section with a screenshot), `Progress.md` (status table, validation, limits) and `PLAN.md` checkboxes. Screenshots go in `docs/screenshots/` and must be captured from the running app, not mockups.
- The required attribution must appear in the app and the docs: **"Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios"**.
- Art tooling comes from `BattleNusantara.txt`: use the Blender MCP for modeling, rigging and animation, the Rodin MCP (GPT Image Flare) for 2D and sprites, and the `frontend-design` skill (`.claude/skills/`) for UI work. Record third-party asset licenses in `docs/ASSETS.md`.
- Rust is allowed only for profiled bottlenecks, behind a C ABI, and must be benchmarked against the C# reference.
- `artifacts/` (desktop publish output, WebView2 test profiles) and `test-results/` are gitignored outputs.

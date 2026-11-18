# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Battle of Nusantara**: an Indonesian-legends RTS/MOBA/Adventure game. The code is a **playable prototype** (v0.13). `solution-design.md` is the long-term AAA concept, and most of it is not implemented. `Progress.md` records what actually exists and its known limits, and `PLAN.md` is the roadmap. Don't describe features from the design doc (Orleans, PostgreSQL, Redis, Semantic Kernel, 5v5 ranked, 128 players, WebGPU, ray tracing) as implemented.

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
./scripts/run.ps1 -HostType Server -Lan                                                                      # binds 0.0.0.0:5080

# Browser (Playwright, Edge channel, tests/browser/). Starts the server on :5080 itself and reuses one already running outside CI
npm ci; npm run vendor; npm test
npx playwright test -g "RTS outposts block movement"   # single browser test by title
# Browser scenarios that click the arena disable the opening cinematic (the `calm` init script); pass `ready(page, true)` to keep it

# Windows installer (Inno Setup 6): publishes Desktop + Server self-contained, writes artifacts/installer/*.exe
./scripts/package.ps1

# Native WPF smoke test: drives the built exe through WebView2 CDP on port 9223
node scripts/desktop-smoke.cjs [path\to\BattleNusantara.Desktop.exe]
```

`npm run vendor` (`scripts/vendor.mjs`) copies Three.js 0.180.0, GLTFLoader, BufferGeometryUtils and SkeletonUtils from `node_modules` into `src/BattleNusantara.UI/wwwroot/vendor/` and rewrites their imports to relative paths. The game must stay **offline: no CDN loads**. Re-run vendor after bumping `three`, and don't hand-edit the vendored files.

CI (`.github/workflows/build.yml`, windows-latest) runs: npm ci → vendor → build Release → `dotnet test --no-build` → Playwright msedge → `npm test`.

## Architecture

The solution has four projects plus tests (`BattleNusantara.slnx`). Dependencies point one way: **Desktop/Server → UI → Core**.

- **Core** (`net10.0`, no UI or networking deps): deterministic game rules. `Match.cs` holds the simulation: units, economy, workers, construction, combat, and objectives for each `GameMode`; `Match.Adventure.cs` (same partial class) holds quests, dialogue and save/load, and `Story.cs` the authored quest graph and bilingual dialogue. `Catalog.cs` holds enums, heroes and `CommandKind`. `NavigationMap.cs` is a visibility graph around rectangular obstacles, using Dijkstra and line-of-sight checks. All input arrives as a `GameCommand` record, and all output leaves as an immutable `MatchView`/`UnitView` snapshot.
- **UI** (Razor class library), shared by both hosts. `GameShell.razor` is the whole app UI: menus, HUD, options, credits, and Indonesian/English strings. `GameClient` runs either a **local `Match`** (solo) or a **SignalR connection** to the server (LAN), and exposes the same `View` either way. `TacticalGroups` holds the local-only control groups, and its `Filter` is the one rule for which unit IDs may be selected. Selection and groups never reach the simulation. `wwwroot/world.js` is the Three.js renderer, loaded via JS interop with `mount`/`update(state, owner, selection, attackMove, buildMode)`/`dispose`. It only renders snapshots and interpolates between them. It never owns game state. `Atmosphere.At(tick)` (Core) gives weather and daylight; the shell passes it to `update` as a sixth argument. Models come from `assets/blender/`: `quantum-shrine.glb` (`create_temple.py`), `characters.glb` + `naga.glb` (`create_characters.py`: 24 variants on one rig, clips Idle/Walk/Attack/Cast/Hit/Death) and `props.glb` (`create_props.py`). `UnitView.Model` names the variant for heroes, champions, NPCs and bosses; other kinds map by kind on the client. Portraits are in `wwwroot/portraits/`, voice clips in `wwwroot/voice/` (`{dialogueKey}-{line}-{id|en}.mp3`; regenerate when `Story.Dialogues` changes).
- **Server** (ASP.NET Core): Blazor interactive server hosting plus `/health`, `/api/account/*`, `/api/status` and the hub at `/hubs/battle`. `Platform/` holds accounts and sessions (`Accounts.cs`: one SQL store for SQLite and PostgreSQL), matchmaking (`Matchmaking.cs`: rules in `Matchmaker`, serialized by the Orleans `LobbyGrain`) and `ServerHost.Build` (`Platform.cs`), which tests call with `ui: false`. `Platform/Npc.cs` is the optional NPC voice (`/api/npc/ask` topics, `/api/npc/talk` typed questions; Semantic Kernel + Azure OpenAI, off by default, authored and keyword fallback). `Rooms.cs` contains `RoomRegistry` (room codes, membership, the ten-player cap (five in MOBA), command validation, rate limits and finished-match reporting), `BattleHub`, and `BattleLoop`. `BattleLoop` is a `BackgroundService` that ticks every room at 10 Hz and pushes snapshots.
- **Desktop** (WPF + `BlazorWebView`, `net10.0-windows10.0.19041.0`): hosts the same `GameShell` and registers `GameClient` in its own DI container.
- **Tests** reference the Server project, so they can reach Core and Server types. Test names follow `Method_Scenario_ExpectedResult`.

Invariants to preserve:
- **Fixed 100 ms tick.** Local matches serialize access with a semaphore, and shared rooms with the registry lock.
- **Server authority.** The player owner comes from the SignalR connection, never from a client-supplied ID. Coordinates must be finite and are clamped. The simulation or server checks mode, ownership, costs, cooldowns and paths *before* applying any part of a group order. Rejection is explicit and leaves the previous order intact.
- **Determinism.** Formation slots are sorted by unit ID, construction continuation follows creation order, and refunds use integer math (`75 * remainingTicks / 80`).
- **Fog of war is applied in `Match.Snapshot`**, never in the client. RTS snapshots omit enemies outside team vision and carry the vision circles in `MatchView.Vision`. Tests that need to see hidden enemies call `Snapshot(fog: false)`.
- **MOBA heroes respawn with the same unit ID**, and only MOBA skips the defeat-on-hero-loss rule. AI heroes are kind `champion`, never `hero`: code looks up a player's unit by `Kind == "hero"` and owner.
- **Navigation rebuilds when outposts are created or destroyed**, and that clears cached routes.
- **Adventure story text never crosses the wire.** The snapshot carries quest states and a `"key:line"` dialogue reference; `Story.cs` holds the text and the client resolves it. Quests in `Story.Quests` must list prerequisites before dependants.
- **`Match.Load` treats saves as untrusted input** and returns null on any violation. Only a solo Adventure in progress can be saved, and a save must stay under 12,000 characters (it travels over the Blazor UI circuit, limit 32 KB; the game hub keeps a 4 KB limit).
- **Identity comes from the authenticated connection.** `BattleHub` reads the account from `Context.User`; never accept an account or owner from a client argument. Guests have a null account and are never written to match results.
- **`SqlAccountStore` must stay portable**: only SQL that both SQLite and PostgreSQL accept. The PostgreSQL path is tested against PostgreSQL 17 and the Redis queue against Garnet (Redis protocol, not Redis itself) by `ExternalStoreTests`; those tests and `NpcLiveTests` are skipped unless `~/.nusantara/test.env` or the environment configures them, so CI never runs them. Do not claim more than that. Schema changes are new entries appended to `Migrations`, never edits to old ones.
- **Weather has one rule:** `Atmosphere.SightFactor(tick)` (Core) shortens sight in mist and storm; everything else about weather, animation, audio, cinematics and NPC replies is presentation and never enters `Match`, snapshots or saves. Character clips are inferred on the client. NPC typed questions reach the model only as the user message, never inside the system prompt, which holds authored text only; the API key comes from `Platform__Npc__ApiKey`, never from a file in the repo.
- **Original characters only.** The concept lists licensed comic heroes; do not model, name or portray them. The superhero faction uses originals (`solution-design.md`).
- **The session token is sent only to the server that issued it** (`GameClient.Build`). Passwords are never stored client-side.
- **LAN seats.** A dropped connection keeps its seat for `GraceTicks`; `Rejoin` needs the seat token. `Leave` or expiry calls `Match.RemovePlayer`. Spectators have owner -1 and can never issue commands.
- The **snapshot shape is the LAN protocol**. Changing `MatchView`/`UnitView`/`GameCommand` breaks clients on other versions (there's no backward compatibility), so bump the version in `Directory.Build.props`, `package.json` and the `/health` response together.

`docs/ARCHITECTURE.md` has the detailed rules for economy, construction, navigation, MOBA growth, Adventure and rooms (exact numbers, HP, limits). Read it before changing `Match.cs` behavior. `docs/RENDERING.md` records the WebGPU measurement (WebGL2 stays) and the paper-only ray tracing and destruction assessments. `docs/PERFORMANCE.md` lists the budgets that `PerformanceTests` and one browser test enforce; `FullMatchTests` play whole matches with scripted policies and must stay deterministic.

## Conventions

- C#: four-space indentation (`.editorconfig`; JS/JSON/CSS use 2), LF line endings, nullable enabled. Keep allocations low in tick and gameplay loops.
- **Documentation is bilingual (English + Bahasa Indonesia)**, usually as paired paragraphs or `English / Indonesia` lines (see README, `docs/GUIDE-EN.md`, `docs/PANDUAN-ID.md`). Keep Indonesian character and faction names exactly as written.
- Each feature release updates `README.md` (a versioned section with a screenshot), `Progress.md` (status table, validation, limits) and `PLAN.md` checkboxes. Screenshots go in `docs/screenshots/` and must be captured from the running app, not mockups.
- The required attribution must appear in the app and the docs: **"Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios"**.
- Art tooling comes from `BattleNusantara.txt`: use the Blender MCP for modeling, rigging and animation, the Rodin MCP (GPT Image Flare) for 2D and sprites, and the `frontend-design` skill (`.claude/skills/`) for UI work. Record third-party asset licenses in `docs/ASSETS.md`.
- Rust is allowed only for profiled bottlenecks, behind a C ABI, and must be benchmarked against the C# reference.
- `artifacts/` (desktop publish output, WebView2 test profiles) and `test-results/` are gitignored outputs.

# Repository Guidelines

## Project Structure & Module Organization

**Battle of Nusantara** is an Indonesian RTS, MOBA, and adventure game. The repository holds a playable prototype; `solution-design.md` is the long-term concept and most of it is not implemented. `Progress.md` records what exists and its limits.

- `src/BattleNusantara.Core` — deterministic simulation (`Match`, `NavigationMap`, `Catalog`), with no UI or networking dependencies.
- `src/BattleNusantara.UI` — shared Razor UI (`GameShell.razor`), `GameClient`, and the Three.js renderer in `wwwroot/world.js`.
- `src/BattleNusantara.Server` — ASP.NET Core host, SignalR hub `/hubs/battle`, rooms and the 10 Hz loop.
- `src/BattleNusantara.Desktop` — WPF + WebView2 host for the same UI.
- `tests/BattleNusantara.Tests` (xUnit) and `tests/browser` (Playwright, Edge).
- `assets/blender` — source for the shrine model; `docs/` — bilingual guides, architecture, screenshots.
- `BattleNusantara.txt` records the owner's requirements. `.mcp.json` configures MCP tooling; review changes carefully for local settings and credentials.

Dependencies point one way: Desktop/Server → UI → Core. Orleans, PostgreSQL, Redis and Semantic Kernel are design intentions, not implemented capabilities. See `docs/ARCHITECTURE.md` for simulation rules.

## Build, Test, and Development Commands

Prerequisites: .NET SDK 10; Windows with WebView2 for the desktop host; Node.js 22+ and Microsoft Edge for vendoring and browser tests.

- `dotnet build BattleNusantara.slnx -c Release` — compile; warnings are errors.
- `dotnet test tests/BattleNusantara.Tests -c Release` — unit tests. Add `--filter "FullyQualifiedName~ClassName"` for one class.
- `dotnet run --project src/BattleNusantara.Server -c Release --no-launch-profile --urls http://localhost:5080` — browser and LAN host.
- `dotnet run --project src/BattleNusantara.Desktop -c Release` — desktop app.
- `npm ci; npm run vendor; npm test` — vendor Three.js locally and run browser tests (starts the server on port 5080). `npx playwright test -g "title"` runs one.
- `node scripts/desktop-smoke.cjs [exe]` — smoke-test the built WPF executable.

## Coding Style & Naming Conventions

For C# code, use four-space indentation, PascalCase for types and public members, and camelCase for parameters and local variables. JS, JSON and CSS use two spaces (`.editorconfig`). Favor clear module boundaries and measure performance-sensitive changes, particularly allocations in gameplay loops. The game must run offline: do not load assets from a CDN, and do not hand-edit `wwwroot/vendor/`.

Keep documentation clear in both English and Bahasa Indonesia. Preserve Indonesian character and faction names consistently.

## Testing Guidelines

Add xUnit tests under `tests/BattleNusantara.Tests` for simulation and server rules, named `Method_Scenario_ExpectedResult`. Add a Playwright scenario under `tests/browser` for UI flows; these also capture the screenshots in `docs/screenshots/`. Verify documentation changes against the design and requirement files.

## Commit & Pull Request Guidelines

Use concise imperative commit subjects, such as `docs: clarify multiplayer design`. Pull requests should describe the change, link relevant issues, and explain validation performed. Include screenshots for visual changes.

## Documentation Requirements

Each release updates `README.md`, `PLAN.md` (roadmap), `Progress.md` (status, validation, limits) and both guides. Changing `MatchView`, `UnitView` or `GameCommand` changes the LAN protocol; bump the version in `Directory.Build.props`, `package.json` and the `/health` response together. Screenshots must come from the running application. Include the required attribution: “Dibuat oleh Ariana Mischa Fadhila dari Syubadubin Studios”.

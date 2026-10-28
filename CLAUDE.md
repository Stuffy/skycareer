# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

SkyCareer is a standalone Windows career add-on for Microsoft Flight Simulator 2024 (C# / .NET 10, WPF host with Blazor Hybrid UI and MudBlazor 9). UI text and user-facing messages are German; code, identifiers and comments are English.

## Commands

```bash
dotnet build SkyCareer.sln
dotnet test SkyCareer.sln
dotnet test tests/SkyCareer.Sim.Tests --filter "FullyQualifiedName~FlightTrackingTests.Touchdown_reports_rate_and_peak_g"
dotnet run --project src/SkyCareer.App
dotnet tool restore
dotnet ef migrations add <Name> --project src/SkyCareer.Data -o Migrations
```

- A running `SkyCareer.App.exe` locks `src/SkyCareer.App/bin/...`; close it first, or build the app elsewhere with `dotnet build src/SkyCareer.App -o <tempdir>`.
- The app migrates the database on startup. The DB lives at `%LOCALAPPDATA%\SkyCareer\skycareer.db` (override with the `SKYCAREER_DB` env var, e.g. to test the UI against a throwaway DB without touching the user's careers), user content in `Documents\SkyCareer\content`, exported flight plans in `Documents\SkyCareer\Flugplaene`.

## SimConnect is optional at build time

`SkyCareer.Sim.csproj` compiles the real `SimConnectSource` only when the MSFS 2024 SDK is found (via `SimConnectSdkDir`, the `MSFS2024_SDK` env var, or the default `C:\MSFS 2024 SDK`). It then defines `HAS_SIMCONNECT`, references the managed wrapper, copies the native `SimConnect.dll`, and targets x64. Without the SDK everything still builds and the app works in replay mode. Code outside `SimConnect/SimConnectSource.cs` must not reference SimConnect types; go through `FlightDataSourceFactory` and the interfaces in `Telemetry.cs` / `AircraftInspection.cs` (`IFlightDataSource`, `IAircraftLoader`, `IAircraftInspector`).

SimConnect facts verified against the live sim (keep them in mind when changing `SimConnectSource`):
- L:Vars can be read directly in data definitions, but **unknown L:Vars return 0 instead of an error**. Detect add-ons by plausible values, not by success (see `GsxState.Detected`).
- `DESIGN SPEED VC` already returns cruise **TAS** (525 kt for the Citation X).
- One-shot reads go through `RequestOnceAsync`, which maps SimConnect exceptions back to the request by packet id. Batch reads use a fixed 16-double struct (`ReadVariablesAsync`), padded with dummy L:Vars.
- Optional variables (`ReadDesignAsync`) sit in a separate request so a missing variable doesn't break the whole snapshot.
- Airport facility data (`ReadAirportAsync`): ILS is detected from **approaches of type 4 (ILS)**. The runway `PRIMARY/SECONDARY_ILS_ICAO` fields are empty at some airports (LSZH) and only serve as extra info. Read char[8] idents as byte arrays (string marshaling produced garbage). Unknown idents answer with an exception and no end record. Results are cached in the `AirportFacilities` table; OurAirports has no ILS data (`navaids.csv` only covers VOR/NDB/DME/TACAN).

## Architecture

Dependency direction: `Domain` ← `Sim`, `Missions`, `Data`, `Integrations.*` ← `Application` ← `App`. `Domain` has no external dependencies.

- **Domain**: units (`Mass` in kg, SimConnect pounds converted at the edge; `Money` as decimal), aircraft profiles, load planning (`LoadSheetBuilder`, `FuelPlanner`, `LoadVerifier`), the finance cost model, career entities (`Career`, `FleetAircraft`, `MissionContract`, `FlightRecord`, `LedgerEntry`), and the simulator-independent `FlightResult`.
  - Crew convention: `OperatingEmptyKg` equals the sim's EMPTY WEIGHT. Crew is loaded into crew stations on top, unless `CrewIncludedInEmptyWeight`. `LoadSheet.SimPayload` (payload + crew) is what the sim should report. Crew mass and crew costs use `AircraftProfile.Pilots` = max(`CrewCount`, `MinimumPilots`): two pilots for jets, MTOW > 5.7 t or > 9 seats unless tagged `single_pilot`. Pilots count `CrewMassKg` (default 85 kg), cabin crew `CabinCrewMassKg` (default 75 kg, EASA standard masses); SimBrief has no crew weight of its own (crew sits in its airframe OEW).
- **Sim**: `FlightSession` combines `FlightPhaseTracker` (phase state machine with hysteresis, touchdown/bounce detection) and `IntegrityMonitor` (slew, sim rate, refuelling in flight, payload change, teleport, aircraft change). Payload is locked when the takeoff roll starts (released again after a rejected takeoff) and ignored once the phase is `Arrived` (deboarding). `ReplaySource` and `FlightLogFile` (JSON Lines) make all tracking testable without MSFS; tests build telemetry with `SyntheticFlight`.
- **Missions**: data-driven content. `ContentLoader` layers content roots (a later root overrides by id). `ContentStore` holds the current `GameContent` (shipped `content/` plus the user folder) and reloads after edits. Mission types are JSON (`content/missions`); the `RequirementEvaluator` matches them to aircraft via tags/seats/cargo/surfaces/range. `IMissionGenerator` implementations are selected by the mission type's `generator` name. `StandardMissionGenerator` only offers jobs whose load sheet fits the limits, weights destinations log-normally around 45 % of the aircraft's range (divided by distance^1.5 so the many far-away airports don't crowd out medium trips), and honours an optional `MissionSearch` (also used as the market list filter). `Settlement/FlightSettlement` is a pure function: contract + `FlightResult` → outcome, score, ledger lines, wear, reputation.
- **Data**: one SQLite DB for all careers (scoped by `CareerId`, FK cascade) plus shared airport data from OurAirports (`OurAirportsImporter`). At import each airport is matched to the city it serves (`GeoNamesCities`, GeoNames cities15000: population fading with distance, capital flag); mission types weight destinations by that via `destinations.cityWeight` and can demand a per-search `minCapitalShare` (enforced in `SearchMarketAsync` like `minLargeShare`, via `RequireCapital`); `distanceInfluence` (< 1) damps the preferred-distance weighting so city importance wins. Military fields are flagged by name at import (`OurAirportsImporter.IsMilitaryField`, joint-use fields with scheduled service stay civil) and only offered to profiles tagged `military`. By convention, enums and `decimal` are stored as strings, so sums and money sorting happen in memory.
- **Application**: `CareerService` is the only place career state changes (fleet incl. relocation by paid ferry flight via `CostModel.Ferry` or free move, market, accept/start/complete flight, wait, monthly fixed costs). Mission pay = rule terms + `pay.hourlyRateFactor` × `FleetEconomics.HourlyRate` (rent + fuel per block hour), so payouts follow the aircraft class. Each method uses its own `DbContext` from `IDbContextFactory` and raises `Changed`. Game time is flight-based: it only advances through flights (+ turnaround) and `WaitAsync`.
- **Integrations**: `SimBrief` (dispatch link + OFP fetch/validation, no API key), `Gsx` (reads `FSDT_GSX_*` L:Vars; `GsxLoadSync` turns GSX boarding/deboarding progress into scaled station loads), `BeyondAtc` (BeyondATC has no API; `PlnExporter` writes MSFS `.pln` files it can import).
- **App**: singletons `FlightMonitorService` (owns the data source and the current `FlightSession`, thread-safe summaries), `GsxCoordinator` (polls GSX every 2 s; writes payload only before block-off and after parking) and `DepartureCoordinator`, which ticks `Application/FlightAutomation` about once a second. There is no start button: an accepted job is armed (tracked, still editable) when the aircraft is on the ground at its origin, starts at liftoff unless the departure light (`Missions/Departure/DepartureCheck`) is red for position or type, records other red items as `DepartureIssues` (10 % payout each at settlement) and is settled automatically once parked. App-wide (non-career) settings live in `AppSettings` (`appsettings.json` next to the DB). Its `DebugMode` (Einstellungen → Erweitert) shows the "Debug" menu group (Debug-Werkzeuge `/debug`, Beladungs-Spike; both wrapped in `DebugGate`) plus replay/test tracking on Live-Flug and free relocation in Fleet; the debug operations sit in `CareerService`'s "Debug tools" section (cash changes are booked as `LedgerCategory.Adjustment`, excluded from the monthly result like the starting capital). Menu: main pages, then the collapsible "Inhalte" group (profiles, mission types), "Debug", Einstellungen. Pages derive from `CareerPageBase`, which reloads on `CareerService.Changed` and turns `CareerException` into snackbars. `MainLayout` shows `CareerSelect` instead of the app while no career is active. The mission map is Leaflet via `wwwroot/js/missionMap.js` (CDN, keyless tiles; CARTO now needs an API key).

## SimBrief facts (verified against a real OFP)

- `weights.cargo` is bags **plus** freight; the freight alone is `weights.freight_added`. Compare mission cargo with the freight.
- SimBrief takes passenger and bag weight from the airframe (175 lb / 50 lb in the user's OFPs, `pax_weight`/`bag_weight`) and adds one bag per passenger itself. The dispatch page ignores `acdata`, so the link only sends `pax` and `cargo` = freight (omitted when 0). Instead, offers copy SimBrief's model: `Career.PassengerWeightKg`/`BagWeightKg` (Einstellungen → Beladung, default 175/50 lb) via `PassengerMassTable.ForCareer` and `GenerationContext.Masses` — children weigh as adults, every seated passenger has one bag unless the type is `handOnly`/`none`. `OfpValidator` reports an OFP whose pax/bag weight differs from the career's.
- When the airframe's MZFW/MTOW limits the payload, SimBrief silently drops bags (`bag_count_actual` < `bag_count`), then passengers (`pax_count_actual`). `OfpValidator` reports this.
- The sim only reports max gross weight, so profile drafts must estimate MZFW/MLW (`AircraftProfileEstimator`); MZFW = MTOW on a jet lets missions overload the aircraft.

## Releases & updates

- Velopack + public GitHub Releases. `tools/release.ps1 -Version x.y.z` tests, publishes (framework-dependent, win-x64), checks that `SimConnect.dll` and `content/` are included, packs (`vpk` local tool, delta against the last release) and uploads with the token from `SKYCAREER_GH_TOKEN`. `-PackOnly` builds the installer locally only.
- Version and repo URL live in `Directory.Build.props` (`Version`, `SkyCareerRepoUrl`); the app reads the URL via `AssemblyMetadata("RepoUrl")`.
- `Program.Main` (`StartupObject`) must call `VelopackApp.Build().Run()` before WPF starts.
- Pack ID is `SkyCareerApp`, **not** `SkyCareer`: Velopack installs to `%LOCALAPPDATA%\<packId>` and deletes it on uninstall, and the careers DB lives in `%LOCALAPPDATA%\SkyCareer`.
- `UpdateService` is disabled unless installed by Velopack (dev builds show "Entwicklungsversion"). It checks 10 s after start and every 6 h, downloads in the background, refuses to restart during a tracked flight (`CanRestartNow`) and applies a downloaded update on exit (`App.OnExit` → `ApplyOnExit`).

## Conventions and pitfalls

- Never put padding utility classes (`pa-4` etc., they are `!important`) on `MudMainContent`: its `padding-top` reserves the app bar height. Page padding lives on the inner `div` in `MainLayout`.
- Telemetry and GSX events arrive on background threads; UI components must use `InvokeAsync(StateHasChanged)` and should throttle re-renders.
- Never block the UI thread on async cleanup (`OnExit` disposes services via `Task.Run` with a timeout); use `ConfigureAwait(false)` in library-side shutdown paths.
- Values that must stay identical across app restarts (e.g. the daily fuel price in `CostModel`) use a stable FNV hash, not `string.GetHashCode`.
- The values in `content/aircraft/*.json` are approximations. Station indices can be checked with the "Beladungs-Spike" page or the profile editor ("Stationen aus Sim übernehmen").
- Tests that need shipped content locate the repo root by searching upward for `SkyCareer.sln`.

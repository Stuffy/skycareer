# SkyCareer

**Career mode for Microsoft Flight Simulator 2024** – a standalone Windows application that puts every flight into an
airline career: accept jobs, build a fleet, keep costs under control and earn reputation.

> **Status:** early test version (0.x). Feedback and bug reports are very welcome.

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation and updates](#installation-and-updates)
- [Getting started](#getting-started)
- [Integrations](#integrations)
- [Custom aircraft profiles](#custom-aircraft-profiles)
- [Data locations](#data-locations)
- [Reporting bugs](#reporting-bugs)
- [Development](#development)
- [License and third parties](#license-and-third-parties)

## Features

- **Job market** – scheduled, charter, VIP, air taxi, cargo, air ambulance and bush flights that match the aircraft, its
  range and runway requirements. Big cities and capitals are preferred destinations; military airfields are only open to
  military aircraft. Scheduled and charter flights are usually well booked, and the pay follows the load factor. Some jobs
  come as an outbound and return package ("Round trip") with a bonus when both legs are flown.
- **Fleet and finances** – buy, lease or rent aircraft; fuel, fees, crew, maintenance and insurance are booked
  realistically. Aircraft can be repositioned by ferry flight. The remaining fuel stays in the aircraft and is set in the
  simulator for the next job; you only pay for what is uplifted. The wear MSFS 2024 reports during a flight (landing gear,
  tires, brakes, engine …) feeds into the condition of the components; worn components make maintenance and repairs more
  expensive and show up in the departure check. Every aircraft has a detail page with its values, components (repairable
  individually or per group), economics, flight history and open jobs.
- **Load sheet and loading** – passengers, baggage, cargo and fuel are calculated, checked against MZFW/MTOW/MLW and, on
  request, written straight into the simulator.
- **Automatic flight flow** – no start button: once the aircraft is at the departure airport, the job is tracked. A
  departure light checks type, position, payload, fuel, weight limits and OFP. The job starts at liftoff and is settled
  automatically after shutdown on stand at the destination.
- **Scoring and integrity** – landing, punctuality and handling feed into score and reputation; a better reputation
  unlocks mission types and raises the pay of new jobs (up to +50 % at reputation 100). Slew, time acceleration,
  in-flight refuelling and position jumps are detected and cost part of the pay, but never void the flight.
- **Crash-safe** – flights in progress are recorded and resumed automatically after an app restart.
- **Integrations** – SimBrief (planning and OFP check), GSX Pro (boarding and deboarding in sync, settlement after
  deboarding, GSX receipts as costs) and BeyondATC (flight plan export).
- **Languages** – English and German; the app follows the Windows display language and can be switched under
  **Settings → Language**.

## Requirements

- Windows 10 (19041) or Windows 11, 64-bit
- Microsoft Flight Simulator 2024
- Internet access for the one-time airport data import, the map view and updates
- Optional: a SimBrief account, GSX Pro, BeyondATC

The installer adds the .NET Desktop Runtime and the WebView2 Runtime automatically if needed.

## Installation and updates

1. Download and run `SkyCareerApp-win-Setup.exe` from [Releases](https://github.com/Stuffy/skycareer/releases/latest).
2. The installer is not code-signed, so Windows SmartScreen shows "Windows protected your PC" on first launch – choose
   **More info → Run anyway** to continue.
3. SkyCareer installs for the current user (no administrator rights needed) and creates shortcuts on the desktop and in
   the Start menu.

**Updates** are checked at startup and every six hours and downloaded in the background. A banner offers a restart; the
app never restarts during a flight – the update is then installed when you close the app. Careers and custom content are
kept across updates and uninstalls.

## Getting started

1. **Import airport data** – once, on first start, via "Download & import" (OurAirports and GeoNames, about 18 MB).
2. **Create a career** – choose pilot name, airline, home base and difficulty; optionally enter your SimBrief username.
3. **Get an aircraft** – buy, lease or rent one under *Fleet*. It is parked at your home base.
4. **Accept a job** – search the market under *Jobs* (filters for aircraft, mission type, distance, payload and more) and
   accept a job.
5. **Prepare the flight** – load the matching aircraft at the departure airport in the simulator. The steps below the job
   (plan in SimBrief → load OFP → load in sim → verify load) are possible until liftoff; the departure light shows the status.
6. **Fly** – the job starts at liftoff. After shutdown on stand at the destination (engines off; parking brake set or
   30 seconds stationary) the flight is settled with revenue, costs, score, reputation and wear.

Game time only passes through flights and deliberate waiting (title bar) – breaks outside the simulator cost nothing.

## Integrations

### SimBrief
"Plan in SimBrief" opens the dispatch page with route, passengers, cargo and the exact zero fuel weight of the load
sheet. SimBrief keeps the passenger count and plans exactly this ZFW, so fuel planning matches the flight whatever empty
weight your SimBrief airframe has; it adjusts the freight in the OFP to make up the difference. SimBrief splits passengers
and baggage with the weights of the selected airframe; with the same values under **Settings → Loading** (default: 175 lbs
per passenger, 50 lbs per bag) the freight in the OFP matches the job as well.

Optionally, **Varying bag weights per passenger** makes bags weigh differently per passenger in new jobs.

"Load OFP" takes over block fuel and route and reports differences in route, type, passengers, cargo and weights. Custom
airframes can be linked by entering the SimBrief airframe ID in the aircraft profile.

### GSX Pro
With the simulator connected, the job card shows the status of the GSX services (boarding, refuelling, pushback,
deboarding). With **Sync loading with GSX**, SkyCareer fills cabin and holds as boarding progresses, sets the block fuel
after refuelling and empties the aircraft during deboarding. It only writes before block-off and after shutdown on stand.
GSX takes the passenger count from SimBrief; differences to the job are shown.

With **Use GSX receipts as costs**, SkyCareer books the bills GSX issues for handling, buses, catering and fuel with their
amount (converted to euros) in the ledger. The model costs for fuel, handling and catering are then dropped at settlement;
fuel uplifted without GSX is free. The receipts can be opened from Finances and the logbook and printed or saved as PDF
from there. With GSX in use, the settlement waits for deboarding so its receipts belong to the flight.

### BeyondATC
BeyondATC has no public interface. "Flight plan (.pln)" saves an MSFS flight plan to `Documents\SkyCareer\Flugplaene`,
which BeyondATC (VFR › file import) and the EFB can load. For IFR flights BeyondATC uses the current SimBrief OFP directly.

## Custom aircraft profiles

Profiles for the Cessna 172, Cessna 208B, Airbus H145, King Air 350 and Airbus A320neo are included. More aircraft can be
added under **Content → Aircraft Profiles**:

- **From the current sim aircraft:** empty weight, MTOW, tanks, payload stations, engine type and cruise speed are read
  from the simulator. "SimBrief type" takes MZFW, MTOW, MLW, fuel capacity, fuel burn, cruise speed and minimum runway
  from SimBrief's type list instead of estimating them.
- **From a SimBrief type:** creates the complete profile from SimBrief, even without the simulator running. The payload
  stations are added later with "Take stations from sim".
- Missing values (prices, cargo capacity) are derived from the most similar known profile.
- The editor checks the profile continuously and shows which mission types it can fly.

Custom profiles live in `Documents\SkyCareer\content\aircraft` and replace included profiles with the same ID. The
included values are approximations; the station layout can be checked in the profile editor with "Take stations from sim".

## Data locations

| Path | Content |
|---|---|
| `%LOCALAPPDATA%\SkyCareer\skycareer.db` | Careers, fleets, jobs, ledger and airport data |
| `%LOCALAPPDATA%\SkyCareer\tracks` | Recordings of completed and active flights (JSON Lines) |
| `%LOCALAPPDATA%\SkyCareer\logs` | Error logs (14 days) |
| `Documents\SkyCareer\content` | Custom aircraft profiles and mission types |
| `Documents\SkyCareer\Flugplaene` | Exported flight plans (`.pln`) |

The program files themselves live in `%LOCALAPPDATA%\SkyCareerApp`.

## Reporting bugs

Please report bugs and suggestions as an [issue](https://github.com/Stuffy/skycareer/issues). Under
**Settings → Advanced → Copy diagnostics**, SkyCareer puts the version, simulator status and the latest log lines on the
clipboard – please attach this text to every report, together with a short description of the steps that led to the bug.

**Debug mode** (Settings → Advanced) unlocks developer and test tools, such as setting cash and reputation. It is meant for
testing; cash corrections are noted in the ledger.

## Development

### Build and test

Requires the .NET 10 SDK.

```bash
dotnet tool restore
dotnet build SkyCareer.sln
dotnet test SkyCareer.sln
dotnet run --project src/SkyCareer.App
```

The SimConnect connection is only compiled when the **MSFS 2024 SDK** is found – via the `MSFS2024_SDK` environment
variable, the default path `C:\MSFS 2024 SDK` or `-p:SimConnectSdkDir="…\SimConnect SDK"`. Without the SDK the project
still builds and tests completely; the app then only works with recorded flights (replay).

The `SKYCAREER_DB` environment variable selects a separate database, e.g. for testing without touching existing careers.
Schema changes go through EF Core migrations:

```bash
dotnet ef migrations add <Name> --project src/SkyCareer.Data -o Migrations
```

The app migrates the database automatically at startup.

### Translations

UI texts are written in English in the code (`L.T("…")`, `L.F("…", …)`, in Razor `@T("…")`); the English text is the key.
German translations live in `src/SkyCareer.Localization/de/*.json`. A test checks that every text has a German entry with
the same placeholders.

### Project structure

| Project | Responsibility |
|---|---|
| `SkyCareer.Localization` | UI language, translations |
| `SkyCareer.Domain` | Units, aircraft profiles, load sheet, fuel planning, cost model, career entities |
| `SkyCareer.Sim` | Data sources (SimConnect, replay), flight phase detection, integrity checks, flight recording |
| `SkyCareer.Missions` | Data-driven mission types, job generation, departure check, settlement |
| `SkyCareer.Data` | EF Core with SQLite, migrations, OurAirports and GeoNames import |
| `SkyCareer.Application` | `CareerService` (the only place that changes state), flight automation, logging |
| `SkyCareer.Integrations.*` | SimBrief, GSX, BeyondATC |
| `SkyCareer.App` | WPF host with Blazor Hybrid UI (MudBlazor), updater |
| `content/` | Included aircraft profiles and mission types as JSON |

More architecture and implementation details are in [`CLAUDE.md`](CLAUDE.md).

### Releases

Releases are distributed with [Velopack](https://velopack.io) via GitHub Releases; installed apps get delta updates.
Requirements: the MSFS 2024 SDK, a fine-grained token with *Contents: Read and write* for this repository in
`SKYCAREER_GH_TOKEN`, and a clean Git state.

```bash
powershell -File tools/release.ps1 -Version 0.2.0 -Notes notes.md
```

The script runs the tests, publishes the build, checks for `SimConnect.dll` and the included content, creates the
installer and update packages and uploads them. With `-PackOnly` it only packs locally under `releases/`.

## License and third parties

The source code is licensed under the [MIT License](LICENSE), except for:

- `SimConnect.dll` – © Microsoft, part of the MSFS 2024 SDK, only included in the release packages
- Airport data from [OurAirports](https://ourairports.com/data/) (public domain)
- City data from [GeoNames](https://www.geonames.org/) ([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/))
- Maps with [Leaflet](https://leafletjs.com/); map tiles by Esri, [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors and OpenTopoMap (attribution in the map)

SkyCareer is an independent project and not affiliated with Microsoft, Asobo Studio, Navigraph/SimBrief, FSDreamTeam or
BeyondATC.

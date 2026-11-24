# Changelog

All notable changes to SkyCareer. Versions follow [Semantic Versioning](https://semver.org/).

## Unreleased

### Added
- **Jobs with several stops:** bush supply, cargo feeder and the new mail run sometimes come as a run over 3–4 stops
  (A → B → C …, mail runs and bush rounds back home). The legs are accepted together, each one starts by itself once
  the aircraft is at its origin, and the last leg pays a bonus on all payouts when every leg was flown. The job market
  shows the whole route and the map draws every leg. VIP and charter flights sometimes come as a roadshow: the same
  group travels to 1–2 stops and is flown back home, with the same passengers and bags on every leg.
- **Recurring clients:** eight companies (a mining company, a hospital group, a tour operator, an express service, an aid
  organisation, a business travel agency, a livestock breeder and a chemical company) hand out jobs of their kind and
  keep their own standing with your airline. Good flights raise it, late arrivals, diversions and cancellations lower
  it; silver and gold pay 5 % and 10 % more. From silver a client offers a series (4–5 flights in 7 game days, 20 %
  bonus), and two clients have story chains – short series of jobs with briefings, unlocked by standing. The new
  "Clients" page shows standing, series and story progress.
- **Sightseeing and survey flights:** round trips from your airfield over 2–4 towns of the region and back – sightseeing
  for guests below 3,000 ft above ground, aerial survey at 1,500 ft (±300 ft) and pipeline patrol below 1,000 ft. Live
  flight shows the next waypoint with course and distance and whether you are in the height band; every waypoint
  not passed within 1.5 nm costs its share of the pay. The map draws the tour, the flight plan contains the waypoints.
- **Delivery flights:** bought and leased aircraft now come from their factory (Cessna Wichita/Independence, Beechcraft
  Wichita, Airbus Toulouse, Airbus Helicopters Augsburg). Have them delivered – paid like a ferry flight, the aircraft
  is unavailable meanwhile – or pick them up and fly the delivery to the home base yourself, with fuel stops planned
  automatically when the distance is beyond the aircraft's range. Rentals are still ready at the home base at once.
- **Events during flights:** on about every tenth flight something happens that asks for a decision – a medical
  emergency on board (divert to the nearest suitable airfield), the customer changing the destination (new destination
  and pay) or a technical warning from a worn component (precautionary landing, paid for the share flown). A banner on
  every page with a sound asks for the decision; without an answer within 5 minutes the flight continues as planned.
  The settlement judges the decision. Can be switched off under Settings → Advanced.

- **New job filters:** pay per block hour (the offer list also shows €/h), only airfields known to MSFS, and – for the
  list – client, story jobs only, packages only and demand bonus only.

### Changed
- New aircraft are no longer delivered to the home base for free and at once (see delivery flights).
- **Clearer map:** routes are only drawn for accepted jobs and the selected offer; other offers show as destination pins.
- **Better spread market:** at most 10 open offers of one mission type go to the same destination; new searches
  prefer destinations that have no offer yet.

### Removed
- The "ILS at destination" filter: it needed MSFS running during the search. Known ILS data is still shown in the list.

## 0.5.0 – 2026-11-15

### Added
- **Navigation for bush and VFR flights:** every job card shows the course (magnetic while connected to MSFS), distance
  and time en route, the destination's elevation, pattern altitude and runway, and its coordinates (for a user waypoint
  in the GPS, and with a copy button in the decimal format the MSFS world map search takes). Live flight shows a direct-to line from the current position with distance, time,
  your track and how far it is off course. The map draws the 3 nm arrival radius around accepted jobs' destinations and
  the flight path flown.

- **Passenger comfort:** passenger flights are rated for how they were flown (G load, steep turns, fast descents,
  landing) and the passengers comment on the flight in the settlement. Comfort replaces the old "smoothness" part of
  the score, and a very comfortable flight earns a tip of up to 5 % of the pay. The logbook keeps the rating.
- **Sensitive loads and tight deadlines:** mission types can set handling limits (G, bank, sink rate) that cost part of
  the pay when exceeded, and pay a bonus for every minute before a tight deadline. New mission types: organ transport,
  livestock transport and dangerous goods; air ambulance flights now have limits for the patient. Live flight shows
  the current values against the limits.
- **Bonuses for demanding landings:** a decent landing at night, in visibility below 5 km or with 10 kt crosswind or
  more pays 5 % extra each. Live flight shows wind, visibility and day or night.
- **Demand events:** every game week some regions (about 90 × 90 nm) are in demand (trade fair, festival, sports final,
  holiday rush, relief supplies, harvest). Matching jobs to any airfield in the region come up more often and pay
  25–30 % more; the job market lists the week's events and outlines the regions on the map. Existing careers see different offers than before the update.
- **Regional fuel prices:** fuel is cheaper in oil-producing countries, dearer in high-tax countries and at strips
  without an ICAO code. The job card shows the price at both ends of the flight, so tankering can be planned.

### Fixed
- **Airfields MSFS doesn't know:** small strips such as "BR-2197" are not in the simulator's database. The flight plan
  (.pln) now leads to them as a user waypoint with their coordinates and ends at the nearest airport MSFS knows (MSFS
  only loads plans between airports), the job card
  marks them "not in MSFS database", and SimBrief planning is hidden for such legs. Departure and arrival were already
  detected by position and work as before.

## 0.4.0 – 2026-11-07

### Added
- **Reputation for GSX services:** catering, cabin cleaning, potable water and lavatory service completed in GSX Pro
  before departure earn +1 reputation each (at most +2 per flight) on passenger flights that arrive at their destination.
  The job card shows which services were counted, the settlement notes the bonus.

## 0.3.1 – 2026-11-06

### Fixed
- **Fuel after restarting MSFS:** when the next job was prepared right after parking and MSFS was closed before the
  next flight, the aircraft started the new flight with its default fuel (e.g. 50 %). SkyCareer now sets the fuel
  left over again when the job is prepared in a newly loaded flight.
- **Black window after a display change:** when MSFS switched between full screen and window or changed the display
  mode, the app could show an error message and then stay black without any controls. SkyCareer now rebuilds the
  display quietly as soon as the screen has settled. Closing the app is now noted in the log.

## 0.3.0 – 2026-11-04

### Added
- **Three times as many jobs:** a search now adds up to 60 jobs per aircraft (was 20), and up to 240 open offers are
  kept per aircraft. The offer list is split into pages of 50.
- **Reproducible job market:** every career has a market seed (random, or entered when starting a career; shown on the
  airline page). Jobs follow from the seed, the game date, the aircraft and the search criteria: the same search
  gives the same jobs after a restart or in another career with the same seed, and each further search continues with
  new jobs.
- The job search filter, its sorting and whether it is expanded are saved with the career.
- **Airline page:** a new menu entry for your airline. Change its name, set an ICAO code and radio callsign (or let them
  be derived from the name), change pilot name and home base, and see key figures (fleet, flights, block hours,
  punctuality, revenue, reputation bonus). The career details moved here from the settings.
- **Flight numbers:** every job gets a flight number, shown as callsign (e.g. SCA1234) in the job list and the logbook.
  Scheduled flights keep one number per route, return flights take the next even number. "Plan in SimBrief" sends the
  airline code, flight number and callsign.

### Changed
- **Reputation pays:** new jobs pay more the better your reputation, rising linearly to +50 % at reputation 100 (+5 % at
  the starting value 10). Offers and accepted jobs keep the pay they were offered with. The market page shows the
  current bonus.

### Fixed
- **Last GSX receipts:** after GSX deboarding, SkyCareer now waits up to 5 minutes before settling, so receipts GSX
  writes only minutes later (e.g. the crew bus) are part of the flight's costs. Starting an engine, moving the aircraft
  or "Settle now" still settles at once.
- **Crew weights:** on aircraft without a seat station for the cabin crew (e.g. the Citation X), the cabin crew's weight
  was spread over the pilot seats, so each pilot weighed about 270 lbs. It now goes to the baggage hold, as in the
  SimBrief OFP, and stays there during GSX boarding and deboarding.
- **Fuel after ending a flight in MSFS:** ending the flight before SkyCareer settled it reset the aircraft to its
  defaults (e.g. 50 % fuel), and that value was stored as the fuel left over, so the next flight started with it. The
  fuel left over and the MSFS wear are now taken when the aircraft is parked. When MSFS resets fuel and payload right
  after the stored fuel was set at the start of a flight, SkyCareer sets it again.
- **Fuel at the start of a flight:** the fuel restored from the last flight was overwritten with the planned block fuel
  right after the job was prepared, when GSX still showed a completed refuelling from earlier. SkyCareer now sets the
  planned block fuel only after it saw GSX refuel or board, and never over fuel that GSX, the EFB or the restore loaded.

## 0.2.1 – 2026-11-01

### Fixed
- **Settlement after GSX deboarding:** GSX reports a completed deboarding only for a moment; SkyCareer missed it and
  settled the flight only after the 20-minute timeout. It now settles about 15 seconds after deboarding.
- **Several aircraft on jobs:** the job of the aircraft type loaded in the sim is prepared first, and loading another
  aircraft at the stand switches to its job. Fuel, GSX, wear and GSX receipts only follow that job; the market page no
  longer points GSX at another job.
- A flight left unsettled when the app was closed no longer takes over another aircraft: once its recording ended at the
  gate it is settled from the recorded data, otherwise it waits until its aircraft is loaded again.

## 0.2.0 – 2026-11-01

### Added
- **English and German:** the whole app is now available in English and German. It follows the Windows display language
  (German Windows → German, otherwise English) and can be switched under *Settings → Language* or on the career
  selection screen. Mission types have names and descriptions in both languages.

### Changed
- README and changelog are now in English. Ledger entries and flight notes keep the language they were created in, so
  older entries stay German.
- Log files are written in English.
- **Exact zero fuel weight in SimBrief:** "Plan in SimBrief" now also sends the load sheet's zero fuel weight. SimBrief
  keeps the passenger count and plans exactly this weight, so fuel planning is right whatever empty weight, crew or bag
  weight your SimBrief airframe has; it adjusts the freight in the OFP to match. "Load OFP" then only checks passengers,
  bags and the ZFW. The crew is no longer sent as passengers, so GSX doesn't board it.
- *Send detailed load to SimBrief* is now called *Varying bag weights per passenger*: the SimBrief airframe no longer
  needs a bag weight of 0.

### Fixed
- The loading screen ("Running pre-flight checks …") is shown in the app's language.

## 0.1.5 – 2026-10-31

### Fixed
- Aircraft that don't report the parking brake to the simulator (e.g. Fenix A320/A321) were never detected as parked at
  the destination and never settled. An aircraft now also counts as parked after 30 seconds stationary with the engines
  shut down; with the parking brake set, still after 5 seconds.

## 0.1.4 – 2026-10-31

### Added
- **Round trips:** scheduled flights, charter, VIP charter and air taxi sometimes come as an outbound and return package
  ("Round trip" chip in the offer list). Both legs are accepted together, and the return leg starts automatically once the
  aircraft is at the destination. Flying both earns a bonus (10–15 % on both payouts). Cancelling the outbound cancels
  both. The return leg's card stays collapsed until it is due.

### Changed
- **Well-booked flights:** on scheduled and charter flights most seats are taken (85–90 % on average), slightly fewer on
  VIP charter and air taxi; there are no more empty or always-full flights. The list shows the load factor.
- **Pay by load factor:** part of the pay depends on how full the cabin is; scheduled and charter flights also pay an
  amount per passenger and distance. Half-empty flights pay noticeably less.
- **Clearer preparation:** the steps below a job are numbered and in order (plan in SimBrief → load OFP → load in sim →
  verify load); completed steps get a check mark and the next one is highlighted.

## 0.1.3 – 2026-10-30

### Added
- **Fuel stays in the aircraft** (*Settings → Loading*, on by default): the remaining fuel is stored after the flight,
  shown in the fleet and set in the simulator when the next job is armed. You then only pay for the fuel uplifted instead
  of the fuel burnt.
- **Aircraft profiles from SimBrief types:** profiles can be created completely from SimBrief's type list (even without the
  simulator running), or completed with SimBrief's exact weight limits and performance data when created from the sim.
- **Wear from MSFS 2024:** the wear the simulator reports during a flight lowers the condition of the aircraft and its
  components (landing gear, tires, brakes, engine, flight controls …). The fleet shows the components; components below
  70 % appear yellow in the departure check and raise the maintenance reserve and repair costs. A repair in SkyCareer does
  not reset the wear in the simulator (MSFS doesn't allow that).
- **Aircraft detail page** (click the registration in the fleet): values, components with repairs of single components or
  whole groups (tires and brakes 2 h in the workshop, engine 1 day, otherwise 6 h), economics by cost type, all flights of
  the aircraft (with a link to the logbook) and open jobs.
- **Debug:** test page for the MSFS 2024 wear and dirt values (read and test-write).
- **Settlement after GSX deboarding:** with GSX in use, the settlement waits after shutdown until GSX has deboarded the
  passengers; the GSX receipts are then part of the settlement. If no deboarding starts, the flight is settled after
  20 minutes, immediately when an engine is started, or via "Settle now" on the live flight page.

### Changed
- **Rule violations no longer void a flight.** Instead each kind costs part of the pay (slew, position jump, aircraft
  change 20 %, in-flight refuelling 15 %, payload change 10 %, at most 20 % in total), one reputation point and score.
  Time acceleration is only noted and costs nothing. The live flight page shows the violations with the expected deduction.

### Fixed
- After an app restart in flight, the GSX sync emptied the cabin, which counted as a payload change and voided the flight.
  Payload is now never written in flight; after a restart GSX only follows deboarding.
- If the app was closed for a while, weight changes and a landing during that time no longer count as a violation or a
  wrong landing rate but are noted in the settlement. Unloading while taxiing to the stand is no longer a violation.
- A late GSX deboarding receipt is no longer assigned to the next job from the same airport.
- Flights resumed without a flight recording calculate fuel correctly again.
- Wear: if the start value is missing (restart, read error), it is taken later in flight; the settlement notes this.

## 0.1.2 – 2026-10-29

### Added
- **Send detailed load to SimBrief** (optional, *Settings → Loading*): sends the crew as passengers and baggage plus weight
  differences as cargo, so the ZFW in the OFP matches the load sheet exactly. Bags then weigh differently per passenger.
  Requires a bag weight of 0 in the SimBrief airframe. Without the option everything stays as before, with SimBrief's
  standard weights.

### Fixed
- After a flight, offers from the previous airport stayed on the job market and could be accepted. They are now removed,
  also in existing saves.

## 0.1.1 – 2026-10-29

### Added
- **GSX bills as costs:** with *Use GSX receipts as costs* (Settings → Integrations), SkyCareer books the bills GSX Pro
  issues for handling, buses, catering and fuel.
  - Amounts are converted to euros with the exchange rates GSX itself uses.
  - The model costs for fuel, handling and catering are then dropped at settlement.
  - Fuel uplifted without GSX is free.
  - Only receipts from the moment the option is switched on are counted.
- **View bills:** GSX bookings show a receipt icon in *Finances*. In the *Logbook* the receipts of a flight can be expanded.
  The bill opens in the browser, where it can be printed or saved as PDF.
- **Debug mode – set flight phase:** on the *Live flight* page the flight phase can be set by hand and optionally frozen to
  test flows without a real flight. Manual phase changes are noted in the settlement.

### Changed
- README revised.

## 0.1.0 – 2026-10-28

First public test version.

- Career with fleet, job market, finances, logbook and flight-based game time.
- Automatic flight tracking via SimConnect: departure check, start at liftoff, settlement after parking.
- Integrations: SimBrief (planning and OFP check), GSX Pro (boarding in sync), BeyondATC (flight plan export).
- Error log, "Copy diagnostics" and resuming a flight after an app restart.
- Automatic updates via GitHub Releases.

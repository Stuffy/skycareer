# SkyCareer

Karriere-Addon für Microsoft Flight Simulator 2024 als eigenständige Windows-App (.NET 10, WPF + Blazor Hybrid, MudBlazor).

## Bauen & Testen

```
dotnet build SkyCareer.sln
dotnet test SkyCareer.sln
```

SimConnect wird nur einkompiliert, wenn das MSFS 2024 SDK gefunden wird – über die Umgebungsvariable `MSFS2024_SDK`,
den Standardpfad `C:\MSFS 2024 SDK` oder `dotnet build -p:SimConnectSdkDir="…\SimConnect SDK"`. Ohne SDK läuft die App im Replay-Modus.

Schemaänderungen: `dotnet ef migrations add <Name> --project src/SkyCareer.Data -o Migrations` (Tool via `dotnet tool restore`).
Die App migriert die Datenbank beim Start automatisch.

## Spielablauf

1. **Start**: Beim Öffnen erscheint die Karriereauswahl – bestehende Karriere fortsetzen oder neue mit Heimatbasis anlegen
   (beim ersten Mal vorher die Flughafendaten von OurAirports importieren). Kontostand, Reputation und Spielzeit stehen danach
   in der Titelleiste; dort lässt sich auch Zeit vergehen lassen und die Karriere wechseln.
2. **Flotte**: Flugzeug kaufen, leasen oder mieten – es steht an der Heimatbasis.
3. **Aufträge**: Markt aktualisieren, Auftrag annehmen, optional SimBrief-OFP laden, im Sim beladen. Steht das Flugzeug am
   Abflughafen, verfolgt die App den Auftrag automatisch; die Abflug-Ampel zeigt, ob Typ, Position, Gewichte und Treibstoff passen.
4. Fliegen: Der Auftrag startet beim Abheben und wird nach dem Abstellen am Ziel (Triebwerke aus, Parkbremse) automatisch
   abgerechnet – Erlöse, Kosten, Score, Reputation, Verschleiß.

## Installation & Updates

Die fertige App gibt es unter **Releases** auf GitHub: `SkyCareerApp-win-Setup.exe` herunterladen und starten (fehlende
.NET-Desktop-Runtime und WebView2 werden mitinstalliert). Da der Installer nicht signiert ist, meldet Windows beim ersten
Start „Der Computer wurde durch Windows geschützt“ → „Weitere Informationen“ → „Trotzdem ausführen“.

Die App sucht beim Start und alle 6 Stunden nach neuen Versionen, lädt sie im Hintergrund und bietet „Neu starten“ an
(nie während eines Flugs; sonst wird das Update beim Beenden installiert). Karrieren (`%LOCALAPPDATA%\SkyCareer`) und eigene
Inhalte (`Dokumente\SkyCareer`) bleiben bei Updates und bei der Deinstallation erhalten.

### Release veröffentlichen

Voraussetzungen: MSFS 2024 SDK (für SimConnect), ein GitHub-Token mit „Contents: Read and write“ für dieses Repo in der
Umgebungsvariable `SKYCAREER_GH_TOKEN`, alles committet.

```
tools/release.ps1 -Version 0.2.0 -Notes notes.md
```

Das Skript testet, baut, packt mit [Velopack](https://velopack.io) (inkl. Delta-Update gegen das letzte Release) und
lädt das Release hoch. `-PackOnly` baut nur den Installer lokal unter `releases/`.

`SimConnect.dll` stammt aus dem MSFS SDK und wird nur im Release mitgeliefert, nicht im Repository.

## Eigene Flugzeugprofile

Seite „Flugzeugprofile“: Ist im Sim ein unbekanntes Flugzeug geladen, legt „Aus aktuellem Sim-Flugzeug“ ein Profil an –
Leergewicht, MTOW, Tanks, Payload-Stationen, Triebwerkstyp und Reisegeschwindigkeit kommen aus dem Sim, Verbrauch,
Frachtkapazität und Preise werden aus dem ähnlichsten bekannten Profil (nach MTOW skaliert) geschätzt. MZFW und MLW meldet
der Sim nicht und müssen nachgetragen werden. Der Editor prüft das Profil live und zeigt, welche Missionen möglich sind.

Eigene Profile liegen unter `Dokumente\SkyCareer\content\aircraft` und überschreiben mitgelieferte Profile mit gleicher ID.

## GSX & BeyondATC

- **GSX Pro** (optional): Bei verbundenem Sim zeigt die Auftragskarte die GSX-Dienste (Boarding, Betankung, Pushback, Deboarding).
  Mit „Beladung mit GSX synchronisieren“ füllt die App Kabine und Frachträume im Takt des GSX-Boardings, setzt den Block Fuel nach der
  GSX-Betankung (bzw. am Ende des Boardings) und leert das Flugzeug beim Deboarding. Geschrieben wird nur vor dem Block-Off und nach dem Abstellen.
  Die Passagierzahl übernimmt GSX aus SimBrief („Trust SimBrief“) – weicht sie vom Auftrag ab, zeigt die App eine Warnung.
- **BeyondATC** hat keine öffentliche API. „Flugplan (.pln)“ speichert einen MSFS-Flugplan unter `Dokumente\SkyCareer\Flugplaene`,
  den BeyondATC (VFR › Datei-Import) und das EFB laden. Für IFR-Flüge nutzt BeyondATC direkt den letzten SimBrief-OFP.

## Beladungs-Spike

Seite „Beladungs-Spike“ in der App: Flugzeug am Boden laden, mit MSFS verbinden, dann

1. **Auslesen** – zeigt Payload-Stationen, Tanks und Gewichte, wie der Sim sie meldet; „Profil-Entwurf erzeugen“ liefert ein JSON für `content/aircraft/`.
2. **Berechnen → In den Sim schreiben & prüfen** – schreibt die Stationen und den Treibstoff und vergleicht danach Soll/Ist pro Station.
3. **Variable lesen** – liest SimVars/L:Vars. Unbekannte L:Vars liefern 0, nicht einen Fehler.

## Struktur

| Projekt | Inhalt |
|---|---|
| `SkyCareer.Domain` | Einheiten (Mass, Money, GeoPosition), Flugzeugprofile, Load-Sheet, Treibstoffplanung, Beladungsprüfung |
| `SkyCareer.Sim` | `IFlightDataSource`, SimConnect-Quelle, Replay, Flugphasen-Erkennung, Integritätsprüfung |
| `SkyCareer.Missions` | Missionstypen (JSON), Requirement-Evaluator, Missionsgeneratoren, Content-Loader |
| `SkyCareer.Integrations.SimBrief` | Dispatch-Link, OFP-Import, OFP-Abgleich |
| `SkyCareer.Data` | EF Core / SQLite (`%LOCALAPPDATA%\SkyCareer\skycareer.db`), Migrationen, OurAirports-Import |
| `SkyCareer.Application` | `CareerService`: Karriere, Flotte (Kauf/Leasing/Miete), Missionsmarkt, Flugabrechnung, Spielzeit |
| `SkyCareer.App` | Desktop-Oberfläche |
| `content/` | Flugzeugprofile und Missionstypen – neue Inhalte = neue JSON-Datei |

Die Werte in `content/aircraft/*.json` (Gewichte, Payload-Stationen) sind Näherungen und müssen im Simulator geprüft werden.

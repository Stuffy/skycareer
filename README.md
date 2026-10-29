# SkyCareer

**Karrieremodus für Microsoft Flight Simulator 2024** – eine eigenständige Windows-Anwendung, die jeden Flug in eine
wirtschaftliche Karriere einbettet: Aufträge annehmen, Flotte aufbauen, Kosten im Griff behalten und Reputation sammeln.

> **Status:** frühe Testversion (0.x). Rückmeldungen und Fehlerberichte sind ausdrücklich willkommen.

## Inhalt

- [Funktionen](#funktionen)
- [Voraussetzungen](#voraussetzungen)
- [Installation und Updates](#installation-und-updates)
- [Erste Schritte](#erste-schritte)
- [Integrationen](#integrationen)
- [Eigene Flugzeugprofile](#eigene-flugzeugprofile)
- [Datenablage](#datenablage)
- [Fehler melden](#fehler-melden)
- [Entwicklung](#entwicklung)
- [Lizenz und Drittanbieter](#lizenz-und-drittanbieter)

## Funktionen

- **Auftragsmarkt** – Linien-, Charter-, VIP-, Lufttaxi-, Fracht-, Ambulanz- und Buschflüge, passend zu Flugzeug, Reichweite und
  Pistenanforderungen. Großstädte und Hauptstädte werden als Ziele bevorzugt; Militärflugplätze stehen nur Militärflugzeugen offen.
- **Flotte und Finanzen** – Flugzeuge kaufen, leasen oder mieten; Treibstoff, Gebühren, Crew, Wartung und Versicherung
  werden realistisch verbucht. Flugzeuge lassen sich per Überführungsflug an andere Standorte verlegen.
- **Load-Sheet und Beladung** – Passagiere, Gepäck, Fracht und Treibstoff werden berechnet, gegen MZFW/MTOW/MLW geprüft
  und auf Wunsch direkt in den Simulator geschrieben.
- **Automatischer Flugablauf** – kein Startknopf: Steht das Flugzeug am Abflughafen, wird der Auftrag verfolgt. Eine
  Abflug-Ampel prüft Typ, Position, Zuladung, Treibstoff, Gewichtsgrenzen und OFP. Der Auftrag beginnt beim Abheben und
  wird nach dem Abstellen am Ziel automatisch abgerechnet.
- **Bewertung und Integrität** – Landung, Pünktlichkeit und Flugverhalten fließen in Score und Reputation ein. Slew,
  Zeitraffer, Betanken in der Luft oder Positionssprünge werden erkannt.
- **Absturzsicher** – laufende Flüge werden mitgeschrieben und nach einem Neustart der App automatisch fortgesetzt.
- **Integrationen** – SimBrief (Planung und OFP-Abgleich), GSX Pro (synchrones Boarding, GSX-Rechnungen als Kosten) und BeyondATC (Flugplan-Export).

## Voraussetzungen

- Windows 10 (19041) oder Windows 11, 64 Bit
- Microsoft Flight Simulator 2024
- Internetzugang für den einmaligen Import der Flughafendaten, die Kartenansicht und Updates
- Optional: ein SimBrief-Konto, GSX Pro, BeyondATC

Die .NET-Desktop-Runtime und die WebView2-Runtime installiert der Installer bei Bedarf automatisch.

## Installation und Updates

1. Unter [Releases](https://github.com/Stuffy/skycareer/releases/latest) die Datei `SkyCareerApp-win-Setup.exe` herunterladen und ausführen.
2. Der Installer ist nicht code-signiert. Windows SmartScreen meldet daher beim ersten Start „Der Computer wurde durch
   Windows geschützt“ – über **Weitere Informationen → Trotzdem ausführen** lässt sich die Installation fortsetzen.
3. SkyCareer wird für den aktuellen Benutzer installiert (keine Administratorrechte nötig) und legt Verknüpfungen auf dem
   Desktop und im Startmenü an.

**Updates** werden beim Start und danach alle sechs Stunden geprüft und im Hintergrund geladen. Ein Hinweis bietet den
Neustart an; während eines Flugs wird nie neu gestartet – das Update wird dann beim Beenden der App installiert.
Karrieren und eigene Inhalte bleiben bei Updates und bei einer Deinstallation erhalten.

## Erste Schritte

1. **Flughafendaten importieren** – beim ersten Start einmalig über „Herunterladen & importieren“ (OurAirports und GeoNames, ca. 18 MB).
2. **Karriere anlegen** – Pilotenname, Airline, Heimatbasis und Schwierigkeitsgrad wählen; optional den SimBrief-Benutzernamen eintragen.
3. **Flugzeug beschaffen** – unter *Flotte* kaufen, leasen oder mieten. Das Flugzeug steht an der Heimatbasis.
4. **Auftrag annehmen** – unter *Aufträge* den Markt durchsuchen (Filter nach Flugzeug, Missionstyp, Distanz, Zuladung u. a.)
   und einen Auftrag annehmen.
5. **Flug vorbereiten** – im Simulator am Abflughafen mit dem passenden Flugzeug laden. Planung in SimBrief, OFP-Import,
   Beladung und Flugplan-Export sind bis zum Abheben möglich; die Abflug-Ampel zeigt den Stand.
6. **Fliegen** – der Auftrag startet beim Abheben. Nach dem Abstellen am Ziel (Parkbremse gesetzt, Triebwerke aus) folgt
   die Abrechnung mit Erlösen, Kosten, Score, Reputation und Verschleiß.

Spielzeit vergeht nur durch Flüge und bewusstes Warten (Titelleiste) – Pausen außerhalb des Simulators kosten nichts.

## Integrationen

### SimBrief
„In SimBrief planen“ öffnet die Dispatch-Seite mit Strecke, Passagieren und Fracht. SimBrief übernimmt Passagier- und
Gepäckgewicht aus dem gewählten Airframe; damit Auftrag und OFP übereinstimmen, sollten unter **Einstellungen → Beladung**
dieselben Werte eingetragen sein (Standard: 175 lbs je Passagier, 50 lbs je Gepäckstück). „OFP laden“ übernimmt Block Fuel
und Route und meldet Abweichungen bei Strecke, Typ, Passagieren, Fracht und Gewichten. Für eigene Airframes kann im
Flugzeugprofil die SimBrief-Airframe-ID hinterlegt werden.

### GSX Pro
Bei verbundenem Simulator zeigt die Auftragskarte den Status der GSX-Dienste (Boarding, Betankung, Pushback, Deboarding).
Mit **Beladung mit GSX synchronisieren** füllt SkyCareer Kabine und Frachträume im Takt des Boardings, setzt den Block Fuel
nach der Betankung und leert das Flugzeug beim Deboarding. Geschrieben wird ausschließlich vor dem Block-Off und nach dem
Abstellen. Die Passagierzahl übernimmt GSX aus SimBrief; Abweichungen zum Auftrag werden angezeigt.

Mit **GSX-Belege als Kosten verwenden** bucht SkyCareer die Rechnungen, die GSX für Abfertigung, Busse, Catering und
Betankung ausstellt, mit ihrem Betrag (in Euro umgerechnet) ins Kassenbuch. Die Modellkosten für Treibstoff, Abfertigung und
Catering entfallen dann bei der Abrechnung; ohne GSX getankter Treibstoff ist kostenlos. Die Rechnungen lassen sich in
Finanzen und im Flugbuch öffnen und von dort drucken oder als PDF speichern.

### BeyondATC
BeyondATC bietet keine öffentliche Schnittstelle. „Flugplan (.pln)“ speichert einen MSFS-Flugplan unter
`Dokumente\SkyCareer\Flugplaene`, den BeyondATC (VFR › Datei-Import) und das EFB laden können. Für IFR-Flüge verwendet
BeyondATC direkt den aktuellen SimBrief-OFP.

## Eigene Flugzeugprofile

Mitgeliefert werden Profile für Cessna 172, Cessna 208B, Airbus H145, King Air 350 und Airbus A320neo. Weitere Flugzeuge
lassen sich unter **Inhalte → Flugzeugprofile → Aus aktuellem Sim-Flugzeug** anlegen:

- Leergewicht, MTOW, Tanks, Payload-Stationen, Triebwerkstyp und Reisegeschwindigkeit werden aus dem Simulator gelesen.
- Verbrauch, Frachtkapazität und Preise werden aus dem ähnlichsten bekannten Profil abgeleitet; MZFW und MLW geschätzt.
  Diese Werte sollten mit Handbuch oder SimBrief abgeglichen werden.
- Der Editor prüft das Profil laufend und zeigt, welche Missionstypen damit möglich sind.

Eigene Profile liegen unter `Dokumente\SkyCareer\content\aircraft` und ersetzen mitgelieferte Profile gleicher ID. Die
mitgelieferten Werte sind Näherungen; die Stationsbelegung lässt sich im Profil-Editor über „Stationen aus Sim übernehmen“ prüfen.

## Datenablage

| Pfad | Inhalt |
|---|---|
| `%LOCALAPPDATA%\SkyCareer\skycareer.db` | Karrieren, Flotten, Aufträge, Kassenbuch und Flughafendaten |
| `%LOCALAPPDATA%\SkyCareer\tracks` | Aufzeichnungen abgeschlossener und laufender Flüge (JSON Lines) |
| `%LOCALAPPDATA%\SkyCareer\logs` | Fehlerprotokolle (14 Tage) |
| `Dokumente\SkyCareer\content` | Eigene Flugzeugprofile und Missionstypen |
| `Dokumente\SkyCareer\Flugplaene` | Exportierte Flugpläne (`.pln`) |

Die Programmdateien selbst liegen unter `%LOCALAPPDATA%\SkyCareerApp`.

## Fehler melden

Fehler und Vorschläge bitte als [Issue](https://github.com/Stuffy/skycareer/issues) einreichen. Unter
**Einstellungen → Erweitert → Diagnose kopieren** legt SkyCareer Version, Simulator-Status und die letzten
Protokollzeilen in die Zwischenablage – diesen Text bitte jeder Meldung beifügen, zusammen mit einer kurzen Beschreibung
der Schritte bis zum Fehler.

Der **Debug-Modus** (Einstellungen → Erweitert) schaltet Entwickler- und Testwerkzeuge frei, u. a. das Setzen von Kontostand
und Reputation. Er ist für Tests gedacht; Korrekturen am Kontostand werden im Kassenbuch vermerkt.

## Entwicklung

### Bauen und testen

Voraussetzung ist das .NET 10 SDK.

```bash
dotnet tool restore
dotnet build SkyCareer.sln
dotnet test SkyCareer.sln
dotnet run --project src/SkyCareer.App
```

Die SimConnect-Anbindung wird nur kompiliert, wenn das **MSFS 2024 SDK** gefunden wird – über die Umgebungsvariable
`MSFS2024_SDK`, den Standardpfad `C:\MSFS 2024 SDK` oder `-p:SimConnectSdkDir="…\SimConnect SDK"`. Ohne SDK lässt sich
das Projekt vollständig bauen und testen; die Anwendung arbeitet dann nur mit aufgezeichneten Flügen (Replay).

Mit der Umgebungsvariable `SKYCAREER_DB` lässt sich eine separate Datenbank verwenden, etwa zum Testen ohne Einfluss auf
bestehende Karrieren. Schemaänderungen erfolgen über EF-Core-Migrationen:

```bash
dotnet ef migrations add <Name> --project src/SkyCareer.Data -o Migrations
```

Die Anwendung migriert die Datenbank beim Start automatisch.

### Projektstruktur

| Projekt | Verantwortung |
|---|---|
| `SkyCareer.Domain` | Einheiten, Flugzeugprofile, Load-Sheet, Treibstoffplanung, Kostenmodell, Karriere-Entitäten |
| `SkyCareer.Sim` | Datenquellen (SimConnect, Replay), Flugphasenerkennung, Integritätsprüfung, Flugaufzeichnung |
| `SkyCareer.Missions` | Datengetriebene Missionstypen, Auftragsgenerierung, Abflug-Check, Abrechnung |
| `SkyCareer.Data` | EF Core mit SQLite, Migrationen, Import von OurAirports und GeoNames |
| `SkyCareer.Application` | `CareerService` (einzige Stelle für Zustandsänderungen), Flugautomatik, Protokollierung |
| `SkyCareer.Integrations.*` | SimBrief, GSX, BeyondATC |
| `SkyCareer.App` | WPF-Host mit Blazor-Hybrid-Oberfläche (MudBlazor), Updater |
| `content/` | Mitgelieferte Flugzeugprofile und Missionstypen als JSON |

Weitere Architektur- und Implementierungsdetails stehen in [`CLAUDE.md`](CLAUDE.md).

### Releases

Releases werden mit [Velopack](https://velopack.io) über GitHub Releases verteilt; installierte Anwendungen erhalten
Delta-Updates. Voraussetzungen: MSFS 2024 SDK, ein Fine-grained Token mit *Contents: Read and write* für dieses Repository
in `SKYCAREER_GH_TOKEN` und ein sauberer Git-Stand.

```bash
powershell -File tools/release.ps1 -Version 0.2.0 -Notes notes.md
```

Das Skript führt die Tests aus, veröffentlicht den Build, prüft auf `SimConnect.dll` und mitgelieferte Inhalte, erstellt
Installer und Update-Pakete und lädt sie hoch. Mit `-PackOnly` wird nur lokal unter `releases/` gepackt.

## Lizenz und Drittanbieter

Der Quellcode steht unter der [MIT-Lizenz](LICENSE). Davon ausgenommen sind:

- `SimConnect.dll` – © Microsoft, Bestandteil des MSFS 2024 SDK, nur in den Release-Paketen enthalten
- Flughafendaten von [OurAirports](https://ourairports.com/data/) (Public Domain)
- Städtedaten von [GeoNames](https://www.geonames.org/) ([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/))
- Kartendarstellung mit [Leaflet](https://leafletjs.com/); Kartenkacheln von Esri, [OpenStreetMap](https://www.openstreetmap.org/copyright)-Mitwirkenden und OpenTopoMap (Quellenangabe in der Karte)

SkyCareer ist ein unabhängiges Projekt und steht in keiner Verbindung zu Microsoft, Asobo Studio, Navigraph/SimBrief,
FSDreamTeam oder BeyondATC.

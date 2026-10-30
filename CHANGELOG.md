# Changelog

Alle nennenswerten Änderungen an SkyCareer. Versionen folgen [Semantic Versioning](https://semver.org/lang/de/).

## Unveröffentlicht

### Neu
- **Tankstand bleibt im Flugzeug** (*Einstellungen → Beladung*, standardmäßig an): Der Resttreibstoff wird nach dem Flug
  gespeichert, in der Flotte angezeigt und beim Scharfschalten des nächsten Auftrags im Simulator gesetzt. Bezahlt wird
  dann nur der nachgetankte Treibstoff statt des verbrauchten.
- **Flugzeugprofile aus SimBrief-Typen:** Profile lassen sich komplett aus der SimBrief-Typenliste anlegen (auch ohne
  laufenden Simulator) oder beim Anlegen aus dem Sim mit den genauen Gewichtsgrenzen und Leistungsdaten von SimBrief ergänzen.
- **Abnutzung aus MSFS 2024:** Die Abnutzung, die der Simulator während eines Flugs meldet, senkt den Zustand des
  Flugzeugs und seiner Bauteile (Fahrwerk, Reifen, Bremsen, Triebwerk, Steuerflächen …). Die Flotte zeigt die Bauteile,
  Bauteile unter 70 % erscheinen gelb in der Abflugprüfung und erhöhen Wartungsrücklage und Reparaturkosten. Eine Reparatur
  in SkyCareer setzt die Abnutzung im Simulator nicht zurück (das lässt MSFS nicht zu).
- **Detailseite für Flugzeuge** (Klick auf das Kennzeichen in der Flotte): Werte, Bauteile mit Reparatur einzelner
  Bauteile oder ganzer Baugruppen (Reifen und Bremsen 2 h Werkstatt, Triebwerk 1 Tag, sonst 6 h), Wirtschaftlichkeit nach
  Kostenarten, alle Flüge des Flugzeugs (mit Sprung ins Flugbuch) und offene Aufträge.
- **Debug:** Testseite für die Abnutzungs- und Schmutzwerte von MSFS 2024 (lesen und testweise schreiben).

## 0.1.2 – 2026-10-29

### Neu
- **Genaue Beladung an SimBrief** (optional, *Einstellungen → Beladung*): übergibt die Crew als Passagiere und Gepäck
  sowie Gewichtsunterschiede als Fracht, sodass das ZFW im OFP genau dem Load-Sheet entspricht. Gepäck wiegt dann je
  Passagier unterschiedlich. Voraussetzung: im SimBrief-Flugzeug das Gepäckgewicht (Bag Weight) auf 0 stellen.
  Ohne die Option bleibt alles wie bisher, mit den Standardgewichten von SimBrief.

### Behoben
- Nach einem Flug blieben Angebote vom alten Flughafen im Auftragsmarkt stehen und ließen sich annehmen.
  Sie werden jetzt entfernt, auch in bestehenden Spielständen.

## 0.1.1 – 2026-10-29

### Neu
- **GSX-Rechnungen als Kosten:** Mit der Option *GSX-Belege als Kosten verwenden* (Einstellungen → Integrationen)
  bucht SkyCareer die Rechnungen, die GSX Pro für Abfertigung, Busse, Catering und Betankung ausstellt.
  - Die Beträge werden in Euro umgerechnet, mit den Wechselkursen, die GSX selbst verwendet.
  - Die Modellkosten für Treibstoff, Abfertigung und Catering entfallen dann bei der Abrechnung.
  - Treibstoff, der ohne GSX getankt wird, ist kostenlos.
  - Gezählt werden nur Belege ab dem Einschalten.
- **Rechnungen ansehen:** GSX-Buchungen tragen in *Finanzen* ein Beleg-Symbol. Im *Flugbuch* lassen sich die Belege eines Flugs aufklappen. Die Rechnung öffnet sich im Browser und lässt sich dort drucken oder als PDF speichern.
- **Debug-Modus – Flugphase setzen:** Auf der Seite *Live-Flug* lässt sich die Flugphase von Hand setzen und auf Wunsch
  einfrieren, um Abläufe ohne echten Flug zu testen. Manuelle Phasenwechsel werden in der Abrechnung vermerkt.

### Geändert
- README überarbeitet.

## 0.1.0 – 2026-10-28

Erste öffentliche Testversion.

- Karriere mit Flotte, Auftragsmarkt, Finanzen, Flugbuch und flugbasierter Spielzeit.
- Automatische Flugverfolgung über SimConnect: Abflug-Check, Start beim Abheben, Abrechnung nach dem Parken.
- Integrationen: SimBrief (Planung und OFP-Abgleich), GSX Pro (synchrones Boarding), BeyondATC (Flugplan-Export).
- Fehlerprotokoll, „Diagnose kopieren“ und Fortsetzen eines Flugs nach einem Neustart der App.
- Automatische Updates über GitHub Releases.

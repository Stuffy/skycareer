# Changelog

Alle nennenswerten Änderungen an SkyCareer. Versionen folgen [Semantic Versioning](https://semver.org/lang/de/).

## Unveröffentlicht

### Neu
- **Hin- und Rückflüge:** Linienflüge, Charter, VIP-Charter und Lufttaxi kommen teils als Paket aus Hin- und Rückflug
  (Chip „Hin & Rück“ in der Angebotsliste). Beide Strecken werden zusammen angenommen, der Rückflug startet automatisch,
  sobald das Flugzeug am Ziel steht. Wer beide fliegt, bekommt einen Bonus (10–15 % auf beide Vergütungen). Wer den
  Hinflug storniert, storniert beide.

### Geändert
- **Gut gebuchte Flüge:** Bei Linien- und Charterflügen sind die meisten Plätze belegt (im Mittel 85–90 %), bei VIP-Charter
  und Lufttaxi etwas weniger; ganz leere oder immer volle Flüge gibt es nicht mehr. Die Liste zeigt die Auslastung.
- **Vergütung nach Auslastung:** Ein Teil der Vergütung hängt davon ab, wie voll die Kabine ist, dazu kommt bei Linie und
  Charter ein Betrag pro Passagier und Strecke. Halbvolle Flüge zahlen spürbar weniger.
- **Klarere Vorbereitung:** Die Schritte unter einem Auftrag sind nummeriert und in Reihenfolge (In SimBrief planen →
  OFP laden → Im Sim beladen → Beladung prüfen); erledigte Schritte bekommen einen Haken, der nächste ist hervorgehoben.

## 0.1.3 – 2026-10-30

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
- **Abrechnung nach dem GSX-Deboarding:** Wird GSX genutzt, wartet die Abrechnung nach dem Parken, bis GSX die Passagiere
  ausgeladen hat; die GSX-Belege sind dann Teil der Abrechnung. Startet kein Deboarding, wird nach 20 Minuten abgerechnet,
  ebenso sofort beim Anlassen eines Triebwerks oder über „Jetzt abrechnen“ im Live-Flug.

### Geändert
- **Regelverstöße annullieren keinen Flug mehr.** Stattdessen kostet jede Art einen Teil der Vergütung (Slew, Positionssprung,
  Flugzeugwechsel 20 %, Tanken im Flug 15 %, Ladungsänderung 10 %, zusammen höchstens 20 %), einen Rufpunkt und Score.
  Zeitbeschleunigung wird nur noch vermerkt und kostet nichts mehr. Der Live-Flug zeigt die Verstöße mit dem erwarteten Abzug.

### Behoben
- Nach einem App-Neustart im Flug hat die GSX-Synchronisierung die Kabine geleert, was als Ladungsänderung gewertet und der
  Flug annulliert wurde. Im Flug wird jetzt nie mehr Zuladung geschrieben; nach dem Neustart folgt GSX nur noch dem Deboarding.
- War die App länger geschlossen, werden Gewichtsänderungen und eine Landung in dieser Zeit nicht mehr als Verstoß bzw.
  falsche Landerate gewertet, sondern in der Abrechnung vermerkt. Ausladen beim Rollen zum Stand ist kein Verstoß mehr.
- Ein verspäteter GSX-Deboarding-Beleg wird nicht mehr dem nächsten Auftrag ab demselben Flughafen zugeordnet.
- Wiederaufgenommene Flüge ohne Flugaufzeichnung berechnen den Treibstoff wieder richtig.
- Abnutzung: Fehlt der Startwert (Neustart, Lesefehler), wird er im Flug nachgeholt; die Abrechnung vermerkt das.

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

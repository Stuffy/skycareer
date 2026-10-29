# Changelog

Alle nennenswerten Änderungen an SkyCareer. Versionen folgen [Semantic Versioning](https://semver.org/lang/de/).

## Unveröffentlicht

### Geändert
- **SimBrief:** Die Crew wird als Passagiere übergeben, Gepäck und Gewichtsunterschiede der Crew als Fracht. Dadurch
  stimmt das ZFW im OFP mit dem Load-Sheet überein. **Im SimBrief-Airframe das Gepäckgewicht auf 0 stellen.**
- **Gepäck:** Aufgegebenes Gepäck wiegt je Passagier unterschiedlich viel. Der Wert unter *Einstellungen → Beladung* ist
  jetzt der Durchschnitt, manche Passagiere reisen ohne aufgegebenes Gepäck.

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

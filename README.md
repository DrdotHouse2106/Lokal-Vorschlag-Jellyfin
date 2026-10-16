# Jellyfin Lokale Empfehlungen (Plugin) – Jellyfin 12

[![Lizenz: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml/badge.svg)](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml)

Datenschutzfreundliche, personalisierte Empfehlungen für Jellyfin – ausschließlich auf Basis des lokalen Wiedergabeverlaufs und der Metadaten-Ähnlichkeit. Keine Cloud-Dienste, kein Tracking. Funktioniert auf allen Jellyfin-Clients (auch auf Fernsehern).

> [!NOTE]
> **Vorlage / Original:** Dieses Plugin basiert auf dem originalen Projekt **[rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)** von Roger Pharr.
> Dieses Repository ist eine an **Jellyfin 12** angepasste Version davon, da das Original für Jellyfin 12 nicht mehr aktualisiert wird. Alle Rechte und der Dank für die eigentliche Entwicklung gehen an den ursprünglichen Autor.
>
> English version: [README.en.md](README.en.md)

> [!IMPORTANT]
> Ab Version **12.2.0** erscheinen die Empfehlungen als **eigene Zeilen auf der Startseite** (über das Plugin „Home Screen Sections“) statt als eigene Bibliotheken. Die Empfehlungs-Bibliotheken älterer Versionen werden beim ersten Start automatisch entfernt. **Native TV-Apps** (Android TV, Fire TV, Roku) zeigen diese Zeilen nicht an.

## Funktionen

- **Personalisierung pro Benutzer** – Empfehlungen passend zum eigenen Wiedergabeverlauf
- **Inhaltsbasierte Filterung** – TF-IDF-Embeddings mit Kosinus-Ähnlichkeit
- **Zeitliche Ähnlichkeit** – Gruppierung nach Jahrzehnten
- **Zeilen auf der Startseite** – „Empfohlene Filme für dich“ und „Empfohlene Serien für dich“, für jeden Benutzer eigene
- **Echte Einträge** – es werden die Filme und Serien aus deinen Bibliotheken gezeigt: alle Infos, gemeinsamer „Gesehen“-Status, keine Duplikate bei „Weiterschauen“, keine zusätzlichen Bibliotheken
- **Live-TV** – das Plugin schreibt mit, welche Sendungen ihr live schaut (über alle Apps, auch TV-Apps), und zeigt die Zeile **„Heute im TV für dich“** mit passenden Sendungen aus dem Programm (EPG)
- **Datenschutz** – alle Berechnungen laufen lokal auf deinem Server

## Voraussetzungen

- **Jellyfin-Server:** 12.0 oder neuer (getestet mit 12.2)
- **.NET-Runtime:** 10.0 (ist in Jellyfin 12 enthalten)
- **Target ABI:** 12.0.0.0
- **Für die Startseite:** die Plugins **Home Screen Sections**, **File Transformation** und **Plugin Pages** von IAmParadox27 (Repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`)
- **Clients:** Browser, Jellyfin-Desktop-App und Handy-Apps. Native TV-Apps (Android TV, Fire TV, Roku) zeigen die Zeilen nicht.

Für Jellyfin 10.11.x bitte die Original-Version 0.6.1 verwenden.

**Versionsnummer:** Die Versionen dieses Forks sind an Jellyfin angelehnt (12.x). Jellyfin prüft nur, ob ein Plugin eine *mindestens* passende Server-Version verlangt. Durch die höhere Versionsnummer werden 10.11-Versionen des Originals auf Jellyfin 12 nie als Update angeboten.

**Umstieg vom Original:** Die Plugin-ID ist gleich geblieben. Ein installiertes Original wird daher direkt aktualisiert, die Einstellungen bleiben erhalten. Seine Empfehlungs-Bibliotheken werden durch die Zeilen auf der Startseite ersetzt und automatisch entfernt. Das Original-Repository kann danach aus den Plugin-Repositories entfernt werden.

## Installation

### Variante A: Über das Plugin-Repository

> Funktioniert nur, wenn dieses GitHub-Repository **öffentlich** ist.

1. Dashboard → Plugins → Repositories → Hinzufügen
   `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json`
2. Dashboard → Plugins → Katalog → „Local Recommendations“ installieren
3. Jellyfin-Server neu starten

### Variante B: Manuell

1. [`release/jellyfin-plugin-localrecs-v12.3.2.zip`](release/jellyfin-plugin-localrecs-v12.3.2.zip) herunterladen und entpacken.
2. Die Datei `Jellyfin.Plugin.LocalRecs.dll` in einen neuen Ordner im Plugin-Verzeichnis legen, z. B.
   - Linux: `/var/lib/jellyfin/plugins/LocalRecs_12.3.2.0/`
   - Docker: `/config/plugins/LocalRecs_12.3.2.0/`
   - Windows: `%LOCALAPPDATA%\jellyfin\plugins\LocalRecs_12.3.2.0\`
3. Jellyfin-Server neu starten.

## Einrichtung

1. **Plugins installieren:** Unter Dashboard → Plugins → Repositories das Repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json` hinzufügen und **Home Screen Sections**, **File Transformation** und **Plugin Pages** installieren. Danach Jellyfin neu starten.
2. **Bereiche einschalten:** Dashboard → Plugins → **Home Screen Sections** → Plugin aktivieren und die Bereiche **„Empfohlene Filme für dich“**, **„Empfohlene Serien für dich“** und **„Heute im TV für dich“** für alle Benutzer einschalten. (Alternativ schaltet jeder Benutzer sie selbst über das Menü „Modular Home“ ein.)
3. **Fertig.** Die erste Berechnung startet automatisch nach dem Serverstart. Danach aktualisiert die geplante Aufgabe „Refresh Local Recommendations“ die Empfehlungen täglich um 4 Uhr (oder sofort über Dashboard → Geplante Aufgaben).

Im Plugin unter **Einstellungen → Startseite** siehst du, ob alles verbunden ist, und kannst die Titel der Zeilen ändern.

### Live-TV

- **Mitschreiben:** Jellyfin meldet beim Live-TV nur den Sender. Das Plugin schaut im EPG nach, welche Sendung gerade lief, und zählt die Minuten pro Sendung – bei allen Apps, auch den TV-Apps.
- **Senderwechsel (z. B. Fußball-Konferenz):** Jede Sendung bekommt ihre eigenen Minuten. Da alle Spiele „Fußball“ heißen bzw. das Genre „Sport“ haben, addieren sie sich beim Interesse. Wer samstags zwischen Sky 1 und Sky 2 springt, bekommt danach kommende Fußballspiele empfohlen.
- **Sportart-Erkennung:** Das Plugin erkennt die Sportart aus Genre, Titel und Episodentitel (z. B. „Fußball“, „1. Bundesliga“, „Bundesliga 26/27“, „Handball: Bundesliga“). Sportsendungen werden nur bei gleicher Sportart empfohlen – wer Fußball schaut, bekommt alle Fußballspiele (Sky, DAZN, Sportdigital …), aber kein Handball, Golf oder Tennis. Mehrere Spiele gleichzeitig werden einzeln angezeigt.
- **Zappen** (kürzer als 3 Minuten) wird ignoriert. **Nachrichten, News und Wetter** werden ignoriert, weil sie oft nur nebenbei laufen (einstellbar).
- **„Heute im TV für dich“:** Sendungen der nächsten 24 Stunden (einstellbar), die zu Live-TV-Verlauf und Bibliothek passen. Läuft eine Serie, die du in der Bibliothek schaust, steht sie weit vorne. Jede Sendung erscheint nur einmal (nächster Termin).
- **Bibliothek:** Wurde ein Titel insgesamt mindestens 15 Minuten live geschaut und gibt es ihn auch in der Bibliothek, zählt er dort als gesehen und beeinflusst die Film- und Serien-Empfehlungen.
- Gespeichert wird lokal in `plugins/LocalRecs/livetv-history.json`. Einträge werden nach 180 Tagen gelöscht (einstellbar), ältere zählen schon vorher weniger (Halbwertszeit wie bei den Bibliotheks-Empfehlungen, Standard 365 Tage).

**Gut zu wissen**
- Jeder Benutzer sieht nur seine eigenen Empfehlungen.
- Schon gesehene oder angefangene Filme verschwinden sofort aus der Zeile, nicht erst bei der nächsten Berechnung.
- Beim Umstieg von einer älteren Version entfernt das Plugin die alten Bibliotheken „Empfohlene Filme/Serien – Name“ und den Ordner mit den Verknüpfungen. Die Medien selbst werden nicht angefasst. Doppelte Einträge bei „Weiterschauen“ verschwinden nach dem nächsten Bibliotheks-Scan.
- Benutzer, die durch Version 12.1 von „Zugriff auf alle Bibliotheken“ auf eine Liste umgestellt wurden, behalten diese Liste. Falls gewünscht, den Haken unter Dashboard → Benutzer → Bibliothekszugriff wieder setzen.

## Selbst bauen

**Voraussetzungen:** .NET 10.0 SDK, Git

```bash
git clone https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin.git
cd Lokal-Vorschlag-Jellyfin

dotnet build -c Release   # Ausgabe: Jellyfin.Plugin.LocalRecs/bin/Release/net10.0/
dotnet test -c Release
```

## Lizenz

GNU General Public License v3.0 – siehe [LICENSE.txt](LICENSE.txt). Ursprünglicher Autor: Roger Pharr ([rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)).

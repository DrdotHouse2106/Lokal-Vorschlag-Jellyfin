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
> Ab Version **12.2.0** erscheinen die Empfehlungen als **eigene Zeilen auf der Startseite** (über das Plugin „Home Screen Sections“) statt als eigene Bibliotheken. Die Empfehlungs-Bibliotheken älterer Versionen werden beim ersten Start automatisch entfernt. **Native Apps** (Android TV, Fire TV, Roku, Swiftfin auf iPhone/iPad/Apple TV) zeigen diese Zeilen nicht an. Für Android TV / Fire TV gibt es dafür die angepasste App [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client), die die Empfehlungen über `GET /LocalRecs/Recommendations` lädt (ab Version 12.5.0).

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
- **Clients:** Browser, Jellyfin-Desktop-App, die Android-App „Jellyfin“ und die iOS-App „Jellyfin Mobile“ (beide zeigen die Weboberfläche). Native Apps (Android TV, Fire TV, Roku, **Swiftfin** auf iPhone/iPad/Apple TV) zeigen die Zeilen nicht – dort bleibt die normale Startseite. Gegebenenfalls App neu starten bzw. Cache leeren.

Für Jellyfin 10.11.x bitte die Original-Version 0.6.1 verwenden.

**Versionsnummer:** Die Versionen dieses Forks sind an Jellyfin angelehnt (12.x). Jellyfin prüft nur, ob ein Plugin eine *mindestens* passende Server-Version verlangt. Durch die höhere Versionsnummer werden 10.11-Versionen des Originals auf Jellyfin 12 nie als Update angeboten.

**Umstieg vom Original:** Die Plugin-ID ist gleich geblieben. Ein installiertes Original wird daher direkt aktualisiert, die Einstellungen bleiben erhalten. Seine Empfehlungs-Bibliotheken werden durch die Zeilen auf der Startseite ersetzt und automatisch entfernt. Das Original-Repository kann danach aus den Plugin-Repositories entfernt werden.

## Installation

### Variante A: Über das Plugin-Repository

Das Plugin steht im gemeinsamen Plugin-Repository [DrdotHouse2106/Jellyfin-Plugins](https://github.com/DrdotHouse2106/Jellyfin-Plugins), das alle eigenen Jellyfin-Plugins bündelt.

1. Dashboard → Plugins → Repositories → Hinzufügen
   `https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Plugins/main/manifest.json`
2. Dashboard → Plugins → Katalog → „Local Recommendations“ installieren
3. Jellyfin-Server neu starten

Hast du früher das Repository `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json` eingetragen, funktioniert es weiter. Trag aber besser nur noch das gemeinsame Repository oben ein, damit das Plugin nicht doppelt im Katalog auftaucht.

### Variante B: Manuell

1. [`release/jellyfin-plugin-localrecs-v12.14.0.zip`](release/jellyfin-plugin-localrecs-v12.14.0.zip) herunterladen und entpacken.
2. Die Datei `Jellyfin.Plugin.LocalRecs.dll` in einen neuen Ordner im Plugin-Verzeichnis legen, z. B.
   - Linux: `/var/lib/jellyfin/plugins/LocalRecs_12.14.0.0/`
   - Docker: `/config/plugins/LocalRecs_12.14.0.0/`
   - Windows: `%LOCALAPPDATA%\jellyfin\plugins\LocalRecs_12.14.0.0\`
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
- **Stammsendungen:** Wird eine Sendung an mindestens 3 verschiedenen Tagen live geschaut (z. B. „Die Sendung mit der Maus“), gilt sie als Stammsendung. Sie steht immer in der Zeile – auch wenn sie erst in einigen Tagen läuft (bis 7 Tage im Voraus).
- **Sendezeit auf der Karte:** Erste Zeile Titel (bei Serien mit Episode, z. B. „The Rookie: Leben und Tod“, bei Sport die Paarung), zweite Zeile immer Zeit und Sender, z. B. „Heute 20:15 · ZDF“, „Morgen 09:10 · RTL“ oder „Läuft bis 15:00 · ARTE“ (Zeitzone einstellbar, Standard Europe/Berlin). Die Zeile ist chronologisch sortiert und zeigt höchstens ein Drittel aus demselben Genre.
- **Erfassung unabhängig von der App:** Zusätzlich zu den Wiedergabe-Meldungen schaut das Plugin jede Minute nach, wer gerade Live-TV schaut – das funktioniert auch mit Apps, die keinen Fortschritt melden.
- **Zappen** (kürzer als 3 Minuten) wird ignoriert. **Nachrichten, News und Wetter** werden ignoriert, weil sie oft nur nebenbei laufen (einstellbar).
- **„Heute im TV für dich“:** Sendungen der nächsten 24 Stunden (einstellbar), die zu Live-TV-Verlauf und Bibliothek passen. Läuft eine Serie, die du in der Bibliothek schaust, steht sie weit vorne. Jede Sendung erscheint nur einmal (nächster Termin).
- **Bibliothek:** Wurde ein Titel insgesamt mindestens 15 Minuten live geschaut und gibt es ihn auch in der Bibliothek, zählt er dort als gesehen und beeinflusst die Film- und Serien-Empfehlungen.
- **Darsteller aus der XMLTV-Datei (optional):** Jellyfin speichert für Live-TV-Sendungen keine Darsteller. Unter **Einstellungen → Startseite → „XMLTV-Quelle für Darsteller“** lässt sich eine XMLTV-Datei angeben (Pfad im Jellyfin-Container oder URL, `.xml` oder `.xml.gz`). Das Plugin liest daraus Darsteller (bis 10), Regie, Produktionsjahr und Land und ordnet sie über Startzeit und Titel den Sendungen zu (die Senderkennungen dürfen abweichen). Dann zählen Lieblingsschauspieler aus Live-TV **und** aus den gesehenen Filmen der Bibliothek für „Heute im TV für dich“. Die Datei wird bei Änderungen bzw. alle 6 Stunden neu gelesen; ist sie fehlerhaft, bleibt der letzte Stand erhalten. Bei Docker den Ordner read-only einbinden, z. B. `/pfad/zum/epg:/epg-source:ro`, und `/epg-source/epg.xml.gz` eintragen. Ein passendes Hilfsskript, das den deutschen EPG von epgshare01 zusätzlich um Bundesliga-Paarungen ergänzt, liegt unter [`extras/epg-enrich`](extras/epg-enrich/).
- **Fehlersuche:** Unter **Einstellungen → Startseite** stehen die Wiedergabe-Meldungen seit dem Serverstart (davon Live-TV) und eine Liste der zuletzt gesehenen Wiedergaben mit Benutzer, App, Typ und Titel. Bleibt „davon Live-TV“ bei 0, obwohl Live-TV läuft, sieht man dort, als was die App die Wiedergabe meldet. Fehlt im Programmführer die laufende Sendung, wird das ebenfalls angezeigt.
- Gespeichert wird lokal in `plugins/LocalRecs/livetv-history.json`. Einträge werden nach 180 Tagen gelöscht – einstellbar bis 3650 Tage (10 Jahre). Wie stark ältere Einträge zählen, bestimmt die Halbwertszeit („Recency Decay Half-Life“, Standard 365 Tage, einstellbar bis 3650).

### Bewertung

Filme und Serien mit einer Community-Bewertung unter **5.0** werden nicht empfohlen (Titel ohne Bewertung bleiben drin; einstellbar unter „Mindestbewertung“, 0 = aus). Außerdem rücken gut bewertete Titel bei ähnlicher Passung nach vorne („Gewicht der Bewertung“, Standard 0.15).

### Abwechslung und Lernen aus dem Verhalten (ab 12.8.0)

- **Ignorierte Empfehlungen:** Steht ein Titel länger als 14 Tage in der Zeile, ohne geschaut zu werden, rutscht er nach und nach nach hinten (2 % pro Tag, höchstens auf die Hälfte). So bleibt die Zeile frisch. Einstellbar unter „Ignorierte Empfehlungen abwerten nach“, 0 = aus.
- **Abgebrochene Filme:** Wird ein Film vor einem Viertel der Laufzeit beendet und drei Tage lang nicht weitergeschaut, zählt das als leises „Gefällt mir nicht“: Ähnliche Titel werden etwas seltener empfohlen (halb so stark wie ein echtes „Gefällt mir nicht“).
- **Mehr Abwechslung:** Höchstens ein Drittel der Film- bzw. Serienzeile stammt aus demselben Hauptgenre, der Rest wird mit den nächstbesten Titeln aufgefüllt.
- **Filmreihen (ab 12.9.0):** Wurde ein Teil einer Jellyfin-Sammlung gesehen (z. B. „Harry Potter“), steht der nächste ungesehene Teil vorne in der Filmzeile – höchstens ein Viertel der Zeile, Mindestbewertung gilt weiterhin. Abschaltbar unter „Filmreihen: nächsten Teil vorziehen“.
- **Begründung (ab 12.9.0):** Über dem Titel steht auf der Startseite, warum etwas empfohlen wird, z. B. „Weil du „Frieren“ geschaut hast“ (ähnlichster gesehener Titel) oder „Nächster Teil nach „Harry Potter 1““. Voraussetzung: In Home Screen Sections ist die Anzeige der Titel eingeschaltet. Abschaltbar unter „Begründung auf den Karten anzeigen“. Apps holen die Begründungen über `GET /LocalRecs/Recommendations/Reasons` (Zuordnung Element-ID → Text).
- **Keine gesehenen Folgen im TV:** „Heute im TV für dich“ schlägt keine Folge vor, die schon live oder in der Bibliothek geschaut wurde (Abgleich über Sendung und Episodentitel). Läuft danach eine neue Folge, erscheint diese.

### Auswertung

Das Plugin merkt sich, was es wem empfohlen hat, und zählt, was davon danach geschaut wurde (Filme und Serien ab 10 Minuten oder bis zum Ende, Live-TV ab der Mindestdauer). Im Reiter **Auswertung** stehen pro Bereich Anzahl der Empfehlungen, Treffer, Trefferquote und die durchschnittliche Zeit bis zum Anschauen, dazu die zuletzt geschauten Empfehlungen. Die Daten liegen in `plugins/LocalRecs/feedback.json` und lassen sich im Reiter mit **„Auswertung herunterladen (JSON)“** speichern oder über `GET /LocalRecs/Evaluation?days=30` (Admin) abrufen – z. B. um die Empfehlungen gezielt weiterzuentwickeln.

### Schnittstelle für TV-Apps

Apps, die „Home Screen Sections“ nicht anzeigen (z. B. [Jellyfin-TV-Client](https://github.com/DrdotHouse2106/Jellyfin-TV-Client) für Android TV / Fire TV), holen die Empfehlungen direkt:

```
GET /LocalRecs/Recommendations?type=movies|series|livetv&limit=20
```

- Nur für den **angemeldeten Benutzer** (normales Jellyfin-Token, kein Admin nötig).
- Antwort wie bei Jellyfin üblich: `{ "Items": [ … ], "TotalRecordCount": n }` – dieselben Einträge wie in den Startseiten-Zeilen.
- `limit` ist optional; ohne `limit` gilt die Anzahl aus den Plugin-Einstellungen.
- Funktioniert auch **ohne** Home Screen Sections.

Außerdem (ab 12.6.0):

```
GET /LocalRecs/Top10?type=all|movies|series&days=60&limit=10
```

- **„Top 10 bei euch“**: die meistgesehenen Filme und Serien **aller Benutzer** der letzten `days` Tage. Zuerst zählt, wie viele verschiedene Personen einen Titel geschaut haben, dann die Anzahl der Wiedergaben; Folgen zählen für ihre Serie.
- Kein Admin nötig. Geliefert werden nur Titel, die der angemeldete Benutzer sehen darf – keine Namen, keine Einzeldaten.

Besetzung von Live-TV-Sendungen (ab 12.7.1, nur mit gesetzter „XMLTV-Quelle für Darsteller“):

```
GET /LocalRecs/LiveTv/Program/{programId}/Credits
GET /LocalRecs/LiveTv/Credits?ids=id1,id2,…   (bis 200 IDs)
```

- `programId` ist die Jellyfin-ID der Sendung (LiveTvProgram). Normales Token, der Benutzer braucht Live-TV-Zugriff.
- Antwort: `{ "ProgramId", "Matched", "Actors": [{ "Name", "Role" }], "Directors", "Writers", "Year", "Country", "Episode", "ImdbId", "TmdbId" }`. Ohne Treffer in der XMLTV-Datei `Matched: false` (HTTP 200). Unbekannte ID: 404 bzw. bei mehreren IDs nicht in der Antwort enthalten.

**Welten wie bei Disney+ (ab 12.10.0):** Marvel, Star Wars, DC, Pixar, Disney, DreamWorks, Illumination, Harry Potter, Mittelerde, James Bond, Jurassic Park, Studio Ghibli. Welche Titel dazugehören, steht in einer **gepflegten Liste auf GitHub** – ab 12.12.0 im eigenen Repository [Jellyfin-Welten](https://github.com/DrdotHouse2106/Jellyfin-Welten), dort wöchentlich vollständig aus TMDB aktualisiert (auch angekündigte Titel); von Hand ergänzte Titel und Ausschlüsse stehen in dessen `regeln.json` und gehen nie verloren: Studios, Namen von Jellyfin-Sammlungen und einzelne Titel per TMDB-/IMDb-/TVDB-ID, dazu Ausschlüsse. Das Plugin lädt die Liste täglich und behält bei Fehlern den letzten Stand; die Bibliothek wird nicht verändert. Wer eine andere Zuordnung möchte, trägt unter **Settings → Welten-Liste** einen Link zu einer eigenen Datei ein (leer = nur die Regeln aus dem Textfeld). Im Reiter **Welten** steht pro Welt, welche Titel warum dazugehören, dazu Vorschläge für fehlende Titel mit fertiger Zeile zum Einfügen in die Liste. Angezeigt werden nur Welten mit mindestens 5 Titeln.

```
GET /LocalRecs/Worlds                              → Welten des Benutzers (zuletzt geschaute zuerst)
GET /LocalRecs/Worlds/{id}/Recommendations?limit=20 → Titel der Welt, persönlich sortiert
GET /LocalRecs/Worlds/Recent?limit=20              → zuletzt geschaute Welt mit Titeln („Mehr aus dem …-Universum“), sonst 204
```

- Sortiert nach dem Geschmack des Benutzers (ohne Verlauf nach Bewertung). Der nächste ungesehene Teil einer begonnenen Filmreihe steht vorne. Gesehene, angefangene und mit „Gefällt mir nicht“ bewertete Titel fehlen, Serien bleiben bis zur letzten Folge.
- Die Begründungen der Welt-Titel stehen ebenfalls in `/LocalRecs/Recommendations/Reasons`.
- **Logos (ab 12.13.0):** Hat eine Welt in der Liste ein Logo (Link, z. B. auf ein TMDB-Firmenlogo), steht in `/LocalRecs/Worlds` das Feld `LogoUrl` (`/LocalRecs/Worlds/{id}/Logo`). Das Plugin lädt das Bild, speichert es auf dem Server zwischen und liefert es ohne Anmeldung aus – Apps sprechen also nur den Jellyfin-Server an. Erlaubt sind nur https-Adressen bzw. Bilder neben einer lokalen Liste. Ab 12.14.0 kann eine Welt mehrere Logos in Reihenfolge haben, darunter Reihen-Logos von **fanart.tv** (`fanart:collection/{TMDB-Sammlung}`, z. B. Star Wars, Wizarding World, Mittelerde, Jurassic Park, James Bond) – dafür unter **Settings** den eigenen fanart.tv-API-Schlüssel eintragen; ohne Schlüssel gelten die Studio-Logos von TMDB. Frühere IDs einer Welt (`aliase`, z. B. `harry-potter` nach der Umbenennung in „Wizarding World“) funktionieren in allen Adressen weiter.
- `TmdbCollectionIds` (ab 12.10.1) nennt die TMDB-Sammlungen einer Welt (per Name zugeordnet oder mit mindestens einem Titel der Welt). Darüber kann eine App fehlende Teile z. B. bei Jellyseerr nachschlagen und zum Anfragen anbieten.

**Daumen hoch/runter (ab 12.6.0):** „Gefällt mir“ zählt wie ein Favorit, auch wenn der Titel noch nicht geschaut wurde. „Gefällt mir nicht“ wird nie empfohlen und macht ähnliche Titel seltener. Bewerten lässt sich in der TV-App auf der Detailseite (gespeichert wird in Jellyfin selbst).

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

# Quallenkinoplugin für Jellyfin – Jellyfin 12

[![Lizenz: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml/badge.svg)](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml)

[![Spendier mir einen Kaffee](https://img.shields.io/badge/☕_Spendier_mir_einen_Kaffee-PayPal-0070ba?style=for-the-badge&logo=paypal&logoColor=white)](https://paypal.me/DrdotHouse)

Das **Quallenkinoplugin** (bis 12.27 „Local Recommendations“) erweitert Jellyfin um persönliche Empfehlungen – ausschließlich auf Basis des lokalen Wiedergabeverlaufs und der Metadaten-Ähnlichkeit, ohne Cloud-Dienste und ohne Tracking –, dazu Live-TV mit eigenem Fernsehprogramm-Anbieter, Erinnerungen, Aufnehmen, „Wer schaut?“ mit Profilbildern und eine Oberfläche im Quallenkino-Stil für Jellyfin Web. Die TV-App [Quallenkino](https://github.com/DrdotHouse2106/Jellyfin-TV-Client) nutzt dieselben Schnittstellen.

> [!NOTE]
> **Vorlage / Original:** Dieses Plugin basiert auf dem originalen Projekt **[rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)** von Roger Pharr.
> Dieses Repository ist eine an **Jellyfin 12** angepasste und stark erweiterte Version davon, da das Original für Jellyfin 12 nicht mehr aktualisiert wird. Alle Rechte und der Dank für die eigentliche Entwicklung gehen an den ursprünglichen Autor.
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
- **Eigener Fernsehprogramm-Anbieter „LocalRecs EPG“** – ersetzt den XMLTV-Anbieter von Jellyfin; Film oder Serie entscheiden Regel-Bausteine, Staffel und Folge bleiben erhalten, Aufnahmen landen richtig
- **Jellyfin Web im Quallenkino-Stil** (Browser, iOS- und Android-App) – Startseite mit allen Reihen, Detailansicht, Quallenkino-Player, Überspringen mit Countdown, aufgeräumte Kopfleiste und Profilmenü
- **„Wer schaut?“** – Profilauswahl beim Öffnen, Wechsel per Klick ohne PIN
- **Profilbilder** – Figur und Hintergrundfarbe wählen, der Server erzeugt das runde Bild
- **Live-TV-Erinnerungen** – Sendungen und Serien merken, Kalender-Abo (iCal) mit Erinnerung, QR-Code fürs Handy
- **Aufnehmen** – Sendung oder Serie direkt aus der Info-Ansicht aufnehmen
- **Welten wie bei Disney+** und **„Top 10 bei euch“** – für Apps wie Quallenkino und die Quallenkino-Startseite
- **Schnittstellen für TV-Apps** – alles auch für eigene Apps (Quallenkino für Android TV, Fire TV und webOS)
- **Ähnliche Titel von TMDB und Trakt** – öffentliche Ähnlichkeitslisten als Zusatzsignal, besonders bei wenig Sehverlauf; dazu persönliche Trakt-Empfehlungen für Benutzer mit Trakt-Konto
- **Datenschutz** – alle Berechnungen laufen lokal auf deinem Server; nach außen gehen nur Titel-IDs (siehe „Ähnliche Titel“), persönliche Trakt-Empfehlungen nur nach Einschalten durch den Benutzer, Sicherungen der Einstellungen ohne Schlüssel

## Voraussetzungen

- **Jellyfin-Server:** 12.0 oder neuer (getestet mit 12.2)
- **.NET-Runtime:** 10.0 (ist in Jellyfin 12 enthalten)
- **Target ABI:** 12.0.0.0
- **Für die Startseite:** die Plugins **Home Screen Sections**, **File Transformation** und **Plugin Pages** von IAmParadox27 (Repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`)
- **Clients:** Browser, Jellyfin-Desktop-App, die Android-App „Jellyfin“ und die iOS-App „Jellyfin Mobile“ (beide zeigen die Weboberfläche). Native Apps (Android TV, Fire TV, Roku, **Swiftfin** auf iPhone/iPad/Apple TV) zeigen die Zeilen nicht – dort bleibt die normale Startseite. Gegebenenfalls App neu starten bzw. Cache leeren.

Für Jellyfin 10.11.x bitte die Original-Version 0.6.1 verwenden.

**Name:** In Jellyfin heißt das Plugin **„Quallenkino“**, außerhalb (Repository, Suche) „Quallenkinoplugin für Jellyfin“. Frühere Namen: „Local Recommendations“ (bis 12.27), „Quallenkinoplugin für Jellyfin“ (12.28). Jellyfin legt Plugins in Ordnern `Name_Version` an und räumt alte Versionen nur bei gleichem Namen weg. Ab 12.29.0 entfernt das Plugin beim Start selbst ältere Ordner mit derselben Plugin-ID. Beim ersten Umstieg auf 12.29.0 nach dem Update **vor dem Neustart** den alten Ordner löschen (Dashboard → Plugins → alte Version deinstallieren bzw. `DELETE /Plugins/{id}/{version}`). Sonst laden beide Versionen, und alle `/LocalRecs`-Adressen melden einen Fehler 500, bis Jellyfin noch einmal neu gestartet ist. **Achtung bei Versionen vor 12.29.1:** Laufen zwei Versionen gleichzeitig, kann Jellyfin die Einstellungsdatei nicht lesen und überschreibt sie mit Standardwerten. Vorher `plugins/configurations/Jellyfin.Plugin.LocalRecs.xml` sichern. Ab 12.29.1 verhindert das Plugin das und legt vor jedem Speichern eine Sicherung an (`plugins/configurations/Jellyfin.Plugin.LocalRecs.sicherungen/`, die letzten 10). Ab 12.34.0 enthalten die Sicherungen **keine Schlüssel** (TMDB, Trakt-Client-Secret, fanart.tv; ältere Sicherungen werden beim Start bereinigt). Wiederherstellen per `GET /LocalRecs/Config/Backups` und `POST /LocalRecs/Config/Backups/{name}/Restore` (Admin) – dabei bleiben die aktuellen Schlüssel erhalten. Wer eine Sicherung von Hand zurückkopiert, trägt die Schlüssel danach neu ein.

**Versionsnummer:** Die Versionen dieses Forks sind an Jellyfin angelehnt (12.x). Jellyfin prüft nur, ob ein Plugin eine *mindestens* passende Server-Version verlangt. Durch die höhere Versionsnummer werden 10.11-Versionen des Originals auf Jellyfin 12 nie als Update angeboten.

**Umstieg vom Original:** Die Plugin-ID ist gleich geblieben. Ein installiertes Original wird daher direkt aktualisiert, die Einstellungen bleiben erhalten. Seine Empfehlungs-Bibliotheken werden durch die Zeilen auf der Startseite ersetzt und automatisch entfernt. Das Original-Repository kann danach aus den Plugin-Repositories entfernt werden.

## Installation

### Variante A: Über das Plugin-Repository

Das Plugin steht im gemeinsamen Plugin-Repository [DrdotHouse2106/Jellyfin-Plugins](https://github.com/DrdotHouse2106/Jellyfin-Plugins), das alle eigenen Jellyfin-Plugins bündelt.

1. Dashboard → Plugins → Repositories → Hinzufügen
   `https://raw.githubusercontent.com/DrdotHouse2106/Jellyfin-Plugins/main/manifest.json`
2. Dashboard → Plugins → Katalog → **„Quallenkino“** installieren (in Jellyfin heißt das Plugin kurz „Quallenkino“; bis 12.27 „Local Recommendations“, 12.28 „Quallenkinoplugin für Jellyfin“ – ein installiertes Plugin wird einfach aktualisiert, alte Ordner räumt es ab 12.29 beim Start selbst weg)
3. Jellyfin-Server neu starten

Hast du früher das Repository `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json` eingetragen, funktioniert es weiter. Trag aber besser nur noch das gemeinsame Repository oben ein, damit das Plugin nicht doppelt im Katalog auftaucht.

### Variante B: Manuell

1. Im Ordner [`release/`](release/) die neueste Datei `jellyfin-plugin-localrecs-v<Version>.zip` herunterladen (die aktuelle Version steht oben im [CHANGELOG](CHANGELOG.md)) und entpacken.
2. Die Datei `Jellyfin.Plugin.LocalRecs.dll` in einen Ordner `LocalRecs_<Version>` im Plugin-Verzeichnis legen (alten `LocalRecs_…`-Ordner vorher löschen), z. B.
   - Linux: `/var/lib/jellyfin/plugins/LocalRecs_<Version>/`
   - Docker: `/config/plugins/LocalRecs_<Version>/`
   - Windows: `%LOCALAPPDATA%\jellyfin\plugins\LocalRecs_<Version>\`
3. Jellyfin-Server neu starten.

## Einrichtung

1. **Plugins installieren:** Unter Dashboard → Plugins → Repositories das Repository `https://www.iamparadox.dev/jellyfin/plugins/manifest.json` hinzufügen und **Home Screen Sections**, **File Transformation** und **Plugin Pages** installieren. Danach Jellyfin neu starten.
2. **Bereiche einschalten:** Dashboard → Plugins → **Home Screen Sections** → Plugin aktivieren und die Bereiche **„Empfohlene Filme für dich“**, **„Empfohlene Serien für dich“** und **„Heute im TV für dich“** für alle Benutzer einschalten. (Alternativ schaltet jeder Benutzer sie selbst über das Menü „Modular Home“ ein.)
3. **Fertig.** Die erste Berechnung startet automatisch nach dem Serverstart. Danach aktualisiert die geplante Aufgabe „Empfehlungen aktualisieren“ (bis 12.25: „Refresh Local Recommendations“) die Empfehlungen täglich um 4 Uhr (oder sofort über Dashboard → Geplante Aufgaben).

In den Plugin-Einstellungen (Dashboard → Plugins → Quallenkino) siehst du im Reiter **„Startseite“**, ob alles verbunden ist, und kannst die Titel der Zeilen ändern. Die Reiter: **Startseite** (Status, Zeilen, Live-TV), **Einstellungen** (Empfehlungen, Jellyfin Web, Kalender, Welten), **Auswertung**, **Welten**, **Fernsehprogramm** (LocalRecs EPG) und **Aktionen**.

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
- **Darsteller aus der XMLTV-Datei (optional):** Jellyfin speichert für Live-TV-Sendungen keine Darsteller. Im Reiter **„Startseite“** unter **„XMLTV-Quelle für Darsteller“** lässt sich eine XMLTV-Datei angeben (Pfad im Jellyfin-Container oder URL, `.xml` oder `.xml.gz`). Das Plugin liest daraus Darsteller (bis 10), Regie, Produktionsjahr und Land und ordnet sie über Startzeit und Titel den Sendungen zu (die Senderkennungen dürfen abweichen; ab 12.17.0 darf die Startzeit bis zu 15 Minuten abweichen – bei mehreren Treffern gewinnt der gleiche Sender, dann die nächste Startzeit). Dann zählen Lieblingsschauspieler aus Live-TV **und** aus den gesehenen Filmen der Bibliothek für „Heute im TV für dich“. Die Datei wird bei Änderungen bzw. alle 6 Stunden neu gelesen; ist sie fehlerhaft, bleibt der letzte Stand erhalten. Bei Docker den Ordner read-only einbinden, z. B. `/pfad/zum/epg:/epg-source:ro`, und `/epg-source/epg.xml.gz` eintragen. Mit dem eigenen Fernsehprogramm-Anbieter (siehe unten) ist es meist dieselbe Datei. Ein Hilfsskript, das den deutschen EPG von epgshare01 um Bundesliga-Paarungen ergänzt, liegt unter [`extras/epg-enrich`](extras/epg-enrich/).
- **Fehlersuche:** Im Reiter **„Startseite“** stehen die Wiedergabe-Meldungen seit dem Serverstart (davon Live-TV) und eine Liste der zuletzt gesehenen Wiedergaben mit Benutzer, App, Typ und Titel. Bleibt „davon Live-TV“ bei 0, obwohl Live-TV läuft, sieht man dort, als was die App die Wiedergabe meldet. Fehlt im Programmführer die laufende Sendung, wird das ebenfalls angezeigt.
- Gespeichert wird lokal in `plugins/LocalRecs/livetv-history.json`. Einträge werden nach 180 Tagen gelöscht – einstellbar bis 3650 Tage (10 Jahre). Wie stark ältere Einträge zählen, bestimmt die „Halbwertszeit des Sehverlaufs“ (Standard 365 Tage, einstellbar bis 3650).

### Bewertung

Filme und Serien mit einer Community-Bewertung unter **5.0** werden nicht empfohlen (Titel ohne Bewertung bleiben drin; einstellbar unter „Mindestbewertung“, 0 = aus). Außerdem rücken gut bewertete Titel bei ähnlicher Passung nach vorne („Gewicht der Bewertung“, Standard 0.15).

### Abwechslung und Lernen aus dem Verhalten (ab 12.8.0)

- **Ignorierte Empfehlungen:** Steht ein Titel länger als 14 Tage in der Zeile, ohne geschaut zu werden, rutscht er nach und nach nach hinten (2 % pro Tag, höchstens auf die Hälfte). So bleibt die Zeile frisch. Einstellbar unter „Ignorierte Empfehlungen abwerten nach“, 0 = aus.
- **Abgebrochene Filme:** Wird ein Film vor einem Viertel der Laufzeit beendet und drei Tage lang nicht weitergeschaut, zählt das als leises „Gefällt mir nicht“: Ähnliche Titel werden etwas seltener empfohlen (halb so stark wie ein echtes „Gefällt mir nicht“).
- **Mehr Abwechslung:** Höchstens ein Drittel der Film- bzw. Serienzeile stammt aus demselben Hauptgenre, der Rest wird mit den nächstbesten Titeln aufgefüllt.
- **Filmreihen (ab 12.9.0):** Wurde ein Teil einer Jellyfin-Sammlung gesehen (z. B. „Harry Potter“), steht der nächste ungesehene Teil vorne in der Filmzeile – höchstens ein Viertel der Zeile, Mindestbewertung gilt weiterhin. Abschaltbar unter „Filmreihen: nächsten Teil vorziehen“.
- **Begründung (ab 12.9.0):** Über dem Titel steht auf der Startseite, warum etwas empfohlen wird, z. B. „Weil du „Frieren“ geschaut hast“ (ähnlichster gesehener Titel) oder „Nächster Teil nach „Harry Potter 1““. Voraussetzung: In Home Screen Sections ist die Anzeige der Titel eingeschaltet. Abschaltbar unter „Begründung auf den Karten anzeigen“. Apps holen die Begründungen über `GET /LocalRecs/Recommendations/Reasons` (Zuordnung Element-ID → Text).
- **Keine gesehenen Folgen im TV:** „Heute im TV für dich“ schlägt keine Folge vor, die schon live oder in der Bibliothek geschaut wurde (Abgleich über Sendung und Episodentitel). Läuft danach eine neue Folge, erscheint diese.

### Ähnliche Titel von TMDB und Trakt (ab 12.33.0)

Die eigene Ähnlichkeit (Genres, Darsteller, Regie, Stichwörter) kennt nur, was in den Metadaten steht. Dazu kommen jetzt öffentliche Listen, die auf dem Verhalten vieler Menschen beruhen:

- **TMDB „Empfehlungen“ und „Ähnlich“** je Film/Serie, optional **Trakt „Related“**. Titel der eigenen Bibliothek, die dort als ähnlich zu gesehenen oder gemochten Titeln genannt werden, bekommen einen Bonus („Gewicht der ähnlichen Titel“, Standard 0.25). Begründung auf der Karte: **„Ähnlich wie „…““**.
- **Wenig Sehverlauf:** Wer weniger als die Mindestzahl gesehen hat, bekam bisher nur gut bewertete Titel. Jetzt zählt der Bonus dort doppelt – schon zwei, drei gesehene Titel ergeben passende Vorschläge.
- **Datenschutz:** Gesendet wird nur die TMDB- bzw. IMDb-ID eines Titels – keine Benutzer, kein Sehverlauf, keine Pfade. Abgefragt wird nach und nach **der ganze Bestand** (gesehene Titel zuerst), sodass die Anfragen den Bestand abbilden und nicht, was geschaut wurde. Zwischenspeicher 7 Tage (`plugins/LocalRecs/aehnliche-titel.json`), höchstens 400 Titel je Lauf, kleine Pause zwischen den Anfragen.
- **Schlüssel:** Ohne Eintrag nutzt das Plugin den TMDB-Schlüssel aus Jellyfins TMDb-Einstellungen bzw. – wenn dort keiner steht – **Jellyfins eingebauten Schlüssel**. Welcher gerade genutzt wird, steht in den Einstellungen unter dem Schlüsselfeld; abschaltbar mit „Ohne eigenen Schlüssel den von Jellyfin mitbenutzen“. Ein eigener Schlüssel (v3 oder v4-Lese-Token) geht unter „Eigener TMDB-Schlüssel“. Trakt „Related“ braucht eine eigene Trakt-App (Client-ID). Ohne Schlüssel oder bei Fehlern bleibt es still weg – die eigenen Empfehlungen laufen immer.

**Persönliche Trakt-Empfehlungen:** Für Benutzer mit Trakt-Konto, **die es selbst einschalten** (Standard aus, ab 12.34.0), fließen zusätzlich deren persönliche Empfehlungen bei Trakt ein (`/recommendations/movies` und `/shows`, nur Titel der eigenen Bibliothek, „Gewicht der persönlichen Trakt-Empfehlungen“, Standard 0.5). Begründung: **„Für dich bei Trakt empfohlen“**.

- **Zugang:** Bevorzugt eine eigene Verbindung – im Profilmenü **„Trakt-Empfehlungen“ → „Mit Trakt verbinden“**, Code auf trakt.tv/activate eingeben. Dafür hinterlegt der Admin eine eigene Trakt-App (Client-ID und Client-Secret, Redirect-URI `urn:ietf:wg:oauth:2.0:oob`). Ohne eigene Verbindung nutzt das Plugin – **nur lesend** – die Verknüpfung im [Trakt-Plugin](https://github.com/jellyfin/jellyfin-plugin-trakt) (`Trakt.xml`); dessen Zugang wird nie erneuert oder zurückgeschrieben.
- **Pro Benutzer** im selben Dialog ein- und abschaltbar, Standard aus. Erst nach dem Einschalten liest das Plugin die Verknüpfung des Trakt-Plugins. Abgerufen wird nur die Empfehlungsliste, alle 12 Stunden; es wird nichts an Trakt gesendet. Das Scrobbeln macht weiter das Trakt-Plugin.
- Eigene Zugänge liegen in `plugins/LocalRecs/trakt-konten.json` auf dem Server und gehen nie an den Browser. „Verbindung trennen“ widerruft den Zugang bei Trakt.
- Schnittstelle (für den angemeldeten Benutzer): `GET/POST /LocalRecs/Trakt/Me`, `POST/DELETE /LocalRecs/Trakt/Me/Login`.

Ausblick: Ein Konzept für freiwillig geteilte Sehverläufe (gemeinsame Empfehlungen über mehrere Server) steht in [docs/konzept-gemeinsame-empfehlungen.md](docs/konzept-gemeinsame-empfehlungen.md) – noch nicht umgesetzt.

### Leistung und Sichtbarkeit (ab 12.34.0)

- **Welten** (`GET /LocalRecs/Worlds`, `…/Recent`, `…/{id}/Items`): Was ein Benutzer sehen darf, kommt aus **einer** Bibliotheksabfrage und wird 15 Minuten zwischengespeichert – bei geänderten Freigaben (Bibliotheken, Altersfreigabe, Tags) sofort neu. „Zuletzt geschaut“ 5 Minuten. Die Welten-Zuordnung rechnet nach Ablauf des Zwischenspeichers nur eine Anfrage, die anderen warten darauf.
- **Top 10:** Die Rangliste wird serverweit je Typ und Zeitraum 10 Minuten zwischengespeichert; pro Benutzer bleibt nur der Sichtbarkeitsfilter.
- **Ähnliche Titel (TMDB/Trakt)** werden erst geholt, **nachdem** die Empfehlungen gespeichert sind – ein langer erster Abgleich verzögert sie nicht mehr.
- **Behoben:** Welten und Top 10 haben bis 12.33 Titel aus Bibliotheken gezeigt, die für den Benutzer gesperrt sind (Jellyfins `IsVisible` prüft nur Altersfreigabe und Tags, nicht die Bibliotheksfreigabe). Jetzt gilt überall die Freigabe des Benutzers.

### Auswertung

Das Plugin merkt sich, was es wem empfohlen hat, und zählt, was davon danach geschaut wurde (Filme und Serien ab 10 Minuten oder bis zum Ende, Live-TV ab der Mindestdauer). Im Reiter **Auswertung** stehen pro Bereich Anzahl der Empfehlungen, Treffer, Trefferquote und die durchschnittliche Zeit bis zum Anschauen, dazu die zuletzt geschauten Empfehlungen. Die Daten liegen in `plugins/LocalRecs/feedback.json` und lassen sich im Reiter mit **„Auswertung herunterladen (JSON)“** speichern oder über `GET /LocalRecs/Evaluation?days=30` (Admin) abrufen – z. B. um die Empfehlungen gezielt weiterzuentwickeln.

### Startseite im Quallenkino-Stil für Jellyfin Web (ab 12.18.0)

Im Browser und in den Jellyfin-Apps für iOS und Android kann die Startseite wie in der TV-App Quallenkino aussehen, mit all ihren Reihen. Voraussetzung ist das Plugin **File Transformation** (aus dem gleichen Katalog wie Home Screen Sections). Einschalten im Reiter **„Einstellungen“**, Abschnitt **Jellyfin Web**: **„Startseite im Quallenkino-Stil“**, die Farben separat über **„Quallenkino-Farben“**.

- **Großer Infobereich** mit Hintergrundbild, Logo, Begründung und den Knöpfen „Abspielen“ und „Weitere Infos“. Hat der Titel einen lokalen Trailer, läuft er nach kurzer Zeit im Hintergrund – immer stumm, auf dem PC mit Ton-Knopf. Die Kopfleiste ist über dem Bild durchsichtig und wird beim Scrollen dunkel.
- **Reihen:** Weiterschauen, Als Nächstes, Empfohlene Filme und Serien (mit Begründung), Heute im TV für dich, Top 10 (große Ziffern), Welten (Kachel öffnet die Welten-Seite mit Filter und Sortierung), Jetzt im TV, „Neu in …“ je Bibliothek, Genre-Reihen und Bibliotheken. Alles wird gleichzeitig geladen; die letzte Startseite erscheint sofort aus dem Speicher des Browsers und wird im Hintergrund Reihe für Reihe aktualisiert.
- **PC:** Pfeile am Rand jeder Reihe, beim Überfahren eine Vorschau-Karte (Abspielen, Meine Liste, Gefällt mir, Weitere Infos; bei künftigen Sendungen zusätzlich Glocke und Aufnahme-Knopf). „Weitere Infos“ öffnet ein Fenster mit Folgen je Staffel, ähnlichen Titeln und Besetzung statt einer neuen Seite – bei Sendungen auch, wenn sie gerade laufen.
- **Handy** wie eine Streaming-App: Infobereich als Karte mit Poster, Logo und Genres, „Abspielen“ und „Meine Liste“, Chips „Serien“, „Filme“, „Meine Liste“, Hochformat-Poster, Welten als Kreise und eine Tab-Leiste unten (Startseite, Suche, Live-TV, Meine Liste, Mehr).
- **Kopfleiste aufgeräumt** (ab 12.18.2): links Logo, Startseite, Serien, Filme, Live-TV, Meine Liste und „Bibliotheken ▾“, rechts nur Cast, Suche und Profil (Handy: Menü, Suche, Profil). In der iOS-App entfällt die eigene Tab-Leiste, weil die App schon eine hat.
- **Profilmenü im Quallenkino-Stil** (ab 12.23.0): oben die anderen gespeicherten Profile mit Bild (ein Klick wechselt) und „Profile verwalten“, dann „Profilbild wählen“, „Erinnerungen im Kalender“, SyncPlay und Knöpfe anderer Plugins (z. B. „Zufälliger Titel“ von Jellyfin Enhanced), danach die Einträge von Jellyfin; „Abmelden“ abgesetzt unten.
- **Überspringen wie in Quallenkino** (ab 12.18.4): Bei Intro, Abspann, Rückblick oder Vorschau (Media Segments, z. B. von Intro Skipper) erscheint im Player „Intro überspringen“ mit Fortschrittsbalken; nach 5 Sekunden wird automatisch gesprungen. Abbrechen mit ×, Esc oder Zurück. Der Player bekommt mit den Quallenkino-Farben den Quallenkino-Look.
- **Detailseite** (ab 12.18.5, Folgen und Staffeln ab 12.18.7): Filme, Serien, Folgen und Staffeln zeigen auch über Bibliothek oder Suche die Quallenkino-Ansicht (Staffeln und Folgen, ähnliche Titel, Besetzung). Die Knöpfe von Jellyfin (Gesehen, Trailer, Download, ⋮ mit Löschen, Bearbeiten, Zur Sammlung hinzufügen …) stehen ab 12.18.6 direkt im neuen Design. „Alle Optionen“ führt bei Filmen und Serien zur Jellyfin-Detailseite mit Versionen und Ton/Untertitel. Bei Sendungen stehen ab 12.25.0 Merken, Serie merken, Aufnehmen, Serie aufnehmen und „Sender einschalten“ direkt in der Ansicht – die alte Programmseite wird nicht mehr gebraucht.
- **Quallenkino-Player (ab 12.19.0, ab 12.29.0 standardmäßig für alle)** – Schalter „Quallenkino-Player für alle Benutzer“ im Reiter „Einstellungen“ (aus = nur die dort ausgewählten Benutzer), im Browser und in den Jellyfin-Apps für iOS und Android (ab 12.31.2 steuert er Jellyfins Player direkt, auch in der iOS-App): eigene Steuerleiste (Zurück und Titel oben, große Play/Pause- und ±10-Sekunden-Knöpfe, Zeitleiste mit Vorschaubildern, Ton & Untertitel in einem Menü, Folgenliste, Geschwindigkeit, Vollbild, ⋮ für Qualität, Kapitel, Bild in Bild). Abgespielt wird weiter von Jellyfin. Live-TV ohne Spulen, mit Senderwechsel; auf dem Handy Doppeltipp links/rechts = ±10 s. Unter ⋮ „Klassischer Player“ (pro Gerät), zurück über „Quallenkino-Player einschalten“ im Profilmenü.
- „Abspielen“ startet die Wiedergabe im selben Browser bzw. in derselben App. Ganz unten auf der Startseite kann jeder Benutzer zur klassischen Ansicht zurückwechseln.
- **Diagnose (ab 12.30.0)** – Schalter „Diagnose: Fehler der Weboberfläche ins Serverlog schreiben“ im Reiter „Einstellungen“ (Standard aus). Browser und Apps melden dann Skriptfehler und, nach einem Absturz (z. B. wenn die iOS-App die Seite neu lädt), die letzten Schritte davor: Ansicht, Abspielen, Player, Video-Ereignisse, Lebenszeichen alle 5 s. Im Serverlog als Warnung „Quallenkino-Diagnose [Benutzer · App Version · Gerät]“. Zugangsschlüssel werden entfernt. Nach der Fehlersuche wieder ausschalten.
- **Lange Beschreibungen** (z. B. YouTube-Kanäle mit vielen Links) zeigt die Detailansicht ab 12.29.2 auf 4 Zeilen gekürzt mit „Mehr“; lange Links brechen um, die Folgenliste bleibt auch auf dem Handy in der Bildschirmbreite.
- Skript und Stylesheet werden mit der Plugin-Version im Link geladen (`?v=…`). So kommen Updates auch in Apps mit Web-Zwischenspeicher (z. B. der iOS-App) sofort an.

### „Wer schaut?“ – Profilauswahl (ab 12.21.0)

Damit jede Person ihre eigenen Vorschläge bekommt, zeigt Jellyfin Web beim Öffnen eine Profilauswahl mit Profilbildern – wie bei Streaming-Diensten. Einschalten im Reiter **„Einstellungen“**, Abschnitt Jellyfin Web: **„Wer schaut?“ – Profilauswahl** (Standard: aus; benötigt ebenfalls **File Transformation**).

- **Ohne PIN:** Jedes Profil meldet sich einmal über **„Profil hinzufügen“** an, danach genügt ein Klick zum Wechseln. Die anderen Profile bleiben dabei angemeldet.
- Die Anmeldungen werden **nur im jeweiligen Browser** gespeichert (wie Jellyfin selbst seine Anmeldung speichert) – nicht auf dem Server und nicht in anderen Geräten.
- Die Auswahl erscheint einmal pro geöffnetem Tab bzw. App-Start, jederzeit erreichbar über **„Profil wechseln“** im Profilmenü. Esc schließt sie.
- **„Profile verwalten“** entfernt ein Profil nur aus diesem Browser. Wer sich in Jellyfin über „Abmelden“ abmeldet, macht den gespeicherten Zugang ungültig; das Profil verschwindet dann beim nächsten Klick mit einem Hinweis und kann neu hinzugefügt werden.
- Ohne Profilbild erscheint der Anfangsbuchstabe auf einer Farbe.
- `/LocalRecs/Web/Config` meldet `"ProfilesEnabled": true|false` – so können auch eigene Apps die Einstellung übernehmen.

### Profilbilder aus Figuren (ab 12.22.0)

Jeder Benutzer kann sich ein Profilbild aus einer **Figur** und einer **Hintergrundfarbe** zusammenstellen. In Jellyfin Web geht das im Profilmenü unter **„Profilbild wählen“** oder in „Wer schaut?“ → **„Profile verwalten“** → Profil antippen. Der Server erzeugt daraus ein rundes PNG (512 × 512) und setzt es als Jellyfin-Profilbild. Jeder ändert nur sein eigenes Bild, an den Benutzerrechten ändert sich nichts. Wem in Jellyfin „Benutzereinstellungen ändern“ verboten ist, der darf auch hier nichts ändern. Quallenkino nutzt dieselbe Schnittstelle.

Einstellungen im Reiter **„Einstellungen“**, Abschnitt Jellyfin Web:
- **„Profilbilder aus Figuren wählen“** (Standard: an).
- **Katalog der Profilbilder:** leer bedeutet der Standard aus [Jellyfin-Welten/icons](https://github.com/DrdotHouse2106/Jellyfin-Welten/tree/main/icons) mit rund 100 Figuren: OpenMoji (CC BY-SA 4.0) und eigene Zeichnungen (Superhelden, Märchen & Magie, Weltraum-Abenteuer). Von diesem Katalog kommt auch die Farbpalette.
- **Weitere Kataloge** (eine Adresse pro Zeile): Deren Kategorien werden ergänzt, z. B. aus einem eigenen Icon-Repository.

Format der `icons.json`:

```json
{
  "format": 1,
  "name": "Meine Profilbilder",
  "license": "Lizenz-/Quellenhinweis",
  "colors": ["#e50914", "#2e96e0"],
  "categories": [
    { "id": "tiere", "name": "Tiere", "icons": [
      { "id": "katze", "name": "Katze", "file": "tiere/katze.png" },
      { "id": "hund", "name": "Hund", "url": "https://example.org/hund.png", "fit": "cover" }
    ] }
  ]
}
```

- **IDs:** nur `a–z`, `0–9`, `-` und `_`.
- **`file`** steht relativ zur Katalog-Adresse, **`url`** ist absolut (http/https).
- **Bilder:** PNG, JPEG oder WebP, quadratisch, höchstens 2 MB. Für Figuren am besten PNG mit transparentem Hintergrund (z. B. 256 × 256).
- **`fit`:** `contain` (Standard) setzt die Figur mit 80 % Größe auf die Farbe, `cover` füllt den Kreis.
- **Zwischenspeicher:** Der Server hält den Katalog 6 Stunden und die Bilder 7 Tage vor. Ist die Quelle nicht erreichbar, nutzt er den letzten Stand.

Schnittstelle:

```
GET  /LocalRecs/Avatars                    → { Name, License, Colors, Categories: [{ Id, Name, Icons: [{ Id: "tiere/katze", Name, Fit, ImageUrl }] }], Current? }
GET  /LocalRecs/Avatars/Icons/{kat}/{icon} → Bild (ohne Anmeldung)
POST /LocalRecs/Avatars/Me  { "Icon": "tiere/katze", "Color": "#2e96e0" } → { PrimaryImageTag }   (400: nicht im Katalog, 503: Katalog/Bild nicht erreichbar)
```

`/LocalRecs/Web/Config` meldet `"Avatars": true|false` und `"AvatarCatalogUrl"`.

### Live-TV-Erinnerungen und Kalender (ab 12.20.0)

Sendungen aus dem Programmführer lassen sich merken – einzeln oder als ganze Serie (alle künftigen Ausstrahlungen mit gleichem Titel, gleiche Folgen nur einmal). Gemerktes erscheint in einem **Kalender-Abo (iCal)** pro Benutzer, mit Erinnerung vor Beginn (Standard 10 Minuten). So erinnert das Handy über den eigenen Kalender, ganz ohne zusätzliche App. In Jellyfin Web gibt es dafür mehrere Stellen:
- **Glocke** auf den Kacheln künftiger Sendungen, auf der Startseite und ab 12.24.0 auch im Live-TV-Bereich von Jellyfin (Tab „Programme“). Auf dem PC hat ab 12.25.1 auch die Vorschau-Karte eine Glocke (sie verdeckt sonst die Glocke der Kachel) – für alle Benutzer, auch ohne Aufnahmerecht.
- **Kontextmenü** einer künftigen Sendung (⋮ oder Rechtsklick): „An Sendung erinnern“ und „An Serie erinnern“.
- **Dialog einer Sendung im Fernsehprogramm** sowie Info-Ansicht: „Merken“ und „Serie merken“.
- **Aufnehmen** (ab 12.25.0): In der Info-Ansicht einer Sendung gibt es „Aufnehmen“ und bei Serien „Serie aufnehmen“, auf der Vorschaukarte einen roten Aufnahme-Knopf. Der Status wird angezeigt („Aufnahme geplant ✓“), ein zweiter Klick bricht ab. Es gelten die Aufnahme-Vorgaben von Jellyfin (Vor- und Nachlauf, Pfad). Die Knöpfe sieht nur, wer in Jellyfin Aufnahmen verwalten darf. „Sender einschalten“ ersetzt bei Sendungen „Alle Optionen“, die alte Programmseite wird nicht mehr gebraucht.
- **Profilmenü:** „Erinnerungen im Kalender“ (Abonnieren, Link kopieren, QR-Code, Link erneuern).

In den Programm-Reihen von Jellyfin (Serien, Filme, Sport, Für Kinder, Nachrichten) stehen ab 12.24.0 nur noch künftige Sendungen, nach Beginn sortiert. Laufende Sendungen stehen in „Gerade läuft“.

```
GET    /LocalRecs/Reminders                    → [{ ProgramId, ChannelId, ChannelName, SeriesKey, Name, EpisodeTitle, Overview, StartDate, EndDate, IsSeries }]
                                                 (gemerkte Sendungen + künftige Ausstrahlungen gemerkter Serien, nach Beginn sortiert, bis 2 Tage nach Ende)
POST   /LocalRecs/Reminders/{programId}        → 204 gemerkt (auch wenn schon gemerkt) · 404 unbekannte Sendung · 409 schon vorbei · 403 kein Live-TV
DELETE /LocalRecs/Reminders/{programId}        → 204 entfernt · 404 war nicht gemerkt
GET    /LocalRecs/Reminders/Series             → [{ Key, Name, Added }]
POST   /LocalRecs/Reminders/Series/{programId} → 200 { Key, Name, Added } – Serie dieser Sendung merken · 404 unbekannte Sendung
DELETE /LocalRecs/Reminders/Series/{key}       → 204 · 404 (statt Key geht auch die Id einer Sendung der Serie)
GET    /LocalRecs/Calendar                     → { Url, WebcalUrl, PageUrl, QrUrl } – Abo-Links (Schlüssel wird beim ersten Aufruf erzeugt)
POST   /LocalRecs/Calendar/Renew               → wie oben, mit neuem Schlüssel (alter Link wird ungültig)
GET    /LocalRecs/Calendar/Qr?target=page|webcal|https → QR-Code als SVG (skalierbar, mit Ruhezone; Standard ab 12.23.0: page)
GET    /LocalRecs/Calendar/{token}.ics         → iCal-Feed ohne Anmeldung (der geheime Schlüssel schützt ihn)
GET    /LocalRecs/Calendar/{token}/Abonnieren  → Abo-Seite ohne Anmeldung: Knopf „Im Kalender abonnieren“ (webcal), Link für Google Kalender, „Link kopieren“
```

- **iPhone:** Die Kamera-App öffnet webcal-Links aus einem QR-Code nicht („Im Kalender öffnen“ bewirkt nichts). Deshalb führt der QR-Code ab 12.23.0 auf die Abo-Seite; dort öffnet der Knopf in Safari den Dialog „Kalenderabo hinzufügen“. In der iOS-App öffnet „Im Kalender abonnieren“ ebenfalls diese Seite. Ein leerer Kalender enthält einen unsichtbaren Platzhalter (1.1.2000), weil iOS Abos ganz ohne Ereignis ablehnt.
- Ids (`programId`) mit oder ohne Bindestriche. Alle Endpunkte außer dem `.ics`-Feed brauchen die normale Anmeldung (`Authorization: MediaBrowser Token="…"`, bei Bild-Links `?ApiKey=…`).
- `/LocalRecs/Web/Config` meldet `"Reminders": true`, Apps können die Funktion darüber erkennen.
- Die Abo-Adresse ergibt sich aus der Anfrage (beachtet `X-Forwarded-Proto`/`X-Forwarded-Host`) oder aus der Einstellung „Öffentliche Adresse für Kalender-Links“.
- iPhone: `webcal://`-Link bzw. QR-Code → Abo bestätigen. Google Kalender: https-Link am PC unter „Weitere Kalender → Per URL“ einfügen.

### Fernsehprogramm-Anbieter „LocalRecs EPG“ (ab 12.26.0)

Der eingebaute XMLTV-Anbieter von Jellyfin entscheidet nur über die Liste „Filmkategorien“, ob eine Sendung ein Film ist. Steht dort z. B. „Komödie“, wird jede Comedy-Serie zum Film. Jellyfin verwirft dann Folge und Folgentitel, und Aufnahmen landen unter `Filme/` statt unter `Serien/…/Season N/`. MagentaTV kennzeichnet Filme nur über das Genre, deshalb lässt sich das mit der Liste allein nicht lösen.

**LocalRecs EPG** ist ein eigener Fernsehprogramm-Anbieter für XMLTV-Quellen (URL oder Pfad, auch `.gz`). Er ersetzt den eingebauten Anbieter, die Senderzuordnung bleibt erhalten. Statt einer Kategorienliste entscheiden **Regel-Bausteine**, jeder einzeln schaltbar (Reiter **„Fernsehprogramm“**):

| Baustein | Wirkung |
|---|---|
| **Serie: Folgennummer** | `episode-num` (xmltv_ns oder onscreen wie `S02E02`, „Staffel 2, Folge 2“, `2x02`) → Serienfolge mit Staffel, Folge und Folgentitel (`sub-title`), egal welche Kategorie |
| **Film** | ohne Folgennummer, mindestens 75 Minuten (einstellbar) **und** ein Film-Merkmal: Regie (`credits/director`), IMDb-Link (`url`) oder eine Film-Kategorie (Standard „Spielfilm“). Das Produktionsjahr (`date`) allein zählt ab 12.27.0 nur, wenn ausdrücklich eingeschaltet, weil Shows und Teleshopping oft eins haben. Nie bei Sport oder Nachrichten. Lange Sendungen ohne Merkmal bleiben „Sonstiges“ |
| **Serie: Folgentitel** | kein Film und kein Sport, aber ein `sub-title` → Serienfolge |
| **Nie Film** (ab 12.29.0) | Kategorien wie Dokumentation, Doku, Reportage, Magazin – auch mit Regie und Überlänge |
| **Weder Film noch Serie** (ab 12.29.0) | Teleshopping über Kategorie (Teleshopping, Shopping …) oder Sender (HSE, QVC, 1-2-3.tv, Juwelo …) |
| **Kinder / Nachrichten / Sport** | über Kategorie (als ganzes Wort) **oder** Sender (z. B. KiKA = immer Kinder, Sky Sport = Sport) |

- **Film-Merkmale aus der zweiten Quelle (ab 12.27.0):** Viele EPG-Werkzeuge (z. B. m3u-editor) geben ein abgespecktes XMLTV ohne Regie, Jahr und IMDb weiter, ihre Sender-IDs passen aber zur Tuner-Liste. Deshalb kommen Sendungen und Sender-IDs aus der Programmquelle, Regie, Jahr und IMDb dagegen aus der **„XMLTV-Quelle für Darsteller“** (Reiter „Startseite“). Der Abgleich läuft über Startzeit (±15 Minuten) und Titel, die Sender-IDs dürfen sich unterscheiden. Beim ersten Laden nach dem Start wartet der Anbieter bis zu 2 Minuten auf diese Datei.
- **Genres bleiben vollständig erhalten**, sie schalten nur nicht mehr „Film“ ein.
- Filme bekommen das Jahr aus `date` und die IMDb-ID aus der URL. Serien bekommen eine Serien-Kennung, damit „Serie aufnehmen“ funktioniert.
- Die Kennungen der Sendungen (Id, ShowId, SeriesId) sind dieselben wie beim eingebauten Anbieter. Geplante Aufnahmen und Serienaufnahmen bleiben beim Umstieg erhalten.

**Einrichten (Reiter „Fernsehprogramm“):**
1. Unter „Senderzuordnung, Tuner und Quelle übernehmen von“ den bisherigen XMLTV-Anbieter wählen. Die Quelle nur eintragen, wenn sie sich ändern soll.
2. **„LocalRecs EPG einrichten“**: Der Anbieter wird angelegt, steht an erster Stelle (Jellyfin nutzt ihn vor dem alten) und die Aufgabe „Fernsehprogramm aktualisieren“ startet.
3. Mit **„Regeln testen“** prüfen. Der Test zeigt Anzahl Serien, Filme und Sonstiges, wie viele Sendungen Merkmale aus der Darsteller-Quelle bekommen haben, sowie Beispiele mit Begründung, z. B. „Serie: Folgennummer S02E02“ oder „Film: 105 Min. und Regie (…)“. Mit Titel-Filter und Auswahl wie „nur Filme“.
4. Passt alles, den alten XMLTV-Anbieter unter Dashboard → Live-TV entfernen.

Nach geänderten Regeln: **„Speichern“** und **„Fernsehprogramm neu laden“**. Jellyfin übernimmt dann die neue Einordnung für alle Sendungen.

```
GET  /LocalRecs/Epg/Providers                       → Fernsehprogramm-Anbieter von Jellyfin (Reihenfolge, Senderzuordnungen)
POST /LocalRecs/Epg/Setup   { Path?, FromProviderId? } → LocalRecs EPG anlegen/aktualisieren (Zuordnung übernehmen, an erste Stelle)
POST /LocalRecs/Epg/Refresh                          → Quelle neu laden und Fernsehprogramm aktualisieren
GET  /LocalRecs/Epg/Test?search=&kind=series|movie|other|kids|news|sports&limit=50&path=
                                                     → Zählung und Beispiele mit Begründung (nur Admins)
```

**EPG-Quelle:** Die Datei kommt z. B. von m3u-editor oder einem eigenen Skript. [`extras/epg-enrich`](extras/epg-enrich/) enthält die Fassung für epgshare01 mit Bundesliga-Paarungen. Es mischt auf Wunsch 14 Tage MagentaTV ein (über iptv-org/epg, Dateien unter [`extras/epg-enrich/magentatv`](extras/epg-enrich/magentatv/)) und ergänzt Bundesliga-Paarungen.

### Schnittstelle für TV-Apps

**Identitätsnachweis im Heimnetz (ab 12.35.0):** Bevor eine App ihren Zugangsschlüssel unverschlüsselt (http) an eine lokale Adresse schickt, kann sie prüfen, dass dort wirklich ihr Server antwortet:

```
GET /LocalRecs/Identity/Challenge?nonce=<32 Zufallsbytes als 64 Hex-Zeichen, klein>
Header: X-Device-Id: <Jellyfin-Geräte-ID der App>
→ { "ServerId": "<ohne Bindestriche, klein>", "Host": "<Adresse, wie der Server sie sieht>", "Proofs": ["<hex>", …] }
```

`Proof = hex(HMAC-SHA256(Schlüssel = SHA-256(UTF-8 Zugangsschlüssel), Nachricht = UTF-8 "nonce|ServerId|host"))`, `host` = die angesprochene Adresse in Kleinbuchstaben mit Port, z. B. `192.168.1.10:8096`. Die App rechnet den Proof selbst aus und schickt ihren Schlüssel nur, wenn er in `Proofs` steht (ein Eintrag je Anmeldung des Geräts) und `ServerId` stimmt. Ohne Anmeldung, aber nur bei **direkter Verbindung aus dem lokalen Netz** (laut Jellyfins Netzwerkeinstellung, keine Weiterleitungs-Header wie `X-Forwarded-For`) – sonst 403. So kann ein falscher Server die Anfrage nicht über das Internet an den echten durchreichen; die angesprochene Adresse ist Teil des Proofs. Höchstens 30 Anfragen pro Minute und Adresse (429). Unbekanntes Gerät: leere `Proofs`.

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

**Welten wie bei Disney+ (ab 12.10.0):** Marvel, Star Wars, DC, Pixar, Disney, DreamWorks, Illumination, Harry Potter, Mittelerde, James Bond, Jurassic Park, Studio Ghibli. Welche Titel dazugehören, steht in einer **gepflegten Liste auf GitHub** – ab 12.12.0 im eigenen Repository [Jellyfin-Welten](https://github.com/DrdotHouse2106/Jellyfin-Welten), dort wöchentlich vollständig aus TMDB aktualisiert (auch angekündigte Titel); von Hand ergänzte Titel und Ausschlüsse stehen in dessen `regeln.json` und gehen nie verloren: Studios, Namen von Jellyfin-Sammlungen und einzelne Titel per TMDB-/IMDb-/TVDB-ID, dazu Ausschlüsse. Das Plugin lädt die Liste täglich und behält bei Fehlern den letzten Stand; die Bibliothek wird nicht verändert. Wer eine andere Zuordnung möchte, trägt im Reiter **„Einstellungen“** unter **„Welten-Liste“** einen Link zu einer eigenen Datei ein (leer = nur die Regeln aus dem Textfeld). Im Reiter **Welten** steht pro Welt, welche Titel warum dazugehören, dazu Vorschläge für fehlende Titel mit fertiger Zeile zum Einfügen in die Liste. Angezeigt werden nur Welten mit mindestens 5 Titeln.

```
GET /LocalRecs/Worlds                              → Welten des Benutzers (zuletzt geschaute zuerst)
GET /LocalRecs/Worlds/{id}/Recommendations?limit=20 → Titel der Welt, persönlich sortiert
GET /LocalRecs/Worlds/Recent?limit=20              → zuletzt geschaute Welt mit Titeln („Mehr aus dem …-Universum“), sonst 204
GET /LocalRecs/Worlds/{id}/Items?sort=year|year-asc|name|rating|added&type=movies|series&startIndex=0&limit=100
                                                   → alle Titel der Welt (auch gesehene), z. B. für die Welt-Seite (ab 12.15.0)
```

- Sortiert nach dem Geschmack des Benutzers (ohne Verlauf nach Bewertung). Der nächste ungesehene Teil einer begonnenen Filmreihe steht vorne. Gesehene, angefangene und mit „Gefällt mir nicht“ bewertete Titel fehlen, Serien bleiben bis zur letzten Folge.
- Die Begründungen der Welt-Titel stehen ebenfalls in `/LocalRecs/Recommendations/Reasons`.
- **Logos (ab 12.13.0):** Hat eine Welt in der Liste ein Logo (Link, z. B. auf ein TMDB-Firmenlogo), steht in `/LocalRecs/Worlds` das Feld `LogoUrl` (`/LocalRecs/Worlds/{id}/Logo`). Das Plugin lädt das Bild, speichert es auf dem Server zwischen und liefert es ohne Anmeldung aus – Apps sprechen also nur den Jellyfin-Server an. Erlaubt sind nur https-Adressen bzw. Bilder neben einer lokalen Liste. Ab 12.14.0 kann eine Welt mehrere Logos in Reihenfolge haben, darunter Reihen-Logos von **fanart.tv** (`fanart:collection/{TMDB-Sammlung}`, z. B. Star Wars, Wizarding World, Mittelerde, Jurassic Park, James Bond) – dafür im Reiter **„Einstellungen“** den eigenen fanart.tv-API-Schlüssel eintragen; ohne Schlüssel gelten die Studio-Logos von TMDB. Frühere IDs einer Welt (`aliase`, z. B. `harry-potter` nach der Umbenennung in „Wizarding World“) funktionieren in allen Adressen weiter.
- **Welten-Trailer (ab 12.16.0):** `/LocalRecs/Worlds` nennt je Welt `TrailerItemId`, `TrailerItemName` und `TrailerId` – einen Titel der Welt mit **lokalem Trailer** (Datei im Ordner `Trailers/` bzw. mit Endung `-trailer`, z. B. von Trailarr) und dessen ersten Trailer. Apps können ihn wie bei Disney+ beim Fokus auf eine Welt-Kachel oder im Kopf der Welt-Seite im Hintergrund abspielen (`/Videos/{TrailerId}/stream` bzw. PlaybackInfo). Gewählt wird einer der sieben zuletzt hinzugefügten Titel mit Trailer – ab 12.16.1 täglich wechselnd, innerhalb eines Tages gleich; in der Liste lässt sich je Welt ein Titel festlegen (`"trailer": 671` als TMDB-ID oder `{ "imdb": "…", "typ": "film" }`). Ohne lokalen Trailer bleiben die Felder leer.
- `TmdbCollectionIds` (ab 12.10.1) nennt die TMDB-Sammlungen einer Welt (per Name zugeordnet oder mit mindestens einem Titel der Welt). Darüber kann eine App fehlende Teile z. B. bei Jellyseerr nachschlagen und zum Anfragen anbieten.

**Daumen hoch/runter (ab 12.6.0):** „Gefällt mir“ zählt bei gesehenen Titeln wie ein Favorit. Bei nie gespielten Titeln zählt es ab 12.17.0 nur schwach (30 % eines gesehenen Titels, ohne Favoriten-Bonus) – Jellyfin Enhanced nutzt „Gefällt mir“ z. B. als Merkliste für angefragte Titel, das ist kein echtes Urteil. „Gefällt mir nicht“ wird nie empfohlen und macht ähnliche Titel seltener. Bewerten lässt sich in der TV-App auf der Detailseite (gespeichert wird in Jellyfin selbst).

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

## Datenquellen

Daten und Bilder: [TMDB](https://www.themoviedb.org). Dieses Produkt nutzt die TMDB-API, ist aber nicht von TMDB unterstützt oder zertifiziert.

Ähnliche Titel und persönliche Empfehlungen (optional): [Trakt](https://trakt.tv).

Logos der Welten (optional): [fanart.tv](https://fanart.tv).

## Unterstützen

Die Entwicklung kostet Zeit und Geld (Server, Testgeräte, KI-Werkzeuge). Wenn dir das Projekt gefällt, freue ich mich über eine kleine Spende:

[![Spenden via PayPal](https://img.shields.io/badge/Spenden-PayPal-0070ba?logo=paypal&logoColor=white)](https://paypal.me/DrdotHouse)

### 🎮 Auch eine Zocker-Ader?

Dann ist vielleicht mein anderes Projekt genau das Richtige für dich: **[ZockerDB](https://zockerdb.de)** –
deine Spielesammlung an einem Platz. Spiele, Konsolen und Zubehör erfassen (auch per Barcode-Scan mit
dem Handy), bewerten und verwalten, mit Preisverlauf, Editionen und PAL/USK-Angaben. Direkt auf
[zockerdb.de](https://zockerdb.de) nutzen oder als [ZockerDB Basis](https://github.com/DrdotHouse2106/ZockerDBBasis) auf dem eigenen Server hosten.

## Lizenz

GNU General Public License v3.0 – siehe [LICENSE.txt](LICENSE.txt). Ursprünglicher Autor: Roger Pharr ([rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)).

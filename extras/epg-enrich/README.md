# EPG-Anreicherung (optional)

`enrich_epg.py` ist ein kleines Hilfsskript für den deutschen EPG von
[epgshare01](https://epgshare01.online/) (`epg_ripper_DE1.xml.gz`). Es ist
**kein Teil des Plugins**, passt aber gut zu „Heute im TV für dich“.

## Was es macht

- **MagentaTV einmischen (optional, 14 Tage):** Mit `--merge <datei>` bzw. der
  Umgebungsvariable `EPG_MERGE_FILE` (Beispiel: `/data/epg-iptvorg/magenta.xml`)
  ersetzt das Skript für jeden Sender, der dort Sendungen hat, die Sendungen aus
  epgshare01. Doppelte (gleicher Sender und Beginn) zählen nur einmal. Fehlt die
  Datei oder ist sie älter als 48 Stunden, bleibt es bei epgshare01. `--merge ""`
  schaltet das ab. Wie die Datei entsteht, steht unten unter „MagentaTV“.
- **MagentaTV-Bundesliga:** Titel wie „Live BL: FC Augsburg - 1. FC Union Berlin,
  7. Spieltag“ liefern die Paarungen direkt.
- **Sky Sport Bundesliga:** Die Einzelkanäle haben im Original nur
  „Live: BL“ bzw. „Live: 2. BL“ als Titel. Das Skript sammelt die Paarungen aus
  den übrigen Bundesliga-Sendungen (Highlights, Countdown, Kompakt …), bestimmt
  über das Stadion in der Beschreibung die Heimmannschaft und setzt den Titel auf
  z. B. „Bundesliga: Bayern München – Borussia Dortmund“. Ohne Treffer heißt
  es „Bundesliga: Heimspiel <Verein>“. So kann LocalRecs Spiele nach Verein
  empfehlen.
- Alles andere bleibt unverändert, auch Darsteller, Regie, Jahr und Land
  (`<credits>`, `<date>`, `<country>`).

## Zusammenspiel mit LocalRecs

Viele EPG-Werkzeuge (z. B. m3u-editor) geben `<credits>` nicht an Jellyfin
weiter, und Jellyfin speichert für Live-TV-Sendungen ohnehin keine Personen.
Ab LocalRecs 12.7.0 kann das Plugin die Darsteller deshalb direkt aus einer
XMLTV-Datei lesen: Einstellung **„XMLTV-Quelle für Darsteller“**
(`LiveTvXmltvSource`), Pfad oder http(s)-URL, `.xml` oder `.xml.gz`.

Die Ausgabe dieses Skripts eignet sich dafür. Binde den Ordner z. B.
schreibgeschützt in den Jellyfin-Container ein und trage den Pfad ein:

```yaml
# docker-compose.yml, Dienst jellyfin
volumes:
  - /pfad/zum/epg-ordner:/epg-source:ro
```

```
XMLTV-Quelle für Darsteller: /epg-source/epg_DE1_enriched.xml.gz
```

## Aufruf

Python 3.9+, keine zusätzlichen Pakete.

```sh
python3 enrich_epg.py --out /pfad/zum/epg-ordner/epg_DE1_enriched.xml.gz
# andere Quelle:
python3 enrich_epg.py --out /pfad/epg.xml.gz --source https://…/epg.xml.gz
```

Per Cron kurz vor dem EPG-Abgleich deines EPG-Werkzeugs bzw. von Jellyfin,
z. B. alle 6 Stunden:

```
50 3,9,15,21 * * * /usr/bin/python3 /pfad/enrich_epg.py --out /pfad/zum/epg-ordner/epg_DE1_enriched.xml.gz >> /pfad/enrich.log 2>&1
```

Die Ausgabe wird atomar ersetzt (Temp-Datei + `os.replace`), Leser sehen also
nie eine halb geschriebene Datei. Bei einem Fehler bleibt die letzte gute Datei
liegen, das Log zeigt `FEHLER: …` und der Exit-Code ist 1. Pro Lauf eine
Log-Zeile, z. B.:

```
2026-10-17 21:47:26 OK: MagentaTV: 108 Sender, 61234 Sendungen; 38 Spiele benannt, 2 ohne Paarung, 272 Paarungen gefunden
```

## MagentaTV (14 Tage) über iptv-org/epg

Freie deutsche EPG-Quellen reichen nur 3–4 Tage. [iptv-org/epg](https://github.com/iptv-org/epg)
liest u. a. die Programmseite von MagentaTV und liefert 14 Tage mit Beschreibung, Bildern,
Regie und – mit dem Patch – Darstellern. Die Dateien liegen unter [`magentatv/`](magentatv/):

| Datei | Zweck |
|---|---|
| [`run.sh`](magentatv/run.sh) | Holt alle 12 Stunden 14 Tage (`--maxConnections=1` – mit 3 Verbindungen drosselt MagentaTV und liefert leere Sender-Tage), mischt mit `merge.mjs` und verwirft Ergebnisse unter 80 % des letzten Stands. Ergebnis: `/work/magenta.xml` |
| [`merge.mjs`](magentatv/merge.mjs) | „Nur aktualisieren“: Sender-Tage, die im neuen Abruf leer sind, kommen aus dem letzten Stand |
| [`magenta-actors.patch`](magentatv/magenta-actors.patch) | Ergänzt die Darsteller in der MagentaTV-Konfiguration von iptv-org/epg |
| [`magenta.channels.beispiel.xml`](magentatv/magenta.channels.beispiel.xml) | Beispiel: MagentaTV-Sender (`site_id`) → eigene EPG-ID (`xmltv_id`, muss zur epgshare01-Quelle bzw. zum m3u-editor passen). Alle Sender: `sites/web.magentatv.de/web.magentatv.de.channels.xml` in iptv-org/epg |

Einrichten (Docker):

```bash
mkdir -p epg-iptvorg && cp magentatv/* epg-iptvorg/
mv epg-iptvorg/magenta.channels.beispiel.xml epg-iptvorg/magenta.channels.xml   # anpassen!
cd epg-iptvorg && git clone https://github.com/iptv-org/epg.git epg
cd epg && git checkout 6f6cfe2 && git apply --ignore-whitespace ../magenta-actors.patch
```

```yaml
# docker-compose.yml
epg-grabber:
  image: node:22-alpine
  container_name: epg-grabber
  working_dir: /work/epg
  command: sh /work/run.sh
  environment:
    TZ: Europe/Berlin
  volumes:
    - ./epg-iptvorg:/work
  restart: unless-stopped
```

Danach `enrich_epg.py` mit `--merge /pfad/epg-iptvorg/magenta.xml` aufrufen (Cron wie oben,
jeweils 10 Minuten vor dem Abgleich des EPG-Werkzeugs). Im m3u-editor `DEFAULT_EPG_DAYS=14`
setzen (Standard 7, danach `php artisan app:clear-playlist-epg-files --force`), in Jellyfin
„Tage im Fernsehprogramm“ = 14.

Der m3u-editor gibt `<credits>`, `<date>` und `<url>` nicht weiter. Das Plugin liest die
angereicherte Datei deshalb direkt („XMLTV-Quelle für Darsteller“) – für Darsteller und für die
Film-Merkmale des eigenen Fernsehprogramm-Anbieters „LocalRecs EPG“.

Ohne MagentaTV-Daten (bleiben bei epgshare01) waren zuletzt: Servus TV, DAZN, Sky Sport Austria,
Syfy, Silverline, Nickelodeon, Sky Replay.

## Pflege

Neue Stadien bzw. Aufsteiger trägst du im Wörterbuch `STADIUMS` im Skript ein
(Teilstring des Stadionnamens → Suchwort für die Heimmannschaft).

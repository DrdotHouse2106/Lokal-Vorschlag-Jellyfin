# EPG-Anreicherung (optional)

`enrich_epg.py` ist ein kleines Hilfsskript für den deutschen EPG von
[epgshare01](https://epgshare01.online/) (`epg_ripper_DE1.xml.gz`). Es ist
**kein Teil des Plugins**, passt aber gut zu „Heute im TV für dich“.

## Was es macht

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
2026-10-17 21:47:26 OK: 38 Spiele benannt, 2 ohne Paarung, 272 Paarungen gefunden
```

## Pflege

Neue Stadien bzw. Aufsteiger trägst du im Wörterbuch `STADIUMS` im Skript ein
(Teilstring des Stadionnamens → Suchwort für die Heimmannschaft).

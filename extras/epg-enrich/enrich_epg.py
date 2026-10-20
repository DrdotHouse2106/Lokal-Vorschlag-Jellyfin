#!/usr/bin/env python3
"""Reichert das epgshare01-DE1-EPG mit Bundesliga-Paarungen an.

Die Sky-Sport-Bundesliga-Einzelkanaele haben im EPG nur "Live: BL" und das
Stadion in der Beschreibung. Dieses Skript sammelt alle Paarungen aus den
uebrigen Sendungen (Highlight-Shows, Countdown, Kompakt ...), bestimmt ueber
das Stadion die Heimmannschaft und setzt die passende Paarung als Titel.

Laeuft per Cron vor dem EPG-Sync (z. B. des m3u-editors). Bei Fehlern bleibt
die letzte erzeugte Datei unveraendert liegen.

Aufruf:
    enrich_epg.py --out /pfad/epg_DE1_enriched.xml.gz [--source URL]

Alle uebrigen Sendungen bleiben unveraendert, auch <credits>, <date> und
<country>. LocalRecs (ab 12.7.0) kann die Datei als "XMLTV-Quelle fuer
Darsteller" direkt lesen.
"""
import argparse
import gzip
import os
import re
import sys
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
from datetime import datetime

SOURCE_URL = "https://epgshare01.online/epgshare01/epg_ripper_DE1.xml.gz"
# Laengeres EPG (14 Tage) von MagentaTV, geholt vom Container epg-grabber (iptv-org/epg).
# Die Sender-IDs darin sind schon die von DE1; fuer diese Sender ersetzt es die DE1-Sendungen.
MERGE_FILE = os.environ.get("EPG_MERGE_FILE", "/opt/media-stack/epg-grabber/magenta.xml")
MERGE_MAX_AGE_H = 48
USER_AGENT = "Mozilla/5.0 (X11; Linux x86_64) mediastack-epg-enrich/1.0"

# Stadion (Teilstring der Beschreibung) -> Suchwort fuer die Heimmannschaft
STADIUMS = {
    "Allianz Arena": "Bayern",
    "Signal Iduna Park": "Dortmund",
    "Red Bull Arena": "Leipzig",
    "Deutsche Bank Park": "Frankfurt",
    "WWK Arena": "Augsburg",
    "Alten Försterei": "Union",
    "BayArena": "Leverkusen",
    "Borussia-Park": "Mönchengladbach",
    "PreZero Arena": "Hoffenheim",
    "Europa-Park Stadion": "Freiburg",
    "MHPArena": "Stuttgart",
    "Weserstadion": "Bremen",
    "Volkswagen Arena": "Wolfsburg",
    "Mewa Arena": "Mainz",
    "Voith-Arena": "Heidenheim",
    "RheinEnergieStadion": "Köln",
    "Volksparkstadion": "Hamburger SV",
    "Millerntor": "St. Pauli",
    "Home Deluxe Arena": "Paderborn",
    "Holstein-Stadion": "Kiel",
    "Ruhrstadion": "Bochum",
    "Fritz-Walter-Stadion": "Kaiserslautern",
    "Max-Morlock-Stadion": "Nürnberg",
    "Heinz von Heyden Arena": "Hannover",
    "Niedersachsenstadion": "Hannover",
    "Olympiastadion Berlin": "Hertha",
    "Böllenfalltor": "Darmstadt",
    "Ronhof": "Fürth",
    "SchücoArena": "Bielefeld",
    "Rudolf-Harbig-Stadion": "Dresden",
    "Avnet Arena": "Magdeburg",
    "MDCC-Arena": "Magdeburg",
    "Wildpark": "Karlsruhe",
    "Merkur Spiel-Arena": "Düsseldorf",
    "Veltins-Arena": "Schalke",
    "Stadion der Freundschaft": "Cottbus",
    "LEAG Energie Stadion": "Cottbus",
    "Bremer Brücke": "Osnabrück",
    "Ostseestadion": "Rostock",
    "Brita-Arena": "Wiesbaden",
    "Hafenstraße": "Essen",
    "Ursapharm-Arena": "Elversberg",
    "Preußenstadion": "Münster",
    "Donaustadion": "Ulm",
    "Jahnstadion": "Regensburg",
    "Eintracht-Stadion": "Braunschweig",
}

PAIR_RE = re.compile(
    r"([A-ZÄÖÜ0-9][\w.'ÄÖÜäöüß-]*(?: [\w.'ÄÖÜäöüß-]+){0,4}) – "
    r"([A-ZÄÖÜ0-9][\w.'ÄÖÜäöüß-]*(?: [\w.'ÄÖÜäöüß-]+){0,4}?)(?=,| \(|:|$| und |\.)"
)
LIVE_RE = re.compile(r"^(?:Live: )?(2\. )?BL$")
MAGENTA_RE = re.compile(r"^Live (2\. )?BL: (?!.*Konferenz)([^:,]+?) - ([^:,]+?)(?:,|$)")


def log(msg):
    print(f"{datetime.now():%Y-%m-%d %H:%M:%S} {msg}", flush=True)


def ts(s):
    return datetime.strptime(s[:14], "%Y%m%d%H%M%S")


def text(el, tag):
    c = el.find(tag)
    return (c.text or "") if c is not None else ""


def collect_pairs(root):
    """Alle Paarungen aus Bundesliga-Sendungen mit Liga und Sendezeit."""
    pairs = []
    for p in root.iter("programme"):
        title = text(p, "title")
        # MagentaTV: "Live BL: FC Augsburg - 1. FC Union Berlin, 7. Spieltag"
        m = MAGENTA_RE.match(title)
        if m:
            pairs.append(("2" if m.group(1) else "1", m.group(2).strip(), m.group(3).strip(), ts(p.get("start"))))
            continue
        if "Bundesliga" not in title or "Frauen" in title or "U19" in title:
            continue
        league = "2" if "2. Bundesliga" in title else "1"
        start = ts(p.get("start"))
        for field in (text(p, "sub-title"), text(p, "desc")):
            for home, away in PAIR_RE.findall(field):
                home = re.sub(r"^(?:Matchplan|Highlights XX[L:]*|Klassiker der Woche|Topspiel der Woche, \d+\. Spieltag|90in30): ?", "", home)
                home = re.sub(r"^.*\bmit ", "", home).strip()
                away = away.strip().rstrip(".")
                if len(home) > 2 and len(away) > 2:
                    pairs.append((league, home, away, start))
    return pairs


def find_pair(pairs, league, keyword, when):
    """Naechstgelegene Paarung (zeitlich), bei der das Suchwort die Heimmannschaft trifft."""
    kw = keyword.lower()
    best = None
    for lg, home, away, start in pairs:
        if lg != league or kw not in home.lower():
            continue
        dist = abs((start - when).total_seconds())
        if dist > 4 * 86400:
            continue
        if best is None or dist < best[0]:
            best = (dist, home, away)
    return best


def squash(s):
    return re.sub(r"[\s-]", "", s.lower())


def home_keyword(desc):
    d = squash(desc)
    for stadium, kw in STADIUMS.items():
        if squash(stadium) in d:
            return kw, stadium
    m = re.search(r"(?:in|,) ([A-ZÄÖÜ][a-zäöüß]+(?:-[A-ZÄÖÜ][a-zäöüß]+)?), Deutschland", desc)
    if m:
        return m.group(1), None
    return None, None


def merge(root, merge_file):
    """Sendungen aus merge_file uebernehmen: fuer jeden Sender, der dort Sendungen hat, ersetzen
    sie die aus DE1. Doppelte (gleicher Sender, gleicher Beginn) zaehlen nur einmal. Fehlt die
    Datei oder ist sie aelter als MERGE_MAX_AGE_H, bleibt es bei DE1."""
    if not merge_file or not os.path.exists(merge_file):
        return "ohne MagentaTV (keine Datei)"
    age_h = (datetime.now().timestamp() - os.path.getmtime(merge_file)) / 3600
    if age_h > MERGE_MAX_AGE_H:
        return f"ohne MagentaTV (Datei {age_h:.0f} h alt)"
    extra = ET.parse(merge_file).getroot()
    programmes, seen = [], set()
    for p in extra.iter("programme"):
        key = (p.get("channel"), p.get("start"))
        if key not in seen:
            seen.add(key)
            programmes.append(p)
    channels = {ch for ch, _ in seen}
    for p in [p for p in root.findall("programme") if p.get("channel") in channels]:
        root.remove(p)
    root.extend(programmes)
    return f"MagentaTV: {len(channels)} Sender, {len(programmes)} Sendungen"


def main(source_url, out_file, merge_file=MERGE_FILE):
    req = urllib.request.Request(source_url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=180) as r:
        raw = r.read()
    data = gzip.decompress(raw) if raw[:2] == b"\x1f\x8b" else raw
    root = ET.fromstring(data)
    merged = merge(root, merge_file)

    pairs = collect_pairs(root)
    changed = unmatched = 0
    for p in root.iter("programme"):
        if not p.get("channel", "").startswith("Sky.Sport.Bundesliga"):
            continue
        t = p.find("title")
        m = LIVE_RE.match((t.text or "").strip()) if t is not None else None
        if not m:
            continue
        league = "2" if m.group(1) else "1"
        league_name = "2. Bundesliga" if league == "2" else "Bundesliga"
        desc = text(p, "desc")
        kw, stadium = home_keyword(desc)
        if not kw:
            unmatched += 1
            continue
        hit = find_pair(pairs, league, kw, ts(p.get("start")))
        if hit:
            t.text = f"{league_name}: {hit[1]} – {hit[2]}"
            changed += 1
        else:
            t.text = f"{league_name}: Heimspiel {kw}"
            unmatched += 1
        if p.find("sub-title") is None:
            ET.SubElement(p, "sub-title").text = desc.removesuffix(".") or league_name

    out_dir = os.path.dirname(os.path.abspath(out_file))
    os.makedirs(out_dir, exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=out_dir, suffix=".tmp")
    with os.fdopen(fd, "wb") as f, gzip.GzipFile(fileobj=f, mode="wb") as gz:
        gz.write(b'<?xml version="1.0" encoding="UTF-8"?>\n')
        gz.write(ET.tostring(root, encoding="utf-8"))
    os.chmod(tmp, 0o644)
    os.replace(tmp, out_file)
    log(f"OK: {merged}; {changed} Spiele benannt, {unmatched} ohne Paarung, {len(pairs)} Paarungen gefunden")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Reichert das epgshare01-DE1-EPG mit Bundesliga-Paarungen an.")
    parser.add_argument("--out", required=True, help="Zieldatei (.xml.gz)")
    parser.add_argument("--source", default=SOURCE_URL, help="XMLTV-Quelle (URL, .xml oder .xml.gz)")
    parser.add_argument("--merge", default=MERGE_FILE, help="Zusaetzliches XMLTV (z. B. MagentaTV), leer = ohne")
    args = parser.parse_args()
    try:
        main(args.source, args.out, args.merge)
    except Exception as e:
        log(f"FEHLER: {e!r} - letzte Datei bleibt bestehen")
        sys.exit(1)

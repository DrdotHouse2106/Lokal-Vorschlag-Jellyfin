# Konzept: Gemeinsame Empfehlungen über freiwillig geteilte Sehverläufe

Stand: 20.10.2026 · Status: **Konzept, nicht umgesetzt** · Betreiber des Dienstes: Inhaber von `quallenkino.de`

> Dieses Dokument beschreibt Technik und Pflichten so genau wie möglich. Es ist **keine Rechtsberatung**. Vor dem Start sollte eine fachkundige Stelle (Datenschutzbeauftragte:r oder Fachanwalt für IT-/Datenschutzrecht) den Einwilligungstext, die Datenschutzerklärung und die Datenschutz-Folgenabschätzung prüfen.

## 1. Ziel

Die Empfehlungen des Quallenkinoplugins beruhen auf Metadaten (Genres, Darsteller, Regie) und seit 12.33.0 zusätzlich auf öffentlichen Listen von TMDB und Trakt. Was fehlt, ist das Wissen, **welche Titel Menschen mit ähnlichem Geschmack tatsächlich zusammen schauen**, und zwar aus dem Kreis der Quallenkino-Nutzer selbst.

Der Dienst sammelt dazu **freiwillig** geteilte, **pseudonyme** Sehverläufe mehrerer Jellyfin-Server. Er gibt nur **zusammengefasste** Ergebnisse zurück: eine Ähnlichkeitstabelle „Titel A ↔ Titel B“. Die Empfehlungen berechnet weiterhin jeder Server lokal.

**Abgrenzung:** Bis genug Teilnehmer mitmachen (Abschnitt 9), bleibt Teil 1 (TMDB/Trakt) die Grundlage. Der Dienst wird erst eingeschaltet, wenn er besser ist als diese.

## 2. Grundsätze

1. **Opt-in pro Benutzer.** Nicht der Admin entscheidet für alle. Der Admin schaltet die Funktion auf seinem Server nur *frei*. Jede Person willigt selbst ein, im Profilmenü, und kann jederzeit widerrufen.
2. **Pseudonym statt Name.** Beim ersten Einschalten erzeugt das Plugin eine **zufällige Kennung** (UUID v4, 122 Zufallsbits) und ordnet sie dem Jellyfin-Konto zu. Sie wird nicht aus Name, Mail, Benutzer- oder Server-ID abgeleitet.
3. **Löschen vor Vergessen.** Beim Abschalten schickt das Plugin **zuerst** eine Löschanfrage mit der Kennung an den Dienst. Erst nach deren Bestätigung löscht es die Kennung lokal. Scheitert die Anfrage, bleibt der Benutzer im Zustand „Löschung ausstehend“, und das Plugin versucht es weiter.
4. **Datensparsamkeit.** Übertragen werden nur:
   - Titel-IDs (TMDB, ersatzweise IMDb),
   - die Art des Signals: gesehen, abgebrochen, Daumen hoch/runter, Favorit,
   - das Datum, höchstens auf die Kalenderwoche gerundet.

   Nicht übertragen werden Uhrzeiten, Geräte, Namen, Bibliothekspfade, IP-Adressen im Inhalt oder Server-Kennungen.
5. **Nur Aggregate nach außen.** Der Dienst gibt nie Rohdaten anderer heraus, nur Paare bzw. Signale, zu denen **mindestens 20 verschiedene Kennungen** beigetragen haben (k-Schwelle).
6. **Keine IP-Speicherung.** Weder in der Anwendung noch im Webserver-Log, Reverse-Proxy oder CDN (Abschnitt 6.4).

## 3. Rechtliche Einordnung (DSGVO)

### 3.1 Pseudonymisierung, keine Anonymisierung

Ein Sehverlauf ist auch ohne Namen **wiedererkennbar**. Narayanan und Shmatikov haben 2008 Teilnehmer des Netflix-Prize-Datensatzes anhand weniger, grob datierter Bewertungen über IMDb-Profile identifiziert. Die Daten im Dienst sind daher **personenbezogene Daten in pseudonymisierter Form** (Art. 4 Nr. 5 DSGVO). Die DSGVO gilt vollständig.

Zusätzlich können Sehverläufe Rückschlüsse auf **besondere Kategorien** zulassen, etwa sexuelle Orientierung, religiöse oder politische Überzeugungen (Art. 9 DSGVO). Deshalb braucht es eine **ausdrückliche Einwilligung** nach Art. 9 Abs. 2 lit. a. Eine Rechtsgrundlage „berechtigtes Interesse“ scheidet aus.

### 3.2 Rollen

| Rolle | Wer | Aufgabe |
|---|---|---|
| Verantwortlicher für den Dienst | Betreiber von `quallenkino.de` | Erhebt, speichert und wertet die geteilten Daten aus, Art. 4 Nr. 7 |
| Admin eines Jellyfin-Servers | z. B. Familien-Admin | Schaltet die Funktion frei. Die Übermittlung geschieht auf Wunsch der einzelnen Person, ihre Einwilligung gilt gegenüber dem Betreiber |
| Auftragsverarbeiter | Hoster des Dienstes (Server, Backups) | Vertrag nach Art. 28 nötig |

Bei rein familiärer Nutzung des eigenen Jellyfin-Servers greift für den Admin oft die Haushaltsausnahme (Art. 2 Abs. 2 lit. c). Für den **Dienst** auf `quallenkino.de` greift sie nicht, weil er Daten fremder Personen verarbeitet.

### 3.3 Was der Betreiber liefern muss

- [ ] **Einwilligungstext** (im Plugin angezeigt, Version mit Datum). Er muss in einfacher Sprache sagen:
  - was übertragen wird,
  - wohin,
  - wofür,
  - wie lange es gespeichert wird,
  - dass die Daten pseudonym, aber nicht anonym sind,
  - dass Rückschlüsse nach Art. 9 möglich sind,
  - wie man widerruft (Art. 7 Abs. 3: so einfach wie die Einwilligung, also ein Schalter).
- [ ] **Datenschutzerklärung** auf `quallenkino.de` nach Art. 13: Verantwortlicher mit Anschrift, Zwecke, Rechtsgrundlage (Art. 6 Abs. 1 lit. a, Art. 9 Abs. 2 lit. a), Speicherdauer, Empfänger (Hoster), Rechte der Betroffenen, Beschwerderecht bei der Aufsichtsbehörde, keine Drittlandübermittlung (EU-Hosting).
- [ ] **Impressum** für `quallenkino.de` (§ 5 DDG).
- [ ] **Betroffenenrechte** über die Kennung: Auskunft (Art. 15), Löschung (Art. 17), Datenübertragbarkeit (Art. 20). Technisch über die API in Abschnitt 5; das Plugin bietet dazu Knöpfe im Profilmenü an.
- [ ] **Verzeichnis von Verarbeitungstätigkeiten** (Art. 30).
- [ ] **Datenschutz-Folgenabschätzung** (Art. 35). Wegen der Nähe zu Art.-9-Daten und der systematischen Auswertung von Verhalten **empfohlen bzw. wahrscheinlich nötig**.
- [ ] **Technische und organisatorische Maßnahmen** (Art. 32), Abschnitt 6.
- [ ] **AV-Vertrag** mit dem Hoster (Art. 28). Hosting in der EU.
- [ ] **Meldeprozess für Datenpannen** (Art. 33/34): Wer prüft, wer meldet binnen 72 Stunden.

## 4. Datenformat

Das Plugin lädt pro Kennung immer den **vollständigen aktuellen Stand** hoch (ersetzen statt anhängen). Das hält den Dienst einfach und Löschungen einzelner Einträge sind automatisch enthalten.

```json
{
  "schema": 1,
  "einwilligung": "2026-11-01",
  "eintraege": [
    { "art": "movie", "tmdb": "603",  "signal": "gesehen",       "woche": "2026-W42" },
    { "art": "movie", "tmdb": "604",  "signal": "abgebrochen",   "woche": "2026-W42" },
    { "art": "tv",    "tmdb": "1396", "signal": "daumen_hoch" },
    { "art": "movie", "imdb": "tt0816692", "signal": "favorit" }
  ]
}
```

- `art`: `movie` oder `tv`. Serien zählen als Ganzes, keine einzelnen Folgen.
- `signal`: `gesehen`, `abgebrochen`, `daumen_hoch`, `daumen_runter`, `favorit`. Ein Titel kann mehrere Einträge haben.
- `woche`: optional, ISO-Kalenderwoche. Im Plugin einstellbar: Woche, Monat oder weglassen.
- `einwilligung`: Version des Einwilligungstextes, dem zugestimmt wurde.
- Höchstens 5 000 Einträge. Titel ohne TMDB- bzw. IMDb-ID werden nicht übertragen.
- Kein Feld für Server, Gerät, Sprache oder Uhrzeit. Die App-Version steht nur im `User-Agent`, ohne Build-Details.

## 5. API (`https://api.quallenkino.de/v1`)

Die Kennung ist zugleich das Geheimnis. Wer sie kennt, kann den Datensatz lesen und löschen. Der Dienst speichert sie **nur als Hash**: `SHA-256(kennung)`. Ein Datenbank-Leck verrät so keine gültigen Kennungen.

| Methode | Pfad | Zweck | Antwort |
|---|---|---|---|
| `PUT` | `/profil/{kennung}` | Stand hochladen bzw. ersetzen (Body wie Abschnitt 4) | `204` |
| `GET` | `/profil/{kennung}` | Auskunft: gespeicherter Stand und Zeitpunkt (Art. 15/20) | `200` JSON |
| `DELETE` | `/profil/{kennung}` | Löschen (Art. 17). Sofort aus den Rohdaten, aus dem Modell beim nächsten Neuberechnen (≤ 7 Tage) | `204` (auch wenn unbekannt) |
| `GET` | `/modell/aktuell` | Verweis auf das aktuelle Modell: Version, Größe, Prüfsumme | `200` JSON |
| `GET` | `/modell/{version}.json.gz` | Ähnlichkeitstabelle zum **vollständigen Herunterladen** | `200` |

**Warum ein Download statt Einzelabfragen?** Fragt ein Server „Was ist ähnlich zu Titel X?“, verrät er, dass jemand dort X kennt. Das komplette Modell herunterzuladen (gzip, voraussichtlich 5–30 MB) verrät nichts. Die Zuordnung zur eigenen Bibliothek passiert lokal, genau wie bei Teil 1.

Modellformat (Auszug):

```json
{
  "version": "2026-W45",
  "k": 20,
  "titel": {
    "movie:603": [ ["movie:604", 0.62], ["movie:27205", 0.41] ],
    "tv:1396":   [ ["tv:60059", 0.71] ]
  }
}
```

Weitere Regeln:
- Fehlerantworten ohne Details zu anderen Kennungen.
- Ratenbegrenzung im Speicher, ohne Persistenz: Schlüssel ist `HMAC(tagessalz, ip)`, das Salz wird täglich verworfen.
- Uploads höchstens einmal je Kennung und Stunde.

## 6. Technische und organisatorische Maßnahmen

### 6.1 Speicherung
- Nur der jeweils **aktuelle Stand** je Kennung. Keine Historie und keine Uploadzeiten außer „zuletzt aktualisiert“ (auf den Tag gerundet).
- Kennungen ohne Upload seit 180 Tagen werden automatisch gelöscht.
- Verschlüsselte Datenträger, Backups höchstens 14 Tage, ebenfalls verschlüsselt. Löschungen wirken nach spätestens 14 Tagen auch in Backups.

### 6.2 Zugriff
- Rohdaten nur für den Auswertungsprozess. Kein Admin-Werkzeug zum Durchblättern einzelner Profile.
- Zugang zum Server nur per SSH-Schlüssel, Protokoll über Anmeldungen.

### 6.3 Ausgabe
- Ein Paar (A, B) erscheint im Modell nur, wenn **≥ 20 verschiedene Kennungen** beide Titel positiv markiert haben.
- Titel, die insgesamt weniger als 20 Kennungen haben, erscheinen gar nicht. Das schützt Nischen-Sehverläufe.
- Ausgegeben wird die Ähnlichkeit gerundet auf 2 Nachkommastellen, nie die Zählung selbst.

### 6.4 Keine IP-Adressen
- nginx/Caddy: `access_log off;` bzw. ein Logformat ohne `$remote_addr`. Fehlerlogs ebenfalls ohne Client-Adresse.
- **Kein CDN bzw. Proxy eines Dritten** (z. B. Cloudflare) vor der API. Der sähe die IP-Adressen und bräuchte einen AV-Vertrag. Falls doch, im Konzept und in der Datenschutzerklärung nennen.
- Anwendung: IP-Adresse nie in Datenbank oder Log. Die Ratenbegrenzung ist in 5 beschrieben.
- Hinweis für die Datenschutzerklärung: Die IP-Adresse wird technisch für die Verbindung verarbeitet, aber nicht gespeichert.

### 6.5 Transport
- Nur HTTPS (TLS 1.2+), HSTS. Das Plugin prüft das Zertifikat. Kein Fallback auf HTTP.

## 7. Auswertung

### 7.1 Start: Item-Item-Kookkurrenz (ab ca. 300 Teilnehmern)

1. Positive Menge je Kennung: gesehen (nicht abgebrochen), Daumen hoch, Favorit. Negativ: abgebrochen, Daumen runter.
2. **Beitrag begrenzen:** höchstens 300 positive Titel je Kennung (zufällige Auswahl, wöchentlich neu). Vielseher dominieren so nicht, und die Empfindlichkeit für Rauschen (7.3) bleibt beschränkt.
3. Für jedes Titelpaar: `c_ab` = Zahl der Kennungen mit beiden positiv, `c_a`, `c_b` = Zahl je Titel.
4. Ähnlichkeit, Kosinus mit Schrumpfung: `sim = c_ab / sqrt(c_a · c_b) · c_ab / (c_ab + 10)`.
5. Abzug, wenn viele A positiv und B negativ markiert haben (`n_ab_neg`): `sim −= 0,5 · n_ab_neg / (c_a + 10)`.
6. Je Titel die 50 ähnlichsten mit `c_ab ≥ 20` ins Modell. Neuberechnung wöchentlich; dabei werden Löschungen wirksam.

Umsetzung als Batch in Python mit `scipy.sparse`: Nutzer×Titel-Matrix `X`, dann `XᵀX` mit Popularitätsschnitt. Bei 10 000 Kennungen × 300 Titeln sind das wenige Minuten auf einem Kern.

### 7.2 Später: Matrixfaktorisierung (ab ca. 5 000 Teilnehmern)

Implizites ALS (z. B. Bibliothek `implicit`). Ausgegeben werden **nur Titel-Vektoren**, keine Nutzer-Vektoren. Das Plugin berechnet den Nutzer-Vektor lokal aus dem eigenen Verlauf. Die Ausgabe bleibt unter derselben k-Schwelle: Titel mit < 20 Kennungen bekommen keinen Vektor.

### 7.3 Optional: Differential Privacy

- Laplace-Rauschen auf `c_ab` vor der Schwelle. Mit Beitragsbegrenzung (300 Titel → höchstens 300·299/2 Paare je Person) ist echtes Nutzer-DP mit kleinem ε nur bei sehr vielen Teilnehmern brauchbar.
- Empfehlung: zunächst **Beitragsbegrenzung + k-Schwelle + Rauschen pro Paar (ε ≈ 1 je Release)** als zusätzliche Hürde. Das ist ehrlich als „Rauschen“ zu dokumentieren, nicht als formale DP-Garantie.
- Formales Nutzer-DP (z. B. über ein DP-SGD-trainiertes Modell) erst ab vielen tausend Teilnehmern prüfen.

### 7.4 Wochen-Datum

Wird nur für Aktualität genutzt (neuere Paare stärker gewichten, Halbwertszeit 1 Jahr) und für optionale Trends wie „diese Woche oft geschaut“ (nur Titel mit ≥ 20 Kennungen in der Woche). Wer das Datum im Plugin weglässt, zählt als „unbekannt, mittleres Gewicht“.

## 8. Umsetzung im Plugin (später)

- Admin-Einstellung „Gemeinsame Empfehlungen freigeben“ (Standard aus), Dienst-Adresse (Standard `https://api.quallenkino.de/v1`).
- Profilmenü „Gemeinsame Empfehlungen“:
  - Einwilligungstext mit Link zur Datenschutzerklärung,
  - Schalter,
  - Wahl der Datumsgenauigkeit (Woche, Monat, keins),
  - „Meine Daten beim Dienst anzeigen“ (Auskunft),
  - „Löschen und abschalten“.
- Kennung pro Benutzer in `plugins/LocalRecs/gemeinsam.json`. Sie wird nie geloggt und nie an den Browser gegeben.
- Upload wöchentlich mit zufälliger Verzögerung (0–24 h), damit Uploads nicht mit Sehzeiten zusammenfallen.
- Modell-Download wöchentlich (ETag). Einbindung wie Teil 1 als weitere Quelle im `ExternalSimilarityScorer` mit eigenem Gewicht und Begründung „Wird oft zusammen mit „…“ geschaut“.
- Ohne Freigabe oder ohne Einwilligung: kein einziger Aufruf an den Dienst, auch kein Modell-Download.

## 9. Wann lohnt es sich?

Grobe Abschätzung: Ein Paar braucht ≥ 20 Kennungen, die **beide** Titel gesehen haben. Bei typischen Bibliotheken (Schwerpunkt bekannte Filme und Serien, je Person 100–300 gesehene Titel) gilt:

| Teilnehmer (aktiv) | Erwartete Abdeckung |
|---|---|
| < 100 | praktisch keine Paare über der Schwelle, TMDB/Trakt deutlich besser |
| 300–500 | Paare für die bekanntesten ~500–1 000 Titel. Lohnt als Ergänzung für Mainstream |
| 1 000–3 000 | gute Abdeckung bekannter Titel, erste Nischen (Anime, Dokus) |
| > 5 000 | Matrixfaktorisierung sinnvoll, auch Kaltstart profitiert spürbar |

**Startkriterium (Vorschlag):** ≥ 300 Kennungen mit je ≥ 50 positiven Titeln **und** Beiträge von ≥ 10 verschiedenen Servern. Damit ein einzelner Haushalt das Modell nicht prägt, kann optional pro Paar verlangt werden, dass Kennungen von ≥ 3 Servern beitragen. Das bräuchte eine zusätzliche, zufällige Server-Kennung im Upload und ist abzuwägen, weil es mehr Verknüpfbarkeit bedeutet. Bis dahin sammelt der Dienst nicht: Die Freigabe im Plugin erscheint erst, wenn der Betreiber sie aktiviert. Alternativ läuft eine Vormerkliste ohne Datenübertragung.

## 10. Serverbedarf

| Teilnehmer | Rohdaten | Rechner |
|---|---|---|
| bis 10 000 | ≤ 3 Mio. Einträge, < 1 GB (PostgreSQL oder SQLite) | 1–2 vCPU, 2 GB RAM, 20 GB SSD, ein kleiner EU-VPS für wenige Euro im Monat |
| 10 000–100 000 | ≤ 30 Mio. Einträge | 4 vCPU, 8–16 GB RAM für den wöchentlichen Batch |

Komponenten:
- kleine API (z. B. Python/FastAPI oder ASP.NET Minimal API) hinter Caddy/nginx ohne Zugriffslog,
- Datenbank,
- wöchentlicher Batch-Job (Cron),
- statische Ablage für die Modelldateien.

Monitoring nur über Zähler (Anzahl Kennungen, Uploads pro Tag), ohne Einzeldaten.

## 11. Fahrplan

1. **Jetzt:** Teil 1 (TMDB/Trakt) läuft, das Konzept wird abgestimmt.
2. Betreiber erstellt Datenschutzerklärung, Einwilligungstext, DSFA und AV-Vertrag mit dem Hoster und lässt sie prüfen.
3. Dienst aufsetzen (API, Speicherung, Batch) und mit Testdaten prüfen, auch die Löschwege.
4. Plugin-Seite bauen: Freigabe, Einwilligung, Upload, Löschen, Auskunft, Modell-Download.
5. Vormerkphase: Freigabe sichtbar, Uploads erst ab Startkriterium (Abschnitt 9).
6. Start, danach regelmäßig prüfen, ob das Modell die Trefferquote im Reiter „Auswertung“ verbessert.

## 12. Offene Fragen an den Betreiber

- Rechtsform bzw. Anschrift für Impressum und Datenschutzerklärung, Kontakt für Betroffenenanfragen.
- Hoster und Standort (EU), Backup-Konzept.
- Datumsgenauigkeit als Standard: Woche, Monat oder keins?
- Sollen „abgebrochen“ und „Daumen runter“ überhaupt geteilt werden (mehr Qualität, mehr Rückschlüsse)?
- Server-Vielfalt pro Paar (Abschnitt 9) ja/nein?
- Wer führt die DSFA durch, wer prüft die Texte?

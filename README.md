# Jellyfin Lokale Empfehlungen (Plugin) – Jellyfin 12

[![Lizenz: GPLv3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Tests](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml/badge.svg)](https://github.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/actions/workflows/tests.yml)

Datenschutzfreundliche, personalisierte Empfehlungen für Jellyfin – ausschließlich auf Basis des lokalen Wiedergabeverlaufs und der Metadaten-Ähnlichkeit. Keine Cloud-Dienste, kein Tracking. Funktioniert auf allen Jellyfin-Clients (auch auf Fernsehern).

> [!NOTE]
> **Vorlage / Original:** Dieses Plugin basiert auf dem originalen Projekt **[rdpharr/jellyfin-plugin-localrecs](https://github.com/rdpharr/jellyfin-plugin-localrecs)** von Roger Pharr.
> Dieses Repository ist eine an **Jellyfin 12** angepasste Version davon, da das Original für Jellyfin 12 nicht mehr aktualisiert wird. Alle Rechte und der Dank für die eigentliche Entwicklung gehen an den ursprünglichen Autor.
>
> English version: [README.en.md](README.en.md)

> ⚠️ **Windows-Server:** Das Plugin legt Dateisystem-Symlinks an. Unter Windows muss Jellyfin dafür **als Administrator** laufen oder der **Entwicklermodus** aktiviert sein (Einstellungen → Datenschutz & Sicherheit → Für Entwickler). Linux und Docker unter Linux sind nicht betroffen.

## Funktionen

- **Personalisierung pro Benutzer** – Empfehlungen passend zum eigenen Wiedergabeverlauf
- **Inhaltsbasierte Filterung** – TF-IDF-Embeddings mit Kosinus-Ähnlichkeit
- **Zeitliche Ähnlichkeit** – Gruppierung nach Jahrzehnten
- **Virtuelle Bibliotheken** – funktionieren auf allen Clients (Web, Mobil, Roku, TV …)
- **Wiedergabestatus-Abgleich** – „Gesehen“-Status wird mit der echten Bibliothek synchronisiert
- **Datenschutz** – alle Berechnungen laufen lokal auf deinem Server

## Voraussetzungen

- **Jellyfin-Server:** 12.0 oder neuer (getestet mit 12.2)
- **.NET-Runtime:** 10.0 (ist in Jellyfin 12 enthalten)
- **Target ABI:** 12.0.0.0

Für Jellyfin 10.11.x bitte die Original-Version 0.6.1 verwenden.

**Versionsnummer:** Die Versionen dieses Forks sind an Jellyfin angelehnt (12.x). Jellyfin prüft nur, ob ein Plugin eine *mindestens* passende Server-Version verlangt. Durch die höhere Versionsnummer werden 10.11-Versionen des Originals auf Jellyfin 12 nie als Update angeboten.

**Umstieg vom Original:** Die Plugin-ID ist gleich geblieben. Ein installiertes Original wird daher direkt aktualisiert, Einstellungen und Empfehlungs-Bibliotheken bleiben erhalten. Das Original-Repository kann danach aus den Plugin-Repositories entfernt werden.

## Installation

### Variante A: Über das Plugin-Repository

> Funktioniert nur, wenn dieses GitHub-Repository **öffentlich** ist.

1. Dashboard → Plugins → Repositories → Hinzufügen
   `https://raw.githubusercontent.com/DrdotHouse2106/Lokal-Vorschlag-Jellyfin/main/manifest.json`
2. Dashboard → Plugins → Katalog → „Local Recommendations“ installieren
3. Jellyfin-Server neu starten

### Variante B: Manuell

1. [`release/jellyfin-plugin-localrecs-v12.0.0.zip`](release/jellyfin-plugin-localrecs-v12.0.0.zip) herunterladen und entpacken.
2. Die Datei `Jellyfin.Plugin.LocalRecs.dll` in einen neuen Ordner im Plugin-Verzeichnis legen, z. B.
   - Linux: `/var/lib/jellyfin/plugins/LocalRecs_12.0.0.0/`
   - Docker: `/config/plugins/LocalRecs_12.0.0.0/`
   - Windows: `%LOCALAPPDATA%\jellyfin\plugins\LocalRecs_12.0.0.0\`
3. Jellyfin-Server neu starten.

## Einrichtung

1. **Pfade anzeigen:** Dashboard → Plugins → Local Recommendations → Setup Guide. Dort stehen pro Benutzer zwei Pfade (Filme und Serien).
2. **Virtuelle Bibliotheken anlegen:** Für jeden Benutzer zwei Bibliotheken erstellen (Dashboard → Bibliotheken → Medienbibliothek hinzufügen):
   - Inhaltstyp **Filme** → Pfad „Movie Library Path“
   - Inhaltstyp **Serien** → Pfad „TV Library Path“
   - Den vorgeschlagenen Namen verwenden (z. B. „Max's Recommended Movies“)
3. **Berechtigungen setzen:** Jede Empfehlungs-Bibliothek nur für den jeweiligen Benutzer freigeben (Dashboard → Benutzer → Zugriff).
4. **Empfehlungen erzeugen:** Dashboard → Geplante Aufgaben → „Refresh Local Recommendations“ ausführen. Danach läuft die Aufgabe automatisch.

Eine ausführliche Beschreibung aller Einstellungen, des Algorithmus und der Fehlerbehebung findest du in der [englischen README](README.en.md) und in [DESIGN.md](DESIGN.md).

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

# Welten-Liste

`welten.json` legt fest, welche Filme und Serien zu welcher „Welt“ gehören (Marvel, Star Wars, Pixar …). Das Plugin **Local Recommendations** lädt diese Datei einmal täglich (Einstellung „Welten-Liste“, Standard: diese Datei auf GitHub) und zeigt die Welten in Apps wie Quallenkino persönlich sortiert an. Die Jellyfin-Bibliothek selbst wird dabei nicht verändert.

Die Titel-Listen sind **vollständig aus TMDB** zusammengestellt, auch mit angekündigten Titeln. Sie sagen also nichts darüber aus, was in einer bestimmten Bibliothek liegt; fehlende Titel werden einfach ignoriert und gehören automatisch zur richtigen Welt, sobald sie hinzukommen.

Eine eigene Liste ist möglich: Datei kopieren, anpassen und in den Plugin-Einstellungen unter „Welten-Liste“ den Link (oder einen Pfad auf dem Server) eintragen.

## Aufbau

```json
{
  "welten": [
    {
      "name": "Pixar",
      "studios": ["Pixar"],
      "sammlungen": ["Toy Story", "Cars"],
      "titel": [
        { "tmdb": "862", "typ": "film", "name": "Toy Story" }
      ],
      "ausschluss": [
        { "imdb": "tt0000000", "name": "Falsch zugeordneter Titel" }
      ]
    }
  ]
}
```

| Feld | Bedeutung |
|---|---|
| `name` | Anzeigename der Welt. Die ID entsteht daraus (klein, Umlaute als ae/oe/ue/ss, Sonderzeichen als `-`), alternativ eigenes Feld `id`. |
| `studios` | Teile von Studionamen. Passt eines, gehört der Titel dazu. |
| `sammlungen` | Teile von Namen von Jellyfin-Sammlungen (BoxSets). Alle Filme/Serien darin gehören dazu. |
| `titel` | Einzelne Titel, die immer dazugehören – z. B. wenn das Studio in den Metadaten fehlt. |
| `ausschluss` | Titel, die nie dazugehören, auch wenn eine Regel passt. Statt einer ID geht auch `"sammlung": "Indiana Jones"` – dann alle Titel dieser Sammlung. |

Ein Titel wird über `tmdb`, `imdb` oder `tvdb` erkannt. Bei `tmdb` bitte `typ` angeben (`film` oder `serie`), weil sich die TMDB-IDs von Filmen und Serien überschneiden. `name` dient nur der Lesbarkeit.

## Pflegen

Im Plugin unter **Einstellungen → Welten** steht pro Welt, welche Titel dazugehören und warum (Studio, Sammlung oder Liste). Darunter stehen **Vorschläge**: Titel aus derselben Sammlung, die noch fehlen. Für jeden Titel gibt es eine fertige Zeile zum Kopieren in `titel` oder `ausschluss`. Nach dem Speichern auf GitHub dort **„Liste neu laden“** drücken.

## Quelle

Daten: [TMDB](https://www.themoviedb.org). Dieses Produkt nutzt die TMDB-API, ist aber nicht von TMDB unterstützt oder zertifiziert.

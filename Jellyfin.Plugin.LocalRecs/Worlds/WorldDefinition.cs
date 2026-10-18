using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Eine „Welt“ wie bei Disney+ (z. B. Marvel, Star Wars, Pixar): alle Filme und Serien bestimmter Studios
    /// oder aus bestimmten Jellyfin-Sammlungen.
    /// </summary>
    public class WorldDefinition
    {
        /// <summary>
        /// Default definitions, one per line: Name | Studios | Collections (comma separated, partial names).
        /// </summary>
        public const string DefaultDefinitions =
            "Marvel | Marvel | Marvel, Avengers, Iron Man, Thor, Captain America, Guardians of the Galaxy, Spider-Man, X-Men, Deadpool, Ant-Man, Black Panther, Doctor Strange\n"
            + "Star Wars | Lucasfilm | Star Wars\n"
            + "DC | DC Comics, DC Entertainment, DC Films, DC Studios | Batman, Superman, Justice League, Wonder Woman, Aquaman, The Dark Knight\n"
            + "Pixar | Pixar | Toy Story, Cars, Findet Nemo, Finding Nemo, Die Unglaublichen, The Incredibles, Monster AG, Alles steht Kopf, Inside Out\n"
            + "Disney | Walt Disney Pictures, Walt Disney Animation, Walt Disney Productions, Disney | Disney\n"
            + "DreamWorks | DreamWorks | Shrek, Madagascar, Drachenzähmen, How to Train Your Dragon, Kung Fu Panda\n"
            + "Illumination | Illumination | Ich – Einfach unverbesserlich, Despicable Me, Minions, Pets\n"
            + "Harry Potter | | Harry Potter, Phantastische Tierwesen, Fantastic Beasts\n"
            + "Mittelerde | | Der Herr der Ringe, Lord of the Rings, Der Hobbit, The Hobbit\n"
            + "James Bond | Eon Productions | James Bond\n"
            + "Jurassic Park | | Jurassic\n"
            + "Studio Ghibli | Ghibli |";

        /// <summary>Gets or sets the ID (lower case, used in URLs).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the studio names (partial, case-insensitive).</summary>
        public IReadOnlyList<string> Studios { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the collection names (partial, case-insensitive).</summary>
        public IReadOnlyList<string> Collections { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets titles that always belong to the world (by TMDB/IMDb/TVDB ID).</summary>
        public IReadOnlyList<WorldTitleRef> Includes { get; set; } = Array.Empty<WorldTitleRef>();

        /// <summary>Gets or sets titles that never belong to the world, even if a rule matches.</summary>
        public IReadOnlyList<WorldTitleRef> Excludes { get; set; } = Array.Empty<WorldTitleRef>();

        /// <summary>
        /// Gets or sets the logo sources in order of preference: "fanart:collection/{tmdbCollectionId}",
        /// "fanart:movie/{tmdbId}", "fanart:tv/{tvdbId}", https URL or path relative to the world list.
        /// </summary>
        public IReadOnlyList<string> Logos { get; set; } = Array.Empty<string>();

        /// <summary>Gets the first logo source, or null.</summary>
        public string? Logo => Logos.Count > 0 ? Logos[0] : null;

        /// <summary>
        /// Gets or sets the title whose local trailer represents the world (field "trailer": TMDB ID or object with tmdb/imdb/tvdb/typ), or null for automatic choice.
        /// </summary>
        public WorldTitleRef? Trailer { get; set; }

        /// <summary>Gets or sets former IDs of the world (e.g. after a rename), still accepted in URLs.</summary>
        public IReadOnlyList<string> Aliases { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Parses a world list in JSON (file <c>welten.json</c>):
        /// <c>{ "welten": [ { "name", "id"?, "studios": [], "sammlungen": [], "titel": [ { "tmdb", "imdb", "tvdb", "typ", "name" } ], "ausschluss": [ … ] } ] }</c>.
        /// </summary>
        /// <param name="json">The JSON text.</param>
        /// <returns>The worlds; duplicate IDs and worlds without rules or titles are dropped.</returns>
        /// <exception cref="JsonException">Thrown when the JSON is invalid.</exception>
        public static List<WorldDefinition> ParseJson(string json)
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!document.RootElement.TryGetProperty("welten", out var worldsElement) || worldsElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Die Liste braucht ein Feld \"welten\" mit einer Liste von Welten.");
            }

            var result = new List<WorldDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in worldsElement.EnumerateArray())
            {
                var name = GetString(element, "name")?.Trim();
                var id = ToId(GetString(element, "id") ?? name);
                if (string.IsNullOrEmpty(name) || id.Length == 0 || !ids.Add(id))
                {
                    continue;
                }

                var world = new WorldDefinition
                {
                    Id = id,
                    Name = name,
                    Studios = GetStrings(element, "studios"),
                    Collections = GetStrings(element, "sammlungen"),
                    Includes = GetTitles(element, "titel"),
                    Excludes = GetTitles(element, "ausschluss"),
                    Logos = GetLogos(element),
                    Trailer = GetTrailer(element),
                    Aliases = GetStrings(element, "aliase").Select(ToId).Where(a => a.Length > 0 && a != id).Distinct().ToList()
                };
                if (world.Studios.Count > 0 || world.Collections.Count > 0 || world.Includes.Count > 0)
                {
                    result.Add(world);
                }
            }

            return result;
        }

        /// <summary>
        /// Parses the world definitions (one per line: Name | Studios | Collections). Lines starting with # are ignored.
        /// </summary>
        /// <param name="text">The definitions.</param>
        /// <returns>The worlds; duplicate IDs are dropped.</returns>
        public static List<WorldDefinition> Parse(string? text)
        {
            var result = new List<WorldDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rawLine in (text ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var parts = line.Split('|');
                var name = parts[0].Trim();
                var id = ToId(name);
                if (id.Length == 0 || !ids.Add(id))
                {
                    continue;
                }

                var world = new WorldDefinition
                {
                    Id = id,
                    Name = name,
                    Studios = SplitList(parts.ElementAtOrDefault(1)),
                    Collections = SplitList(parts.ElementAtOrDefault(2))
                };
                if (world.Studios.Count > 0 || world.Collections.Count > 0)
                {
                    result.Add(world);
                }
            }

            return result;
        }

        /// <summary>
        /// Builds an ID from a name: lower case, umlauts replaced, other characters as dashes.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The ID, e.g. "star-wars".</returns>
        public static string ToId(string? name)
        {
            var builder = new StringBuilder();
            foreach (var c in (name ?? string.Empty).ToLowerInvariant())
            {
                switch (c)
                {
                    case 'ä': builder.Append("ae"); break;
                    case 'ö': builder.Append("oe"); break;
                    case 'ü': builder.Append("ue"); break;
                    case 'ß': builder.Append("ss"); break;
                    default:
                        if (char.IsAsciiLetterOrDigit(c))
                        {
                            builder.Append(c);
                        }
                        else if (builder.Length > 0 && builder[^1] != '-')
                        {
                            builder.Append('-');
                        }

                        break;
                }
            }

            return builder.ToString().Trim('-');
        }

        /// <summary>
        /// Checks whether the ID or a former ID of the world matches.
        /// </summary>
        /// <param name="id">ID from a request.</param>
        /// <returns>True if it is this world.</returns>
        public bool HasId(string? id)
        {
            return !string.IsNullOrEmpty(id)
                && (string.Equals(Id, id, StringComparison.OrdinalIgnoreCase) || Aliases.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Checks whether one of the studios belongs to this world.
        /// </summary>
        /// <param name="studios">Studios of an item.</param>
        /// <returns>True if a studio matches.</returns>
        public bool MatchesStudio(IEnumerable<string>? studios)
        {
            return studios != null && studios.Any(s => !string.IsNullOrWhiteSpace(s)
                && Studios.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Checks whether a collection belongs to this world.
        /// </summary>
        /// <param name="collectionName">Name of the collection.</param>
        /// <returns>True if the name matches.</returns>
        public bool MatchesCollection(string? collectionName)
        {
            return !string.IsNullOrWhiteSpace(collectionName)
                && Collections.Any(c => collectionName.Contains(c, StringComparison.OrdinalIgnoreCase));
        }

        private static string? GetString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        private static List<string> GetLogos(JsonElement element)
        {
            if (!element.TryGetProperty("logo", out var logo))
            {
                return new List<string>();
            }

            if (logo.ValueKind == JsonValueKind.String)
            {
                var value = logo.GetString()?.Trim();
                return string.IsNullOrEmpty(value) ? new List<string>() : new List<string> { value };
            }

            return GetStrings(element, "logo");
        }

        private static WorldTitleRef? GetTrailer(JsonElement element)
        {
            if (!element.TryGetProperty("trailer", out var trailer))
            {
                return null;
            }

            if (trailer.ValueKind == JsonValueKind.String || trailer.ValueKind == JsonValueKind.Number)
            {
                var tmdb = GetString(element, "trailer")?.Trim();
                return string.IsNullOrEmpty(tmdb) ? null : new WorldTitleRef { Tmdb = tmdb };
            }

            if (trailer.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var reference = new WorldTitleRef
            {
                Tmdb = GetString(trailer, "tmdb")?.Trim(),
                Imdb = GetString(trailer, "imdb")?.Trim(),
                Tvdb = GetString(trailer, "tvdb")?.Trim(),
                Type = GetString(trailer, "typ")?.Trim().ToLowerInvariant(),
                Name = GetString(trailer, "name")?.Trim()
            };
            return string.IsNullOrEmpty(reference.Tmdb) && string.IsNullOrEmpty(reference.Imdb) && string.IsNullOrEmpty(reference.Tvdb) ? null : reference;
        }

        private static List<string> GetStrings(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return array.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString()!.Trim())
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<WorldTitleRef> GetTitles(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return new List<WorldTitleRef>();
            }

            return array.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.Object)
                .Select(v => new WorldTitleRef
                {
                    Tmdb = GetString(v, "tmdb")?.Trim(),
                    Imdb = GetString(v, "imdb")?.Trim(),
                    Tvdb = GetString(v, "tvdb")?.Trim(),
                    Type = GetString(v, "typ")?.Trim().ToLowerInvariant(),
                    Collection = GetString(v, "sammlung")?.Trim(),
                    Name = GetString(v, "name")?.Trim()
                })
                .Where(t => !string.IsNullOrEmpty(t.Tmdb) || !string.IsNullOrEmpty(t.Imdb) || !string.IsNullOrEmpty(t.Tvdb) || !string.IsNullOrEmpty(t.Collection))
                .ToList();
        }

        private static List<string> SplitList(string? value)
        {
            return (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}

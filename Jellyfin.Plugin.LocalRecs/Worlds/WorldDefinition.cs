using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            + "Jurassic Park | | Jurassic";

        /// <summary>Gets or sets the ID (lower case, used in URLs).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the studio names (partial, case-insensitive).</summary>
        public IReadOnlyList<string> Studios { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the collection names (partial, case-insensitive).</summary>
        public IReadOnlyList<string> Collections { get; set; } = Array.Empty<string>();

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

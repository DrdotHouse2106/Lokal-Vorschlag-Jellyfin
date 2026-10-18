using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Bestimmt, welche Titel zu welcher Welt gehören und warum: über Studios, Sammlungen und die Titel-Liste
    /// (TMDB/IMDb/TVDB), abzüglich der Ausschlüsse.
    /// </summary>
    public class WorldMembership
    {
        /// <summary>Gets the worlds.</summary>
        public IReadOnlyList<WorldDefinition> Worlds { get; private set; } = Array.Empty<WorldDefinition>();

        /// <summary>Gets the members per world ID.</summary>
        public Dictionary<string, HashSet<Guid>> Members { get; } = new Dictionary<string, HashSet<Guid>>();

        /// <summary>Gets why an item belongs to a world (world ID → item ID → reason, e.g. "Studio: Pixar").</summary>
        public Dictionary<string, Dictionary<Guid, string>> Sources { get; } = new Dictionary<string, Dictionary<Guid, string>>();

        /// <summary>Gets the TMDB collection IDs per world.</summary>
        public Dictionary<string, SortedSet<string>> TmdbCollections { get; } = new Dictionary<string, SortedSet<string>>();

        /// <summary>Gets the collections of the library (for suggestions).</summary>
        public IReadOnlyList<CollectionEntry> Collections { get; private set; } = Array.Empty<CollectionEntry>();

        /// <summary>Gets the library items by ID.</summary>
        public IReadOnlyDictionary<Guid, LibraryEntry> Items { get; private set; } = new Dictionary<Guid, LibraryEntry>();

        /// <summary>
        /// Computes the members of all worlds.
        /// </summary>
        /// <param name="worlds">World definitions.</param>
        /// <param name="items">Movies and series of the library.</param>
        /// <param name="collections">Collections (BoxSets) of the library.</param>
        /// <returns>The membership.</returns>
        public static WorldMembership Compute(IReadOnlyList<WorldDefinition> worlds, IReadOnlyList<LibraryEntry> items, IReadOnlyList<CollectionEntry> collections)
        {
            ArgumentNullException.ThrowIfNull(worlds);
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(collections);

            var itemsById = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            var result = new WorldMembership { Worlds = worlds, Collections = collections, Items = itemsById };
            foreach (var world in worlds)
            {
                var sources = new Dictionary<Guid, string>();

                // 1. Studios
                foreach (var item in items)
                {
                    var studio = item.Studios.FirstOrDefault(s => world.MatchesStudio(new[] { s }));
                    if (studio != null)
                    {
                        sources.TryAdd(item.Id, "Studio: " + studio);
                    }
                }

                // 2. Sammlungen
                foreach (var collection in collections.Where(c => world.MatchesCollection(c.Name)))
                {
                    foreach (var child in collection.ChildIds.Where(itemsById.ContainsKey))
                    {
                        sources.TryAdd(child, "Sammlung: " + collection.Name);
                    }
                }

                // 3. Titel aus der Liste (überschreibt nichts, ergänzt nur)
                foreach (var item in items.Where(i => world.Includes.Any(r => Matches(r, i))))
                {
                    sources.TryAdd(item.Id, "Liste");
                }

                // 4. Ausschlüsse (einzelne Titel oder ganze Sammlungen)
                foreach (var item in items.Where(i => world.Excludes.Any(r => Matches(r, i))))
                {
                    sources.Remove(item.Id);
                }

                foreach (var reference in world.Excludes.Where(r => !string.IsNullOrEmpty(r.Collection)))
                {
                    foreach (var collection in collections.Where(c => c.Name.Contains(reference.Collection!, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var child in collection.ChildIds)
                        {
                            sources.Remove(child);
                        }
                    }
                }

                result.Sources[world.Id] = sources;
                result.Members[world.Id] = sources.Keys.ToHashSet();

                // TMDB-Sammlungen: per Name zugeordnet oder mit mindestens einem Titel der Welt
                result.TmdbCollections[world.Id] = new SortedSet<string>(
                    collections
                        .Where(c => !string.IsNullOrEmpty(c.TmdbId) && (world.MatchesCollection(c.Name) || c.ChildIds.Any(sources.ContainsKey)))
                        .Select(c => c.TmdbId!),
                    StringComparer.Ordinal);
            }

            return result;
        }

        /// <summary>
        /// Checks whether a list entry refers to a library item (TMDB, IMDb or TVDB ID; the type must fit).
        /// </summary>
        /// <param name="reference">List entry.</param>
        /// <param name="item">Library item.</param>
        /// <returns>True if they match.</returns>
        public static bool Matches(WorldTitleRef reference, LibraryEntry item)
        {
            ArgumentNullException.ThrowIfNull(reference);
            ArgumentNullException.ThrowIfNull(item);
            if (!reference.FitsType(item.IsSeries))
            {
                return false;
            }

            return Same(reference.Tmdb, item.Tmdb) || Same(reference.Imdb, item.Imdb) || Same(reference.Tvdb, item.Tvdb);
        }

        /// <summary>
        /// Suggests titles that probably belong to a world: other parts of collections that contain world titles.
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <returns>Item IDs with reason.</returns>
        public IReadOnlyList<(Guid ItemId, string Reason)> Suggest(string worldId)
        {
            if (!Members.TryGetValue(worldId, out var members))
            {
                return Array.Empty<(Guid, string)>();
            }

            var world = Worlds.First(w => w.Id == worldId);
            var result = new Dictionary<Guid, string>();
            foreach (var collection in Collections.Where(c => c.ChildIds.Any(members.Contains)))
            {
                foreach (var child in collection.ChildIds.Where(id => !members.Contains(id)))
                {
                    if (Items.TryGetValue(child, out var item) && world.Excludes.Any(r => Matches(r, item)))
                    {
                        continue;
                    }

                    result.TryAdd(child, "In der Sammlung „" + collection.Name + "“ mit Titeln dieser Welt");
                }
            }

            return result.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        private static bool Same(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A movie or series of the library.
        /// </summary>
        public class LibraryEntry
        {
            /// <summary>Gets or sets the item ID.</summary>
            public Guid Id { get; set; }

            /// <summary>Gets or sets a value indicating whether the item is a series.</summary>
            public bool IsSeries { get; set; }

            /// <summary>Gets or sets the studios.</summary>
            public IReadOnlyList<string> Studios { get; set; } = Array.Empty<string>();

            /// <summary>Gets or sets the TMDB ID.</summary>
            public string? Tmdb { get; set; }

            /// <summary>Gets or sets the IMDb ID.</summary>
            public string? Imdb { get; set; }

            /// <summary>Gets or sets the TVDB ID.</summary>
            public string? Tvdb { get; set; }
        }

        /// <summary>
        /// A collection (BoxSet) of the library.
        /// </summary>
        public class CollectionEntry
        {
            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the TMDB collection ID.</summary>
            public string? TmdbId { get; set; }

            /// <summary>Gets or sets the movies and series in the collection.</summary>
            public IReadOnlyList<Guid> ChildIds { get; set; } = Array.Empty<Guid>();
        }
    }
}

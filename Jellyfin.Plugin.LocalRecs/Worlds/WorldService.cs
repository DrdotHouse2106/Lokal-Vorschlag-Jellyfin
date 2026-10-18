using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.LocalRecs.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// Ordnet Filme und Serien den Welten zu (über Studios und Jellyfin-Sammlungen) und liefert pro Benutzer,
    /// welche Welten er sehen kann und welche er zuletzt geschaut hat.
    /// </summary>
    public class WorldService
    {
        private const int MaxRecentMovies = 300;
        private const int MaxRecentEpisodes = 500;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        private readonly ILogger<WorldService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly object _lock = new object();
        private (DateTime Created, string Definitions, IReadOnlyList<WorldDefinition> Worlds, IReadOnlyDictionary<string, HashSet<Guid>> Members, IReadOnlyDictionary<string, SortedSet<string>> TmdbCollections)? _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorldService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        public WorldService(ILogger<WorldService> logger, ILibraryManager libraryManager, IUserDataManager userDataManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the configured worlds and their members (movie and series IDs). Cached for 30 minutes.
        /// </summary>
        /// <returns>Worlds and members by world ID.</returns>
        public (IReadOnlyList<WorldDefinition> Worlds, IReadOnlyDictionary<string, HashSet<Guid>> Members) GetWorlds()
        {
            var data = GetData();
            return (data.Worlds, data.Members);
        }

        /// <summary>
        /// Gets the TMDB IDs of the collections (BoxSets) belonging to a world: collections matched by name and
        /// collections containing at least one title of the world. Apps can use them to show missing parts (e.g. via Jellyseerr).
        /// </summary>
        /// <param name="worldId">World ID.</param>
        /// <returns>The TMDB collection IDs.</returns>
        public IReadOnlyList<string> GetTmdbCollectionIds(string worldId)
        {
            return GetData().TmdbCollections.TryGetValue(worldId, out var ids) ? ids.ToList() : new List<string>();
        }

        /// <summary>
        /// Gets the movies and series a user watched recently, with the time of watching
        /// (series: most recently watched episode).
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>Last watched time by item ID.</returns>
        public Dictionary<Guid, DateTime> GetLastWatched(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            var result = new Dictionary<Guid, DateTime>();

            var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = MaxRecentMovies
            });
            foreach (var movie in movies)
            {
                var data = _userDataManager.GetUserData(user, movie);
                if (data?.LastPlayedDate is { } played)
                {
                    result[movie.Id] = played;
                }
            }

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsPlayed = true,
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                Limit = MaxRecentEpisodes
            }).OfType<Episode>();
            foreach (var episode in episodes)
            {
                var data = _userDataManager.GetUserData(user, episode);
                if (data?.LastPlayedDate is { } played && (!result.TryGetValue(episode.SeriesId, out var known) || known < played))
                {
                    result[episode.SeriesId] = played;
                }
            }

            return result;
        }

        /// <summary>
        /// Checks whether an item should be offered in a world row: visible, not watched, not started, not disliked.
        /// Series stay until they are watched completely.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="item">The item.</param>
        /// <returns>True if the item can be shown.</returns>
        public bool IsOpenFor(User user, BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(item);
            if (!item.IsVisible(user))
            {
                return false;
            }

            var data = _userDataManager.GetUserData(user, item);
            if (data == null)
            {
                return true;
            }

            if (data.Likes == false || data.Played)
            {
                return false;
            }

            return item is Series || data.PlaybackPositionTicks <= 0;
        }

        /// <summary>
        /// Picks a representative item with a backdrop for the world tile (best rated visible title).
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="members">Members of the world.</param>
        /// <returns>The item ID, or null.</returns>
        public Guid? GetImageItem(User user, IEnumerable<Guid> members)
        {
            return members
                .Select(id => _libraryManager.GetItemById(id))
                .Where(i => i != null && i.IsVisible(user) && i.HasImage(ImageType.Backdrop))
                .OrderByDescending(i => i!.CommunityRating ?? 0)
                .Select(i => (Guid?)i!.Id)
                .FirstOrDefault();
        }

        private (IReadOnlyList<WorldDefinition> Worlds, IReadOnlyDictionary<string, HashSet<Guid>> Members, IReadOnlyDictionary<string, SortedSet<string>> TmdbCollections) GetData()
        {
            var definitions = Config.WorldDefinitions ?? string.Empty;
            lock (_lock)
            {
                if (_cache is { } cache && cache.Definitions == definitions && DateTime.UtcNow - cache.Created < CacheDuration)
                {
                    return (cache.Worlds, cache.Members, cache.TmdbCollections);
                }
            }

            var worlds = WorldDefinition.Parse(definitions);
            var members = worlds.ToDictionary(w => w.Id, _ => new HashSet<Guid>());
            var tmdbCollections = worlds.ToDictionary(w => w.Id, _ => new SortedSet<string>(StringComparer.Ordinal));
            if (worlds.Count > 0)
            {
                try
                {
                    // Studios
                    var items = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                        Recursive = true
                    });
                    foreach (var item in items)
                    {
                        foreach (var world in worlds.Where(w => w.MatchesStudio(item.Studios)))
                        {
                            members[world.Id].Add(item.Id);
                        }
                    }

                    // Sammlungen (liegen außerhalb der Bibliotheken – daher ohne Benutzer abfragen)
                    var boxSets = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                    }).OfType<BoxSet>().ToList();
                    var children = boxSets.ToDictionary(b => b.Id, b => b.GetLinkedChildren().Where(c => c is Movie || c is Series).Select(c => c.Id).ToList());
                    foreach (var boxSet in boxSets)
                    {
                        foreach (var world in worlds.Where(w => w.MatchesCollection(boxSet.Name)))
                        {
                            members[world.Id].UnionWith(children[boxSet.Id]);
                        }
                    }

                    // TMDB-Sammlungen je Welt: per Name zugeordnet oder mit mindestens einem Titel der Welt
                    foreach (var boxSet in boxSets)
                    {
                        var tmdbId = boxSet.GetProviderId(MetadataProvider.Tmdb);
                        if (string.IsNullOrEmpty(tmdbId))
                        {
                            continue;
                        }

                        foreach (var world in worlds.Where(w => w.MatchesCollection(boxSet.Name) || children[boxSet.Id].Any(members[w.Id].Contains)))
                        {
                            tmdbCollections[world.Id].Add(tmdbId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Welten konnten nicht zugeordnet werden");
                }
            }

            _logger.LogDebug("Welten: {Worlds}", string.Join(", ", worlds.Select(w => w.Name + " (" + members[w.Id].Count + ")")));
            lock (_lock)
            {
                _cache = (DateTime.UtcNow, definitions, worlds, members, tmdbCollections);
            }

            return (worlds, members, tmdbCollections);
        }
    }
}

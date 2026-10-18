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
        private const int TrailerRotation = 7;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        private readonly ILogger<WorldService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly object _lock = new object();
        private readonly WorldListSource? _listSource;
        private readonly Dictionary<(Guid User, string World), (DateTime Day, WorldMembership Data, (Guid Item, Guid Trailer)? Choice)> _trailerCache = new();
        private (DateTime Created, string Key, WorldMembership Data)? _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorldService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="listSource">Maintained world list (GitHub or file), optional.</param>
        public WorldService(ILogger<WorldService> logger, ILibraryManager libraryManager, IUserDataManager userDataManager, WorldListSource? listSource = null)
        {
            _listSource = listSource;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Picks one entry per day from a list (rotates daily, stable within a day).
        /// </summary>
        /// <typeparam name="T">Entry type.</typeparam>
        /// <param name="entries">Entries.</param>
        /// <param name="date">The day.</param>
        /// <returns>The entry of the day, or default for an empty list.</returns>
        public static T? PickForDay<T>(IReadOnlyList<T> entries, DateTime date)
        {
            ArgumentNullException.ThrowIfNull(entries);
            return entries.Count == 0 ? default : entries[(int)((long)date.Date.Subtract(DateTime.UnixEpoch.Date).TotalDays % entries.Count)];
        }

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
        /// Gets the full membership (with reasons and suggestions), e.g. for the admin view.
        /// </summary>
        /// <returns>The membership.</returns>
        public WorldMembership GetMembership() => GetData();

        /// <summary>
        /// Clears the cache so that the next request reads the library and the world list again.
        /// </summary>
        public void Invalidate()
        {
            lock (_lock)
            {
                _cache = null;
            }
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

        /// <summary>
        /// Picks the title whose local trailer plays behind the world (like Disney+): the title set in the list
        /// (field "trailer") if it has a local trailer, otherwise one of the most recently added visible titles with one,
        /// changing daily. The choice is cached per user and world for the day.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="worldId">World ID.</param>
        /// <param name="members">Members of the world visible to the user.</param>
        /// <returns>The title and its first local trailer, or null.</returns>
        public (BaseItem Item, BaseItem Trailer)? GetTrailer(User user, string worldId, IEnumerable<Guid> members)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(members);
            var data = GetData();
            var today = DateTime.Now.Date;
            var key = (user.Id, worldId);
            lock (_lock)
            {
                if (_trailerCache.TryGetValue(key, out var cached) && cached.Day == today && ReferenceEquals(cached.Data, data))
                {
                    return cached.Choice is { } ids
                        && _libraryManager.GetItemById(ids.Item) is { } cachedItem
                        && _libraryManager.GetItemById(ids.Trailer) is { } cachedTrailer
                        ? (cachedItem, cachedTrailer)
                        : null;
                }
            }

            (BaseItem Item, BaseItem Trailer)? choice = null;
            if (data.PinnedTrailers.TryGetValue(worldId, out var pinnedId) && _libraryManager.GetItemById(pinnedId) is { } pinned)
            {
                choice = FindTrailer(user, pinned);
            }

            if (choice == null)
            {
                var withTrailer = members
                    .Select(id => _libraryManager.GetItemById(id))
                    .Where(i => i != null)
                    .OrderByDescending(i => i!.DateCreated)
                    .ThenByDescending(i => i!.CommunityRating ?? 0)
                    .Select(i => FindTrailer(user, i!))
                    .Where(c => c != null)
                    .Take(TrailerRotation)
                    .ToList();
                choice = PickForDay(withTrailer, today);
            }

            lock (_lock)
            {
                if (_trailerCache.Count > 10000)
                {
                    _trailerCache.Clear();
                }

                _trailerCache[key] = (today, data, choice is { } c ? (c.Item.Id, c.Trailer.Id) : null);
            }

            return choice;
        }

        private static (BaseItem Item, BaseItem Trailer)? FindTrailer(User user, BaseItem item)
        {
            if (!item.IsVisible(user) || item is not IHasTrailers hasTrailers)
            {
                return null;
            }

            var trailer = hasTrailers.LocalTrailers.FirstOrDefault(t => t.IsVisible(user));
            return trailer == null ? null : (item, trailer);
        }

        private WorldMembership GetData()
        {
            // Gepflegte Liste (GitHub oder Datei), sonst die Regeln aus den Einstellungen
            var listWorlds = _listSource?.GetWorlds();
            var key = listWorlds != null ? "list:" + _listSource!.Version : "text:" + (Config.WorldDefinitions ?? string.Empty);
            lock (_lock)
            {
                if (_cache is { } cache && cache.Key == key && DateTime.UtcNow - cache.Created < CacheDuration)
                {
                    return cache.Data;
                }
            }

            var worlds = listWorlds ?? WorldDefinition.Parse(Config.WorldDefinitions);
            WorldMembership data;
            try
            {
                var items = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                    Recursive = true
                }).Select(i => new WorldMembership.LibraryEntry
                {
                    Id = i.Id,
                    IsSeries = i is Series,
                    Studios = i.Studios ?? Array.Empty<string>(),
                    Tmdb = i.GetProviderId(MetadataProvider.Tmdb),
                    Imdb = i.GetProviderId(MetadataProvider.Imdb),
                    Tvdb = i.GetProviderId(MetadataProvider.Tvdb)
                }).ToList();

                // Sammlungen liegen außerhalb der Bibliotheken – daher ohne Benutzer abfragen
                var collections = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                }).OfType<BoxSet>().Select(b => new WorldMembership.CollectionEntry
                {
                    Name = b.Name ?? string.Empty,
                    TmdbId = b.GetProviderId(MetadataProvider.Tmdb),
                    ChildIds = b.GetLinkedChildren().Where(c => c is Movie || c is Series).Select(c => c.Id).Distinct().ToList()
                }).ToList();

                data = WorldMembership.Compute(worlds, items, collections);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Welten konnten nicht zugeordnet werden");
                data = WorldMembership.Compute(worlds, Array.Empty<WorldMembership.LibraryEntry>(), Array.Empty<WorldMembership.CollectionEntry>());
            }

            _logger.LogDebug("Welten: {Worlds}", string.Join(", ", worlds.Select(w => w.Name + " (" + data.Members[w.Id].Count + ")")));
            lock (_lock)
            {
                _cache = (DateTime.UtcNow, key, data);
            }

            return data;
        }
    }
}

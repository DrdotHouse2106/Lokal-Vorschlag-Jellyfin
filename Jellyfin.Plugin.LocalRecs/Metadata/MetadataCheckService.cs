using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Metadata
{
    /// <summary>
    /// Metadaten-Check: wie vollständig die Filme und Serien jeder Bibliothek beschrieben sind, welche Titel zu wenig
    /// Metadaten haben, und neuer Abruf der Metadaten nur für diese Titel (normaler Jellyfin-Abruf, nichts wird ersetzt).
    /// </summary>
    public class MetadataCheckService
    {
        /// <summary>Most titles listed.</summary>
        public const int MaxListed = 500;

        /// <summary>Most titles refreshed per request.</summary>
        public const int MaxRefresh = 200;

        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<MetadataCheckService> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="MetadataCheckService"/> class.
        /// </summary>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="providerManager">Provider manager.</param>
        /// <param name="fileSystem">File system.</param>
        /// <param name="logger">Logger.</param>
        public MetadataCheckService(ILibraryManager libraryManager, IProviderManager providerManager, IFileSystem fileSystem, ILogger<MetadataCheckService> logger)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _providerManager = providerManager ?? throw new ArgumentNullException(nameof(providerManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Checks all movie and series libraries.
        /// </summary>
        /// <returns>Summary per library and the titles with little metadata, worst first.</returns>
        public CheckResult Run()
        {
            var result = new CheckResult();
            var listed = new List<CheckedItem>();
            foreach (var folder in _libraryManager.GetVirtualFolders())
            {
                if (!Guid.TryParse(folder.ItemId, out var folderId))
                {
                    continue;
                }

                var items = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    ParentId = folderId,
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                    Recursive = true,
                    IsVirtualItem = false
                });
                if (items.Count == 0)
                {
                    continue;
                }

                var library = new LibrarySummary { Name = folder.Name ?? string.Empty, Total = items.Count };
                foreach (var field in MetadataCheck.Fields)
                {
                    library.Missing[field] = 0;
                }

                foreach (var item in items)
                {
                    var missing = MetadataCheck.Missing(Facts(item));
                    foreach (var field in missing)
                    {
                        library.Missing[field]++;
                    }

                    var level = MetadataCheck.Level(missing);
                    if (level.Length == 0)
                    {
                        continue;
                    }

                    if (level == "zu wenig")
                    {
                        library.TooLittle++;
                    }
                    else
                    {
                        library.Little++;
                    }

                    listed.Add(new CheckedItem
                    {
                        Id = item.Id.ToString("N"),
                        Name = item.Name ?? string.Empty,
                        Year = item.ProductionYear,
                        Type = item is Movie ? "Film" : "Serie",
                        Library = library.Name,
                        Level = level,
                        Score = MetadataCheck.Score(missing),
                        Missing = missing
                    });
                }

                result.Libraries.Add(library);
            }

            result.ListedTotal = listed.Count;
            result.Items = listed
                .OrderByDescending(i => i.Score)
                .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxListed)
                .ToList();
            result.Checked = DateTime.UtcNow;
            return result;
        }

        /// <summary>
        /// Queues a metadata refresh for titles (missing metadata is filled, existing metadata and images stay).
        /// </summary>
        /// <param name="itemIds">The titles (movies and series only, at most <see cref="MaxRefresh"/>).</param>
        /// <returns>The number of queued titles.</returns>
        public int Refresh(IEnumerable<Guid> itemIds)
        {
            ArgumentNullException.ThrowIfNull(itemIds);
            var queued = 0;
            foreach (var id in itemIds.Distinct().Take(MaxRefresh))
            {
                if (_libraryManager.GetItemById(id) is not { } item || item is not (Movie or MediaBrowser.Controller.Entities.TV.Series))
                {
                    continue;
                }

                var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
                {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ImageRefreshMode = MetadataRefreshMode.Default,
                    ReplaceAllMetadata = false,
                    ReplaceAllImages = false
                };
                _providerManager.QueueRefresh(item.Id, options, RefreshPriority.Low);
                queued++;
            }

            _logger.LogInformation("Metadaten-Check: Abruf für {Count} Titel angestoßen", queued);
            return queued;
        }

        private MetadataCheck.ItemFacts Facts(BaseItem item)
        {
            var people = _libraryManager.GetPeople(new InternalPeopleQuery { ItemId = item.Id });
            return new MetadataCheck.ItemFacts
            {
                IsMovie = item is Movie,
                GenreCount = item.Genres?.Length ?? 0,
                TagCount = item.Tags?.Length ?? 0,
                ActorCount = people.Count(p => p.Type == PersonKind.Actor || p.Type == PersonKind.GuestStar),
                DirectorCount = people.Count(p => p.Type == PersonKind.Director),
                HasProviderId = !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Tmdb)) || !string.IsNullOrEmpty(item.GetProviderId(MetadataProvider.Imdb)),
                HasYear = item.ProductionYear is > 0,
                HasRating = item.CommunityRating is > 0,
                HasOverview = !string.IsNullOrWhiteSpace(item.Overview),
                HasImage = item.HasImage(ImageType.Primary)
            };
        }

        /// <summary>Result of a check.</summary>
        public class CheckResult
        {
            /// <summary>Gets or sets when it was checked (UTC).</summary>
            public DateTime Checked { get; set; }

            /// <summary>Gets the summaries per library.</summary>
            public List<LibrarySummary> Libraries { get; } = new List<LibrarySummary>();

            /// <summary>Gets or sets the number of titles with little metadata (may be more than listed).</summary>
            public int ListedTotal { get; set; }

            /// <summary>Gets or sets the titles with little metadata, worst first.</summary>
            public IReadOnlyList<CheckedItem> Items { get; set; } = Array.Empty<CheckedItem>();
        }

        /// <summary>Summary of a library.</summary>
        public class LibrarySummary
        {
            /// <summary>Gets or sets the library name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the number of movies and series.</summary>
            public int Total { get; set; }

            /// <summary>Gets or sets the number of titles with too little metadata (genres or ID missing).</summary>
            public int TooLittle { get; set; }

            /// <summary>Gets or sets the number of titles with little metadata.</summary>
            public int Little { get; set; }

            /// <summary>Gets the number of titles missing each field.</summary>
            public Dictionary<string, int> Missing { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        /// <summary>A title with little metadata.</summary>
        public class CheckedItem
        {
            /// <summary>Gets or sets the item ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the title.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the year.</summary>
            public int? Year { get; set; }

            /// <summary>Gets or sets "Film" or "Serie".</summary>
            public string Type { get; set; } = string.Empty;

            /// <summary>Gets or sets the library.</summary>
            public string Library { get; set; } = string.Empty;

            /// <summary>Gets or sets "zu wenig" or "wenig".</summary>
            public string Level { get; set; } = string.Empty;

            /// <summary>Gets or sets the score (higher = more missing).</summary>
            public int Score { get; set; }

            /// <summary>Gets or sets the missing fields.</summary>
            public IReadOnlyList<string> Missing { get; set; } = Array.Empty<string>();
        }
    }
}

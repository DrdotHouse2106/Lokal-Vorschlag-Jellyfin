using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Filmreihen: Wurde ein Teil einer Sammlung (z. B. „Harry Potter“) gesehen, ist der nächste ungesehene Teil
    /// eine besonders naheliegende Empfehlung. Grundlage sind die Sammlungen (BoxSets) von Jellyfin.
    /// </summary>
    public class CollectionService
    {
        private readonly ILogger<CollectionService> _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="CollectionService"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="userDataManager">User data manager.</param>
        public CollectionService(
            ILogger<CollectionService> logger,
            ILibraryManager libraryManager,
            IUserManager userManager,
            IUserDataManager userDataManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _userDataManager = userDataManager ?? throw new ArgumentNullException(nameof(userDataManager));
        }

        /// <summary>
        /// Finds the next part to recommend in a collection.
        /// </summary>
        /// <param name="parts">Parts in release order.</param>
        /// <returns>Index of the next part and the title of the part watched before it, or null.</returns>
        public static (int Index, string PreviousTitle)? FindNextPart(IReadOnlyList<CollectionPart> parts)
        {
            ArgumentNullException.ThrowIfNull(parts);

            var lastWatched = -1;
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i].Played)
                {
                    lastWatched = i;
                }
            }

            // Nichts gesehen oder der zuletzt gesehene Teil hat nicht gefallen: keine Empfehlung aus dieser Reihe
            if (lastWatched < 0 || parts[lastWatched].Likes == false)
            {
                return null;
            }

            for (var i = lastWatched + 1; i < parts.Count; i++)
            {
                var part = parts[i];
                if (!part.Played && !part.Started && part.Likes != false)
                {
                    return (i, parts[lastWatched].Title);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the next unwatched parts of the collections a user has started, with a reason.
        /// </summary>
        /// <param name="userId">User ID.</param>
        /// <returns>Movie IDs and reasons, most recently continued collection first.</returns>
        public IReadOnlyList<(Guid ItemId, string Reason)> GetNextParts(Guid userId)
        {
            var user = _userManager.GetUserById(userId);
            if (user == null)
            {
                return Array.Empty<(Guid, string)>();
            }

            var result = new List<(Guid ItemId, string Reason, DateTime LastPlayed)>();
            try
            {
                // Sammlungen liegen außerhalb der Bibliotheken des Benutzers – daher ohne Benutzer abfragen und
                // die Sichtbarkeit unten je Film prüfen
                var boxSets = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.BoxSet }
                }).OfType<BoxSet>().ToList();
                _logger.LogDebug("Filmreihen: {Count} Sammlungen gefunden", boxSets.Count);

                foreach (var boxSet in boxSets)
                {
                    var movies = boxSet.GetLinkedChildren()
                        .OfType<Movie>()
                        .Where(m => m.IsVisible(user))
                        .GroupBy(m => m.Id)
                        .Select(g => g.First())
                        .OrderBy(m => m.PremiereDate ?? (m.ProductionYear.HasValue ? new DateTime(m.ProductionYear.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.MaxValue))
                        .ToList();
                    if (movies.Count < 2)
                    {
                        continue;
                    }

                    var data = movies.Select(m => _userDataManager.GetUserData(user, m)).ToList();
                    var parts = movies.Select((m, i) => new CollectionPart
                    {
                        Title = m.Name ?? string.Empty,
                        Played = data[i]?.Played == true,
                        Started = data[i] != null && data[i]!.PlaybackPositionTicks > 0,
                        Likes = data[i]?.Likes
                    }).ToList();

                    var next = FindNextPart(parts);
                    if (next != null)
                    {
                        var lastPlayed = data.Where(d => d?.Played == true).Max(d => d!.LastPlayedDate) ?? DateTime.MinValue;
                        result.Add((movies[next.Value.Index].Id, "Nächster Teil nach „" + next.Value.PreviousTitle + "“", lastPlayed));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Filmreihen konnten nicht ausgewertet werden");
            }

            return result.OrderByDescending(r => r.LastPlayed).Select(r => (r.ItemId, r.Reason)).ToList();
        }

        /// <summary>
        /// One part of a collection (for <see cref="FindNextPart"/>).
        /// </summary>
        public class CollectionPart
        {
            /// <summary>Gets or sets the title.</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>Gets or sets a value indicating whether the part was watched.</summary>
            public bool Played { get; set; }

            /// <summary>Gets or sets a value indicating whether the part was started (in progress).</summary>
            public bool Started { get; set; }

            /// <summary>Gets or sets the thumb rating.</summary>
            public bool? Likes { get; set; }
        }
    }
}

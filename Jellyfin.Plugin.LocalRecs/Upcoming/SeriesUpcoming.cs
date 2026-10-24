using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>
    /// Was bei einer Serie als Nächstes kommt (aus Sonarr) und was heute neu dazugekommen ist – für die Detailseite.
    /// </summary>
    public class SeriesUpcoming
    {
        /// <summary>Gets or sets the next episode not yet downloaded (no time limit), or null.</summary>
        public SeriesEpisode? Next { get; set; }

        /// <summary>Gets or sets the line for the detail page, e.g. „Neue Folge heute · 21:00 Uhr · Paramount+ · S02E05 Titel“, or empty.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Gets or sets the number of further new episodes within 30 days after the next one.</summary>
        public int MoreEpisodes { get; set; }

        /// <summary>Gets or sets the local day of the last of these further episodes (yyyy-MM-dd), or null.</summary>
        public string? MoreUntil { get; set; }

        /// <summary>Gets or sets the text for the further episodes, e.g. „+2 weitere bis 06.11.“, or empty.</summary>
        public string MoreText { get; set; } = string.Empty;

        /// <summary>Gets or sets the episodes that aired today and are already in the library.</summary>
        public IReadOnlyList<SeriesEpisode> NewToday { get; set; } = Array.Empty<SeriesEpisode>();

        /// <summary>Gets or sets the text for them, e.g. „Neu: S02E05“ or „Neu: S02E05, S02E06“, or empty.</summary>
        public string NewText { get; set; } = string.Empty;

        /// <summary>
        /// An episode known to Sonarr.
        /// </summary>
        public class SeriesEpisode
        {
            /// <summary>Gets or sets the season number.</summary>
            public int SeasonNumber { get; set; }

            /// <summary>Gets or sets the episode number.</summary>
            public int EpisodeNumber { get; set; }

            /// <summary>Gets or sets the episode title.</summary>
            public string? EpisodeTitle { get; set; }

            /// <summary>Gets or sets the episode overview.</summary>
            public string? EpisodeOverview { get; set; }

            /// <summary>Gets or sets the air time (UTC).</summary>
            public DateTime Date { get; set; }

            /// <summary>Gets or sets the local day (yyyy-MM-dd) in the app's time zone.</summary>
            public string LocalDate { get; set; } = string.Empty;

            /// <summary>Gets or sets the local air time with zone, e.g. „21:00 Uhr MESZ“.</summary>
            public string LocalTime { get; set; } = string.Empty;

            /// <summary>Gets or sets the type: NewEpisode, SeasonPremiere or SeriesPremiere.</summary>
            public string ReleaseType { get; set; } = UpcomingEntry.NewEpisode;

            /// <summary>Gets or sets the network, if Sonarr knows it.</summary>
            public string? Network { get; set; }

            /// <summary>Gets or sets the Jellyfin ID of the episode (only for episodes already in the library), without dashes.</summary>
            public string? ItemId { get; set; }
        }
    }
}

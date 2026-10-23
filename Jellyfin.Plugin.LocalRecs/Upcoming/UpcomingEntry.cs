using System;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.LocalRecs.Upcoming
{
    /// <summary>
    /// Ein Eintrag in „Demnächst“: die nächste neue Folge einer Serie oder ein bald erscheinender Film.
    /// </summary>
    public class UpcomingEntry
    {
        /// <summary>Kind of an episode entry.</summary>
        public const string EpisodeKind = "Episode";

        /// <summary>Kind of a movie entry.</summary>
        public const string MovieKind = "Movie";

        /// <summary>Gets or sets the kind („Episode“ or „Movie“).</summary>
        public string Kind { get; set; } = EpisodeKind;

        /// <summary>Gets or sets the title (series or movie).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Gets or sets the subtitle („Neue Folge S02E05 · Freitag“, „Ab 14.11. verfügbar“).</summary>
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>Gets or sets the air or release time (UTC).</summary>
        public DateTime DateUtc { get; set; }

        /// <summary>Gets or sets the series in Jellyfin (episodes).</summary>
        public BaseItem? SeriesItem { get; set; }

        /// <summary>Gets or sets the season number (episodes).</summary>
        public int SeasonNumber { get; set; }

        /// <summary>Gets or sets the episode number (episodes).</summary>
        public int EpisodeNumber { get; set; }

        /// <summary>Gets or sets the episode title (episodes).</summary>
        public string? EpisodeTitle { get; set; }

        /// <summary>Gets or sets the number of further episodes of the series in the period.</summary>
        public int MoreEpisodes { get; set; }

        /// <summary>Gets or sets a value indicating whether the user watches or likes the series.</summary>
        public bool Priority { get; set; }

        /// <summary>Gets or sets the movie from Radarr (movies).</summary>
        public ArrMovie? Movie { get; set; }
    }
}

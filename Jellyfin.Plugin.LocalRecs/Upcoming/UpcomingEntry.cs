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

        /// <summary>New episode of a running season.</summary>
        public const string NewEpisode = "NewEpisode";

        /// <summary>First episode of a season after the first.</summary>
        public const string SeasonPremiere = "SeasonPremiere";

        /// <summary>First episode of a series (S01E01).</summary>
        public const string SeriesPremiere = "SeriesPremiere";

        /// <summary>Cinema release of a movie.</summary>
        public const string Cinema = "Cinema";

        /// <summary>Digital release of a movie.</summary>
        public const string Digital = "Digital";

        /// <summary>Disc release of a movie.</summary>
        public const string Physical = "Physical";

        /// <summary>Gets or sets the kind („Episode“ or „Movie“).</summary>
        public string Kind { get; set; } = EpisodeKind;

        /// <summary>Gets or sets the title (series or movie).</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Gets or sets the subtitle („Neue Folge am Fr. 24.10.“, „Staffelstart heute“, „Kinostart am …“, „Digital ab …“).</summary>
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>Gets or sets the type of the date: NewEpisode, SeasonPremiere, SeriesPremiere, Cinema, Digital or Physical.</summary>
        public string ReleaseType { get; set; } = NewEpisode;

        /// <summary>Gets or sets the episode overview (episodes).</summary>
        public string? EpisodeOverview { get; set; }

        /// <summary>Gets or sets the network (episodes, if Sonarr knows it).</summary>
        public string? Network { get; set; }

        /// <summary>Gets or sets the local day of the date, for sorting.</summary>
        public DateOnly Day { get; set; }

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

using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Discover
{
    /// <summary>
    /// Ein Titel aus TMDB für die Entdecken-Reihen („Demnächst im Kino“, „Neu dieses Jahr“), mit deutschem Termin,
    /// FSK, Genres, Laufzeit, Trailer und deutschen Streaming-Anbietern. Liegt er schon in der Bibliothek, steht die Jellyfin-ID dabei.
    /// </summary>
    public class DiscoverItem
    {
        /// <summary>Gets or sets the TMDB ID.</summary>
        public int TmdbId { get; set; }

        /// <summary>Gets or sets the media type: „movie“ or „tv“ (as in Jellyseerr).</summary>
        public string MediaType { get; set; } = "movie";

        /// <summary>Gets or sets the (German) title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Gets or sets the original title.</summary>
        public string? OriginalTitle { get; set; }

        /// <summary>Gets or sets the year.</summary>
        public int? Year { get; set; }

        /// <summary>Gets or sets the (German) overview.</summary>
        public string? Overview { get; set; }

        /// <summary>Gets or sets the TMDB poster path (e.g. „/abc.jpg“).</summary>
        public string? PosterPath { get; set; }

        /// <summary>Gets or sets the TMDB backdrop path.</summary>
        public string? BackdropPath { get; set; }

        /// <summary>Gets or sets the full poster address (w342), or null.</summary>
        public string? PosterUrl { get; set; }

        /// <summary>Gets or sets the full backdrop address (w1280), or null.</summary>
        public string? BackdropUrl { get; set; }

        /// <summary>Gets or sets the date that counts (German cinema start, German release or first air date), yyyy-MM-dd.</summary>
        public string? ReleaseDate { get; set; }

        /// <summary>Gets or sets a value indicating whether the date is today or in the past.</summary>
        public bool Released { get; set; }

        /// <summary>Gets or sets the line for the card, e.g. „Kinostart am Do. 30.10.“, „Erschienen am 12.09.“, „Startet morgen“.</summary>
        public string ReleaseText { get; set; } = string.Empty;

        /// <summary>Gets or sets the German genre names.</summary>
        public IReadOnlyList<string> Genres { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the German age rating (FSK), e.g. „12“, or null.</summary>
        public string? OfficialRating { get; set; }

        /// <summary>Gets or sets the runtime in minutes (series: one episode), or null.</summary>
        public int? RuntimeMinutes { get; set; }

        /// <summary>Gets or sets the German streaming providers (flat rate), e.g. „Netflix“.</summary>
        public IReadOnlyList<string> Providers { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the YouTube key of the trailer (German preferred), or null.</summary>
        public string? TrailerKey { get; set; }

        /// <summary>Gets or sets the name of the trailer.</summary>
        public string? TrailerName { get; set; }

        /// <summary>Gets or sets a value indicating whether the title is already in the user's libraries.</summary>
        public bool InLibrary { get; set; }

        /// <summary>Gets or sets the Jellyfin ID (without dashes) if in the library, otherwise null.</summary>
        public string? JellyfinId { get; set; }

        /// <summary>Gets or sets the popularity at TMDB (for ordering).</summary>
        public double Popularity { get; set; }

        /// <summary>
        /// Creates a copy for one user (library state set per user).
        /// </summary>
        /// <returns>The copy.</returns>
        public DiscoverItem Copy() => (DiscoverItem)MemberwiseClone();
    }
}

using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LocalRecs.Configuration
{
    /// <summary>
    /// Plugin configuration.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
        /// </summary>
        public PluginConfiguration()
        {
            MovieRecommendationCount = 25;
            TvRecommendationCount = 25;
            FavoriteBoost = 2.0;
            RecentWatchBoost = 1.0;
            RecencyDecayHalfLifeDays = 365.0;
            MinWatchedItemsForPersonalization = 3;
            MaxVocabularyActors = 500;
            MaxVocabularyDirectors = 0;
            MaxVocabularyTags = 500;
            EnableRatingProximity = true;
            RatingProximityWeight = 0.2;
            MinCommunityRating = 5.0;
            RatingQualityWeight = 0.15;
            IgnoredRecommendationDays = 14;
            EnableCollectionNextParts = true;
            ShowRecommendationReason = true;
            WorldDefinitions = Worlds.WorldDefinition.DefaultDefinitions;
            MinWorldItems = 5;
            MovieSectionTitle = "Empfohlene Filme für dich";
            TvSectionTitle = "Empfohlene Serien für dich";
            EnableLiveTvTracking = true;
            LiveTvMinMinutes = 3;
            LiveTvTitleMatchMinutes = 15;
            LiveTvExcludedGenres = "Nachrichten, News, Wetter";
            LiveTvHistoryDays = 180;
            EnableLiveTvSection = true;
            LiveTvSectionTitle = "Heute im TV für dich";
            LiveTvLookaheadHours = 24;
            LiveTvRecommendationCount = 20;
            LiveTvTimeZone = "Europe/Berlin";
            LiveTvRegularDays = 3;
            LiveTvXmltvSource = string.Empty;
        }

        /// <summary>
        /// Gets or sets on how many different days a show must have been watched live to count as a regular show.
        /// </summary>
        public int LiveTvRegularDays { get; set; }

        /// <summary>
        /// Gets or sets an additional XMLTV file (path or http(s) URL, .xml or .xml.gz) from which actors, directors,
        /// production year and country of live TV programs are read. Empty = off.
        /// </summary>
        public string LiveTvXmltvSource { get; set; }

        /// <summary>
        /// Gets or sets the time zone used to display broadcast times (e.g. "Europe/Berlin"; empty = server time zone).
        /// </summary>
        public string LiveTvTimeZone { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether watched live TV programs are recorded and used for recommendations.
        /// </summary>
        public bool EnableLiveTvTracking { get; set; }

        /// <summary>
        /// Gets or sets the minimum minutes a program must be watched to count (shorter views are treated as zapping).
        /// </summary>
        public int LiveTvMinMinutes { get; set; }

        /// <summary>
        /// Gets or sets the total minutes of a title watched live after which a library item with the same title
        /// counts as watched for the library recommendations.
        /// </summary>
        public int LiveTvTitleMatchMinutes { get; set; }

        /// <summary>
        /// Gets or sets the comma separated genres that are ignored for live TV (e.g. news running in the background).
        /// </summary>
        public string LiveTvExcludedGenres { get; set; }

        /// <summary>
        /// Gets or sets the number of days live TV watch history is kept.
        /// </summary>
        public int LiveTvHistoryDays { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the home screen section with upcoming live TV programs is shown.
        /// </summary>
        public bool EnableLiveTvSection { get; set; }

        /// <summary>
        /// Gets or sets the title of the live TV section on the home screen.
        /// </summary>
        public string LiveTvSectionTitle { get; set; }

        /// <summary>
        /// Gets or sets how many hours ahead the live TV section looks.
        /// </summary>
        public int LiveTvLookaheadHours { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of programs in the live TV section.
        /// </summary>
        public int LiveTvRecommendationCount { get; set; }

        /// <summary>
        /// Gets or sets the title of the movie section on the home screen.
        /// </summary>
        public string MovieSectionTitle { get; set; }

        /// <summary>
        /// Gets or sets the title of the series section on the home screen.
        /// </summary>
        public string TvSectionTitle { get; set; }

        /// <summary>
        /// Gets or sets the number of movie recommendations to generate per user.
        /// </summary>
        public int MovieRecommendationCount { get; set; }

        /// <summary>
        /// Gets or sets the number of TV recommendations to generate per user.
        /// </summary>
        public int TvRecommendationCount { get; set; }

        /// <summary>
        /// Gets or sets the boost multiplier for favorite items.
        /// </summary>
        public double FavoriteBoost { get; set; }

        /// <summary>
        /// Gets or sets the amplification factor for recently watched items.
        /// A just-watched item (decay=1) receives weight × (1 + RecentWatchBoost).
        /// Set to 0 to disable; default is 1.0.
        /// </summary>
        public double RecentWatchBoost { get; set; }

        /// <summary>
        /// Gets or sets the recency decay half-life in days.
        /// </summary>
        public double RecencyDecayHalfLifeDays { get; set; }

        /// <summary>
        /// Gets or sets the minimum watched items required for personalization.
        /// </summary>
        public int MinWatchedItemsForPersonalization { get; set; }

        /// <summary>
        /// Gets or sets the maximum vocabulary size for actors (0 = unlimited).
        /// </summary>
        public int MaxVocabularyActors { get; set; }

        /// <summary>
        /// Gets or sets the maximum vocabulary size for directors (0 = unlimited).
        /// </summary>
        public int MaxVocabularyDirectors { get; set; }

        /// <summary>
        /// Gets or sets the maximum vocabulary size for tags (0 = unlimited).
        /// </summary>
        public int MaxVocabularyTags { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to enable rating proximity weighting.
        /// When enabled, items with ratings closer to the user's average ratings get a boost.
        /// </summary>
        public bool EnableRatingProximity { get; set; }

        /// <summary>
        /// Gets or sets the weight for rating proximity in final score (0.0 to 1.0).
        /// 0.0 = pure content similarity, 1.0 = pure rating match.
        /// Default: 0.2 (20% rating proximity, 80% content similarity).
        /// </summary>
        public double RatingProximityWeight { get; set; }

        /// <summary>
        /// Gets or sets the minimum community rating (0-10) of recommended movies and series.
        /// Titles without a rating are always allowed. 0 = off. Default: 5.0.
        /// </summary>
        public double MinCommunityRating { get; set; }

        /// <summary>
        /// Gets or sets how much the community rating itself raises the score (0.0 to 1.0).
        /// Well rated titles rank higher among similarly matching ones. Default: 0.15.
        /// </summary>
        public double RatingQualityWeight { get; set; }

        /// <summary>
        /// Gets or sets after how many days a recommended but never watched title slowly moves back
        /// (2 % per day, down to half the score). 0 = off. Default: 14.
        /// </summary>
        public int IgnoredRecommendationDays { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the next unwatched part of a started film series (Jellyfin collection)
        /// is put at the front of the movie row. Default: true.
        /// </summary>
        public bool EnableCollectionNextParts { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the reason ("Weil du … geschaut hast") is shown on the home screen cards. Default: true.
        /// </summary>
        public bool ShowRecommendationReason { get; set; }

        /// <summary>
        /// Gets or sets the worlds (one per line: Name | Studios | Collections, comma separated partial names).
        /// </summary>
        public string WorldDefinitions { get; set; }

        /// <summary>
        /// Gets or sets the minimum number of titles a world needs to be offered. Default: 5.
        /// </summary>
        public int MinWorldItems { get; set; }

        /// <summary>
        /// Validates the configuration and returns validation errors.
        /// </summary>
        /// <returns>List of validation error messages, empty if valid.</returns>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (MovieRecommendationCount < 0)
            {
                errors.Add("MovieRecommendationCount must be non-negative");
            }

            if (TvRecommendationCount < 0)
            {
                errors.Add("TvRecommendationCount must be non-negative");
            }

            if (FavoriteBoost < 0)
            {
                errors.Add("FavoriteBoost must be non-negative");
            }

            if (RecentWatchBoost < 0)
            {
                errors.Add("RecentWatchBoost must be non-negative");
            }

            if (RecencyDecayHalfLifeDays <= 0)
            {
                errors.Add("RecencyDecayHalfLifeDays must be positive");
            }

            if (MinWatchedItemsForPersonalization < 0)
            {
                errors.Add("MinWatchedItemsForPersonalization must be non-negative");
            }

            if (MaxVocabularyActors < 0)
            {
                errors.Add("MaxVocabularyActors must be non-negative (0 = unlimited)");
            }

            if (MaxVocabularyDirectors < 0)
            {
                errors.Add("MaxVocabularyDirectors must be non-negative (0 = unlimited)");
            }

            if (MaxVocabularyTags < 0)
            {
                errors.Add("MaxVocabularyTags must be non-negative (0 = unlimited)");
            }

            if (RatingProximityWeight < 0 || RatingProximityWeight > 1)
            {
                errors.Add("RatingProximityWeight must be between 0.0 and 1.0");
            }

            if (MinCommunityRating < 0 || MinCommunityRating > 10)
            {
                errors.Add("MinCommunityRating must be between 0 and 10");
            }

            if (RatingQualityWeight < 0 || RatingQualityWeight > 1)
            {
                errors.Add("RatingQualityWeight must be between 0.0 and 1.0");
            }

            return errors;
        }
    }
}

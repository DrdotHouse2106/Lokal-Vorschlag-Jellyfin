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
            WorldListSource = Worlds.WorldListSource.DefaultUrl;
            FanartApiKey = string.Empty;
            WebHomeEnabled = false;
            WebThemeEnabled = false;
            WebProfilesEnabled = false;
            AvatarsEnabled = true;
            AvatarCatalogUrl = Avatars.AvatarService.DefaultCatalogUrl;
            AvatarExtraCatalogUrls = string.Empty;
            EpgSeriesByEpisodeNumber = true;
            EpgSeriesBySubTitle = true;
            EpgMovieRule = true;
            EpgMovieMinMinutes = 75;
            EpgMovieByDirector = true;
            EpgMovieByYear = false;
            EpgMovieByImdb = true;
            EpgMovieCategories = "Spielfilm";
            EpgNoMovieCategories = "Dokumentation, Doku, Dokumentarfilm, Dokumentarreihe, Reportage, Magazin";
            EpgOtherCategories = "Teleshopping, Shopping, Dauerwerbesendung, Verkaufssendung";
            EpgOtherChannels = "HSE, QVC, 1-2-3.tv, 123.tv, Juwelo, mediashop, Channel21, Pearl, Sonnenklar";
            EpgKidsCategories = "Kinder, Kindersendung, Kinderserie, Kinderfilm, Kids, Children, Zeichentrick";
            EpgKidsChannels = "KiKA, Super RTL, Toggo, Disney Channel, Nickelodeon, Nick Jr, Cartoon Network, Boomerang, Junior";
            EpgNewsCategories = "Nachrichten, News, Nachrichtenmagazin";
            EpgNewsChannels = "tagesschau24, n-tv, ntv, WELT, euronews";
            EpgSportsCategories = "Sport, Fußball, Fussball, Tennis, Handball, Basketball, Eishockey, Motorsport, Formel 1, Radsport, Leichtathletik, Wintersport, Darts, Golf, Boxen";
            EpgSportsChannels = "Sky Sport, Sport1, Eurosport, DAZN, sportdigital, MagentaSport";
            WebPlayerUserIds = System.Array.Empty<string>();
            WebPlayerForAll = true;
            WebDiagnostics = false;
            EnableExternalSimilarity = true;
            ExternalSimilarityWeight = 0.25;
            ExternalSimilarityCacheDays = 7;
            ExternalSimilarityMaxLookupsPerRun = 400;
            TmdbApiKey = string.Empty;
            TraktClientId = string.Empty;
            TraktClientSecret = string.Empty;
            EnableTraktPersonal = true;
            TraktPersonalWeight = 0.5;
            CalendarPublicUrl = string.Empty;
            CalendarAlarmMinutes = 10;
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
        /// Gets or sets the maintained world list (URL or path of a <c>welten.json</c>). Default: the list in the plugin
        /// repository on GitHub. Empty = only the rules from <see cref="WorldDefinitions"/>.
        /// </summary>
        public string WorldListSource { get; set; }

        /// <summary>
        /// Gets or sets the personal fanart.tv API key. Used for world logos of the form "fanart:collection/{id}".
        /// Empty = only other logo sources (e.g. TMDB company logos).
        /// </summary>
        public string FanartApiKey { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Jellyfin Web (browser and the iOS/Android apps) shows the start page
        /// in Quallenkino style with all rows of the TV app (needs the plugin "File Transformation").
        /// </summary>
        public bool WebHomeEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Jellyfin Web uses the Quallenkino colors (dark blue, violet-blue gradient).
        /// </summary>
        public bool WebThemeEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Jellyfin Web shows the "Wer schaut?" profile picker (logins stay in the browser, no PIN).
        /// </summary>
        public bool WebProfilesEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether users can choose a profile image from figure icons and a color.
        /// </summary>
        public bool AvatarsEnabled { get; set; }

        /// <summary>
        /// Gets or sets the catalog of the profile images (icons.json, URL or local path; empty = default in Jellyfin-Welten).
        /// </summary>
        public string AvatarCatalogUrl { get; set; }

        /// <summary>
        /// Gets or sets additional catalogs of profile images (one URL or path per line); their categories are added.
        /// </summary>
        public string AvatarExtraCatalogUrls { get; set; }

        /// <summary>Gets or sets a value indicating whether an episode number (xmltv_ns or onscreen) makes a series episode (EPG rule).</summary>
        public bool EpgSeriesByEpisodeNumber { get; set; }

        /// <summary>Gets or sets a value indicating whether a sub-title makes a series episode if it is no movie (EPG rule).</summary>
        public bool EpgSeriesBySubTitle { get; set; }

        /// <summary>Gets or sets a value indicating whether the movie rule is on (EPG rule).</summary>
        public bool EpgMovieRule { get; set; }

        /// <summary>Gets or sets the minimum length of a movie in minutes.</summary>
        public int EpgMovieMinMinutes { get; set; }

        /// <summary>Gets or sets a value indicating whether a director counts as movie evidence.</summary>
        public bool EpgMovieByDirector { get; set; }

        /// <summary>Gets or sets a value indicating whether a production year alone counts as movie evidence (off by default; replaces EpgMovieByDate of 12.26.0).</summary>
        public bool EpgMovieByYear { get; set; }

        /// <summary>Gets or sets a value indicating whether an IMDb link counts as movie evidence.</summary>
        public bool EpgMovieByImdb { get; set; }

        /// <summary>Gets or sets categories that count as movie evidence (only with the minimum length).</summary>
        public string EpgMovieCategories { get; set; }

        /// <summary>Gets or sets categories that never make a movie (documentaries).</summary>
        public string EpgNoMovieCategories { get; set; }

        /// <summary>Gets or sets categories that are never movie or series (teleshopping).</summary>
        public string EpgOtherCategories { get; set; }

        /// <summary>Gets or sets channels whose programmes are never movie or series (shopping channels).</summary>
        public string EpgOtherChannels { get; set; }

        /// <summary>Gets or sets the kids categories.</summary>
        public string EpgKidsCategories { get; set; }

        /// <summary>Gets or sets the kids channels (name contains).</summary>
        public string EpgKidsChannels { get; set; }

        /// <summary>Gets or sets the news categories.</summary>
        public string EpgNewsCategories { get; set; }

        /// <summary>Gets or sets the news channels.</summary>
        public string EpgNewsChannels { get; set; }

        /// <summary>Gets or sets the sports categories.</summary>
        public string EpgSportsCategories { get; set; }

        /// <summary>Gets or sets the sports channels.</summary>
        public string EpgSportsChannels { get; set; }

        /// <summary>
        /// Gets or sets the users (IDs without dashes) who get the Quallenkino-style player in Jellyfin Web (test, opt-in).
        /// </summary>
        public string[] WebPlayerUserIds { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether every user gets the Quallenkino player (default since 12.29.0; otherwise only the listed users).
        /// </summary>
        public bool WebPlayerForAll { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Jellyfin Web reports errors and the last steps before a crash to the server log (since 12.30.0).
        /// </summary>
        public bool WebDiagnostics { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether public similarity lists (TMDB, optionally Trakt) are used as an extra signal (since 12.33.0).
        /// Only title IDs of the library are sent, never users or watch history.
        /// </summary>
        public bool EnableExternalSimilarity { get; set; }

        /// <summary>
        /// Gets or sets the weight of the public similarity lists (0 = off, 1 = as strong as the own similarity).
        /// </summary>
        public double ExternalSimilarityWeight { get; set; }

        /// <summary>
        /// Gets or sets how many days a fetched list is kept before it is fetched again.
        /// </summary>
        public int ExternalSimilarityCacheDays { get; set; }

        /// <summary>
        /// Gets or sets how many titles are looked up at most per run (the rest follows in the next runs).
        /// </summary>
        public int ExternalSimilarityMaxLookupsPerRun { get; set; }

        /// <summary>
        /// Gets or sets an own TMDB API key (v3 key or v4 read token). Empty = the key of Jellyfin's TMDb plugin.
        /// </summary>
        public string TmdbApiKey { get; set; }

        /// <summary>
        /// Gets or sets the client ID of an own Trakt API app for public "related" lists. Empty = Trakt is not used.
        /// </summary>
        public string TraktClientId { get; set; }

        /// <summary>
        /// Gets or sets the client secret of the own Trakt API app (needed for the login of users with a code).
        /// </summary>
        public string TraktClientSecret { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether personal Trakt recommendations are used for users with a Trakt account
        /// (own login in Quallenkino, or the link of the Trakt plugin read only). Each user can switch it off (since 12.33.0).
        /// </summary>
        public bool EnableTraktPersonal { get; set; }

        /// <summary>
        /// Gets or sets the weight of the personal Trakt recommendations (0 = off).
        /// </summary>
        public double TraktPersonalWeight { get; set; }

        /// <summary>
        /// Gets or sets the public address of the server for calendar links (e.g. https://jellyfin.example.org).
        /// Empty = taken from the request.
        /// </summary>
        public string CalendarPublicUrl { get; set; }

        /// <summary>
        /// Gets or sets the reminder before a remembered programme in minutes (0 = no alarm).
        /// </summary>
        public int CalendarAlarmMinutes { get; set; }

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

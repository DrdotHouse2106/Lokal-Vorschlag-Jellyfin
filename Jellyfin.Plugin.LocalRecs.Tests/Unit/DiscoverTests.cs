using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using System;
using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Discover;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für die Entdecken-Reihen aus TMDB.
    /// </summary>
    public class DiscoverTests
    {
        private const string MovieJson = "{\"id\":550,\"title\":\"Neuer Film\",\"original_title\":\"New Movie\",\"overview\":\"Worum es geht.\",\"poster_path\":\"/p.jpg\",\"backdrop_path\":\"/b.jpg\","
            + "\"release_date\":\"2026-10-01\",\"runtime\":118,\"popularity\":55.5,\"genres\":[{\"id\":28,\"name\":\"Action\"},{\"id\":35,\"name\":\"Komödie\"}],"
            + "\"release_dates\":{\"results\":[{\"iso_3166_1\":\"US\",\"release_dates\":[{\"type\":3,\"release_date\":\"2026-10-24T00:00:00.000Z\",\"certification\":\"R\"}]},"
            + "{\"iso_3166_1\":\"DE\",\"release_dates\":[{\"type\":1,\"release_date\":\"2026-09-10T00:00:00.000Z\",\"certification\":\"\"},{\"type\":3,\"release_date\":\"2026-11-05T00:00:00.000Z\",\"certification\":\"12\"},{\"type\":4,\"release_date\":\"2027-01-10T00:00:00.000Z\"}]}]},"
            + "\"videos\":{\"results\":[{\"site\":\"YouTube\",\"type\":\"Teaser\",\"key\":\"teaserDE\",\"iso_639_1\":\"de\"},{\"site\":\"YouTube\",\"type\":\"Trailer\",\"key\":\"trailerEN\",\"iso_639_1\":\"en\",\"official\":true},"
            + "{\"site\":\"YouTube\",\"type\":\"Trailer\",\"key\":\"trailerDE\",\"name\":\"Trailer (deutsch)\",\"iso_639_1\":\"de\"}]},"
            + "\"watch/providers\":{\"results\":{\"DE\":{\"flatrate\":[{\"provider_name\":\"Netflix\"},{\"provider_name\":\"Netflix\"}]}}}}";

        private const string SeriesJson = "{\"id\":1399,\"name\":\"Neue Serie\",\"first_air_date\":\"2026-03-03\",\"episode_run_time\":[45],\"genres\":[{\"name\":\"Drama\"}],"
            + "\"content_ratings\":{\"results\":[{\"iso_3166_1\":\"US\",\"rating\":\"TV-MA\"},{\"iso_3166_1\":\"DE\",\"rating\":\"16\"}]},"
            + "\"watch/providers\":{\"results\":{\"DE\":{\"flatrate\":[{\"provider_name\":\"Disney Plus\"}]}}}}";

        [Fact]
        public void ParseList()
        {
            TmdbDiscover.ParseList("{\"results\":[{\"id\":1,\"popularity\":3.5},{\"id\":2},{\"title\":\"ohne\"}]}")
                .Should().Equal((1, 3.5), (2, 0.0));
        }

        [Fact]
        public void ParseMovie_GermanRatingTrailerProviders()
        {
            var (item, releases) = TmdbDiscover.ParseDetails(MovieJson, false, "DE")!.Value;
            item.TmdbId.Should().Be(550);
            item.MediaType.Should().Be("movie");
            item.Title.Should().Be("Neuer Film");
            item.Year.Should().Be(2026);
            item.Genres.Should().Equal("Action", "Komödie");
            item.OfficialRating.Should().Be("12");
            item.RuntimeMinutes.Should().Be(118);
            item.PosterUrl.Should().Be("https://image.tmdb.org/t/p/w342/p.jpg");
            item.TrailerKey.Should().Be("trailerDE", "deutscher Trailer vor deutschem Teaser und englischem Trailer");
            item.Providers.Should().Equal("Netflix");
            TmdbDiscover.CinemaStart(releases).Should().Be(new DateOnly(2026, 11, 5), "deutscher Kinostart, nicht US");
            TmdbDiscover.LocalRelease(releases).Should().Be(new DateOnly(2026, 11, 5));
        }

        [Fact]
        public void ParseSeries_RatingRuntimeProviders()
        {
            var (item, releases) = TmdbDiscover.ParseDetails(SeriesJson, true, "DE")!.Value;
            item.MediaType.Should().Be("tv");
            item.OfficialRating.Should().Be("16");
            item.RuntimeMinutes.Should().Be(45);
            item.Providers.Should().Equal("Disney+");
            releases.Should().ContainSingle().Which.Should().Be((0, new DateOnly(2026, 3, 3)));
        }

        [Fact]
        public void Cinema_NextWeeksSoonestFirst()
        {
            var today = new DateOnly(2026, 10, 25);
            var details = new[]
            {
                Detail(1, (3, new DateOnly(2026, 11, 5))),
                Detail(2, (3, new DateOnly(2026, 10, 26))),
                Detail(3, (3, new DateOnly(2026, 10, 1))),
                Detail(4, (3, new DateOnly(2027, 3, 1))),
                Detail(5, (2, new DateOnly(2026, 10, 25)))
            };
            var row = DiscoverService.BuildCinema(details, today);
            row.Select(i => i.TmdbId).Should().Equal(5, 2, 1);
            row[0].ReleaseText.Should().Be("Kinostart heute");
            row[1].ReleaseText.Should().Be("Kinostart morgen");
            row[2].ReleaseText.Should().Be("Kinostart am Do. 05.11.");
            row[2].ReleaseDate.Should().Be("2026-11-05");
        }

        [Fact]
        public void NewThisYear_ReleasedNewestFirstThenUpcoming()
        {
            var today = new DateOnly(2026, 10, 25);
            var details = new[]
            {
                Detail(1, (3, new DateOnly(2026, 3, 1))),
                Detail(2, (4, new DateOnly(2026, 10, 20))),
                Detail(3, (3, new DateOnly(2026, 12, 18))),
                Detail(4, (0, new DateOnly(2025, 12, 30))),
                Detail(5, (3, new DateOnly(2026, 11, 1)))
            };
            var row = DiscoverService.BuildNewThisYear(details, today, false);
            // gleiche Beliebtheit: je näher am heutigen Tag, desto weiter vorne
            row.Select(i => i.TmdbId).Should().Equal(2, 5, 3, 1);
            row[0].ReleaseText.Should().Be("Erschienen am 20.10.");
            row[0].Released.Should().BeTrue();
            row[1].ReleaseText.Should().Be("Erscheint am So. 01.11.");
            row[2].ReleaseText.Should().Be("Erscheint am Fr. 18.12.");
        }

        [Fact]
        public void NewSeries_UsesFirstAirDate()
        {
            var today = new DateOnly(2026, 10, 25);
            var row = DiscoverService.BuildNewThisYear(new[] { Detail(7, (0, new DateOnly(2026, 10, 24))), Detail(8, (0, new DateOnly(2026, 10, 26))) }, today, true);
            row.Select(i => i.ReleaseText).Should().Equal("Gestartet gestern", "Startet morgen");
        }

        [Fact]
        public void NewThisYear_PopularityMixesWithDate_AndNicheTitlesAreLeftOut()
        {
            var today = new DateOnly(2026, 10, 25);
            var popularOlder = Detail(1, (0, new DateOnly(2026, 8, 1)));
            popularOlder.Item.Popularity = 900;
            popularOlder.Item.VoteCount = 3000;
            var nicheRecent = Detail(2, (3, new DateOnly(2026, 10, 24)));
            nicheRecent.Item.VoteCount = 3;
            var chinese = Detail(3, (0, new DateOnly(2026, 10, 20)));
            chinese.Item.Title = "嫁金钗";
            chinese.Item.OriginalTitle = "嫁金钗";
            var untranslated = Detail(4, (0, new DateOnly(2026, 10, 20)));
            untranslated.Item.Overview = null;
            var quietUpcoming = Detail(5, (0, new DateOnly(2026, 11, 3)));
            quietUpcoming.Item.Popularity = 2;
            var normal = Detail(6, (0, new DateOnly(2026, 10, 10)));
            var weakRecent = Detail(7, (0, new DateOnly(2026, 10, 24)));
            weakRecent.Item.Popularity = 12;
            weakRecent.Item.VoteCount = 30;
            var row = DiscoverService.BuildNewThisYear(new[] { popularOlder, nicheRecent, chinese, untranslated, quietUpcoming, normal, weakRecent }, today, true);

            // Nischentitel, nicht übersetzte und nichtlateinische Titel fehlen; ein sehr beliebter älterer Titel steht vor einem
            // kaum beachteten neuen, ein gut beachteter neuer vor dem älteren
            row.Select(i => i.TmdbId).Should().Equal(6, 1, 7);
        }

        [Theory]
        [InlineData("Amazon Prime Video with Ads", "Amazon Prime Video")]
        [InlineData("HBO Max Amazon Channel", "HBO Max")]
        [InlineData("Netflix Standard with Ads", "Netflix")]
        [InlineData("Paramount Plus Apple TV Channel", "Paramount+")]
        [InlineData("Paramount Plus", "Paramount+")]
        [InlineData("Paramount+", "Paramount+")]
        [InlineData("Disney Plus", "Disney+")]
        [InlineData("Crunchyroll Amazon Channel", "Crunchyroll")]
        [InlineData("Anime Digital Network", "ADN")]
        [InlineData("Netflix", "Netflix")]
        public void ProviderName(string name, string expected)
        {
            TmdbDiscover.ProviderName(name).Should().Be(expected);
        }

        [Fact]
        public void Providers_AreMergedToMainNames()
        {
            const string Json = "{\"id\":9,\"name\":\"S\",\"watch/providers\":{\"results\":{\"DE\":{\"flatrate\":[{\"provider_name\":\"Amazon Prime Video\"},{\"provider_name\":\"Amazon Prime Video with Ads\"},"
                + "{\"provider_name\":\"HBO Max Amazon Channel\"},{\"provider_name\":\"HBO Max\"}]}}}}";
            TmdbDiscover.ParseDetails(Json, true, "DE")!.Value.Item.Providers.Should().Equal("Amazon Prime Video", "HBO Max");
        }

        [Fact]
        public void Providers_AtMostThree_PlusAndPlusSignMerged()
        {
            const string Json = "{\"id\":9,\"name\":\"S\",\"watch/providers\":{\"results\":{\"DE\":{\"flatrate\":[{\"provider_name\":\"Paramount Plus\"},{\"provider_name\":\"Paramount+\"},"
                + "{\"provider_name\":\"Crunchyroll\"},{\"provider_name\":\"Anime Digital Network\"},{\"provider_name\":\"Netflix\"}]}}}}";
            TmdbDiscover.ParseDetails(Json, true, "DE")!.Value.Item.Providers.Should().Equal("Paramount+", "Crunchyroll", "ADN");
        }

        [Fact]
        public void NewMovies_LeaveOutCinemaStarts()
        {
            var today = new DateOnly(2026, 10, 25);
            var row = DiscoverService.BuildNewThisYear(new[] { Detail(1, (3, new DateOnly(2026, 10, 30))), Detail(2, (3, new DateOnly(2026, 10, 1))) }, today, false, new HashSet<int> { 1 });
            row.Select(i => i.TmdbId).Should().Equal(2);
        }

        [Theory]
        [InlineData("Deadpool & Wolverine", true)]
        [InlineData("Die fabelhafte Welt der Amélie", true)]
        [InlineData("Ðorđe 2", true)]
        [InlineData("嫁金钗", false)]
        [InlineData("Брат", false)]
        [InlineData("", false)]
        public void IsLatin(string title, bool expected)
        {
            TmdbDiscover.IsLatin(title).Should().Be(expected);
        }

        [Fact]
        public void NoRating_FieldStaysEmpty()
        {
            const string Json = "{\"id\":10,\"title\":\"F\",\"release_dates\":{\"results\":[{\"iso_3166_1\":\"DE\",\"release_dates\":[{\"type\":3,\"release_date\":\"2026-11-05T00:00:00.000Z\",\"certification\":\"\"}]}]}}";
            TmdbDiscover.ParseDetails(Json, false, "DE")!.Value.Item.OfficialRating.Should().BeNull();
        }

        [Theory]
        [InlineData("Official Trailer", "en", "en")]
        [InlineData("Trailer (deutsch)", "en", "de")]
        [InlineData("Offizieller Trailer", "de", "de")]
        [InlineData("Apokalypse Z [Subtitled]", "en", "en")]
        [InlineData("Trailer German Subtitles", "en", "OmU")]
        [InlineData("Trailer OmU", "de", "OmU")]
        [InlineData("CRYSTAL LAKE Trailer German Deutsch OmU (2026)", "de", "OmU")]
        [InlineData("Official Trailer [ENG SUB]", "en", "en")]
        [InlineData("Official Trailer [ENG SUB]", "de", "en")]
        [InlineData("Trailer English Subtitles", "it", "it")]
        [InlineData("Trailer mit deutschen Untertiteln", "en", "OmU")]
        [InlineData("Trailer (OV)", "en", "en")]
        [InlineData("CLAYFACE Finaler Trailer German Deutsch (2026) DC", "de", "de")]
        public void TrailerLanguage_GermanSubtitledOther(string name, string language, string expected)
        {
            TmdbDiscover.TrailerLanguage(name, language).Should().Be(expected);
        }

        [Fact]
        public void ParseMovie_SubtitledWithoutGerman_IsForeign()
        {
            var json = "{\"id\":5,\"title\":\"Film\",\"videos\":{\"results\":["
                + "{\"site\":\"YouTube\",\"type\":\"Trailer\",\"key\":\"sub\",\"name\":\"Trailer [Subtitled]\",\"iso_639_1\":\"en\"},"
                + "{\"site\":\"YouTube\",\"type\":\"Trailer\",\"key\":\"en\",\"name\":\"Official Trailer\",\"iso_639_1\":\"en\"}]}}";
            var item = TmdbDiscover.ParseDetails(json, false, "DE")!.Value.Item;
            item.TrailerKey.Should().Be("sub");
            item.TrailerLanguage.Should().Be("en", "„[Subtitled]“ ohne deutsch ist fremdsprachig, nicht OmU");
            item.TrailerSource.Should().Be("tmdb");
        }

        [Fact]
        public void KinoCheck_PicksGermanTrailerBeforeTeaser()
        {
            var json = "{\"id\":\"x\",\"tmdb_id\":1,\"trailer\":{\"youtube_video_id\":\"teaser1\",\"title\":\"Teaser German Deutsch\",\"language\":\"de\",\"categories\":[\"Teaser\"]},"
                + "\"videos\":[{\"youtube_video_id\":\"clip1\",\"title\":\"Clip\",\"categories\":[\"Clip\"]},"
                + "{\"youtube_video_id\":\"trailerEN\",\"title\":\"Trailer\",\"language\":\"en\",\"categories\":[\"Trailer\"]},"
                + "{\"youtube_video_id\":\"trailer1\",\"title\":\"Buddy Trailer German Deutsch (2026)\",\"language\":\"de\",\"categories\":[\"Trailer\"]}]}";
            KinoCheck.PickTrailer(json).Should().Be(("trailer1", "Buddy Trailer German Deutsch (2026)", "de"));
        }

        [Fact]
        public void KinoCheck_RealAnswer_Clayface()
        {
            // Aufbau einer echten Antwort (gekürzt)
            var json = "{\"id\":\"n27\",\"tmdb_id\":1400940,\"imdb_id\":\"tt34890576\",\"language\":\"de\",\"title\":\"Clayface\",\"url\":\"https://kinocheck.de/film/n27/clayface-2026\","
                + "\"trailer\":{\"id\":\"fyv1\",\"youtube_video_id\":\"KYUtM4-uoFo\",\"title\":\"CLAYFACE Finaler Trailer German Deutsch (2026) DC\",\"language\":\"de\",\"categories\":[\"Trailer\"],\"views\":67326},"
                + "\"recommendations\":[],\"videos\":[{\"youtube_video_id\":\"clip\",\"title\":\"CLAYFACE Clip German Deutsch\",\"language\":\"de\",\"categories\":[\"Clip\"]}]}";
            KinoCheck.PickTrailer(json).Should().Be(("KYUtM4-uoFo", "CLAYFACE Finaler Trailer German Deutsch (2026) DC", "de"));
        }

        [Fact]
        public void KinoCheck_DubbedBeforeOmU_OmUMarked()
        {
            var omuOnly = "{\"trailer\":{\"youtube_video_id\":\"omu\",\"title\":\"WAR Trailer German Deutsch OmU (2026)\",\"language\":\"de\",\"categories\":[\"Trailer\"]},\"videos\":[]}";
            KinoCheck.PickTrailer(omuOnly).Should().Be(("omu", "WAR Trailer German Deutsch OmU (2026)", "OmU"));
            var both = "{\"trailer\":{\"youtube_video_id\":\"omu\",\"title\":\"WAR Trailer German Deutsch OmU (2026)\",\"language\":\"de\",\"categories\":[\"Trailer\"]},"
                + "\"videos\":[{\"youtube_video_id\":\"dub\",\"title\":\"WAR Teaser German Deutsch (2026)\",\"language\":\"de\",\"categories\":[\"Teaser\"]}]}";
            KinoCheck.PickTrailer(both)!.Value.Key.Should().Be("dub", "synchronisierter Teaser vor OmU-Trailer");
        }

        [Theory]
        [InlineData("")]
        [InlineData("[]")]
        [InlineData("{\"id\":\"j8s\",\"tmdb_id\":1514026,\"title\":\"Buddy\",\"trailer\":null,\"videos\":[]}")]
        [InlineData("{\"timestamp\":\"Sun, 01 Nov 2026 12:44:44 +0000\",\"error\":\"Error\",\"message\":\"show not found\",\"path\":\"/shows?tmdb_id=289517&language=de\"}")]
        [InlineData("{\"videos\":[{\"youtube_video_id\":\"c\",\"title\":\"Trailer-Kritik\",\"language\":\"de\",\"categories\":[\"Review\"]}]}")]
        [InlineData("{\"trailer\":null,\"videos\":[]}")]
        [InlineData("kein json")]
        public void KinoCheck_NothingUsable_IsNull(string json)
        {
            KinoCheck.PickTrailer(json).Should().BeNull();
        }

        [Fact]
        public void KinoCheck_PathForMoviesAndShows()
        {
            KinoCheck.Path(42, false).Should().Be("movies?tmdb_id=42&language=de");
            KinoCheck.Path(7, true).Should().Be("shows?tmdb_id=7&language=de");
        }

        [Fact]
        public async Task GermanTrailers_KinoCheckReplacesForeign_Cached_ErrorKeepsTmdb()
        {
            var calls = new List<string>();
            Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
            {
                var url = request.RequestUri!.ToString();
                calls.Add(url);
                var body = url.Contains("tmdb_id=1&", StringComparison.Ordinal)
                    ? "{\"trailer\":{\"youtube_video_id\":\"deTrailer\",\"title\":\"Trailer German Deutsch\",\"language\":\"de\",\"categories\":[\"Trailer\"]},\"videos\":[]}"
                    : "{\"error\":\"Error\",\"message\":\"show not found\"}";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
            }

            var service = new DiscoverService(Mock.Of<ILogger<DiscoverService>>(), Mock.Of<ILibraryManager>(), null, Send);
            DiscoverItem[] Items() => new[]
            {
                new DiscoverItem { TmdbId = 1, MediaType = "movie", TrailerKey = "en", TrailerLanguage = "en", TrailerSource = "tmdb" },
                new DiscoverItem { TmdbId = 2, MediaType = "tv", TrailerKey = "en2", TrailerLanguage = "en", TrailerSource = "tmdb" },
                new DiscoverItem { TmdbId = 3, MediaType = "movie", TrailerKey = "de3", TrailerLanguage = "de", TrailerSource = "tmdb" }
            };

            var first = Items();
            await service.GermanTrailersAsync(first, CancellationToken.None);
            first[0].TrailerKey.Should().Be("deTrailer");
            first[0].TrailerLanguage.Should().Be("de");
            first[0].TrailerSource.Should().Be("kinocheck");
            first[1].TrailerKey.Should().Be("en2", "KinoCheck kennt die Serie nicht – TMDB-Trailer bleibt");
            first[2].TrailerKey.Should().Be("de3");
            calls.Should().HaveCount(2, "Titel mit deutschem TMDB-Trailer werden nicht nachgefragt");
            calls.Should().Contain(c => c.StartsWith("https://api.kinocheck.de/shows?tmdb_id=2&language=de", StringComparison.Ordinal));

            var second = Items();
            await service.GermanTrailersAsync(second, CancellationToken.None);
            second[0].TrailerKey.Should().Be("deTrailer");
            calls.Should().HaveCount(2, "Antworten (auch „nicht gefunden“) werden zwischengespeichert");
        }

        private static (DiscoverItem Item, IReadOnlyList<(int Type, DateOnly Date)> Releases) Detail(int id, params (int Type, DateOnly Date)[] releases)
            => (new DiscoverItem { TmdbId = id, Title = "T" + id, OriginalTitle = "T" + id, Overview = "Text", VoteCount = 100, Popularity = 50 }, releases);
    }
}

using FluentAssertions;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.Metadata;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für den Metadaten-Check und die einmalige Übernahme neuer Standards in alte Einstellungen.
    /// </summary>
    public class MetadataCheckTests
    {
        [Fact]
        public void CompleteMovie_HasNothingMissing()
        {
            var missing = MetadataCheck.Missing(Complete(true));

            missing.Should().BeEmpty();
            MetadataCheck.Level(missing).Should().BeEmpty();
        }

        [Fact]
        public void WithoutGenresOrId_IsTooLittle()
        {
            var facts = Complete(true);
            facts.HasProviderId = false;

            var missing = MetadataCheck.Missing(facts);

            missing.Should().Equal(MetadataCheck.ProviderId);
            MetadataCheck.Level(missing).Should().Be("zu wenig");
        }

        [Fact]
        public void WithoutTags_IsLittle_OneLightGapIsFine()
        {
            var noTags = Complete(true);
            noTags.TagCount = 0;
            var noRating = Complete(true);
            noRating.HasRating = false;

            MetadataCheck.Level(MetadataCheck.Missing(noTags)).Should().Be("wenig");
            MetadataCheck.Level(MetadataCheck.Missing(noRating)).Should().BeEmpty();
        }

        [Fact]
        public void Series_NeedsNoDirector()
        {
            var facts = Complete(false);
            facts.DirectorCount = 0;

            MetadataCheck.Missing(facts).Should().BeEmpty();
        }

        [Fact]
        public void Score_WeighsHeavyMediumLight()
        {
            MetadataCheck.Score(new[] { MetadataCheck.Genres, MetadataCheck.Tags, MetadataCheck.Year }).Should().Be(6);
        }

        [Fact]
        public void Migrate_AddsTravelToNeverMovieOnce()
        {
            var config = new PluginConfiguration { EpgNoMovieCategories = "Dokumentation, Eigene", ConfigRevision = 0 };

            config.Migrate().Should().BeTrue();
            config.EpgNoMovieCategories.Should().Be("Dokumentation, Eigene, Reisen");

            config.EpgNoMovieCategories = "Dokumentation";
            config.Migrate().Should().BeFalse("nur einmal – wer Reisen danach entfernt, behält das");
            config.EpgNoMovieCategories.Should().Be("Dokumentation");
        }

        [Fact]
        public void NewConfiguration_HasTravelWithoutDuplicate()
        {
            var config = new PluginConfiguration();

            config.Migrate();

            config.EpgNoMovieCategories.Should().Be("Dokumentation, Doku, Dokumentarfilm, Dokumentarreihe, Reportage, Magazin, Reisen");
        }

        private static MetadataCheck.ItemFacts Complete(bool movie) => new MetadataCheck.ItemFacts
        {
            IsMovie = movie,
            GenreCount = 2,
            TagCount = 5,
            ActorCount = 8,
            DirectorCount = 1,
            HasProviderId = true,
            HasYear = true,
            HasRating = true,
            HasOverview = true,
            HasImage = true
        };
    }
}

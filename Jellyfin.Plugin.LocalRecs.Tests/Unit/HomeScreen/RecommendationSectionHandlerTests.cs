using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.HomeScreen;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.HomeScreen
{
    /// <summary>
    /// Tests für die Startseiten-Bereiche und den Speicher der Empfehlungen.
    /// </summary>
    public class RecommendationSectionHandlerTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Mock<IUserManager> _userManager = new Mock<IUserManager>();
        private readonly Mock<ILibraryManager> _libraryManager = new Mock<ILibraryManager>();
        private readonly Mock<IUserDataManager> _userDataManager = new Mock<IUserDataManager>();
        private readonly Mock<IDtoService> _dtoService = new Mock<IDtoService>();
        private readonly RecommendationStore _store;
        private readonly RecommendationSectionHandler _handler;
        private readonly User _user;

        public RecommendationSectionHandlerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid().ToString());
            _store = new RecommendationStore(NullLogger<RecommendationStore>.Instance, Path.Combine(_tempDir, "recommendations.json"));

            _user = new User("Anna", "Default", "Default") { Id = Guid.NewGuid() };
            _userManager.Setup(m => m.GetUserById(_user.Id)).Returns(_user);
            _dtoService
                .Setup(m => m.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User?>(), It.IsAny<BaseItem?>(), It.IsAny<bool>()))
                .Returns((IReadOnlyList<BaseItem> items, DtoOptions _, User? _, BaseItem? _, bool _) =>
                    items.Select(i => new BaseItemDto { Id = i.Id }).ToList());

            _handler = new RecommendationSectionHandler(
                NullLogger<RecommendationSectionHandler>.Instance,
                _userManager.Object,
                _libraryManager.Object,
                _userDataManager.Object,
                _dtoService.Object,
                _store);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Fact]
        public void GetResults_ReturnsMoviesInRecommendationOrder()
        {
            var first = AddItem<Movie>();
            var second = AddItem<Movie>();
            Store(movies: new[] { second, first });

            var result = _handler.GetMovieResults(new SectionRequest { UserId = _user.Id });

            result.Items.Select(i => i.Id).Should().Equal(second, first);
            result.TotalRecordCount.Should().Be(2);
        }

        [Fact]
        public void GetResults_SeriesSection_ReturnsSeries()
        {
            var movie = AddItem<Movie>();
            var series = AddItem<Series>();
            Store(movies: new[] { movie }, series: new[] { series });

            var result = _handler.GetSeriesResults(new SectionRequest { UserId = _user.Id });

            result.Items.Select(i => i.Id).Should().Equal(series);
        }

        [Fact]
        public void GetItems_SkipsWatchedStartedInvisibleAndMissingItems()
        {
            var visible = AddItem<Movie>();
            var watched = AddItem<Movie>(played: true);
            var started = AddItem<Movie>(position: 1000);
            var hidden = AddItem<Movie>(visible: false);
            var missing = Guid.NewGuid();
            Store(movies: new[] { watched, started, hidden, missing, visible });

            var items = _handler.GetItems(_user.Id, RecommendationSectionHandler.MoviesSection);

            items.Select(i => i.Id).Should().Equal(visible);
        }

        [Fact]
        public void GetItems_SameMovieInTwoVersions_IsShownOnce()
        {
            var first = AddItem<Movie>();
            var second = AddItem<Movie>();
            var other = AddItem<Movie>();
            _libraryManager.Object.GetItemById(first)!.ProviderIds["Tmdb"] = "123";
            _libraryManager.Object.GetItemById(second)!.ProviderIds["Tmdb"] = "123";
            Store(movies: new[] { first, second, other });

            _handler.GetItems(_user.Id, RecommendationSectionHandler.MoviesSection).Select(i => i.Id).Should().Equal(first, other);
        }

        [Fact]
        public void DuplicateKey_WithoutProviderIds_UsesNameAndYear()
        {
            var a = new Movie { Name = "Meine Wiedergeburt als Schleim", ProductionYear = 2022 };
            var b = new Movie { Name = "meine wiedergeburt als schleim ", ProductionYear = 2022 };
            var c = new Movie { Name = "Meine Wiedergeburt als Schleim", ProductionYear = 2018 };

            RecommendationSectionHandler.DuplicateKey(a).Should().Be(RecommendationSectionHandler.DuplicateKey(b));
            RecommendationSectionHandler.DuplicateKey(a).Should().NotBe(RecommendationSectionHandler.DuplicateKey(c));
        }

        [Fact]
        public void GetItems_UnknownUserOrNoRecommendations_ReturnsEmpty()
        {
            _handler.GetItems(Guid.NewGuid(), RecommendationSectionHandler.MoviesSection).Should().BeEmpty();
            _handler.GetItems(_user.Id, RecommendationSectionHandler.MoviesSection).Should().BeEmpty();
        }

        [Fact]
        public void GetResults_OtherUsersRecommendationsAreNotReturned()
        {
            var otherUser = new User("Ben", "Default", "Default") { Id = Guid.NewGuid() };
            _userManager.Setup(m => m.GetUserById(otherUser.Id)).Returns(otherUser);
            var movie = AddItem<Movie>();
            Store(movies: new[] { movie });

            var result = _handler.GetMovieResults(new SectionRequest { UserId = otherUser.Id });

            result.Items.Should().BeEmpty();
        }

        [Fact]
        public void Store_PersistsRecommendationsAcrossInstances()
        {
            var movie = Guid.NewGuid();
            var series = Guid.NewGuid();
            Store(movies: new[] { movie }, series: new[] { series });

            var reloaded = new RecommendationStore(NullLogger<RecommendationStore>.Instance, Path.Combine(_tempDir, "recommendations.json"));

            reloaded.UserCount.Should().Be(1);
            reloaded.LastUpdated.Should().NotBeNull();
            reloaded.Get(_user.Id)!.Movies.Should().Equal(movie);
            reloaded.Get(_user.Id)!.Series.Should().Equal(series);
        }

        [Fact]
        public void Store_CorruptFile_StartsEmpty()
        {
            Directory.CreateDirectory(_tempDir);
            var path = Path.Combine(_tempDir, "broken.json");
            File.WriteAllText(path, "{ kein json");

            var store = new RecommendationStore(NullLogger<RecommendationStore>.Instance, path);

            store.UserCount.Should().Be(0);
            store.LastUpdated.Should().BeNull();
        }

        private Guid AddItem<T>(bool visible = true, bool played = false, long position = 0)
            where T : BaseItem
        {
            var id = Guid.NewGuid();
            var item = new Mock<T>();
            item.Object.Id = id;
            item.Setup(i => i.IsVisible(_user, It.IsAny<bool>())).Returns(visible);
            item.Setup(i => i.IsVisibleStandalone(_user)).Returns(visible);
            _libraryManager.Setup(m => m.GetItemById(id)).Returns(item.Object);
            _userDataManager
                .Setup(m => m.GetUserData(_user, item.Object))
                .Returns(new UserItemData { Key = id.ToString(), Played = played, PlaybackPositionTicks = position });
            return id;
        }

        private void Store(Guid[]? movies = null, Guid[]? series = null)
        {
            _store.ReplaceAll(new Dictionary<Guid, UserRecommendations>
            {
                [_user.Id] = new UserRecommendations
                {
                    Movies = movies ?? Array.Empty<Guid>(),
                    Series = series ?? Array.Empty<Guid>()
                }
            });
        }
    }
}

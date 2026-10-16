using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Configuration;
using Jellyfin.Plugin.LocalRecs.VirtualLibrary;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using LocalMediaType = Jellyfin.Plugin.LocalRecs.Models.MediaType;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit.VirtualLibrary
{
    /// <summary>
    /// Tests für das automatische Anlegen der Empfehlungs-Bibliotheken und das Setzen der Zugriffsrechte.
    /// </summary>
    public class LibraryProvisioningServiceTests : IDisposable
    {
        private readonly string _basePath;
        private readonly Mock<ILibraryManager> _libraryManager = new Mock<ILibraryManager>();
        private readonly Mock<IUserManager> _userManager = new Mock<IUserManager>();
        private readonly VirtualLibraryManager _virtualLibraryManager;
        private readonly List<VirtualFolderInfo> _folders = new List<VirtualFolderInfo>();
        private readonly List<User> _users = new List<User>();
        private readonly Dictionary<Guid, UserPolicy> _policies = new Dictionary<Guid, UserPolicy>();
        private readonly PluginConfiguration _config = new PluginConfiguration();
        private readonly LibraryProvisioningService _service;
        private int _saveCount;

        public LibraryProvisioningServiceTests()
        {
            _basePath = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid().ToString(), "virtual-libraries");
            _virtualLibraryManager = new VirtualLibraryManager(
                NullLogger<VirtualLibraryManager>.Instance,
                _libraryManager.Object,
                _basePath);

            _libraryManager.Setup(m => m.GetVirtualFolders()).Returns(() => _folders.ToList());
            _libraryManager
                .Setup(m => m.AddVirtualFolder(It.IsAny<string>(), It.IsAny<CollectionTypeOptions?>(), It.IsAny<LibraryOptions>(), It.IsAny<bool>()))
                .Returns((string name, CollectionTypeOptions? type, LibraryOptions options, bool _) =>
                {
                    AddFolder(name, type, options.PathInfos.Select(p => p.Path).ToArray());
                    return Task.CompletedTask;
                });
            _libraryManager
                .Setup(m => m.RemoveVirtualFolder(It.IsAny<string>(), It.IsAny<bool>()))
                .Returns((string name, bool _) =>
                {
                    _folders.RemoveAll(f => f.Name == name);
                    return Task.CompletedTask;
                });

            _userManager.Setup(m => m.GetUsers()).Returns(() => _users);
            _userManager
                .Setup(m => m.GetUserDto(It.IsAny<User>(), It.IsAny<string?>()))
                .Returns((User user, string? _) => new UserDto { Id = user.Id, Policy = ClonePolicy(_policies[user.Id]) });
            _userManager
                .Setup(m => m.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
                .Returns((Guid id, UserPolicy policy) =>
                {
                    _policies[id] = policy;
                    return Task.CompletedTask;
                });

            _service = new LibraryProvisioningService(
                NullLogger<LibraryProvisioningService>.Instance,
                _libraryManager.Object,
                _userManager.Object,
                _virtualLibraryManager,
                () => _config,
                () => _saveCount++);
        }

        public void Dispose()
        {
            var root = Path.GetDirectoryName(_basePath);
            if (root != null && Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                    // Best effort cleanup
                }
            }
        }

        [Theory]
        [InlineData("Empfohlene Filme – {user}", "Anna", "Empfohlene Filme – Anna")]
        [InlineData("{USER}s Filme", "Anna", "Annas Filme")]
        [InlineData("Empfehlungen", "Anna", "Empfehlungen (Anna)")]
        [InlineData("", "Anna", "Anna")]
        public void BuildLibraryName_ReplacesPlaceholder(string template, string username, string expected)
        {
            LibraryProvisioningService.BuildLibraryName(template, username).Should().Be(expected);
        }

        [Fact]
        public async Task ProvisionAll_CreatesMovieAndTvLibraryPerUser()
        {
            var anna = AddUser("Anna");
            var ben = AddUser("Ben");

            var result = await _service.ProvisionAllAsync(false, CancellationToken.None);

            result.LibrariesCreated.Should().Be(4);
            _folders.Should().ContainSingle(f => f.Name == "Empfohlene Filme – Anna"
                && f.CollectionType == CollectionTypeOptions.movies
                && f.Locations.Single() == _virtualLibraryManager.GetUserLibraryPath(anna.Id, LocalMediaType.Movie));
            _folders.Should().ContainSingle(f => f.Name == "Empfohlene Serien – Ben"
                && f.CollectionType == CollectionTypeOptions.tvshows
                && f.Locations.Single() == _virtualLibraryManager.GetUserLibraryPath(ben.Id, LocalMediaType.Series));
        }

        [Fact]
        public async Task ProvisionAll_ExistingLibraryWithSamePath_IsNotCreatedAgain()
        {
            var anna = AddUser("Anna");
            AddFolder("Annas eigene Filme", CollectionTypeOptions.movies, _virtualLibraryManager.GetUserLibraryPath(anna.Id, LocalMediaType.Movie));

            var result = await _service.ProvisionAllAsync(false, CancellationToken.None);

            result.LibrariesCreated.Should().Be(1);
            _folders.Count(f => f.CollectionType == CollectionTypeOptions.movies).Should().Be(1);
        }

        [Fact]
        public async Task ProvisionAll_AutoCreateDisabled_CreatesNothing_UnlessForced()
        {
            AddUser("Anna");
            _config.AutoCreateLibraries = false;

            (await _service.ProvisionAllAsync(false, CancellationToken.None)).LibrariesCreated.Should().Be(0);
            (await _service.ProvisionAllAsync(true, CancellationToken.None)).LibrariesCreated.Should().Be(2);
        }

        [Fact]
        public async Task ProvisionAll_UserWithAllLibraries_SeesRegularLibrariesAndOnlyOwnRecommendations()
        {
            var movies = AddFolder("Filme", CollectionTypeOptions.movies, "/media/filme");
            var shows = AddFolder("Serien", CollectionTypeOptions.tvshows, "/media/serien");
            var anna = AddUser("Anna");
            var ben = AddUser("Ben");

            await _service.ProvisionAllAsync(false, CancellationToken.None);

            var annaPolicy = _policies[anna.Id];
            annaPolicy.EnableAllFolders.Should().BeFalse();
            annaPolicy.EnabledFolders.Should().BeEquivalentTo(new[] { movies, shows }.Concat(RecIds(anna)));
            annaPolicy.EnabledFolders.Should().NotIntersectWith(RecIds(ben));
            _config.ManagedAllLibrariesUserIds.Should().BeEquivalentTo(new[] { anna.Id, ben.Id });
            _saveCount.Should().BeGreaterThan(0);
        }

        [Fact]
        public async Task ProvisionAll_RestrictedUser_KeepsOwnSelection()
        {
            var movies = AddFolder("Filme", CollectionTypeOptions.movies, "/media/filme");
            AddFolder("Serien", CollectionTypeOptions.tvshows, "/media/serien");
            var anna = AddUser("Anna", allFolders: false, enabled: new[] { movies });
            var ben = AddUser("Ben");

            await _service.ProvisionAllAsync(false, CancellationToken.None);

            _policies[anna.Id].EnabledFolders.Should().BeEquivalentTo(new[] { movies }.Concat(RecIds(anna)));
            _config.ManagedAllLibrariesUserIds.Should().NotContain(anna.Id);
            _policies[ben.Id].EnabledFolders.Should().NotIntersectWith(RecIds(anna));
        }

        [Fact]
        public async Task ProvisionAll_RemovesForeignRecommendationLibraryFromUser()
        {
            var anna = AddUser("Anna");
            var ben = AddUser("Ben");
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            // Admin gibt Anna versehentlich Bens Empfehlungen frei
            _policies[anna.Id].EnabledFolders = _policies[anna.Id].EnabledFolders.Concat(RecIds(ben)).ToArray();

            var result = await _service.ProvisionAllAsync(false, CancellationToken.None);

            result.UsersUpdated.Should().Be(1);
            _policies[anna.Id].EnabledFolders.Should().NotIntersectWith(RecIds(ben));
        }

        [Fact]
        public async Task ProvisionAll_NewLibrary_IsGrantedOnlyToFormerAllLibrariesUsers()
        {
            var movies = AddFolder("Filme", CollectionTypeOptions.movies, "/media/filme");
            var anna = AddUser("Anna");
            var ben = AddUser("Ben", allFolders: false, enabled: new[] { movies });
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            var music = AddFolder("Musik", CollectionTypeOptions.music, "/media/musik");
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            _policies[anna.Id].EnabledFolders.Should().Contain(music);
            _policies[ben.Id].EnabledFolders.Should().NotContain(music);
        }

        [Fact]
        public async Task ProvisionAll_LibraryRemovedByAdmin_IsNotGrantedAgain()
        {
            var movies = AddFolder("Filme", CollectionTypeOptions.movies, "/media/filme");
            var shows = AddFolder("Serien", CollectionTypeOptions.tvshows, "/media/serien");
            var anna = AddUser("Anna");
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            _policies[anna.Id].EnabledFolders = _policies[anna.Id].EnabledFolders.Where(id => id != shows).ToArray();
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            _policies[anna.Id].EnabledFolders.Should().Contain(movies);
            _policies[anna.Id].EnabledFolders.Should().NotContain(shows);
        }

        [Fact]
        public async Task ProvisionAll_UserRestrictedByAdminAfterwards_DoesNotGetNewLibraries()
        {
            var movies = AddFolder("Filme", CollectionTypeOptions.movies, "/media/filme");
            var shows = AddFolder("Serien", CollectionTypeOptions.tvshows, "/media/serien");
            var anna = AddUser("Anna");
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            // Admin schränkt Anna nachträglich auf "Filme" ein
            _policies[anna.Id].EnabledFolders = new[] { movies }.Concat(RecIds(anna)).ToArray();
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            var music = AddFolder("Musik", CollectionTypeOptions.music, "/media/musik");
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            _policies[anna.Id].EnabledFolders.Should().NotContain(music).And.NotContain(shows);
            _config.ManagedAllLibrariesUserIds.Should().NotContain(anna.Id);
        }

        [Fact]
        public async Task ProvisionAll_AccessManagementDisabled_LeavesPoliciesUntouched()
        {
            var anna = AddUser("Anna");
            _config.AutoManageLibraryAccess = false;

            await _service.ProvisionAllAsync(false, CancellationToken.None);

            _policies[anna.Id].EnableAllFolders.Should().BeTrue();
            _userManager.Verify(m => m.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()), Times.Never);
        }

        [Fact]
        public async Task RemoveUserLibraries_RemovesOnlyThatUsersLibraries()
        {
            var anna = AddUser("Anna");
            var ben = AddUser("Ben");
            AddFolder("Filme", CollectionTypeOptions.movies, "/media/filme");
            await _service.ProvisionAllAsync(false, CancellationToken.None);

            await _service.RemoveUserLibrariesAsync(anna.Id);

            _folders.Select(f => f.Name).Should().BeEquivalentTo(new[] { "Filme", "Empfohlene Filme – Ben", "Empfohlene Serien – Ben" });
            _config.ManagedAllLibrariesUserIds.Should().NotContain(anna.Id);
        }

        private static UserPolicy ClonePolicy(UserPolicy policy)
        {
            return new UserPolicy
            {
                EnableAllFolders = policy.EnableAllFolders,
                EnabledFolders = policy.EnabledFolders.ToArray(),
                AuthenticationProviderId = policy.AuthenticationProviderId,
                PasswordResetProviderId = policy.PasswordResetProviderId
            };
        }

        private User AddUser(string name, bool allFolders = true, Guid[]? enabled = null)
        {
            var user = new User(name, "Default", "Default") { Id = Guid.NewGuid() };
            _users.Add(user);
            _policies[user.Id] = new UserPolicy
            {
                EnableAllFolders = allFolders,
                EnabledFolders = enabled ?? Array.Empty<Guid>(),
                AuthenticationProviderId = "Default",
                PasswordResetProviderId = "Default"
            };
            return user;
        }

        private Guid AddFolder(string name, CollectionTypeOptions? type, params string[] locations)
        {
            var id = Guid.NewGuid();
            _folders.Add(new VirtualFolderInfo
            {
                Name = name,
                CollectionType = type,
                Locations = locations,
                ItemId = id.ToString("N")
            });
            return id;
        }

        private IEnumerable<Guid> RecIds(User user)
        {
            return new[] { LocalMediaType.Movie, LocalMediaType.Series }
                .Select(t => _service.FindUserLibrary(_folders, user.Id, t))
                .Where(f => f != null)
                .Select(f => Guid.Parse(f!.ItemId))
                .ToList();
        }
    }
}

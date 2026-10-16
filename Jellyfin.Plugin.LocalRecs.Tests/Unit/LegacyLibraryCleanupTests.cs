using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalRecs.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.LocalRecs.Tests.Unit
{
    /// <summary>
    /// Tests für das Entfernen der Empfehlungs-Bibliotheken älterer Plugin-Versionen.
    /// </summary>
    public class LegacyLibraryCleanupTests : IDisposable
    {
        private readonly string _basePath;
        private readonly Mock<ILibraryManager> _libraryManager = new Mock<ILibraryManager>();
        private readonly Mock<IUserManager> _userManager = new Mock<IUserManager>();
        private readonly List<VirtualFolderInfo> _folders = new List<VirtualFolderInfo>();
        private readonly Dictionary<Guid, UserPolicy> _policies = new Dictionary<Guid, UserPolicy>();
        private readonly List<User> _users = new List<User>();
        private readonly LegacyLibraryCleanup _cleanup;

        public LegacyLibraryCleanupTests()
        {
            _basePath = Path.Combine(Path.GetTempPath(), "jellyfin-localrecs-tests", Guid.NewGuid().ToString(), "virtual-libraries");

            _libraryManager.Setup(m => m.GetVirtualFolders()).Returns(() => _folders.ToList());
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
                .Returns((User user, string? _) => new UserDto
                {
                    Id = user.Id,
                    Policy = new UserPolicy
                    {
                        EnableAllFolders = _policies[user.Id].EnableAllFolders,
                        EnabledFolders = _policies[user.Id].EnabledFolders.ToArray(),
                        AuthenticationProviderId = "Default",
                        PasswordResetProviderId = "Default"
                    }
                });
            _userManager
                .Setup(m => m.UpdatePolicyAsync(It.IsAny<Guid>(), It.IsAny<UserPolicy>()))
                .Returns((Guid id, UserPolicy policy) =>
                {
                    _policies[id] = policy;
                    return Task.CompletedTask;
                });

            _cleanup = new LegacyLibraryCleanup(
                NullLogger<LegacyLibraryCleanup>.Instance,
                _libraryManager.Object,
                _userManager.Object,
                _basePath);
        }

        public void Dispose()
        {
            var root = Path.GetDirectoryName(_basePath);
            if (root != null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public async Task Run_RemovesOnlyLegacyLibrariesAndTheirAccessEntries()
        {
            var movies = AddFolder("Filme", "/media/filme");
            var legacyMovies = AddFolder("Empfohlene Filme – Anna", Path.Combine(_basePath, Guid.NewGuid().ToString(), "movies"));
            var legacyTv = AddFolder("Empfohlene Serien – Anna", Path.Combine(_basePath, Guid.NewGuid().ToString(), "tv"));
            var anna = AddUser(movies, legacyMovies, legacyTv);
            var ben = AddUser(movies);

            var removed = await _cleanup.RunAsync(CancellationToken.None);

            removed.Should().Be(2);
            _folders.Select(f => f.Name).Should().Equal("Filme");
            _policies[anna.Id].EnabledFolders.Should().Equal(movies);
            _userManager.Verify(m => m.UpdatePolicyAsync(ben.Id, It.IsAny<UserPolicy>()), Times.Never);
        }

        [Fact]
        public async Task Run_LibraryWithMixedLocations_IsKept()
        {
            AddFolder("Gemischt", "/media/filme", Path.Combine(_basePath, "x", "movies"));

            var removed = await _cleanup.RunAsync(CancellationToken.None);

            removed.Should().Be(0);
            _folders.Should().ContainSingle();
        }

        [Fact]
        public async Task Run_SimilarlyNamedFolderOutsideBasePath_IsKept()
        {
            AddFolder("Fremd", _basePath + "-andere", "x");

            var removed = await _cleanup.RunAsync(CancellationToken.None);

            removed.Should().Be(0);
        }

        [Fact]
        public async Task Run_DeletesLegacyFolder()
        {
            Directory.CreateDirectory(Path.Combine(_basePath, "user", "movies"));
            File.WriteAllText(Path.Combine(_basePath, "user", "movies", "link.mkv"), "x");

            await _cleanup.RunAsync(CancellationToken.None);

            Directory.Exists(_basePath).Should().BeFalse();
        }

        private Guid AddFolder(string name, params string[] locations)
        {
            var id = Guid.NewGuid();
            _folders.Add(new VirtualFolderInfo { Name = name, Locations = locations, ItemId = id.ToString("N") });
            return id;
        }

        private User AddUser(params Guid[] enabled)
        {
            var user = new User(Guid.NewGuid().ToString("N"), "Default", "Default") { Id = Guid.NewGuid() };
            _users.Add(user);
            _policies[user.Id] = new UserPolicy { EnableAllFolders = false, EnabledFolders = enabled };
            return user;
        }
    }
}

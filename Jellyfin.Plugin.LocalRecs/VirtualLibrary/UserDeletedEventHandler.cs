using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.VirtualLibrary
{
    /// <summary>
    /// Handles user deletion events to clean up virtual library directories.
    /// </summary>
    public class UserDeletedEventHandler : IEventConsumer<UserDeletedEventArgs>
    {
        private readonly ILogger<UserDeletedEventHandler> _logger;
        private readonly VirtualLibraryManager _virtualLibraryManager;
        private readonly LibraryProvisioningService _provisioningService;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserDeletedEventHandler"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="virtualLibraryManager">Virtual library manager for directory operations.</param>
        /// <param name="provisioningService">Service that removes the recommendation libraries.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public UserDeletedEventHandler(
            ILogger<UserDeletedEventHandler> logger,
            VirtualLibraryManager virtualLibraryManager,
            LibraryProvisioningService provisioningService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _virtualLibraryManager = virtualLibraryManager ?? throw new ArgumentNullException(nameof(virtualLibraryManager));
            _provisioningService = provisioningService ?? throw new ArgumentNullException(nameof(provisioningService));
        }

        /// <inheritdoc />
        public async Task OnEvent(UserDeletedEventArgs eventArgs)
        {
            var user = eventArgs.Argument;
            if (user == null)
            {
                _logger.LogWarning("Received UserDeletedEventArgs with null user");
                return;
            }

            _logger.LogInformation(
                "User deleted: {Username} ({UserId}) - cleaning up virtual library directories",
                user.Username,
                user.Id);

            try
            {
                await _provisioningService.RemoveUserLibrariesAsync(user.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Empfehlungs-Bibliotheken von {Username} konnten nicht entfernt werden", user.Username);
            }

            _virtualLibraryManager.DeleteUserDirectories(user.Id, user.Username);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.VirtualLibrary
{
    /// <summary>
    /// Handles user creation events to set up virtual library directories for new users.
    /// </summary>
    public class UserCreatedEventHandler : IEventConsumer<UserCreatedEventArgs>
    {
        private readonly ILogger<UserCreatedEventHandler> _logger;
        private readonly VirtualLibraryManager _virtualLibraryManager;
        private readonly LibraryProvisioningService _provisioningService;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserCreatedEventHandler"/> class.
        /// </summary>
        /// <param name="logger">Logger instance.</param>
        /// <param name="virtualLibraryManager">Virtual library manager for directory operations.</param>
        /// <param name="provisioningService">Service that creates the recommendation libraries.</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
        public UserCreatedEventHandler(
            ILogger<UserCreatedEventHandler> logger,
            VirtualLibraryManager virtualLibraryManager,
            LibraryProvisioningService provisioningService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _virtualLibraryManager = virtualLibraryManager ?? throw new ArgumentNullException(nameof(virtualLibraryManager));
            _provisioningService = provisioningService ?? throw new ArgumentNullException(nameof(provisioningService));
        }

        /// <inheritdoc />
        public async Task OnEvent(UserCreatedEventArgs eventArgs)
        {
            var user = eventArgs.Argument;
            if (user == null)
            {
                _logger.LogWarning("Received UserCreatedEventArgs with null user");
                return;
            }

            _logger.LogInformation(
                "User created: {Username} ({UserId}) - initializing virtual library directories",
                user.Username,
                user.Id);

            _virtualLibraryManager.EnsureUserDirectoriesExist(user.Id, user.Username);

            try
            {
                await _provisioningService.ProvisionAllAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automatische Einrichtung der Empfehlungs-Bibliotheken für {Username} fehlgeschlagen", user.Username);
            }
        }
    }
}

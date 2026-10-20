using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LocalRecs.Avatars;
using Jellyfin.Plugin.LocalRecs.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// Profilbilder aus Figuren-Icons und einer Hintergrundfarbe. Der Server erzeugt das runde Bild und setzt es
    /// als Jellyfin-Profilbild des angemeldeten Benutzers – jeder nur für sich selbst, Rechte bleiben unverändert.
    /// </summary>
    [ApiController]
    [Route("LocalRecs/Avatars")]
    public class AvatarsController : ControllerBase
    {
        private readonly AvatarService _avatars;
        private readonly IUserManager _userManager;
        private readonly IProviderManager _providerManager;
        private readonly IServerApplicationPaths _paths;
        private readonly ILogger<AvatarsController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="AvatarsController"/> class.
        /// </summary>
        /// <param name="avatars">Avatar service.</param>
        /// <param name="userManager">User manager.</param>
        /// <param name="providerManager">Provider manager (saves images).</param>
        /// <param name="paths">Server paths.</param>
        /// <param name="logger">Logger instance.</param>
        public AvatarsController(AvatarService avatars, IUserManager userManager, IProviderManager providerManager, IServerApplicationPaths paths, ILogger<AvatarsController> logger)
        {
            _avatars = avatars ?? throw new ArgumentNullException(nameof(avatars));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _providerManager = providerManager ?? throw new ArgumentNullException(nameof(providerManager));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        /// <summary>
        /// Gets the catalog: color palette, categories and icons (images via this plugin), plus the user's last choice.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The catalog; 404 if switched off, 503 if it could not be loaded.</returns>
        [HttpGet]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<CatalogResponse>> GetCatalog(CancellationToken cancellationToken)
        {
            if (!Config.AvatarsEnabled)
            {
                return NotFound();
            }

            var catalog = await _avatars.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            if (catalog == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, _avatars.LastError ?? "Katalog nicht verfügbar");
            }

            Response.Headers.CacheControl = "no-store";
            var choice = _avatars.GetChoice(RecommendationsController.GetUserId(User));
            return new CatalogResponse
            {
                Name = catalog.Name,
                License = catalog.License,
                Colors = catalog.Colors.ToList(),
                Categories = catalog.Categories.Select(c => new CategoryResponse
                {
                    Id = c.Id,
                    Name = c.Name,
                    Icons = c.Icons.Select(i => new IconResponse
                    {
                        Id = c.Id + "/" + i.Id,
                        Name = i.Name,
                        Fit = i.Fit,
                        ImageUrl = "LocalRecs/Avatars/Icons/" + c.Id + "/" + i.Id
                    }).ToList()
                }).ToList(),
                Current = choice != null && catalog.Find(choice.Icon) != null ? new ChoiceRequest { Icon = choice.Icon, Color = choice.Color } : null
            };
        }

        /// <summary>
        /// Gets the image of an icon (no sign-in needed, so apps can show it directly).
        /// </summary>
        /// <param name="categoryId">Category ID.</param>
        /// <param name="iconId">Icon ID.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The image (PNG, JPEG or WebP).</returns>
        [HttpGet("Icons/{categoryId}/{iconId}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> GetIcon([FromRoute] string categoryId, [FromRoute] string iconId, CancellationToken cancellationToken)
        {
            if (!Config.AvatarsEnabled)
            {
                return NotFound();
            }

            var catalog = await _avatars.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            var icon = catalog?.Find(categoryId + "/" + iconId);
            var image = icon == null ? null : await _avatars.GetImageAsync(icon, cancellationToken).ConfigureAwait(false);
            if (image == null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=86400";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(image.Data, image.ContentType);
        }

        /// <summary>
        /// Creates the round profile image (512×512 PNG) from icon and color and sets it as the Jellyfin profile image
        /// of the signed-in user.
        /// </summary>
        /// <param name="request">Icon key "category/icon" and a color of the palette.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The new image tag.</returns>
        [HttpPost("Me")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<SetResponse>> SetMine([FromBody] ChoiceRequest request, CancellationToken cancellationToken)
        {
            if (!Config.AvatarsEnabled)
            {
                return NotFound();
            }

            var user = _userManager.GetUserById(RecommendationsController.GetUserId(User));
            if (user == null)
            {
                return Unauthorized();
            }

            // Wie Jellyfin selbst: ohne „Benutzereinstellungen ändern“ kein eigenes Profilbild (Admins dürfen immer)
            if (!user.EnableUserPreferenceAccess && !User.IsInRole("Administrator"))
            {
                return StatusCode(StatusCodes.Status403Forbidden, "Profilbild darf nicht geändert werden");
            }

            var catalog = await _avatars.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
            if (catalog == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, _avatars.LastError ?? "Katalog nicht verfügbar");
            }

            var icon = catalog.Find(request?.Icon);
            var color = catalog.PaletteColor(request?.Color);
            if (icon == null || color == null)
            {
                return BadRequest("Icon oder Farbe nicht im Katalog");
            }

            var image = await _avatars.GetImageAsync(icon, cancellationToken).ConfigureAwait(false);
            if (image == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Icon nicht verfügbar");
            }

            byte[] png;
            try
            {
                png = AvatarRenderer.Render(image.Data, color, icon.Fit);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is InvalidOperationException || ex is ArgumentException || ex is TypeLoadException || ex is FileNotFoundException || ex is DllNotFoundException)
            {
                _logger.LogWarning(ex, "Profilbild konnte nicht erzeugt werden");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Profilbild konnte nicht erzeugt werden");
            }

            var userDirectory = Path.GetFullPath(_paths.UserConfigurationDirectoryPath);
            var userPath = Path.GetFullPath(Path.Combine(userDirectory, user.Username));
            if (!userPath.StartsWith(userDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return BadRequest("Ungültiger Benutzer");
            }

            if (user.ProfileImage != null)
            {
                await _userManager.ClearProfileImageAsync(user).ConfigureAwait(false);
            }

            user.ProfileImage = new Jellyfin.Database.Implementations.Entities.ImageInfo(Path.Combine(userPath, "profile.png"));
            using (var stream = new MemoryStream(png))
            {
                await _providerManager.SaveImage(stream, "image/png", user.ProfileImage.Path).ConfigureAwait(false);
            }

            await _userManager.UpdateUserAsync(user).ConfigureAwait(false);
            _avatars.SetChoice(user.Id, request!.Icon!.Trim(), color);
            return new SetResponse { PrimaryImageTag = _userManager.GetUserDto(user).PrimaryImageTag };
        }

        /// <summary>
        /// Catalog for the apps.
        /// </summary>
        public class CatalogResponse
        {
            /// <summary>Gets or sets the catalog name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the license / credits.</summary>
            public string License { get; set; } = string.Empty;

            /// <summary>Gets or sets the color palette (#RRGGBB).</summary>
            public IReadOnlyList<string> Colors { get; set; } = Array.Empty<string>();

            /// <summary>Gets or sets the categories.</summary>
            public IReadOnlyList<CategoryResponse> Categories { get; set; } = Array.Empty<CategoryResponse>();

            /// <summary>Gets or sets the last choice of the user, if any.</summary>
            public ChoiceRequest? Current { get; set; }
        }

        /// <summary>
        /// Category of the catalog.
        /// </summary>
        public class CategoryResponse
        {
            /// <summary>Gets or sets the ID.</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the icons.</summary>
            public IReadOnlyList<IconResponse> Icons { get; set; } = Array.Empty<IconResponse>();
        }

        /// <summary>
        /// Icon of the catalog.
        /// </summary>
        public class IconResponse
        {
            /// <summary>Gets or sets the key "category/icon".</summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>Gets or sets the name.</summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>Gets or sets the fit ("contain" or "cover").</summary>
            public string Fit { get; set; } = AvatarCatalog.FitContain;

            /// <summary>Gets or sets the image path relative to the server address.</summary>
            public string ImageUrl { get; set; } = string.Empty;
        }

        /// <summary>
        /// Chosen icon and color.
        /// </summary>
        public class ChoiceRequest
        {
            /// <summary>Gets or sets the icon key "category/icon".</summary>
            public string? Icon { get; set; }

            /// <summary>Gets or sets the color (#RRGGBB from the palette).</summary>
            public string? Color { get; set; }
        }

        /// <summary>
        /// Result of setting the profile image.
        /// </summary>
        public class SetResponse
        {
            /// <summary>Gets or sets the new image tag of the user.</summary>
            public string? PrimaryImageTag { get; set; }
        }
    }
}

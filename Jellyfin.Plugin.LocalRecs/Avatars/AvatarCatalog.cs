using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.LocalRecs.Avatars
{
    /// <summary>
    /// Katalog für Profilbilder (<c>icons.json</c>): feste Farbpalette und Figuren-Icons nach Kategorien.
    /// Bilder stehen als <c>file</c> (relativ zum Katalog) oder <c>url</c> (absolut) im Katalog.
    /// </summary>
    public class AvatarCatalog
    {
        /// <summary>Fit: the icon is drawn centered on the color.</summary>
        public const string FitContain = "contain";

        /// <summary>Fit: the image fills the circle.</summary>
        public const string FitCover = "cover";

        private static readonly Regex IdPattern = new Regex("^[a-z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ColorPattern = new Regex("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly string[] DefaultColors =
        {
            "#e50914", "#f5a623", "#21a179", "#2e96e0", "#7f76d6", "#d94f9c", "#00a8a8", "#8a6d3b", "#4a4a55", "#0b0e14"
        };

        /// <summary>Gets or sets the name of the catalog.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the license / credits text.</summary>
        public string License { get; set; } = string.Empty;

        /// <summary>Gets the color palette (#RRGGBB, lower case).</summary>
        public List<string> Colors { get; } = new List<string>();

        /// <summary>Gets the categories.</summary>
        public List<AvatarCategory> Categories { get; } = new List<AvatarCategory>();

        /// <summary>
        /// Reads a catalog. Invalid entries are skipped; without colors a default palette is used.
        /// </summary>
        /// <param name="json">Contents of icons.json.</param>
        /// <param name="source">URL or path of the catalog (base for relative files).</param>
        /// <returns>The catalog.</returns>
        public static AvatarCatalog Parse(string json, string source)
        {
            ArgumentNullException.ThrowIfNull(json);
            ArgumentNullException.ThrowIfNull(source);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("icons.json: Objekt erwartet");
            }

            var catalog = new AvatarCatalog
            {
                Name = Text(root, "name"),
                License = Text(root, "license")
            };

            if (root.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Array)
            {
                foreach (var color in colors.EnumerateArray())
                {
                    var value = color.ValueKind == JsonValueKind.String ? color.GetString() : null;
                    if (value != null && ColorPattern.IsMatch(value) && !catalog.Colors.Contains(value.ToLowerInvariant()))
                    {
                        catalog.Colors.Add(value.ToLowerInvariant());
                    }
                }
            }

            if (catalog.Colors.Count == 0)
            {
                catalog.Colors.AddRange(DefaultColors);
            }

            if (root.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
            {
                foreach (var categoryElement in categories.EnumerateArray())
                {
                    var category = ParseCategory(categoryElement, source);
                    if (category != null && category.Icons.Count > 0 && catalog.Categories.All(c => c.Id != category.Id))
                    {
                        catalog.Categories.Add(category);
                    }
                }
            }

            return catalog;
        }

        /// <summary>
        /// Resolves the image of an entry: absolute http(s) URLs as they are, files relative to the catalog.
        /// Local files must stay in the catalog's folder.
        /// </summary>
        /// <param name="source">URL or path of the catalog.</param>
        /// <param name="file">Value of "file".</param>
        /// <param name="url">Value of "url".</param>
        /// <returns>The absolute source, or null if invalid.</returns>
        public static string? ResolveImage(string source, string? file, string? url)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp)
                    ? absolute.AbsoluteUri
                    : null;
            }

            if (string.IsNullOrWhiteSpace(file))
            {
                return null;
            }

            file = file.Trim().Replace('\\', '/');
            if (IsUrl(source))
            {
                return Uri.TryCreate(new Uri(source), file, out var resolved) && (resolved.Scheme == Uri.UriSchemeHttps || resolved.Scheme == Uri.UriSchemeHttp)
                    ? resolved.AbsoluteUri
                    : null;
            }

            var directory = Path.GetFullPath(Path.GetDirectoryName(source) ?? ".");
            var path = Path.GetFullPath(Path.Combine(directory, file));
            return path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
        }

        /// <summary>
        /// Checks whether a source is an http(s) URL.
        /// </summary>
        /// <param name="source">Source.</param>
        /// <returns>True for http and https.</returns>
        public static bool IsUrl(string source) =>
            source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Finds an icon by its key "category/icon".
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>The icon, or null.</returns>
        public AvatarIcon? Find(string? key)
        {
            var parts = (key ?? string.Empty).Split('/');
            if (parts.Length != 2)
            {
                return null;
            }

            return Categories.FirstOrDefault(c => c.Id == parts[0])?.Icons.FirstOrDefault(i => i.Id == parts[1]);
        }

        /// <summary>
        /// Normalizes a color of the palette (case-insensitive).
        /// </summary>
        /// <param name="color">Color (#RRGGBB).</param>
        /// <returns>The palette color, or null if it is not in the palette.</returns>
        public string? PaletteColor(string? color)
        {
            var value = color?.Trim().ToLowerInvariant();
            return value != null && Colors.Contains(value) ? value : null;
        }

        private static AvatarCategory? ParseCategory(JsonElement element, string source)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = Text(element, "id");
            if (!IdPattern.IsMatch(id))
            {
                return null;
            }

            var category = new AvatarCategory { Id = id, Name = Fallback(Text(element, "name"), id) };
            var fit = Text(element, "fit");
            if (element.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Array)
            {
                foreach (var iconElement in icons.EnumerateArray())
                {
                    if (iconElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var iconId = Text(iconElement, "id");
                    var image = ResolveImage(source, Text(iconElement, "file"), Text(iconElement, "url"));
                    if (!IdPattern.IsMatch(iconId) || image == null || category.Icons.Any(i => i.Id == iconId))
                    {
                        continue;
                    }

                    var iconFit = Fallback(Text(iconElement, "fit"), fit);
                    category.Icons.Add(new AvatarIcon
                    {
                        Id = iconId,
                        Name = Fallback(Text(iconElement, "name"), iconId),
                        Source = image,
                        Fit = string.Equals(iconFit, FitCover, StringComparison.OrdinalIgnoreCase) ? FitCover : FitContain
                    });
                }
            }

            return category;
        }

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty).Trim() : string.Empty;

        private static string Fallback(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
    }
}

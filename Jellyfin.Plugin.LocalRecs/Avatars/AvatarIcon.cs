namespace Jellyfin.Plugin.LocalRecs.Avatars
{
    /// <summary>
    /// Ein Figuren-Icon des Profilbild-Katalogs.
    /// </summary>
    public class AvatarIcon
    {
        /// <summary>Gets or sets the ID within the category (a–z, 0–9, "-" and "_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the absolute source of the image (http(s) URL or local path).</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Gets or sets how the image is placed: "contain" (icon on the color, default) or "cover" (fills the circle).</summary>
        public string Fit { get; set; } = AvatarCatalog.FitContain;
    }
}

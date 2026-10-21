namespace Jellyfin.Plugin.LocalRecs.Similarity
{
    /// <summary>
    /// State of the personal Trakt recommendations of a user.
    /// </summary>
    public class TraktStatus
    {
        /// <summary>Gets or sets a value indicating whether the admin switched the feature on.</summary>
        public bool FeatureEnabled { get; set; }

        /// <summary>Gets or sets a value indicating whether the user switched the personal recommendations on (default off).</summary>
        public bool Enabled { get; set; }

        /// <summary>Gets or sets the access in use: "eigen", "trakt-plugin" or null (no access).</summary>
        public string? Source { get; set; }

        /// <summary>Gets or sets a value indicating whether an own connection is possible (own Trakt app configured).</summary>
        public bool CanConnect { get; set; }

        /// <summary>Gets or sets a value indicating whether the user has an own connection.</summary>
        public bool OwnConnected { get; set; }

        /// <summary>Gets or sets a value indicating whether the Trakt plugin is installed (its link can be used after switching on).</summary>
        public bool PluginInstalled { get; set; }

        /// <summary>Gets or sets a value indicating whether the link of the Trakt plugin is in use (only after switching on).</summary>
        public bool PluginLinked { get; set; }

        /// <summary>Gets or sets the code to enter at Trakt while a login is running.</summary>
        public string? UserCode { get; set; }

        /// <summary>Gets or sets the address to enter the code at.</summary>
        public string? VerificationUrl { get; set; }

        /// <summary>Gets or sets the error of the last login, if any.</summary>
        public string? LoginError { get; set; }
    }
}

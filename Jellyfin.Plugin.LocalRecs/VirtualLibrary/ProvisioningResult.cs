namespace Jellyfin.Plugin.LocalRecs.VirtualLibrary
{
    /// <summary>
    /// Ergebnis der automatischen Einrichtung der Empfehlungs-Bibliotheken.
    /// </summary>
    public class ProvisioningResult
    {
        /// <summary>
        /// Gets or sets the number of libraries created.
        /// </summary>
        public int LibrariesCreated { get; set; }

        /// <summary>
        /// Gets or sets the number of users whose library access was updated.
        /// </summary>
        public int UsersUpdated { get; set; }
    }
}

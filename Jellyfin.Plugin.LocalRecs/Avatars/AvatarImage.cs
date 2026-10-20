namespace Jellyfin.Plugin.LocalRecs.Avatars
{
    /// <summary>
    /// Bilddaten eines Icons mit erkanntem Format.
    /// </summary>
    public class AvatarImage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AvatarImage"/> class.
        /// </summary>
        /// <param name="data">Image bytes.</param>
        /// <param name="contentType">MIME type (image/png, image/jpeg or image/webp).</param>
        public AvatarImage(byte[] data, string contentType)
        {
            Data = data;
            ContentType = contentType;
        }

        /// <summary>Gets the image bytes.</summary>
        public byte[] Data { get; }

        /// <summary>Gets the MIME type.</summary>
        public string ContentType { get; }
    }
}

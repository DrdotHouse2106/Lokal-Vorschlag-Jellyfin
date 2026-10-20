using System;
using System.IO;
using SkiaSharp;

namespace Jellyfin.Plugin.LocalRecs.Avatars
{
    /// <summary>
    /// Erzeugt das runde Profilbild: Kreis in der gewählten Farbe, darauf das Icon. Nutzt SkiaSharp aus Jellyfin.
    /// </summary>
    public static class AvatarRenderer
    {
        /// <summary>Size of the generated image in pixels.</summary>
        public const int Size = 512;

        /// <summary>Share of the circle the icon takes with fit "contain".</summary>
        public const float ContainScale = 0.8f;

        /// <summary>
        /// Detects PNG, JPEG and WebP by their first bytes.
        /// </summary>
        /// <param name="data">Image bytes.</param>
        /// <returns>The MIME type, or null for other formats.</returns>
        public static string? DetectContentType(byte[] data)
        {
            if (data == null || data.Length < 12)
            {
                return null;
            }

            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                return "image/png";
            }

            if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            {
                return "image/webp";
            }

            return null;
        }

        /// <summary>
        /// Computes where the image is drawn in the circle.
        /// </summary>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="size">Size of the result.</param>
        /// <param name="fit">"contain" or "cover".</param>
        /// <returns>Left, top, width and height of the target rectangle.</returns>
        public static (float Left, float Top, float Width, float Height) Placement(int width, int height, int size, string fit)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            var cover = string.Equals(fit, AvatarCatalog.FitCover, StringComparison.Ordinal);
            var box = cover ? size : size * ContainScale;
            var scale = cover ? Math.Max(box / width, box / height) : Math.Min(box / width, box / height);
            var w = width * scale;
            var h = height * scale;
            return ((size - w) / 2f, (size - h) / 2f, w, h);
        }

        /// <summary>
        /// Draws the round profile image as PNG (outside the circle transparent).
        /// </summary>
        /// <param name="image">Icon image (PNG, JPEG or WebP).</param>
        /// <param name="color">Background color (#RRGGBB).</param>
        /// <param name="fit">"contain" or "cover".</param>
        /// <returns>The PNG bytes.</returns>
        public static byte[] Render(byte[] image, string color, string fit)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (!SKColor.TryParse(color, out var background))
            {
                throw new ArgumentException("Ungültige Farbe", nameof(color));
            }

            using var icon = SKImage.FromEncodedData(image) ?? throw new InvalidDataException("Bild konnte nicht gelesen werden");
            var info = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException("Zeichenfläche nicht verfügbar");
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            using (var paint = new SKPaint { Color = background, IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                canvas.DrawCircle(Size / 2f, Size / 2f, Size / 2f, paint);
            }

            using (var clip = new SKPath())
            {
                clip.AddCircle(Size / 2f, Size / 2f, Size / 2f);
                canvas.Save();
                canvas.ClipPath(clip, SKClipOperation.Intersect, true);
                var place = Placement(icon.Width, icon.Height, Size, fit);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawImage(
                    icon,
                    SKRect.Create(place.Left, place.Top, place.Width, place.Height),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
                    paint);
                canvas.Restore();
            }

            using var result = surface.Snapshot();
            using var data = result.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}

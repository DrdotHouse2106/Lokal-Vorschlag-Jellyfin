using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Macht aus Sendung und Einordnung eine Jellyfin-Sendung (ProgramInfo). Kennungen (Id, ShowId, SeriesId)
    /// werden genau wie beim eingebauten XMLTV-Anbieter gebildet, damit Timer und Serienaufnahmen beim Wechsel erhalten bleiben.
    /// </summary>
    public static class EpgMapper
    {
        /// <summary>Etag prefix Jellyfin uses to skip unchanged XMLTV programmes.</summary>
        public const string EtagPrefix = "xmltv-sha256-v1:";

        /// <summary>
        /// Creates the Jellyfin programme.
        /// </summary>
        /// <param name="programme">Programme from the file.</param>
        /// <param name="kind">Classification.</param>
        /// <returns>The programme info.</returns>
        public static ProgramInfo ToProgramInfo(EpgProgramme programme, EpgClassification kind)
        {
            ArgumentNullException.ThrowIfNull(programme);
            ArgumentNullException.ThrowIfNull(kind);
            var info = new ProgramInfo
            {
                Id = string.Format(CultureInfo.InvariantCulture, "{0}_{1:O}", programme.ChannelId, programme.Start),
                ChannelId = programme.ChannelId,
                Name = programme.Title,
                Overview = programme.Description,
                StartDate = programme.Start.UtcDateTime,
                EndDate = programme.Stop.UtcDateTime,
                Genres = programme.Categories.ToList(),
                EpisodeTitle = kind.IsSeries ? kind.EpisodeTitle : null,
                SeasonNumber = kind.IsSeries ? kind.Season : null,
                EpisodeNumber = kind.IsSeries ? kind.Episode : null,
                IsSeries = kind.IsSeries,
                IsMovie = kind.IsMovie,
                IsKids = kind.IsKids,
                IsNews = kind.IsNews,
                IsSports = kind.IsSports,
                IsRepeat = programme.PreviouslyShown && !programme.IsNew,
                IsPremiere = programme.Premiere,
                IsLive = programme.Live,
                ProductionYear = kind.Year,
                ImageUrl = string.IsNullOrEmpty(programme.IconUrl) ? null : programme.IconUrl,
                HasImage = !string.IsNullOrEmpty(programme.IconUrl),
                ThumbImageUrl = string.IsNullOrEmpty(programme.StillUrl) ? null : programme.StillUrl,
                BackdropImageUrl = string.IsNullOrEmpty(programme.BackdropUrl) ? null : programme.BackdropUrl,
                OfficialRating = programme.Rating,
                CommunityRating = programme.StarRating,
                HomePageUrl = programme.Urls.FirstOrDefault(),
                SeriesId = kind.IsSeries ? Md5Guid(programme.Title) : null
            };

            if (kind.IsMovie && kind.ImdbId != null)
            {
                info.ProviderIds["Imdb"] = kind.ImdbId;
            }

            info.ShowId = string.IsNullOrWhiteSpace(programme.ProgramId) ? ShowId(info) : programme.ProgramId;
            info.Etag = Etag(info);
            return info;
        }

        // Wie Jellyfin (XmlTvListingsProvider) – einschließlich dessen Eigenheit, dass Staffel/Folge den Text ersetzen
        private static string ShowId(ProgramInfo info)
        {
            var unique = (info.Name ?? string.Empty) + (info.EpisodeTitle ?? string.Empty);
            if (info.SeasonNumber.HasValue)
            {
                unique = "-" + info.SeasonNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (info.EpisodeNumber.HasValue)
            {
                unique = "-" + info.EpisodeNumber.Value.ToString(CultureInfo.InvariantCulture);
            }

            var id = Md5Guid(unique);
            if (info.IsSeries && !info.IsRepeat && (info.EpisodeNumber ?? 0) == 0)
            {
                id += info.StartDate.Ticks.ToString(CultureInfo.InvariantCulture);
            }

            return id;
        }

        private static string Md5Guid(string value)
        {
#pragma warning disable CA5351 // Gleiche Kennung wie Jellyfin (MD5 über UTF-16), keine Sicherheitsfunktion
            return new Guid(MD5.HashData(Encoding.Unicode.GetBytes(value))).ToString("N", CultureInfo.InvariantCulture);
#pragma warning restore CA5351
        }

        // Änderungen an der Einordnung (Film/Serie, Folge) ändern den Etag – dann übernimmt Jellyfin die Sendung neu
        private static string Etag(ProgramInfo info)
        {
            var text = string.Join(
                "\u001f",
                "localrecs-epg-v1",
                info.Id,
                info.ChannelId,
                info.Name,
                info.Overview,
                info.StartDate.Ticks.ToString(CultureInfo.InvariantCulture),
                info.EndDate.Ticks.ToString(CultureInfo.InvariantCulture),
                string.Join("\u001e", info.Genres),
                info.EpisodeTitle,
                info.SeasonNumber?.ToString(CultureInfo.InvariantCulture),
                info.EpisodeNumber?.ToString(CultureInfo.InvariantCulture),
                info.IsSeries ? "S" : string.Empty,
                info.IsMovie ? "M" : string.Empty,
                info.IsKids ? "K" : string.Empty,
                info.IsNews ? "N" : string.Empty,
                info.IsSports ? "P" : string.Empty,
                info.IsRepeat ? "R" : string.Empty,
                info.IsPremiere ? "E" : string.Empty,
                info.IsLive ? "L" : string.Empty,
                info.ProductionYear?.ToString(CultureInfo.InvariantCulture),
                info.ImageUrl,
                info.ThumbImageUrl,
                info.BackdropImageUrl,
                info.OfficialRating,
                info.CommunityRating?.ToString(CultureInfo.InvariantCulture),
                info.HomePageUrl,
                info.SeriesId,
                info.ShowId,
                string.Join(",", info.ProviderIds.Select(p => p.Key + "=" + p.Value)));
            return EtagPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace Jellyfin.Plugin.LocalRecs.Epg
{
    /// <summary>
    /// Liest eine XMLTV-Datei vollständig (Sender und Sendungen) – mit allem, was die Regel-Bausteine brauchen:
    /// episode-num (xmltv_ns und onscreen), Regie, date, URLs, Kategorien.
    /// </summary>
    public class EpgFile
    {
        private static readonly Regex OnscreenSeasonEpisode = new Regex(@"S\s*(\d{1,4})\s*[-/.]?\s*E\s*(\d{1,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex OnscreenGerman = new Regex(@"Staffel\s*(\d{1,4})\D{0,6}Folge\s*(\d{1,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex OnscreenCross = new Regex(@"^\s*(\d{1,4})\s*x\s*(\d{1,5})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex OnscreenEpisodeOnly = new Regex(@"(?:Folge|Episode|Ep\.?|E)\s*(\d{1,5})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Gets the channels.</summary>
        public List<EpgChannel> Channels { get; } = new List<EpgChannel>();

        /// <summary>Gets the programmes, sorted by channel and start.</summary>
        public List<EpgProgramme> Programmes { get; } = new List<EpgProgramme>();

        /// <summary>Gets the programmes per channel ID (built when reading).</summary>
        public Dictionary<string, List<EpgProgramme>> ByChannel { get; } = new Dictionary<string, List<EpgProgramme>>(StringComparer.Ordinal);

        /// <summary>
        /// Reads a file.
        /// </summary>
        /// <param name="stream">XMLTV content.</param>
        /// <param name="preferredLanguage">Preferred language for titles (e.g. "de"), optional.</param>
        /// <returns>The file.</returns>
        public static EpgFile Read(Stream stream, string? preferredLanguage = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            var file = new EpgFile();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true, XmlResolver = null };
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Name == "channel")
                {
                    var channel = ReadChannel(reader);
                    if (channel != null)
                    {
                        file.Channels.Add(channel);
                    }
                }
                else if (reader.Name == "programme")
                {
                    var programme = ReadProgramme(reader, preferredLanguage);
                    if (programme != null)
                    {
                        file.Programmes.Add(programme);
                    }
                }
            }

            file.Programmes.Sort((a, b) =>
            {
                var byChannel = string.CompareOrdinal(a.ChannelId, b.ChannelId);
                return byChannel != 0 ? byChannel : a.Start.CompareTo(b.Start);
            });
            foreach (var programme in file.Programmes)
            {
                if (!file.ByChannel.TryGetValue(programme.ChannelId, out var list))
                {
                    list = new List<EpgProgramme>();
                    file.ByChannel[programme.ChannelId] = list;
                }

                list.Add(programme);
            }

            return file;
        }

        /// <summary>
        /// Parses an XMLTV time such as "20261020175500 +0200".
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The time, or null.</returns>
        public static DateTimeOffset? ParseTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var digits = parts[0];
            if (digits.Length < 12 || !digits.Take(14).All(char.IsDigit))
            {
                return null;
            }

            digits = digits.Length >= 14 ? digits[..14] : digits[..12] + "00";
            if (!DateTime.TryParseExact(digits, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return null;
            }

            var offset = TimeSpan.Zero;
            if (parts.Length > 1 && parts[1].Length == 5 && (parts[1][0] == '+' || parts[1][0] == '-')
                && int.TryParse(parts[1].AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
                && int.TryParse(parts[1].AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                offset = new TimeSpan(hours, minutes, 0);
                if (parts[1][0] == '-')
                {
                    offset = -offset;
                }
            }

            return new DateTimeOffset(local, offset);
        }

        /// <summary>
        /// Parses an episode number in the xmltv_ns system ("season.episode.part", zero-based, "x/total" allowed).
        /// </summary>
        /// <param name="value">The value, e.g. "1.1.0/1".</param>
        /// <returns>Season and episode (one-based), each null if missing.</returns>
        public static (int? Season, int? Episode) ParseXmltvNs(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, null);
            }

            var parts = value.Split('.');
            return (parts.Length > 0 ? ZeroBased(parts[0]) : null, parts.Length > 1 ? ZeroBased(parts[1]) : null);
        }

        /// <summary>
        /// Parses an on-screen episode number such as "S02E02", "Staffel 2, Folge 2", "2x02" or "Folge 12".
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>Season and episode, each null if missing.</returns>
        public static (int? Season, int? Episode) ParseOnscreen(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, null);
            }

            foreach (var pattern in new[] { OnscreenSeasonEpisode, OnscreenGerman, OnscreenCross })
            {
                var match = pattern.Match(value);
                if (match.Success)
                {
                    return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
                }
            }

            var episode = OnscreenEpisodeOnly.Match(value);
            return episode.Success ? (null, int.Parse(episode.Groups[1].Value, CultureInfo.InvariantCulture)) : (null, null);
        }

        private static int? ZeroBased(string part)
        {
            var number = part.Split('/')[0].Trim();
            return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value < 100000 ? value + 1 : null;
        }

        private static EpgChannel? ReadChannel(XmlReader reader)
        {
            var channel = new EpgChannel { Id = reader.GetAttribute("id") ?? string.Empty };
            if (reader.IsEmptyElement)
            {
                return channel.Id.Length > 0 ? channel : null;
            }

            using (var inner = reader.ReadSubtree())
            {
                inner.Read();
                inner.Read();
                while (!inner.EOF)
                {
                    // ReadElementContentAsString steht danach schon auf dem nächsten Knoten – nur sonst weiterlesen
                    if (inner.NodeType != XmlNodeType.Element)
                    {
                        inner.Read();
                        continue;
                    }

                    switch (inner.Name)
                    {
                        case "display-name":
                            var name = inner.ReadElementContentAsString().Trim();
                            if (channel.Name.Length == 0)
                            {
                                channel.Name = name;
                            }

                            if (channel.Number == null && name.Length > 0 && name.All(char.IsDigit))
                            {
                                channel.Number = name;
                            }

                            break;
                        case "lcn":
                            channel.Number = inner.ReadElementContentAsString().Trim();
                            break;
                        case "icon":
                            channel.IconUrl ??= inner.GetAttribute("src");
                            inner.Read();
                            break;
                        default:
                            inner.Read();
                            break;
                    }
                }
            }

            if (channel.Name.Length == 0)
            {
                channel.Name = channel.Id;
            }

            return channel.Id.Length > 0 ? channel : null;
        }

        private static EpgProgramme? ReadProgramme(XmlReader reader, string? language)
        {
            var start = ParseTime(reader.GetAttribute("start"));
            var stop = ParseTime(reader.GetAttribute("stop"));
            var programme = new EpgProgramme { ChannelId = reader.GetAttribute("channel") ?? string.Empty };
            string? title = null;
            string? titleAny = null;
            string? subTitle = null;
            string? subTitleAny = null;
            string? desc = null;
            string? descAny = null;
            if (!reader.IsEmptyElement)
            {
                using var inner = reader.ReadSubtree();
                inner.Read();
                inner.Read();
                while (!inner.EOF)
                {
                    // Inhalte lesen bewegt den Leser schon weiter – nur bei anderen Knoten selbst weiterlesen
                    if (inner.NodeType != XmlNodeType.Element)
                    {
                        inner.Read();
                        continue;
                    }

                    var lang = inner.GetAttribute("lang");
                    var preferred = string.IsNullOrEmpty(language) || string.Equals(lang, language, StringComparison.OrdinalIgnoreCase);
                    switch (inner.Name)
                    {
                        case "title":
                            Pick(inner.ReadElementContentAsString(), preferred, ref title, ref titleAny);
                            break;
                        case "sub-title":
                            Pick(inner.ReadElementContentAsString(), preferred, ref subTitle, ref subTitleAny);
                            break;
                        case "desc":
                            Pick(inner.ReadElementContentAsString(), preferred, ref desc, ref descAny);
                            break;
                        case "category":
                            var category = inner.ReadElementContentAsString().Trim();
                            if (category.Length > 0 && !programme.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                            {
                                programme.Categories.Add(category);
                            }

                            break;
                        case "director":
                            var director = inner.ReadElementContentAsString().Trim();
                            if (director.Length > 0)
                            {
                                programme.Directors.Add(director);
                            }

                            break;
                        case "date":
                            programme.Date = inner.ReadElementContentAsString().Trim();
                            break;
                        case "url":
                            var link = inner.ReadElementContentAsString().Trim();
                            if (link.Length > 0)
                            {
                                programme.Urls.Add(link);
                            }

                            break;
                        case "episode-num":
                            var system = inner.GetAttribute("system");
                            var number = inner.ReadElementContentAsString().Trim();
                            if (string.Equals(system, "xmltv_ns", StringComparison.OrdinalIgnoreCase))
                            {
                                programme.EpisodeNumXmltvNs = number;
                            }
                            else if (string.Equals(system, "dd_progid", StringComparison.OrdinalIgnoreCase))
                            {
                                programme.ProgramId = number;
                            }
                            else
                            {
                                programme.EpisodeNumOnscreen ??= number;
                            }

                            break;
                        case "icon":
                            programme.IconUrl ??= inner.GetAttribute("src");
                            inner.Read();
                            break;
                        case "image":
                            var type = inner.GetAttribute("type");
                            var image = inner.ReadElementContentAsString().Trim();
                            if (string.Equals(type, "still", StringComparison.OrdinalIgnoreCase))
                            {
                                programme.StillUrl ??= image;
                            }
                            else if (string.Equals(type, "backdrop", StringComparison.OrdinalIgnoreCase))
                            {
                                programme.BackdropUrl ??= image;
                            }
                            else
                            {
                                programme.IconUrl ??= image;
                            }

                            break;
                        case "rating":
                            programme.Rating ??= ReadValue(inner);
                            inner.Read();
                            break;
                        case "star-rating":
                            programme.StarRating ??= ParseStars(ReadValue(inner));
                            inner.Read();
                            break;
                        case "previously-shown":
                            programme.PreviouslyShown = true;
                            inner.Read();
                            break;
                        case "new":
                            programme.IsNew = true;
                            inner.Read();
                            break;
                        case "premiere":
                            programme.Premiere = true;
                            inner.Read();
                            break;
                        case "live":
                            programme.Live = true;
                            inner.Read();
                            break;
                        default:
                            inner.Read();
                            break;
                    }
                }
            }

            programme.Title = (title ?? titleAny ?? string.Empty).Trim();
            programme.SubTitle = Clean(subTitle ?? subTitleAny);
            programme.Description = Clean(desc ?? descAny);
            if (start == null || programme.ChannelId.Length == 0 || programme.Title.Length == 0)
            {
                return null;
            }

            programme.Start = start.Value;
            programme.Stop = stop ?? start.Value.AddMinutes(30);
            return programme;
        }

        private static void Pick(string value, bool preferred, ref string? best, ref string? any)
        {
            value = value.Trim();
            if (value.Length == 0)
            {
                return;
            }

            if (preferred && best == null)
            {
                best = value;
            }

            any ??= value;
        }

        private static string? ReadValue(XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                return null;
            }

            using var sub = reader.ReadSubtree();
            while (sub.Read())
            {
                if (sub.NodeType == XmlNodeType.Element && sub.Name == "value")
                {
                    var value = sub.ReadElementContentAsString().Trim();
                    return value.Length > 0 ? value : null;
                }
            }

            return null;
        }

        private static float? ParseStars(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var parts = value.Split('/');
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var stars))
            {
                return null;
            }

            if (parts.Length > 1 && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var max) && max > 0)
            {
                stars = stars / max * 10f;
            }

            return stars;
        }

        private static string? Clean(string? value)
        {
            value = value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}

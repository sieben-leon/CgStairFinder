using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CgStairFinder
{
    internal static class MapPinCollectionService
    {
        public static string BuildElapsedAgoText(DateTime createdAt, DateTime now)
        {
            if (createdAt == default(DateTime))
            {
                return string.Empty;
            }

            var elapsed = now - createdAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed.TotalDays >= 1)
            {
                var days = (int)elapsed.TotalDays;
                var hours = elapsed.Hours;
                return hours > 0
                    ? string.Format("{0}d{1}h前", days, hours)
                    : string.Format("{0}d前", days);
            }

            if (elapsed.TotalHours >= 1)
            {
                var hours = (int)elapsed.TotalHours;
                var minutes = elapsed.Minutes;
                return minutes > 0
                    ? string.Format("{0}h{1}m前", hours, minutes)
                    : string.Format("{0}h前", hours);
            }

            var mins = Math.Max(1, (int)elapsed.TotalMinutes);
            return string.Format("{0}m前", mins);
        }

        public static int RemoveExpiredPins(IDictionary<string, IList<MapPin>> source, DateTime now)
        {
            if (source == null || source.Count == 0)
            {
                return 0;
            }

            var removed = 0;
            foreach (var mapCode in source.Keys.ToList())
            {
                IList<MapPin> pins;
                if (!source.TryGetValue(mapCode, out pins) || pins == null || pins.Count == 0)
                {
                    source.Remove(mapCode);
                    continue;
                }

                for (var i = pins.Count - 1; i >= 0; i--)
                {
                    var pin = pins[i];
                    if (pin == null)
                    {
                        pins.RemoveAt(i);
                        removed++;
                        continue;
                    }

                    if (!pin.IsTimed || pin.AutoDeleteHours <= 0 || pin.CreatedAt == default(DateTime))
                    {
                        continue;
                    }

                    if (pin.CreatedAt.AddHours(pin.AutoDeleteHours) <= now)
                    {
                        pins.RemoveAt(i);
                        removed++;
                    }
                }

                if (pins.Count == 0)
                {
                    source.Remove(mapCode);
                }
            }

            return removed;
        }

        public static string BuildPinListText(MapPin pin, bool isShared)
        {
            var prefix = isShared ? "＊" : string.Empty;
            return string.Format("{0}東{1}、南{2} -- {3}", prefix, pin.East, pin.South, pin.Title);
        }

        public static IEnumerable<MapPin> GetMapPins(IDictionary<string, IList<MapPin>> pinSource, string mapCode)
        {
            if (pinSource == null || string.IsNullOrWhiteSpace(mapCode))
            {
                return Enumerable.Empty<MapPin>();
            }

            var normalizedMapCode = NormalizeMapCode(mapCode);
            if (string.IsNullOrWhiteSpace(normalizedMapCode))
            {
                return Enumerable.Empty<MapPin>();
            }

            IList<MapPin> pins;
            if (pinSource.TryGetValue(normalizedMapCode, out pins) && pins != null)
            {
                return pins.Where(x => x != null);
            }

            var merged = pinSource
                .Where(x => string.Equals(NormalizeMapCode(x.Key), normalizedMapCode, StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Value ?? Enumerable.Empty<MapPin>())
                .Where(x => x != null)
                .ToList();
            if (!merged.Any())
            {
                return Enumerable.Empty<MapPin>();
            }

            return merged;
        }

        public static string NormalizeMapCode(string mapCode)
        {
            if (string.IsNullOrWhiteSpace(mapCode))
            {
                return null;
            }

            var normalized = mapCode.Trim().Replace('/', '\\');
            try
            {
                return Path.GetFileName(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return normalized;
            }
        }

        public static bool RemovePin(IDictionary<string, IList<MapPin>> source, string mapCode, MapPin pin)
        {
            if (source == null || string.IsNullOrWhiteSpace(mapCode) || pin == null)
            {
                return false;
            }

            IList<MapPin> pins;
            if (!source.TryGetValue(mapCode, out pins) || pins == null || pins.Count == 0)
            {
                return false;
            }

            var index = -1;
            for (var i = 0; i < pins.Count; i++)
            {
                var candidate = pins[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.East == pin.East &&
                    candidate.South == pin.South &&
                    string.Equals(candidate.Title, pin.Title, StringComparison.Ordinal) &&
                    string.Equals(candidate.Detail ?? string.Empty, pin.Detail ?? string.Empty, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                for (var i = 0; i < pins.Count; i++)
                {
                    var candidate = pins[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (candidate.East == pin.East &&
                        candidate.South == pin.South &&
                        string.Equals(candidate.Title, pin.Title, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }
            }

            if (index < 0)
            {
                return false;
            }

            pins.RemoveAt(index);
            if (pins.Count == 0)
            {
                source.Remove(mapCode);
            }

            return true;
        }

        public static void AddOrUpdatePin(IDictionary<string, IList<MapPin>> source, string mapCode, MapPin pin)
        {
            mapCode = NormalizeMapCode(mapCode);
            if (source == null || string.IsNullOrWhiteSpace(mapCode) || pin == null)
            {
                return;
            }

            IList<MapPin> mapPins;
            if (!source.TryGetValue(mapCode, out mapPins) || mapPins == null)
            {
                mapPins = new List<MapPin>();
                source[mapCode] = mapPins;
            }

            var existing = mapPins.FirstOrDefault(x =>
                x != null &&
                x.East == pin.East &&
                x.South == pin.South &&
                string.Equals(x.Title, pin.Title, StringComparison.Ordinal));
            if (existing == null)
            {
                mapPins.Add(pin);
                return;
            }

            existing.Detail = pin.Detail;
            existing.CreatedAt = pin.CreatedAt;
            existing.IsTimed = pin.IsTimed;
            existing.AutoDeleteHours = pin.AutoDeleteHours;
        }

        public static IEnumerable<LocalPinManagerItem> BuildLocalPinManagerItems(IDictionary<string, IList<MapPin>> localPins)
        {
            if (localPins == null)
            {
                return Enumerable.Empty<LocalPinManagerItem>();
            }

            return localPins
                .SelectMany(x =>
                    (x.Value ?? Enumerable.Empty<MapPin>())
                    .Where(p => p != null)
                    .Select(p => new LocalPinManagerItem
                    {
                        MapCode = NormalizeMapCode(x.Key),
                        Pin = p
                    }))
                .OrderBy(x => x.MapCode)
                .ThenBy(x => x.Pin?.East ?? 0)
                .ThenBy(x => x.Pin?.South ?? 0)
                .ThenBy(x => x.Pin?.Title ?? string.Empty)
                .ToList();
        }
    }
}

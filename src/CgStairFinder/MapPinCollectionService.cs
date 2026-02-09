using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CgStairFinder
{
    internal static class MapPinCollectionService
    {
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

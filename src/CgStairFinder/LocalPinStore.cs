using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CgStairFinder
{
    internal static class LocalPinStore
    {
        private const int CurrentSchemaVersion = 1;

        [DataContract]
        private sealed class PinFile
        {
            [DataMember(Name = "schemaVersion")]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "entries")]
            public List<PinEntry> Entries { get; set; }
        }

        [DataContract]
        private sealed class PinEntry
        {
            [DataMember(Name = "mapCode")]
            public string MapCode { get; set; }

            [DataMember(Name = "pins")]
            public List<PinData> Pins { get; set; }
        }

        [DataContract]
        private sealed class PinData
        {
            [DataMember(Name = "east")]
            public int East { get; set; }

            [DataMember(Name = "south")]
            public int South { get; set; }

            [DataMember(Name = "title")]
            public string Title { get; set; }

            [DataMember(Name = "detail")]
            public string Detail { get; set; }

            [DataMember(Name = "createdAt", EmitDefaultValue = false)]
            public DateTime CreatedAt { get; set; }

            [DataMember(Name = "isTimed", EmitDefaultValue = false)]
            public bool IsTimed { get; set; }

            [DataMember(Name = "autoDeleteHours", EmitDefaultValue = false)]
            public int AutoDeleteHours { get; set; }
        }

        public static IDictionary<string, IList<MapPin>> Load()
        {
            var result = new Dictionary<string, IList<MapPin>>(StringComparer.OrdinalIgnoreCase);
            var path = GetStoragePath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return result;
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(PinFile));
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var payload = serializer.ReadObject(fs) as PinFile;
                    if (payload?.Entries == null)
                    {
                        return result;
                    }

                    foreach (var entry in payload.Entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.MapCode) || entry.Pins == null)
                        {
                            continue;
                        }

                        var pins = entry.Pins
                            .Select(ToMapPin)
                            .Where(x => x != null)
                            .ToList();
                        if (pins.Count == 0)
                        {
                            continue;
                        }

                        result[entry.MapCode] = pins;
                    }
                }
            }
            catch (IOException)
            {
                return new Dictionary<string, IList<MapPin>>(StringComparer.OrdinalIgnoreCase);
            }
            catch (SerializationException)
            {
                return new Dictionary<string, IList<MapPin>>(StringComparer.OrdinalIgnoreCase);
            }

            return result;
        }

        public static void Save(IDictionary<string, IList<MapPin>> localPins)
        {
            var path = GetStoragePath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var payload = new PinFile
                {
                    SchemaVersion = CurrentSchemaVersion,
                    Entries = (localPins ?? new Dictionary<string, IList<MapPin>>())
                        .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                        .Select(x => new PinEntry
                        {
                            MapCode = x.Key,
                            Pins = (x.Value ?? new List<MapPin>())
                                .Select(ToPinData)
                                .Where(y => y != null)
                                .ToList()
                        })
                        .Where(x => x.Pins.Count > 0)
                        .OrderBy(x => x.MapCode, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };

                var serializer = new DataContractJsonSerializer(typeof(PinFile));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(fs, payload);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (SerializationException)
            {
            }
        }

        private static string GetStoragePath()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrWhiteSpace(appData))
                {
                    return null;
                }

                return Path.Combine(appData, "CgStairFinder", "pins.json");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }
        }

        private static MapPin ToMapPin(PinData data)
        {
            if (data == null)
            {
                return null;
            }

            return MapPin.Normalize(new MapPin
            {
                East = data.East,
                South = data.South,
                Title = data.Title,
                Detail = data.Detail,
                CreatedAt = data.CreatedAt,
                IsTimed = data.IsTimed,
                AutoDeleteHours = data.AutoDeleteHours
            });
        }

        private static PinData ToPinData(MapPin pin)
        {
            var normalized = MapPin.Normalize(pin);
            if (normalized == null)
            {
                return null;
            }

            return new PinData
            {
                East = normalized.East,
                South = normalized.South,
                Title = normalized.Title,
                Detail = normalized.Detail ?? string.Empty,
                CreatedAt = normalized.CreatedAt,
                IsTimed = normalized.IsTimed,
                AutoDeleteHours = normalized.AutoDeleteHours
            };
        }
    }
}

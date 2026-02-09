using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CgStairFinder
{
    internal static class HistoryShareService
    {
        private const int CurrentSchemaVersion = 3;

        [DataContract]
        private sealed class SharedHistoryFile
        {
            [DataMember(Name = "schemaVersion")]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "exportedAt")]
            public DateTime ExportedAt { get; set; }

            [DataMember(Name = "entries")]
            public List<SharedHistoryEntry> Entries { get; set; }

            [DataMember(Name = "mapFiles", EmitDefaultValue = false)]
            public List<SharedMapFile> MapFiles { get; set; }
        }

        [DataContract]
        private sealed class SharedHistoryEntry
        {
            [DataMember(Name = "mapCode")]
            public string MapCode { get; set; }

            [DataMember(Name = "mapRelativePath", EmitDefaultValue = false)]
            public string MapRelativePath { get; set; }

            [DataMember(Name = "mapName")]
            public string MapName { get; set; }

            [DataMember(Name = "detectTime")]
            public DateTime DetectTime { get; set; }

            [DataMember(Name = "stairs")]
            public List<SharedStair> Stairs { get; set; }

            [DataMember(Name = "pins", EmitDefaultValue = false)]
            public List<SharedPin> Pins { get; set; }
        }

        [DataContract]
        private sealed class SharedStair
        {
            [DataMember(Name = "east")]
            public int East { get; set; }

            [DataMember(Name = "south")]
            public int South { get; set; }

            [DataMember(Name = "type")]
            public int Type { get; set; }
        }

        [DataContract]
        private sealed class SharedPin
        {
            [DataMember(Name = "east")]
            public int East { get; set; }

            [DataMember(Name = "south")]
            public int South { get; set; }

            [DataMember(Name = "title")]
            public string Title { get; set; }

            [DataMember(Name = "detail")]
            public string Detail { get; set; }
        }

        [DataContract]
        private sealed class SharedMapFile
        {
            [DataMember(Name = "mapCode")]
            public string MapCode { get; set; }

            [DataMember(Name = "relativePath", EmitDefaultValue = false)]
            public string RelativePath { get; set; }

            [DataMember(Name = "data")]
            public byte[] Data { get; set; }
        }

        internal sealed class MapFileExportItem
        {
            public string RelativePath { get; set; }
            public byte[] Data { get; set; }
        }

        internal sealed class ExportResult
        {
            public int ExportedEntries { get; set; }
            public int ExportedMapFiles { get; set; }
            public int MissingMapFiles { get; set; }
        }

        internal sealed class ImportResult
        {
            public int Added { get; set; }
            public int Updated { get; set; }
            public int Skipped { get; set; }
            public int Invalid { get; set; }
            public int TotalEntries { get; set; }

            public int TotalMapFiles { get; set; }
            public int ImportedMapFiles { get; set; }
            public int OverwrittenMapFiles { get; set; }
            public int SkippedMapFiles { get; set; }
            public int InvalidMapFiles { get; set; }
            public int FailedMapFiles { get; set; }

            public int PinMaps { get; set; }
            public int ImportedPins { get; set; }
        }

        public static string ExportToJson(IEnumerable<DetectLog> logs)
        {
            var payload = BuildPayload(logs);
            return ExportPayloadToJson(payload);
        }

        public static int Export(string outputPath, IEnumerable<DetectLog> logs)
        {
            var payload = BuildPayload(logs);
            var json = ExportPayloadToJson(payload);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
            return payload.Entries.Count;
        }

        public static int ExportCompressed(string outputPath, IEnumerable<DetectLog> logs)
        {
            return ExportCompressed(outputPath, logs, false, null).ExportedEntries;
        }

        public static ExportResult ExportCompressed(
            string outputPath,
            IEnumerable<DetectLog> logs,
            bool includeMapFiles,
            Func<DetectLog, MapFileExportItem> mapFileLoader)
        {
            return ExportCompressed(outputPath, logs, includeMapFiles, mapFileLoader, false, null);
        }

        public static ExportResult ExportCompressed(
            string outputPath,
            IEnumerable<DetectLog> logs,
            bool includeMapFiles,
            Func<DetectLog, MapFileExportItem> mapFileLoader,
            bool includePins,
            Func<string, IEnumerable<MapPin>> mapPinLoader)
        {
            int missingMapFiles;
            var payload = BuildPayload(logs, includeMapFiles, mapFileLoader, includePins, mapPinLoader, out missingMapFiles);
            var json = ExportPayloadToJson(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(fs, CompressionMode.Compress))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }

            return new ExportResult
            {
                ExportedEntries = payload.Entries.Count,
                ExportedMapFiles = payload.MapFiles?.Count ?? 0,
                MissingMapFiles = missingMapFiles
            };
        }

        public static ImportResult ImportFromJson(string json, IDictionary<string, DetectLog> targetLogs)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ImportResult();
            }

            var payload = DeserializePayload(json);
            return MergePayload(payload, targetLogs, null);
        }

        public static ImportResult Import(string inputPath, IDictionary<string, DetectLog> targetLogs)
        {
            var json = File.ReadAllText(inputPath, Encoding.UTF8);
            return ImportFromJson(json, targetLogs);
        }

        public static ImportResult ImportCompressed(string inputPath, IDictionary<string, DetectLog> targetLogs)
        {
            return ImportCompressed(inputPath, targetLogs, null);
        }

        public static ImportResult ImportCompressed(string inputPath, IDictionary<string, DetectLog> targetLogs, string mapDirectory)
        {
            return ImportCompressed(inputPath, targetLogs, null, mapDirectory, false);
        }

        public static ImportResult ImportCompressed(
            string inputPath,
            IDictionary<string, DetectLog> targetLogs,
            string mapDirectory,
            bool overwriteExistingMapFiles)
        {
            return ImportCompressed(inputPath, targetLogs, null, mapDirectory, overwriteExistingMapFiles);
        }

        public static ImportResult ImportCompressed(
            string inputPath,
            IDictionary<string, DetectLog> targetLogs,
            IDictionary<string, IList<MapPin>> targetPins,
            string mapDirectory,
            bool overwriteExistingMapFiles)
        {
            byte[] compressed;
            using (var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                compressed = new byte[fs.Length];
                fs.Read(compressed, 0, compressed.Length);
            }

            string json;
            try
            {
                using (var input = new MemoryStream(compressed))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    gzip.CopyTo(output);
                    json = Encoding.UTF8.GetString(output.ToArray());
                }
            }
            catch (InvalidDataException)
            {
                // 旧フォーマット(JSON生ファイル)も取り込めるようにしておく
                json = Encoding.UTF8.GetString(compressed);
            }

            var payload = DeserializePayload(json);
            var result = MergePayload(payload, targetLogs, targetPins);
            MergeMapFiles(payload, mapDirectory, overwriteExistingMapFiles, result);
            return result;
        }

        private static SharedHistoryFile BuildPayload(IEnumerable<DetectLog> logs)
        {
            int unused;
            return BuildPayload(logs, false, null, false, null, out unused);
        }

        private static SharedHistoryFile BuildPayload(
            IEnumerable<DetectLog> logs,
            bool includeMapFiles,
            Func<DetectLog, MapFileExportItem> mapFileLoader,
            bool includePins,
            Func<string, IEnumerable<MapPin>> mapPinLoader,
            out int missingMapFiles)
        {
            missingMapFiles = 0;
            var validLogs = (logs ?? Enumerable.Empty<DetectLog>())
                .Where(x => x != null)
                .Select(x => new
                {
                    Log = x,
                    MapCode = NormalizeMapCode(x.MapCode)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.MapCode))
                .OrderBy(x => x.Log.DetectTime)
                .ToList();

            List<SharedMapFile> mapFiles = null;
            if (includeMapFiles && mapFileLoader != null)
            {
                mapFiles = new List<SharedMapFile>();
                foreach (var item in validLogs
                    .GroupBy(x => x.MapCode, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.Log.DetectTime).First()))
                {
                    var loaded = mapFileLoader(item.Log);
                    if (loaded?.Data == null || loaded.Data.Length == 0)
                    {
                        missingMapFiles++;
                        continue;
                    }

                    var relativePath = NormalizeRelativePath(loaded.RelativePath);
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        relativePath = NormalizeRelativePath(item.Log.MapRelativePath);
                    }
                    if (string.IsNullOrWhiteSpace(relativePath))
                    {
                        relativePath = NormalizeRelativePath(item.MapCode);
                    }

                    mapFiles.Add(new SharedMapFile
                    {
                        MapCode = item.MapCode,
                        RelativePath = relativePath,
                        Data = loaded.Data
                    });
                }
            }

            return new SharedHistoryFile
            {
                SchemaVersion = CurrentSchemaVersion,
                ExportedAt = DateTime.Now,
                Entries = validLogs.Select(item =>
                    ToSharedEntry(
                        item.Log,
                        item.MapCode,
                        includePins && mapPinLoader != null
                            ? mapPinLoader(item.MapCode)
                            : null)).ToList(),
                MapFiles = mapFiles != null && mapFiles.Count > 0 ? mapFiles : null
            };
        }

        private static SharedHistoryFile DeserializePayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(SharedHistoryFile));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(ms) as SharedHistoryFile;
            }
        }

        private static string ExportPayloadToJson(SharedHistoryFile payload)
        {
            var serializer = new DataContractJsonSerializer(typeof(SharedHistoryFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static ImportResult MergePayload(
            SharedHistoryFile payload,
            IDictionary<string, DetectLog> targetLogs,
            IDictionary<string, IList<MapPin>> targetPins)
        {
            var result = new ImportResult();
            if (payload?.Entries == null)
            {
                return result;
            }

            result.TotalEntries = payload.Entries.Count;

            foreach (var entry in payload.Entries)
            {
                var detectLog = ToDetectLog(entry);
                if (detectLog == null)
                {
                    result.Invalid++;
                    continue;
                }

                var logStorageKey = BuildLogStorageKey(detectLog);
                DetectLog existing;
                if (!targetLogs.TryGetValue(logStorageKey, out existing))
                {
                    targetLogs[logStorageKey] = detectLog;
                    result.Added++;
                    MergeEntryPins(targetPins, detectLog.MapCode, entry.Pins, result);
                    continue;
                }

                if (detectLog.DetectTime > existing.DetectTime)
                {
                    targetLogs[logStorageKey] = detectLog;
                    result.Updated++;
                }
                else
                {
                    result.Skipped++;
                }

                MergeEntryPins(targetPins, detectLog.MapCode, entry.Pins, result);
            }

            return result;
        }

        private static string BuildLogStorageKey(DetectLog log)
        {
            if (log == null)
            {
                return "code:";
            }

            var relativePath = NormalizeRelativePath(log.MapRelativePath);
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                return "path:" + relativePath.ToLowerInvariant();
            }

            return "code:" + (NormalizeMapCode(log.MapCode) ?? string.Empty);
        }

        private static void MergeEntryPins(
            IDictionary<string, IList<MapPin>> targetPins,
            string mapCode,
            IEnumerable<SharedPin> sharedPins,
            ImportResult result)
        {
            if (targetPins == null || sharedPins == null || string.IsNullOrWhiteSpace(mapCode) || result == null)
            {
                return;
            }

            var pins = sharedPins
                .Select(ToMapPin)
                .Where(x => x != null)
                .ToList();

            if (pins.Count == 0)
            {
                targetPins.Remove(mapCode);
                return;
            }

            targetPins[mapCode] = pins;
            result.PinMaps++;
            result.ImportedPins += pins.Count;
        }

        private static void MergeMapFiles(
            SharedHistoryFile payload,
            string mapDirectory,
            bool overwriteExistingMapFiles,
            ImportResult result)
        {
            if (result == null || payload?.MapFiles == null || payload.MapFiles.Count == 0)
            {
                return;
            }

            result.TotalMapFiles = payload.MapFiles.Count;
            if (string.IsNullOrWhiteSpace(mapDirectory))
            {
                result.SkippedMapFiles = payload.MapFiles.Count;
                return;
            }

            try
            {
                Directory.CreateDirectory(mapDirectory);
            }
            catch
            {
                result.FailedMapFiles += payload.MapFiles.Count;
                return;
            }

            foreach (var mapFile in payload.MapFiles)
            {
                if (mapFile == null || string.IsNullOrWhiteSpace(mapFile.MapCode) || mapFile.Data == null || mapFile.Data.Length == 0)
                {
                    result.InvalidMapFiles++;
                    continue;
                }

                var relativePath = NormalizeRelativePath(mapFile.RelativePath);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    relativePath = NormalizeRelativePath(mapFile.MapCode);
                }

                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    result.InvalidMapFiles++;
                    continue;
                }

                string outputPath;
                if (!TryBuildSafeOutputPath(mapDirectory, relativePath, out outputPath))
                {
                    result.InvalidMapFiles++;
                    continue;
                }

                try
                {
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    if (File.Exists(outputPath))
                    {
                        if (!overwriteExistingMapFiles)
                        {
                            result.SkippedMapFiles++;
                            continue;
                        }

                        File.WriteAllBytes(outputPath, mapFile.Data);
                        result.OverwrittenMapFiles++;
                        continue;
                    }

                    File.WriteAllBytes(outputPath, mapFile.Data);
                    result.ImportedMapFiles++;
                }
                catch
                {
                    result.FailedMapFiles++;
                }
            }
        }

        private static bool TryBuildSafeOutputPath(string mapDirectory, string relativePath, out string outputPath)
        {
            outputPath = null;
            if (string.IsNullOrWhiteSpace(mapDirectory) || string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            string rootFull;
            string candidateFull;
            try
            {
                rootFull = Path.GetFullPath(mapDirectory).TrimEnd('\\') + "\\";
                candidateFull = Path.GetFullPath(Path.Combine(mapDirectory, relativePath));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }

            if (!candidateFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            outputPath = candidateFull;
            return true;
        }

        private static string NormalizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalized = path.Trim().Replace('/', '\\').TrimStart('\\');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(normalized))
                {
                    return null;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }

            var segments = normalized
                .Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToArray();
            if (segments.Length == 0)
            {
                return null;
            }

            if (segments.Any(x => x == "." || x == ".."))
            {
                return null;
            }

            if (segments.Any(x => x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                return null;
            }

            return string.Join("\\", segments);
        }

        private static SharedHistoryEntry ToSharedEntry(DetectLog log, string normalizedMapCode, IEnumerable<MapPin> pins)
        {
            var sharedPins = (pins ?? Enumerable.Empty<MapPin>())
                .Select(ToSharedPin)
                .Where(x => x != null)
                .ToList();

            return new SharedHistoryEntry
            {
                MapCode = normalizedMapCode,
                MapRelativePath = log.MapRelativePath,
                MapName = log.MapName,
                DetectTime = log.DetectTime,
                Stairs = (log.CgStairs ?? new List<CgStair>()).Select(stair => new SharedStair
                {
                    East = stair.East,
                    South = stair.South,
                    Type = (int)stair.Type
                }).ToList(),
                Pins = sharedPins.Count > 0 ? sharedPins : null
            };
        }

        private static DetectLog ToDetectLog(SharedHistoryEntry entry)
        {
            var mapCode = NormalizeMapCode(entry?.MapCode);
            if (entry == null || string.IsNullOrWhiteSpace(mapCode))
            {
                return null;
            }

            var stairs = new List<CgStair>();
            if (entry.Stairs != null)
            {
                foreach (var stair in entry.Stairs)
                {
                    stairs.Add(new CgStair
                    {
                        East = stair.East,
                        South = stair.South,
                        Type = ToStairType(stair.Type)
                    });
                }
            }

            return new DetectLog
            {
                MapCode = mapCode,
                MapRelativePath = entry.MapRelativePath ?? string.Empty,
                MapName = entry.MapName ?? string.Empty,
                DetectTime = entry.DetectTime == default(DateTime) ? DateTime.MinValue : entry.DetectTime,
                CgStairs = stairs
            };
        }

        private static string NormalizeMapCode(string mapCode)
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

        private static StairType ToStairType(int type)
        {
            return Enum.IsDefined(typeof(StairType), type)
                ? (StairType)type
                : StairType.Unknow;
        }

        private static SharedPin ToSharedPin(MapPin pin)
        {
            var normalized = MapPin.Normalize(pin);
            if (normalized == null)
            {
                return null;
            }

            return new SharedPin
            {
                East = normalized.East,
                South = normalized.South,
                Title = normalized.Title,
                Detail = normalized.Detail ?? string.Empty
            };
        }

        private static MapPin ToMapPin(SharedPin pin)
        {
            if (pin == null)
            {
                return null;
            }

            return MapPin.Normalize(new MapPin
            {
                East = pin.East,
                South = pin.South,
                Title = pin.Title,
                Detail = pin.Detail
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CgStairFinder
{
    internal static class HistoryShareService
    {
        private const int CurrentSchemaVersion = 1;

        [DataContract]
        private sealed class SharedHistoryFile
        {
            [DataMember(Name = "schemaVersion")]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "exportedAt")]
            public DateTime ExportedAt { get; set; }

            [DataMember(Name = "entries")]
            public List<SharedHistoryEntry> Entries { get; set; }
        }

        [DataContract]
        private sealed class SharedHistoryEntry
        {
            [DataMember(Name = "mapCode")]
            public string MapCode { get; set; }

            [DataMember(Name = "mapName")]
            public string MapName { get; set; }

            [DataMember(Name = "detectTime")]
            public DateTime DetectTime { get; set; }

            [DataMember(Name = "stairs")]
            public List<SharedStair> Stairs { get; set; }
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

        internal sealed class ImportResult
        {
            public int Added { get; set; }
            public int Updated { get; set; }
            public int Skipped { get; set; }
            public int Invalid { get; set; }
            public int TotalEntries { get; set; }
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

        public static ImportResult ImportFromJson(string json, IDictionary<string, DetectLog> targetLogs)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ImportResult();
            }

            var serializer = new DataContractJsonSerializer(typeof(SharedHistoryFile));
            SharedHistoryFile payload;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                payload = serializer.ReadObject(ms) as SharedHistoryFile;
            }

            return MergePayload(payload, targetLogs);
        }

        public static ImportResult Import(string inputPath, IDictionary<string, DetectLog> targetLogs)
        {
            var json = File.ReadAllText(inputPath, Encoding.UTF8);
            return ImportFromJson(json, targetLogs);
        }

        private static SharedHistoryFile BuildPayload(IEnumerable<DetectLog> logs)
        {
            var validLogs = (logs ?? Enumerable.Empty<DetectLog>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.MapCode))
                .OrderBy(x => x.DetectTime)
                .ToList();

            return new SharedHistoryFile
            {
                SchemaVersion = CurrentSchemaVersion,
                ExportedAt = DateTime.Now,
                Entries = validLogs.Select(ToSharedEntry).ToList()
            };
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

        private static ImportResult MergePayload(SharedHistoryFile payload, IDictionary<string, DetectLog> targetLogs)
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

                DetectLog existing;
                if (!targetLogs.TryGetValue(detectLog.MapCode, out existing))
                {
                    targetLogs[detectLog.MapCode] = detectLog;
                    result.Added++;
                    continue;
                }

                if (detectLog.DetectTime > existing.DetectTime)
                {
                    targetLogs[detectLog.MapCode] = detectLog;
                    result.Updated++;
                }
                else
                {
                    result.Skipped++;
                }
            }

            return result;
        }

        private static SharedHistoryEntry ToSharedEntry(DetectLog log)
        {
            return new SharedHistoryEntry
            {
                MapCode = log.MapCode,
                MapName = log.MapName,
                DetectTime = log.DetectTime,
                Stairs = (log.CgStairs ?? new List<CgStair>()).Select(stair => new SharedStair
                {
                    East = stair.East,
                    South = stair.South,
                    Type = (int)stair.Type
                }).ToList()
            };
        }

        private static DetectLog ToDetectLog(SharedHistoryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.MapCode))
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
                MapCode = entry.MapCode,
                MapName = entry.MapName ?? string.Empty,
                DetectTime = entry.DetectTime == default(DateTime) ? DateTime.MinValue : entry.DetectTime,
                CgStairs = stairs
            };
        }

        private static StairType ToStairType(int type)
        {
            return Enum.IsDefined(typeof(StairType), type)
                ? (StairType)type
                : StairType.Unknow;
        }
    }
}

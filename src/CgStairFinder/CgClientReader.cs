using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CgStairFinder.Properties;

namespace CgStairFinder
{
    internal sealed class CgClientMapSnapshot
    {
        public string MapName { get; set; }

        public FileInfo MapFile { get; set; }

        public int? MapPathOffset { get; set; }

        public bool UsedLatestMapFallback { get; set; }

        public int East { get; set; }

        public int South { get; set; }
    }

    internal sealed class MapPathProbeResult
    {
        public bool Success { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public long? FoundAddress { get; set; }
        public int? OffsetFromDefault { get; set; }
        public string RawPath { get; set; }
        public string ResolvedPath { get; set; }
        public bool ResolvedPathExists { get; set; }
        public int RegionsScanned { get; set; }
        public long BytesScanned { get; set; }
        public int CandidateCount { get; set; }
        public bool StoppedByByteLimit { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal static class CgClientReader
    {
        private const int ProcessAccessAll = 0x1F0FFF;
        private const int ProcessAccessQueryAndRead = 0x0410;
        private const long AddressMapName = 0x95C870;
        private const long AddressMapPath = 0x18CCCC;
        private const int MapPathBufferSize = 128;
        private const int ProbeChunkSize = 64 * 1024;
        private const long ProbeMaxBytes = 64L * 1024L * 1024L;
        private const long ProbeInitialRangeBytes = 0x10000; // 64KB
        private const long ProbeMaxRangeBytes = 0x200000; // 2MB
        private const long AddressEast = 0x95C88C;
        private const long AddressSouth = 0x95C890;
        private const uint MemCommit = 0x1000;
        private const uint PageGuard = 0x100;
        private const uint PageNoAccess = 0x01;

        private static readonly Encoding CgMapNameEncoding = Encoding.GetEncoding(950);
        private static readonly Dictionary<int, long> MapPathAddressByProcessId = new Dictionary<int, long>();
        private static readonly object MapPathAddressLock = new object();

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualQueryEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            out MEMORY_BASIC_INFORMATION lpBuffer,
            IntPtr dwLength);

        [DllImport("kernel32.dll")]
        private static extern void GetNativeSystemInfo(out SYSTEM_INFO lpSystemInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_INFO
        {
            public ushort ProcessorArchitecture;
            public ushort Reserved;
            public uint PageSize;
            public IntPtr MinimumApplicationAddress;
            public IntPtr MaximumApplicationAddress;
            public IntPtr ActiveProcessorMask;
            public uint NumberOfProcessors;
            public uint ProcessorType;
            public uint AllocationGranularity;
            public ushort ProcessorLevel;
            public ushort ProcessorRevision;
        }

        private sealed class MapPathCandidate
        {
            public long Address { get; set; }
            public string RawPath { get; set; }
            public string ResolvedPath { get; set; }
            public bool ResolvedExists { get; set; }
            public int Score { get; set; }
        }

        public static FileInfo GetLatestMapFile(string cgDir)
        {
            var mapDir = Path.Combine(cgDir ?? string.Empty, "map");
            if (!Directory.Exists(mapDir))
            {
                return null;
            }

            return new DirectoryInfo(mapDir)
                .GetFiles("*.dat", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
        }

        public static bool TryReadSnapshot(Process process, string cgDir, out CgClientMapSnapshot snapshot, out string errorMessage)
        {
            snapshot = null;
            errorMessage = null;

            if (process == null || process.HasExited)
            {
                errorMessage = "\u30A6\u30A3\u30F3\u30C9\u30A6\u306E\u691C\u51FA\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002";
                return false;
            }

            var hProcess = OpenProcess(ProcessAccessAll, false, process.Id);
            if (hProcess == IntPtr.Zero)
            {
                errorMessage = "\u30A6\u30A3\u30F3\u30C9\u30A6\u306E\u30D7\u30ED\u30BB\u30B9\u3092\u958B\u3051\u307E\u305B\u3093\u3067\u3057\u305F\u3002";
                return false;
            }

            try
            {
                var mapNameBuffer = new byte[32];
                if (!TryReadProcessMemory(hProcess, AddressMapName, mapNameBuffer))
                {
                    errorMessage = "\u30DE\u30C3\u30D7\u540D\u3092\u8AAD\u307F\u53D6\u308C\u307E\u305B\u3093\u3002";
                    return false;
                }

                int? mapPathOffset;
                bool usedLatestMapFallback;
                var mapFile = ResolveMapFile(hProcess, process.Id, cgDir, out mapPathOffset, out usedLatestMapFallback);

                var positionBuffer = new byte[4];
                if (!TryReadProcessMemory(hProcess, AddressEast, positionBuffer))
                {
                    errorMessage = "\u73FE\u5728\u5EA7\u6A19(\u6771)\u3092\u8AAD\u307F\u53D6\u308C\u307E\u305B\u3093\u3002";
                    return false;
                }
                var east = (int)(BitConverter.ToSingle(positionBuffer, 0) / 64);

                if (!TryReadProcessMemory(hProcess, AddressSouth, positionBuffer))
                {
                    errorMessage = "\u73FE\u5728\u5EA7\u6A19(\u5357)\u3092\u8AAD\u307F\u53D6\u308C\u307E\u305B\u3093\u3002";
                    return false;
                }
                var south = (int)(BitConverter.ToSingle(positionBuffer, 0) / 64);

                snapshot = new CgClientMapSnapshot
                {
                    MapName = DecodeMapName(mapNameBuffer),
                    MapFile = mapFile,
                    MapPathOffset = mapPathOffset,
                    UsedLatestMapFallback = usedLatestMapFallback,
                    East = east,
                    South = south
                };

                return true;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        public static MapPathProbeResult ProbeMapPathAddress(Process process, string cgDir)
        {
            var result = new MapPathProbeResult
            {
                ProcessId = process?.Id ?? 0,
                ProcessName = process?.ProcessName ?? string.Empty
            };

            if (process == null || process.HasExited)
            {
                result.ErrorMessage = "プロセスが見つからないか、既に終了しています。";
                return result;
            }

            var hProcess = OpenProcess(ProcessAccessQueryAndRead, false, process.Id);
            if (hProcess == IntPtr.Zero)
            {
                result.ErrorMessage = "プロセスのメモリーを開けませんでした。管理者権限を確認してください。";
                return result;
            }

            try
            {
                var scannedBytes = 0L;
                MapPathCandidate bestCandidate = null;
                var range = ProbeInitialRangeBytes;

                while (range <= ProbeMaxRangeBytes)
                {
                    ScanAddressRangeForMapPathCandidate(
                        hProcess,
                        AddressMapPath - range,
                        AddressMapPath + range,
                        cgDir,
                        result,
                        ref scannedBytes,
                        ref bestCandidate);

                    if ((bestCandidate != null && bestCandidate.ResolvedExists) || scannedBytes >= ProbeMaxBytes)
                    {
                        break;
                    }

                    if (range == ProbeMaxRangeBytes)
                    {
                        break;
                    }

                    range = Math.Min(ProbeMaxRangeBytes, range * 2);
                }

                result.StoppedByByteLimit = scannedBytes >= ProbeMaxBytes;
                result.BytesScanned = scannedBytes;
                if (bestCandidate == null || !bestCandidate.ResolvedExists)
                {
                    result.ErrorMessage = "有効な .dat パス候補を見つけられませんでした。";
                    return result;
                }

                // Persist only when the resolved map path actually exists under cgDir\map.
                if (bestCandidate.ResolvedExists)
                {
                    SaveCachedMapPathAddress(process.Id, bestCandidate.Address);
                    SavePersistedMapPathOffset((int)(bestCandidate.Address - AddressMapPath));
                }
                result.Success = true;
                result.FoundAddress = bestCandidate.Address;
                result.OffsetFromDefault = (int)(bestCandidate.Address - AddressMapPath);
                result.RawPath = bestCandidate.RawPath ?? string.Empty;
                result.ResolvedPath = bestCandidate.ResolvedPath ?? string.Empty;
                result.ResolvedPathExists = bestCandidate.ResolvedExists;
                return result;
            }
            catch (Exception ex) when (
                ex is InvalidOperationException ||
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is Win32Exception ||
                ex is NotSupportedException)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        private static bool TryReadProcessMemory(IntPtr hProcess, long address, byte[] buffer)
        {
            if (hProcess == IntPtr.Zero || buffer == null || buffer.Length == 0)
            {
                return false;
            }

            IntPtr bytesRead;
            return ReadProcessMemory(hProcess, new IntPtr(address), buffer, buffer.Length, out bytesRead);
        }

        private static void ScanAddressRangeForMapPathCandidate(
            IntPtr hProcess,
            long scanStart,
            long scanEnd,
            string cgDir,
            MapPathProbeResult result,
            ref long scannedBytes,
            ref MapPathCandidate bestCandidate)
        {
            if (scanStart > scanEnd)
            {
                return;
            }

            SYSTEM_INFO info;
            GetNativeSystemInfo(out info);
            var minimum = info.MinimumApplicationAddress.ToInt64();
            var maximum = info.MaximumApplicationAddress.ToInt64();
            var effectiveStart = Math.Max(scanStart, minimum);
            var effectiveEnd = Math.Min(scanEnd, maximum);
            if (effectiveStart > effectiveEnd)
            {
                return;
            }

            var mbiSize = Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION));
            var current = effectiveStart;
            while (current <= effectiveEnd && scannedBytes < ProbeMaxBytes)
            {
                MEMORY_BASIC_INFORMATION mbi;
                var queried = VirtualQueryEx(hProcess, new IntPtr(current), out mbi, new IntPtr(mbiSize));
                if (queried == IntPtr.Zero)
                {
                    current += 0x1000;
                    continue;
                }

                var baseAddress = mbi.BaseAddress.ToInt64();
                var regionSize = SafeToInt64(mbi.RegionSize);
                if (regionSize <= 0)
                {
                    current += 0x1000;
                    continue;
                }

                var nextAddress = SafeAddressAdd(baseAddress, regionSize, current);
                if (IsReadableCommittedRegion(mbi))
                {
                    var regionStart = Math.Max(baseAddress, effectiveStart);
                    var regionEnd = Math.Min(baseAddress + regionSize - 1, effectiveEnd);
                    if (regionEnd >= regionStart)
                    {
                        result.RegionsScanned++;
                        ScanRegionForMapPathCandidate(
                            hProcess,
                            regionStart,
                            regionEnd - regionStart + 1,
                            cgDir,
                            result,
                            ref scannedBytes,
                            ref bestCandidate);
                    }
                }

                current = nextAddress;
            }
        }

        private static void ScanRegionForMapPathCandidate(
            IntPtr hProcess,
            long readStartAddress,
            long readLengthTotal,
            string cgDir,
            MapPathProbeResult result,
            ref long scannedBytes,
            ref MapPathCandidate bestCandidate)
        {
            var offset = 0L;
            while (offset < readLengthTotal && scannedBytes < ProbeMaxBytes)
            {
                var remainingInRegion = readLengthTotal - offset;
                if (remainingInRegion <= 0)
                {
                    break;
                }

                var remainingBudget = ProbeMaxBytes - scannedBytes;
                if (remainingBudget <= 0)
                {
                    break;
                }

                var readLength = (int)Math.Min(ProbeChunkSize, Math.Min(remainingInRegion, remainingBudget));
                var buffer = new byte[readLength];
                IntPtr bytesRead;
                var success = ReadProcessMemory(hProcess, new IntPtr(readStartAddress + offset), buffer, readLength, out bytesRead);
                if (!success)
                {
                    offset += readLength;
                    continue;
                }

                var actualRead = (int)Math.Max(0, Math.Min((long)readLength, bytesRead.ToInt64()));
                if (actualRead <= 0)
                {
                    offset += readLength;
                    continue;
                }

                scannedBytes += actualRead;
                FindBestMapPathCandidateInBuffer(
                    buffer,
                    actualRead,
                    readStartAddress + offset,
                    cgDir,
                    result,
                    ref bestCandidate);
                offset += actualRead;
            }
        }

        private static void FindBestMapPathCandidateInBuffer(
            byte[] buffer,
            int length,
            long bufferAddress,
            string cgDir,
            MapPathProbeResult result,
            ref MapPathCandidate bestCandidate)
        {
            for (var i = 0; i <= length - 4; i++)
            {
                if (!IsDotDat(buffer, i))
                {
                    continue;
                }

                var start = i;
                while (start > 0 && IsPathChar(buffer[start - 1]))
                {
                    start--;
                }

                var end = i + 4;
                while (end < length && IsPathChar(buffer[end]))
                {
                    end++;
                }

                var candidateLen = end - start;
                if (candidateLen < 8 || candidateLen > 320)
                {
                    continue;
                }

                var bytes = new byte[candidateLen];
                Buffer.BlockCopy(buffer, start, bytes, 0, candidateLen);
                var rawCandidate = NormalizePath(Encoding.Default.GetString(bytes)).Trim('"');
                if (!LooksLikeMapPath(rawCandidate))
                {
                    continue;
                }

                var resolved = ResolveProbePath(rawCandidate, cgDir);
                var resolvedExists = !string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved);
                result.CandidateCount++;

                var score = ScorePathCandidate(rawCandidate, resolvedExists);
                if (bestCandidate != null && score <= bestCandidate.Score)
                {
                    continue;
                }

                bestCandidate = new MapPathCandidate
                {
                    Address = bufferAddress + start,
                    RawPath = rawCandidate,
                    ResolvedPath = resolved ?? string.Empty,
                    ResolvedExists = resolvedExists,
                    Score = score
                };
            }
        }

        private static int ScorePathCandidate(string rawPath, bool resolvedExists)
        {
            var score = 0;
            if (resolvedExists)
            {
                score += 100;
            }

            if (!string.IsNullOrWhiteSpace(rawPath) &&
                rawPath.IndexOf("map\\", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(rawPath) &&
                rawPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
            }

            return score;
        }

        private static string ResolveProbePath(string rawPath, string cgDir)
        {
            string resolvedPath;
            return TryResolveMapDatPath(rawPath, cgDir, out resolvedPath) ? resolvedPath : string.Empty;
        }

        private static bool IsDotDat(byte[] buffer, int index)
        {
            if (buffer == null || index < 0 || index + 3 >= buffer.Length)
            {
                return false;
            }

            return buffer[index] == (byte)'.' &&
                   ToLowerAscii(buffer[index + 1]) == (byte)'d' &&
                   ToLowerAscii(buffer[index + 2]) == (byte)'a' &&
                   ToLowerAscii(buffer[index + 3]) == (byte)'t';
        }

        private static byte ToLowerAscii(byte value)
        {
            return value >= (byte)'A' && value <= (byte)'Z'
                ? (byte)(value + 32)
                : value;
        }

        private static bool IsPathChar(byte b)
        {
            if (b == 0)
            {
                return false;
            }

            if (b < 32 || b > 126)
            {
                return false;
            }

            return true;
        }

        private static bool IsReadableCommittedRegion(MEMORY_BASIC_INFORMATION mbi)
        {
            if (mbi.State != MemCommit)
            {
                return false;
            }

            if ((mbi.Protect & PageGuard) != 0 || (mbi.Protect & PageNoAccess) != 0)
            {
                return false;
            }

            return true;
        }

        private static long SafeToInt64(UIntPtr value)
        {
            try
            {
                return (long)value.ToUInt64();
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }

        private static long SafeAddressAdd(long baseAddress, long size, long fallbackCurrent)
        {
            try
            {
                checked
                {
                    var next = baseAddress + size;
                    if (next > fallbackCurrent)
                    {
                        return next;
                    }
                }
            }
            catch (OverflowException)
            {
            }

            return fallbackCurrent + 0x1000;
        }

        private static FileInfo ResolveMapFile(IntPtr hProcess, int processId, string cgDir, out int? mapPathOffset, out bool usedLatestMapFallback)
        {
            mapPathOffset = null;
            usedLatestMapFallback = false;

            string rawPath;
            long resolvedAddress;
            if (!TryReadMapPath(hProcess, processId, cgDir, out rawPath, out resolvedAddress))
            {
                usedLatestMapFallback = true;
                return GetLatestMapFile(cgDir);
            }

            mapPathOffset = (int)(resolvedAddress - AddressMapPath);

            var fileInfo = TryCreateFileInfo(rawPath);
            if (fileInfo != null && fileInfo.Exists)
            {
                return fileInfo;
            }

            usedLatestMapFallback = true;
            return GetLatestMapFile(cgDir);
        }

        private static bool TryReadMapPath(IntPtr hProcess, int processId, string cgDir, out string mapPath, out long resolvedAddress)
        {
            mapPath = string.Empty;
            resolvedAddress = 0;

            var candidateAddresses = new List<long>();
            long cachedAddress;
            if (TryGetCachedMapPathAddress(processId, out cachedAddress))
            {
                candidateAddresses.Add(cachedAddress);
            }

            var persistedOffset = TryLoadPersistedMapPathOffset();
            if (persistedOffset.HasValue)
            {
                candidateAddresses.Add(AddressMapPath + persistedOffset.Value);
            }

            candidateAddresses.Add(AddressMapPath);

            foreach (var address in candidateAddresses.Distinct())
            {
                if (!TryReadMapPathAtAddress(hProcess, address, out mapPath))
                {
                    continue;
                }

                string resolvedMapPath;
                if (!TryResolveMapDatPath(mapPath, cgDir, out resolvedMapPath))
                {
                    continue;
                }

                var fileInfo = TryCreateFileInfo(resolvedMapPath);
                if (fileInfo == null || !fileInfo.Exists)
                {
                    continue;
                }

                SaveCachedMapPathAddress(processId, address);
                SavePersistedMapPathOffset((int)(address - AddressMapPath));
                mapPath = resolvedMapPath;
                resolvedAddress = address;
                return true;
            }

            mapPath = string.Empty;
            return false;
        }

        private static bool TryReadMapPathAtAddress(IntPtr hProcess, long address, out string mapPath)
        {
            mapPath = string.Empty;
            var buffer = new byte[MapPathBufferSize];
            if (!TryReadProcessMemory(hProcess, address, buffer))
            {
                return false;
            }

            var decoded = DecodePathCandidate(buffer, 0);
            if (!LooksLikeMapPath(decoded))
            {
                return false;
            }

            mapPath = decoded;
            return true;
        }

        private static bool TryGetCachedMapPathAddress(int processId, out long address)
        {
            lock (MapPathAddressLock)
            {
                return MapPathAddressByProcessId.TryGetValue(processId, out address);
            }
        }

        private static void SaveCachedMapPathAddress(int processId, long address)
        {
            lock (MapPathAddressLock)
            {
                MapPathAddressByProcessId[processId] = address;
            }
        }

        private static int? TryLoadPersistedMapPathOffset()
        {
            try
            {
                if (!Settings.Default.mapPathOffsetSaved)
                {
                    return null;
                }

                return Settings.Default.mapPathOffset;
            }
            catch
            {
                return null;
            }
        }

        private static void SavePersistedMapPathOffset(int offset)
        {
            try
            {
                if (Settings.Default.mapPathOffsetSaved && Settings.Default.mapPathOffset == offset)
                {
                    return;
                }

                Settings.Default.mapPathOffset = offset;
                Settings.Default.mapPathOffsetSaved = true;
                Settings.Default.Save();
            }
            catch
            {
            }
        }

        private static string DecodePathCandidate(byte[] buffer, int startIndex)
        {
            var bytes = ReadNullTerminatedBytes(buffer, startIndex);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            return NormalizePath(Encoding.Default.GetString(bytes));
        }

        private static bool LooksLikeMapPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = NormalizePath(path);
            if (!normalized.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return normalized.IndexOf("map\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryResolveMapDatPath(string rawPath, string cgDir, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (!LooksLikeMapPath(rawPath))
            {
                return false;
            }

            var normalizedPath = NormalizePath(rawPath).Trim('"');
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            string candidatePath;
            if (TryIsPathRooted(normalizedPath))
            {
                candidatePath = normalizedPath;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(cgDir))
                {
                    return false;
                }

                candidatePath = TryCombine(cgDir, normalizedPath.TrimStart('\\'));
            }

            var normalizedFullPath = TryGetFullPathNormalized(candidatePath);
            if (string.IsNullOrWhiteSpace(normalizedFullPath))
            {
                return false;
            }

            if (!IsUnderMapDirectory(normalizedFullPath, cgDir))
            {
                return false;
            }

            resolvedPath = normalizedFullPath;
            return true;
        }

        private static bool IsUnderMapDirectory(string fullPath, string cgDir)
        {
            var mapDir = TryCombine(cgDir ?? string.Empty, "map");
            var normalizedMapDir = TryGetFullPathNormalized(mapDir);
            if (string.IsNullOrWhiteSpace(normalizedMapDir))
            {
                return false;
            }

            var normalizedFullPath = TryGetFullPathNormalized(fullPath);
            if (string.IsNullOrWhiteSpace(normalizedFullPath))
            {
                return false;
            }

            return normalizedFullPath.StartsWith(normalizedMapDir + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static string TryGetFullPathNormalized(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return NormalizePath(Path.GetFullPath(path)).TrimEnd('\\');
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return string.Empty;
            }
        }

        private static bool TryIsPathRooted(string path)
        {
            try
            {
                return Path.IsPathRooted(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                return false;
            }
        }

        private static string TryCombine(string left, string right)
        {
            try
            {
                return Path.Combine(left, right);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        private static FileInfo TryCreateFileInfo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return new FileInfo(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        private static string DecodeMapName(byte[] buffer)
        {
            var bytes = ReadNullTerminatedBytes(buffer);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            return CgMapNameEncoding.GetString(bytes).Trim();
        }

        private static byte[] ReadNullTerminatedBytes(byte[] buffer)
        {
            return ReadNullTerminatedBytes(buffer, 0);
        }

        private static byte[] ReadNullTerminatedBytes(byte[] buffer, int startIndex)
        {
            if (buffer == null || buffer.Length == 0 || startIndex < 0 || startIndex >= buffer.Length)
            {
                return Array.Empty<byte>();
            }

            var count = 0;
            for (var i = startIndex; i < buffer.Length && buffer[i] != 0; i++)
            {
                count++;
            }

            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            var bytes = new byte[count];
            Buffer.BlockCopy(buffer, startIndex, bytes, 0, count);
            return bytes;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().Trim('\0').Replace('/', '\\');
        }
    }
}

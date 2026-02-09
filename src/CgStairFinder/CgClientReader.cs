using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CgStairFinder
{
    internal sealed class CgClientMapSnapshot
    {
        public string MapName { get; set; }

        public FileInfo MapFile { get; set; }

        public int East { get; set; }

        public int South { get; set; }
    }

    internal static class CgClientReader
    {
        private const int ProcessAccessAll = 0x1F0FFF;
        private const long AddressMapName = 0x95C870;
        private const long AddressMapPath = 0x18CCC8;
        private const long AddressEast = 0x95C88C;
        private const long AddressSouth = 0x95C890;

        private static readonly Encoding CgMapNameEncoding = Encoding.GetEncoding(950);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

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

                var mapPathBuffer = new byte[32];
                if (!TryReadProcessMemory(hProcess, AddressMapPath, mapPathBuffer))
                {
                    errorMessage = "\u30DE\u30C3\u30D7\u30D5\u30A1\u30A4\u30EB\u30D1\u30B9\u3092\u8AAD\u307F\u53D6\u308C\u307E\u305B\u3093\u3002";
                    return false;
                }

                var mapFile = ResolveMapFile(cgDir, mapPathBuffer);

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

        private static bool TryReadProcessMemory(IntPtr hProcess, long address, byte[] buffer)
        {
            if (hProcess == IntPtr.Zero || buffer == null || buffer.Length == 0)
            {
                return false;
            }

            IntPtr bytesRead;
            return ReadProcessMemory(hProcess, new IntPtr(address), buffer, buffer.Length, out bytesRead);
        }

        private static FileInfo ResolveMapFile(string cgDir, byte[] mapFileBuffer)
        {
            var rawPath = NormalizePath(DecodeMapPath(mapFileBuffer));
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return null;
            }

            var candidates = new List<string>();
            if (TryIsPathRooted(rawPath))
            {
                candidates.Add(rawPath);
            }
            else if (!string.IsNullOrWhiteSpace(cgDir))
            {
                var pathWithoutLeadingSlash = rawPath.TrimStart('\\');
                if (pathWithoutLeadingSlash.StartsWith("map\\", StringComparison.OrdinalIgnoreCase))
                {
                    var combinedFromMapRoot = TryCombine(cgDir, pathWithoutLeadingSlash);
                    if (!string.IsNullOrWhiteSpace(combinedFromMapRoot))
                    {
                        candidates.Add(combinedFromMapRoot);
                    }
                }
                else
                {
                    var combinedUnderMap = TryCombine(TryCombine(cgDir, "map"), pathWithoutLeadingSlash);
                    if (!string.IsNullOrWhiteSpace(combinedUnderMap))
                    {
                        candidates.Add(combinedUnderMap);
                    }
                }

                var combined = TryCombine(cgDir, rawPath);
                if (!string.IsNullOrWhiteSpace(combined))
                {
                    candidates.Add(combined);
                }
            }

            if (!string.IsNullOrWhiteSpace(cgDir))
            {
                var pathWithoutLeadingSlash = rawPath.TrimStart('\\');
                var combinedWithoutLeadingSlash = TryCombine(cgDir, pathWithoutLeadingSlash);
                if (!string.IsNullOrWhiteSpace(combinedWithoutLeadingSlash))
                {
                    candidates.Add(combinedWithoutLeadingSlash);
                }
            }

            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var file = TryCreateFileInfo(candidate);
                if (file != null && file.Exists)
                {
                    return file;
                }
            }

            return null;
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

        private static string DecodeMapPath(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return string.Empty;
            }

            // On some client builds, the 4 bytes at 0x18CCC8 are metadata and the path starts at +4.
            var offsetCandidate = DecodePathCandidate(buffer, 4);
            if (LooksLikeMapPath(offsetCandidate))
            {
                return offsetCandidate;
            }

            // Keep backward compatibility with the old layout.
            var baseCandidate = DecodePathCandidate(buffer, 0);
            if (LooksLikeMapPath(baseCandidate))
            {
                return baseCandidate;
            }

            return offsetCandidate.Length >= baseCandidate.Length ? offsetCandidate : baseCandidate;
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
            return normalized.IndexOf(".dat", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (normalized.StartsWith("map\\", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("\\"));
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

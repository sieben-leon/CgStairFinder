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
                errorMessage = "ウィンドウの検出に失敗しました。";
                return false;
            }

            var hProcess = OpenProcess(ProcessAccessAll, false, process.Id);
            if (hProcess == IntPtr.Zero)
            {
                errorMessage = "ウィンドウのプロセスを開けませんでした。";
                return false;
            }

            try
            {
                var mapNameBuffer = new byte[32];
                if (!TryReadProcessMemory(hProcess, AddressMapName, mapNameBuffer))
                {
                    errorMessage = "マップ名を読み取れません。";
                    return false;
                }

                var mapPathBuffer = new byte[32];
                if (!TryReadProcessMemory(hProcess, AddressMapPath, mapPathBuffer))
                {
                    errorMessage = "マップファイルパスを読み取れません。";
                    return false;
                }

                var mapFile = ResolveMapFile(cgDir, mapPathBuffer);
                if (mapFile == null || !mapFile.Exists)
                {
                    mapFile = GetLatestMapFile(cgDir);
                }

                var positionBuffer = new byte[4];
                if (!TryReadProcessMemory(hProcess, AddressEast, positionBuffer))
                {
                    errorMessage = "現在座標(東)を読み取れません。";
                    return false;
                }
                var east = (int)(BitConverter.ToSingle(positionBuffer, 0) / 64);

                if (!TryReadProcessMemory(hProcess, AddressSouth, positionBuffer))
                {
                    errorMessage = "現在座標(南)を読み取れません。";
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

            var mapDir = TryCombine(cgDir, "map");
            if (!string.IsNullOrWhiteSpace(mapDir) && Directory.Exists(mapDir))
            {
                string fileName;
                try
                {
                    fileName = Path.GetFileName(rawPath);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    try
                    {
                        var file = new DirectoryInfo(mapDir)
                            .GetFiles(fileName, SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (file != null)
                        {
                            return file;
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // mapPath が壊れている場合はフォールバックへ回す。
                    }
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
            var bytes = ReadNullTerminatedBytes(buffer);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            // 日本語化前の挙動に合わせる。
            return Encoding.Default.GetString(bytes).Trim();
        }

        private static byte[] ReadNullTerminatedBytes(byte[] buffer)
        {
            return buffer.TakeWhile(x => x != 0).ToArray();
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

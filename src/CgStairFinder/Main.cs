using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CgStairFinder
{
    public partial class Main : Form
    {
        public Main()
        {
            InitializeComponent();
        }

        #region dll import
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ShowWindow(IntPtr hwnd, int nCmdShow);
        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(int hProcess, int lpBaseAddress, byte[] lpBuffer, int nSize, int lpNumberOfBytesRead);
        [DllImport("kernel32.dll")]
        private static extern int OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        [DllImport("kernel32.dll")]
        private static extern void CloseHandle(int hObject);
        #endregion

        static readonly IDictionary<string, DetectLog> logs = new Dictionary<string, DetectLog>();
        static readonly Encoding CgMapNameEncoding = Encoding.GetEncoding(950);

        private static byte[] ReadNullTerminatedBytes(byte[] buffer)
        {
            return buffer.TakeWhile(x => x != 0).ToArray();
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

            // Keep map path decoding aligned with pre-localization behavior.
            return Encoding.Default.GetString(bytes).Trim();
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().Trim('\0').Replace('/', '\\');
        }

        private static FileInfo ResolveMapFile(string cgDir, byte[] mapFileBuffer)
        {
            var rawPath = NormalizePath(DecodeMapPath(mapFileBuffer));
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return null;
            }

            var directPath = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.Combine(cgDir, rawPath);
            var directFile = new FileInfo(directPath);
            if (directFile.Exists)
            {
                return directFile;
            }

            var pathWithoutLeadingSlash = rawPath.TrimStart('\\');
            var combinedWithoutLeadingSlash = new FileInfo(Path.Combine(cgDir, pathWithoutLeadingSlash));
            if (combinedWithoutLeadingSlash.Exists)
            {
                return combinedWithoutLeadingSlash;
            }

            var mapDir = Path.Combine(cgDir, "map");
            if (Directory.Exists(mapDir))
            {
                var fileName = Path.GetFileName(rawPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var file = new DirectoryInfo(mapDir)
                        .GetFiles(fileName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (file != null)
                    {
                        return file;
                    }
                }
            }

            return directFile;
        }

        private static FileInfo GetLatestMapFile(string cgDir)
        {
            var mapDir = $@"{cgDir}\map";
            if (!Directory.Exists(mapDir))
            {
                return null;
            }

            return new DirectoryInfo(mapDir)
                .GetFiles("*.dat", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
        }

        private void Main_Load(object sender, EventArgs e)
        {
            SetCgDirDisplayText();
            CgListReload(true);
        }

        private void SetCgDirDisplayText()
        {
            var cgDir = Settings.Default.cgDir;

            if (string.IsNullOrEmpty(cgDir))
            {
                linkLabel2.Text = "ゲームフォルダが未設定です";
                return;
            }

            linkLabel2.Text = cgDir;
            toolTip1.SetToolTip(linkLabel2, linkLabel2.Text);
        }

        private void LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            FolderBrowserDialog folderSelectionDialog = new FolderBrowserDialog
            {
                Description = "ゲームフォルダを選択"
            };

            folderSelectionDialog.ShowDialog();
            Settings.Default.cgDir = folderSelectionDialog.SelectedPath;
            Settings.Default.Save();
            SetCgDirDisplayText();
        }

        private void CgListReload(bool selectFirstItem)
        {
            comboBox1.Items.Clear();
            comboBox1.Items.Add("ウィンドウ未選択");
            comboBox1.Items.AddRange(Process.GetProcessesByName("bluecg"));
            comboBox1.Items.AddRange(Process.GetProcessesByName("bluehd"));
            comboBox1.Items.AddRange(Process.GetProcessesByName("cg"));
            comboBox1.DisplayMember = "MainWindowTitle";
            comboBox1.SelectedIndex = Convert.ToInt32(selectFirstItem && comboBox1.Items.Count > 1);
        }

        /// <summary>
        /// 按下開始偵測按鈕觸發
        /// </summary>
        private void Button1_Click(object sender, EventArgs e)
        {
            if (timer1.Enabled)
            {
                Stop();
                return;
            }

            var cgDir = Settings.Default.cgDir;
            if (string.IsNullOrEmpty(cgDir) ||
                !Directory.Exists($@"{cgDir}\map"))
            {
                MessageBox.Show(this, "起動に失敗しました。パスが正しいか確認してください。", "メッセージ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Start();
        }

        private void Start()
        {
            timer1.Interval = 10;
            timer1.Start();
            button1.Text = "検出を停止";
            button2.Enabled = false;
            comboBox1.Enabled = false;
        }

        private void Stop()
        {
            timer1.Stop();
            label2.ResetText();
            listBox1.Items.Clear();
            button1.Text = "検出を開始";
            button2.Enabled = true;
            comboBox1.Enabled = true;
        }

        /// <summary>
        /// 啟動偵測後, 計時器每次的觸發行為
        /// </summary>
        private void Timer1_Tick(object sender, EventArgs e)
        {
            timer1.Interval = 500;

            int? hProcess = null;
            var mapName = string.Empty; // e.g. 法蘭城
            var mapFile = default(FileInfo);
            var isSelectdWindow = comboBox1.SelectedIndex > 0; // 0 is 不選擇

            try
            {
                // 有選擇視窗
                if (isSelectdWindow)
                {
                    var p = (Process)comboBox1.SelectedItem;
                    if (p.HasExited)
                    {
                        throw new Exception("ウィンドウの検出に失敗しました。");
                    }

                    hProcess = OpenProcess(0x1F0FFF, false, p.Id);

                    // 取地圖名
                    var readMapNameBuffer = new byte[32];
                    ReadProcessMemory(hProcess.Value, 0x95C870, readMapNameBuffer, readMapNameBuffer.Length, 0);
                    mapName = DecodeMapName(readMapNameBuffer);

                    // 取當前地圖檔名
                    var readMapFileBuffer = new byte[32];
                    ReadProcessMemory(hProcess.Value, 0x18CCC8, readMapFileBuffer, readMapFileBuffer.Length, 0);
                    mapFile = ResolveMapFile(Settings.Default.cgDir, readMapFileBuffer);
                    if (mapFile == null || !mapFile.Exists)
                    {
                        // Fallback for clients where current map path address is unavailable.
                        mapFile = GetLatestMapFile(Settings.Default.cgDir);
                    }
                }
                else
                {
                    mapFile = GetLatestMapFile(Settings.Default.cgDir);
                }

                if (mapFile == null || !mapFile.Exists)
                {
                    throw new Exception("マップファイルを読み取れません。");
                }

                Text = string.IsNullOrWhiteSpace(mapName) ? mapFile.Name : mapName;

                label2.Text = mapFile.FullName;
                if (label2.Text.Length > 18)
                {
                    var c = mapFile.FullName.ToCharArray();
                    Array.Reverse(c);
                    Array.Resize(ref c, 16);
                    Array.Reverse(c);
                    label2.Text = $"...{new string(c)}";
                }

                listBox1.Items.Clear();
                var cgStairs = new CgMapStairFinder(mapFile).GetStairs();

                if (cgStairs.Count == 0)
                {
                    listBox1.Items.Add("階段が見つかりませんでした。");
                    return;
                }

                logs[mapFile.Name] = new DetectLog { MapCode = mapFile.Name, MapName = mapName, CgStairs = cgStairs, DetectTime = DateTime.Now };

                foreach (var c in cgStairs)
                {
                    var type = CgStair.Translate(c.Type);
                    if (!isSelectdWindow)
                    {
                        listBox1.Items.Add($"東{c.East}、南{c.South} -- {type}");
                        continue;
                    }

                    // 取當前座標
                    var east = default(int?);
                    var south = default(int?);
                    var buffer = new byte[4];
                    try
                    {
                        ReadProcessMemory(hProcess.Value, 0x95C88C, buffer, 4, 0);
                        east = (int)(BitConverter.ToSingle(buffer, 0) / 64);
                        ReadProcessMemory(hProcess.Value, 0x95C890, buffer, 4, 0);
                        south = (int)BitConverter.ToSingle(buffer, 0) / 64;
                    }
                    catch { /* 吃掉 */ }

                    var direction = string.Empty;
                    if (east.HasValue && south.HasValue)
                    {
                        #region 計算樓梯方向
                        var r = Math.Atan2(c.East - east.Value, c.South - south.Value) / Math.PI * 180;

                        if (r <= -135 + 22.5 && r >= -135 - 22.5)
                        {
                            direction = "←";
                        }
                        if (r <= -90 + 22.5 && r >= -90 - 22.5)
                        {
                            direction = "↙";
                        }
                        if (r <= -45 + 22.5 && r >= -45 - 22.5)
                        {
                            direction = "↓";
                        }
                        if (r <= 0 + 22.5 && r >= 0 - 22.5)
                        {
                            direction = "↘";
                        }
                        if (r <= 45 + 22.5 && r >= 45 - 22.5)
                        {
                            direction = "→";
                        }
                        if (r <= 90 + 22.5 && r >= 90 - 22.5)
                        {
                            direction = "↗";
                        }
                        if (r <= 135 + 22.5 && r >= 135 - 22.5)
                        {
                            direction = "↑";
                        }
                        if (r < -135 - 22.5 || (r <= 180 + 22.5 && r >= 180 - 22.5))
                        {
                            direction = "↖";
                        }
                        #endregion
                    }

                    listBox1.Items.Add($"東{c.East}、南{c.South} {direction} -- {type}");
                }
            }
            catch (IOException) { return; }
            catch (Exception ex)
            {
                Stop();
                MessageBox.Show(this, $"エラーが発生したため自動検出を停止しました。\n\n{ex.Message}", "メッセージ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                if (hProcess.HasValue)
                {
                    CloseHandle(hProcess.Value);
                }
            }
        }

        /// <summary>
        /// list box 顏色
        /// </summary>
        private void ListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index == -1)
            {
                return;
            }

            Brush itemColor = Brushes.White;
            if (((ListBox)sender).Items[e.Index].ToString().Contains(Defined.STAIR_TYPE_UP_DISPLAY_TEXT))
            {
                itemColor = Brushes.SpringGreen;
            }
            else if (((ListBox)sender).Items[e.Index].ToString().Contains(Defined.STAIR_TYPE_DOWN_DISPLAY_TEXT))
            {
                itemColor = Brushes.OrangeRed;
            }
            else if (((ListBox)sender).Items[e.Index].ToString().Contains(Defined.STAIR_TYPE_MOVEABLE_DISPLAY_TEXT))
            {
                itemColor = Brushes.Silver;
            }

            e.Graphics.FillRectangle(itemColor, e.Bounds);
            e.Graphics.DrawString(((ListBox)sender).Items[e.Index].ToString(), Font, Brushes.Black, e.Bounds);
            e.DrawFocusRectangle();
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            CgListReload(false);
        }

        private void Button3_Click(object sender, EventArgs e)
        {
            var process = (Process)comboBox1.SelectedItem;
            if (process is null)
            {
                return;
            }

            ShowWindow(process.MainWindowHandle, 9);
            SetForegroundWindow(process.MainWindowHandle);
        }

        private void Button4_Click(object sender, EventArgs e)
        {
            if (!logs.Any())
            {
                MessageBox.Show("記録がありません。", "メッセージ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var tmpPath = Path.GetTempFileName();
            var sb = new StringBuilder();

            foreach (var kv in logs.OrderBy(x => x.Value.DetectTime))
            {
                var text = $"{kv.Value.DetectTime:yyyy-MM-dd HH:mm:ss} {kv.Key}";
                var recordMapName = kv.Value.MapName;
                if (!string.IsNullOrWhiteSpace(recordMapName))
                {
                    text += $"({recordMapName})";
                }

                text += ": ";
                text += string.Join(" | ", from a in kv.Value.CgStairs
                                           orderby a.Type
                                           select $"{a.East}, {a.South} -- {CgStair.Translate(a.Type)}");
                sb.AppendLine(text);
            }

            File.WriteAllText(tmpPath, sb.ToString(), new UTF8Encoding(true));
            Process.Start("notepad.exe", tmpPath);
        }
    }
}

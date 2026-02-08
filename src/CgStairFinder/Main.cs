using System;
using System.Collections.Generic;
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
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ShowWindow(IntPtr hwnd, int nCmdShow);

        private static readonly IDictionary<string, DetectLog> logs = new Dictionary<string, DetectLog>();
        private static readonly IDictionary<string, DetectLog> sharedLogs = new Dictionary<string, DetectLog>();

        private const float MiniMapZoomMin = 1f;
        private const float MiniMapZoomMax = 8f;
        private const float MiniMapZoomStep = 1.2f;
        private const int MiniMapDragRenderIntervalMs = 16;

        private CgMapStairFinder.CgMapData latestMapData;
        private int? latestEast;
        private int? latestSouth;
        private string latestMapPath;

        private float miniMapZoom = MiniMapZoomMin;
        private float miniMapPanX;
        private float miniMapPanY;
        private bool miniMapDragging;
        private Point miniMapDragStart;
        private float miniMapDragStartPanX;
        private float miniMapDragStartPanY;
        private int miniMapLastDragRenderTick;

        private Button buttonRecenterMap;
        private Label labelMiniMapHint;

        public Main()
        {
            InitializeComponent();
        }

        private void Main_Load(object sender, EventArgs e)
        {
            InitializeMiniMapInteractions();
            EnsureMiniMapHintLabel();
            SetCgDirDisplayText();
            CgListReload(true);
        }

        private void InitializeMiniMapInteractions()
        {
            pictureBoxMap.TabStop = true;
            pictureBoxMap.MouseEnter += PictureBoxMap_MouseEnter;
            pictureBoxMap.MouseClick += PictureBoxMap_MouseClick;
            pictureBoxMap.MouseWheel += PictureBoxMap_MouseWheel;
            pictureBoxMap.MouseDown += PictureBoxMap_MouseDown;
            pictureBoxMap.MouseMove += PictureBoxMap_MouseMove;
            pictureBoxMap.MouseUp += PictureBoxMap_MouseUp;
            pictureBoxMap.MouseLeave += PictureBoxMap_MouseLeave;
            pictureBoxMap.Resize += PictureBoxMap_Resize;

            buttonRecenterMap = new Button
            {
                Text = "\u2316",
                Size = new Size(28, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(30, 64, 175),
                Visible = false,
                TabStop = false
            };
            buttonRecenterMap.Font = new Font("Segoe UI Symbol", 10f, FontStyle.Bold);
            buttonRecenterMap.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
            buttonRecenterMap.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 232, 240);
            buttonRecenterMap.FlatAppearance.MouseDownBackColor = Color.FromArgb(203, 213, 225);
            buttonRecenterMap.Click += ButtonRecenterMap_Click;
            toolTip1.SetToolTip(buttonRecenterMap, "\u73FE\u5728\u5730\u3092\u4E2D\u592E\u306B\u623B\u3059");
            pictureBoxMap.Controls.Add(buttonRecenterMap);
            PositionRecenterButton();
        }

        private void EnsureMiniMapHintLabel()
        {
            if (labelMiniMapHint != null)
            {
                return;
            }

            labelMiniMapHint = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(100, 116, 139),
                Text = "\u30DF\u30CB\u30DE\u30C3\u30D7\u64CD\u4F5C: \u30DB\u30A4\u30FC\u30EB\u3067\u62E1\u5927\u30FB\u7E2E\u5C0F / \u30C9\u30E9\u30C3\u30B0\u3067\u79FB\u52D5",
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 4, 3, 2)
            };
            tableLayoutPanel2.Controls.Add(labelMiniMapHint, 0, 5);
            tableLayoutPanel2.SetColumnSpan(labelMiniMapHint, 2);
        }

        private void ButtonRecenterMap_Click(object sender, EventArgs e)
        {
            miniMapPanX = 0f;
            miniMapPanY = 0f;
            RefreshMiniMap();
        }

        private void PictureBoxMap_MouseEnter(object sender, EventArgs e)
        {
            pictureBoxMap.Focus();
        }

        private void PictureBoxMap_MouseClick(object sender, MouseEventArgs e)
        {
            pictureBoxMap.Focus();
        }

        private void PictureBoxMap_MouseWheel(object sender, MouseEventArgs e)
        {
            if (latestMapData == null || e.Delta == 0)
            {
                return;
            }

            var scale = e.Delta > 0 ? MiniMapZoomStep : 1f / MiniMapZoomStep;
            miniMapZoom = Math.Max(MiniMapZoomMin, Math.Min(MiniMapZoomMax, miniMapZoom * scale));

            if (Math.Abs(miniMapZoom - MiniMapZoomMin) < 0.001f)
            {
                miniMapZoom = MiniMapZoomMin;
                miniMapPanX = 0f;
                miniMapPanY = 0f;
            }

            RefreshMiniMap();
        }

        private void PictureBoxMap_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !CanPanMiniMap())
            {
                return;
            }

            miniMapDragging = true;
            miniMapDragStart = e.Location;
            miniMapDragStartPanX = miniMapPanX;
            miniMapDragStartPanY = miniMapPanY;
            pictureBoxMap.Cursor = Cursors.SizeAll;
        }

        private void PictureBoxMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (!miniMapDragging)
            {
                pictureBoxMap.Cursor = CanPanMiniMap() ? Cursors.Hand : Cursors.Default;
                return;
            }

            var dx = e.X - miniMapDragStart.X;
            var dy = e.Y - miniMapDragStart.Y;
            miniMapPanX = miniMapDragStartPanX + dx;
            miniMapPanY = miniMapDragStartPanY + dy;
            UpdateRecenterButtonVisibility();

            var nowTick = Environment.TickCount;
            if (nowTick - miniMapLastDragRenderTick < MiniMapDragRenderIntervalMs)
            {
                return;
            }

            miniMapLastDragRenderTick = nowTick;
            RefreshMiniMap();
        }

        private void PictureBoxMap_MouseUp(object sender, MouseEventArgs e)
        {
            EndMiniMapDrag();
        }

        private void PictureBoxMap_MouseLeave(object sender, EventArgs e)
        {
            EndMiniMapDrag();
        }

        private void EndMiniMapDrag()
        {
            var wasDragging = miniMapDragging;
            miniMapDragging = false;
            pictureBoxMap.Cursor = CanPanMiniMap() ? Cursors.Hand : Cursors.Default;
            if (wasDragging)
            {
                RefreshMiniMap();
            }
        }

        private bool CanPanMiniMap()
        {
            return latestMapData != null &&
                   miniMapZoom > MiniMapZoomMin + 0.001f &&
                   latestEast.HasValue &&
                   latestSouth.HasValue;
        }

        private void PictureBoxMap_Resize(object sender, EventArgs e)
        {
            PositionRecenterButton();
            RefreshMiniMap();
        }

        private void PositionRecenterButton()
        {
            if (buttonRecenterMap == null)
            {
                return;
            }

            var x = Math.Max(4, pictureBoxMap.ClientSize.Width - buttonRecenterMap.Width - 4);
            buttonRecenterMap.Location = new Point(x, 4);
            buttonRecenterMap.BringToFront();
        }

        private void RefreshMiniMap()
        {
            if (latestMapData == null)
            {
                ClearMiniMap();
                UpdateRecenterButtonVisibility();
                return;
            }

            RenderMiniMap(latestMapData, latestEast, latestSouth);
            UpdateRecenterButtonVisibility();
        }

        private void UpdateRecenterButtonVisibility()
        {
            if (buttonRecenterMap == null)
            {
                return;
            }

            var movedFromCenter = Math.Abs(miniMapPanX) > 0.5f || Math.Abs(miniMapPanY) > 0.5f;
            buttonRecenterMap.Visible =
                latestEast.HasValue &&
                latestSouth.HasValue &&
                miniMapZoom > MiniMapZoomMin + 0.001f &&
                movedFromCenter;
        }

        private void SetCgDirDisplayText()
        {
            var cgDir = Settings.Default.cgDir;

            if (string.IsNullOrEmpty(cgDir))
            {
                linkLabel2.Text = "\u30B2\u30FC\u30E0\u30D5\u30A9\u30EB\u30C0\u304C\u672A\u8A2D\u5B9A\u3067\u3059";
                return;
            }

            linkLabel2.Text = cgDir;
            toolTip1.SetToolTip(linkLabel2, linkLabel2.Text);
        }

        private void LinkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var folderSelectionDialog = new FolderBrowserDialog
            {
                Description = "\u30B2\u30FC\u30E0\u30D5\u30A9\u30EB\u30C0\u3092\u9078\u629E"
            };

            folderSelectionDialog.ShowDialog();
            Settings.Default.cgDir = folderSelectionDialog.SelectedPath;
            Settings.Default.Save();
            SetCgDirDisplayText();
        }

        private void CgListReload(bool selectFirstItem)
        {
            comboBox1.Items.Clear();
            comboBox1.Items.Add("\u30A6\u30A3\u30F3\u30C9\u30A6\u672A\u9078\u629E");
            comboBox1.Items.AddRange(Process.GetProcessesByName("bluecg"));
            comboBox1.Items.AddRange(Process.GetProcessesByName("bluehd"));
            comboBox1.Items.AddRange(Process.GetProcessesByName("cg"));
            comboBox1.DisplayMember = "MainWindowTitle";
            comboBox1.SelectedIndex = Convert.ToInt32(selectFirstItem && comboBox1.Items.Count > 1);
        }

        private void Button1_Click(object sender, EventArgs e)
        {
            if (timer1.Enabled)
            {
                Stop();
                return;
            }

            var cgDir = Settings.Default.cgDir;
            if (string.IsNullOrEmpty(cgDir) || !Directory.Exists(Path.Combine(cgDir, "map")))
            {
                MessageBox.Show(
                    this,
                    "\u8D77\u52D5\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\u30D1\u30B9\u304C\u6B63\u3057\u3044\u304B\u78BA\u8A8D\u3057\u3066\u304F\u3060\u3055\u3044\u3002",
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Start();
        }

        private void Start()
        {
            timer1.Interval = 10;
            timer1.Start();
            button1.Text = "\u691C\u51FA\u3092\u505C\u6B62";
            button2.Enabled = false;
            comboBox1.Enabled = false;
        }

        private void Stop()
        {
            timer1.Stop();
            label2.ResetText();
            listBox1.Items.Clear();
            latestMapData = null;
            latestEast = null;
            latestSouth = null;
            latestMapPath = null;
            miniMapZoom = MiniMapZoomMin;
            miniMapPanX = 0f;
            miniMapPanY = 0f;
            miniMapDragging = false;
            ClearMiniMap();
            UpdateRecenterButtonVisibility();
            button1.Text = "\u691C\u51FA\u3092\u958B\u59CB";
            button2.Enabled = true;
            comboBox1.Enabled = true;
        }

        private void ClearMiniMap()
        {
            var oldImage = pictureBoxMap.Image;
            pictureBoxMap.Image = null;
            oldImage?.Dispose();
        }

        private void RenderMiniMap(CgMapStairFinder.CgMapData mapData, int? east, int? south)
        {
            if (mapData == null || mapData.Width <= 0 || mapData.Height <= 0 ||
                pictureBoxMap.ClientSize.Width <= 0 || pictureBoxMap.ClientSize.Height <= 0)
            {
                ClearMiniMap();
                return;
            }

            var bitmap = MiniMapRenderer.Render(
                pictureBoxMap.ClientSize,
                mapData,
                east,
                south,
                checkBoxShowTerrain.Checked,
                miniMapDragging,
                miniMapZoom,
                latestMapPath ?? string.Empty,
                miniMapPanX,
                miniMapPanY);

            var oldImage = pictureBoxMap.Image;
            pictureBoxMap.Image = bitmap;
            oldImage?.Dispose();
        }

        private static string GetDirection(int east, int south, CgStair stair)
        {
            var r = Math.Atan2(stair.East - east, stair.South - south) / Math.PI * 180;

            if (r <= -157.5 && r >= -180) return "\u2190";
            if (r <= -112.5 && r > -157.5) return "\u2199";
            if (r <= -67.5 && r > -112.5) return "\u2193";
            if (r <= -22.5 && r > -67.5) return "\u2198";
            if (r <= 22.5 && r > -22.5) return "\u2192";
            if (r <= 67.5 && r > 22.5) return "\u2197";
            if (r <= 112.5 && r > 67.5) return "\u2191";
            if (r <= 157.5 && r > 112.5) return "\u2196";
            return "\u2190";
        }

        private void PopulateStairList(
            string mapCode,
            bool isSelectedWindow,
            int? east,
            int? south,
            IList<CgStair> localStairs)
        {
            listBox1.Items.Clear();

            foreach (var stair in localStairs ?? Enumerable.Empty<CgStair>())
            {
                listBox1.Items.Add(BuildStairListText(stair, isSelectedWindow, east, south, false));
            }

            DetectLog sharedLog;
            if (!string.IsNullOrWhiteSpace(mapCode) && sharedLogs.TryGetValue(mapCode, out sharedLog))
            {
                foreach (var stair in sharedLog.CgStairs ?? Enumerable.Empty<CgStair>())
                {
                    listBox1.Items.Add(BuildStairListText(stair, isSelectedWindow, east, south, true));
                }
            }

            if (listBox1.Items.Count == 0)
            {
                listBox1.Items.Add("\u968E\u6BB5\u304C\u898B\u3064\u304B\u308A\u307E\u305B\u3093\u3067\u3057\u305F\u3002");
            }
        }

        private static string BuildStairListText(CgStair stair, bool isSelectedWindow, int? east, int? south, bool isShared)
        {
            var prefix = isShared ? "\uFF0A" : string.Empty;
            var type = CgStair.Translate(stair.Type);
            if (!isSelectedWindow)
            {
                return string.Format("{0}\u6771{1}\u3001\u5357{2} -- {3}", prefix, stair.East, stair.South, type);
            }

            var direction = string.Empty;
            if (east.HasValue && south.HasValue)
            {
                direction = GetDirection(east.Value, south.Value, stair);
            }

            return string.Format("{0}\u6771{1}\u3001\u5357{2} {3} -- {4}", prefix, stair.East, stair.South, direction, type);
        }

        private void RefreshStairListFromLatest()
        {
            if (latestMapData == null && string.IsNullOrWhiteSpace(latestMapPath))
            {
                listBox1.Items.Clear();
                return;
            }

            var mapCode = string.IsNullOrWhiteSpace(latestMapPath) ? null : Path.GetFileName(latestMapPath);
            var localStairs = latestMapData?.Stairs ?? (IList<CgStair>)new List<CgStair>();
            var isSelectedWindow = comboBox1.SelectedIndex > 0;
            PopulateStairList(mapCode, isSelectedWindow, latestEast, latestSouth, localStairs);
        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            timer1.Interval = 500;

            var mapName = string.Empty;
            var mapFile = default(FileInfo);
            var isSelectedWindow = comboBox1.SelectedIndex > 0;
            int? east = null;
            int? south = null;

            try
            {
                if (isSelectedWindow)
                {
                    var process = comboBox1.SelectedItem as Process;
                    if (process == null)
                    {
                        throw new Exception("\u30A6\u30A3\u30F3\u30C9\u30A6\u306E\u691C\u51FA\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002");
                    }

                    CgClientMapSnapshot snapshot;
                    string errorMessage;
                    if (!CgClientReader.TryReadSnapshot(process, Settings.Default.cgDir, out snapshot, out errorMessage))
                    {
                        throw new Exception(errorMessage);
                    }

                    mapName = snapshot.MapName;
                    mapFile = snapshot.MapFile;
                    east = snapshot.East;
                    south = snapshot.South;
                }
                else
                {
                    mapFile = CgClientReader.GetLatestMapFile(Settings.Default.cgDir);
                }

                if (mapFile == null || !mapFile.Exists)
                {
                    throw new Exception("\u30DE\u30C3\u30D7\u30D5\u30A1\u30A4\u30EB\u3092\u8AAD\u307F\u53D6\u308C\u307E\u305B\u3093\u3002");
                }

                Text = string.IsNullOrWhiteSpace(mapName) ? mapFile.Name : mapName;
                SetMapPathLabel(mapFile.FullName);

                var mapData = new CgMapStairFinder(mapFile).GetMapData();
                var currentMapPath = mapFile.FullName;
                if (!string.Equals(latestMapPath, currentMapPath, StringComparison.OrdinalIgnoreCase))
                {
                    latestMapPath = currentMapPath;
                    miniMapPanX = 0f;
                    miniMapPanY = 0f;
                }
                latestMapData = mapData;
                latestEast = east;
                latestSouth = south;
                RefreshMiniMap();

                var cgStairs = mapData.Stairs;
                if (cgStairs.Count > 0)
                {
                    logs[mapFile.Name] = new DetectLog
                    {
                        MapCode = mapFile.Name,
                        MapName = mapName,
                        CgStairs = cgStairs,
                        DetectTime = DateTime.Now
                    };
                }

                PopulateStairList(mapFile.Name, isSelectedWindow, east, south, cgStairs);
            }
            catch (IOException)
            {
                return;
            }
            catch (Exception ex)
            {
                Stop();
                MessageBox.Show(
                    this,
                    string.Format("\u30A8\u30E9\u30FC\u304C\u767A\u751F\u3057\u305F\u305F\u3081\u81EA\u52D5\u691C\u51FA\u3092\u505C\u6B62\u3057\u307E\u3057\u305F\u3002\n\n{0}", ex.Message),
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void SetMapPathLabel(string fullPath)
        {
            label2.Text = fullPath;
            if (label2.Text.Length <= 18)
            {
                return;
            }

            var c = fullPath.ToCharArray();
            Array.Reverse(c);
            Array.Resize(ref c, 16);
            Array.Reverse(c);
            label2.Text = string.Format("...{0}", new string(c));
        }

        private void ListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index == -1)
            {
                return;
            }

            var text = ((ListBox)sender).Items[e.Index].ToString();
            var fillColor = Color.White;
            if (text.Contains(Defined.STAIR_TYPE_UP_DISPLAY_TEXT))
            {
                fillColor = Color.FromArgb(220, 252, 231);
            }
            else if (text.Contains(Defined.STAIR_TYPE_DOWN_DISPLAY_TEXT))
            {
                fillColor = Color.FromArgb(254, 226, 226);
            }
            else if (text.Contains(Defined.STAIR_TYPE_MOVEABLE_DISPLAY_TEXT))
            {
                fillColor = Color.FromArgb(226, 232, 240);
            }

            var rowRect = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 2, e.Bounds.Width - 6, e.Bounds.Height - 4);
            using (var fillBrush = new SolidBrush(fillColor))
            {
                e.Graphics.FillRectangle(fillBrush, rowRect);
            }

            if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
            {
                using (var borderPen = new Pen(Color.FromArgb(59, 130, 246), 1))
                {
                    e.Graphics.DrawRectangle(borderPen, rowRect);
                }
            }

            var textRect = new Rectangle(rowRect.X + 8, rowRect.Y + 2, rowRect.Width - 12, rowRect.Height - 2);
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                textRect,
                Color.FromArgb(15, 23, 42),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void Button2_Click(object sender, EventArgs e)
        {
            CgListReload(false);
        }

        private void Button3_Click(object sender, EventArgs e)
        {
            var process = comboBox1.SelectedItem as Process;
            if (process == null)
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
                MessageBox.Show("\u8A18\u9332\u304C\u3042\u308A\u307E\u305B\u3093\u3002", "\u30E1\u30C3\u30BB\u30FC\u30B8", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var tmpPath = Path.GetTempFileName();
            var sb = new StringBuilder();

            foreach (var kv in logs.OrderBy(x => x.Value.DetectTime))
            {
                var text = string.Format("{0:yyyy-MM-dd HH:mm:ss} {1}", kv.Value.DetectTime, kv.Key);
                var recordMapName = kv.Value.MapName;
                if (!string.IsNullOrWhiteSpace(recordMapName))
                {
                    text += string.Format("({0})", recordMapName);
                }

                text += ": ";
                text += string.Join(
                    " | ",
                    from a in kv.Value.CgStairs
                    orderby a.Type
                    select string.Format("{0}, {1} -- {2}", a.East, a.South, CgStair.Translate(a.Type)));
                sb.AppendLine(text);
            }

            File.WriteAllText(tmpPath, sb.ToString(), new UTF8Encoding(true));
            Process.Start("notepad.exe", tmpPath);
        }

        private void Button5_Click(object sender, EventArgs e)
        {
            var exportLogs = logs.Values
                .Concat(sharedLogs.Values)
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.MapCode))
                .GroupBy(x => x.MapCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.DetectTime).First())
                .ToList();

            if (!exportLogs.Any())
            {
                MessageBox.Show("\u8A18\u9332\u304C\u3042\u308A\u307E\u305B\u3093\u3002", "\u30E1\u30C3\u30BB\u30FC\u30B8", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var json = HistoryShareService.ExportToJson(exportLogs);
                Clipboard.SetText(json);
                MessageBox.Show(
                    this,
                    string.Format("\u5C65\u6B74\u60C5\u5831\u3092\u30AF\u30EA\u30C3\u30D7\u30DC\u30FC\u30C9\u306B\u30B3\u30D4\u30FC\u3057\u307E\u3057\u305F\u3002\n\n\u5BFE\u8C61: {0} \u30DE\u30C3\u30D7", exportLogs.Count),
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    string.Format("\u30AF\u30EA\u30C3\u30D7\u30DC\u30FC\u30C9\u3078\u306E\u30B3\u30D4\u30FC\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\n\n{0}", ex.Message),
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void Button6_Click(object sender, EventArgs e)
        {
            string text;
            if (!TryShowHistoryImportDialog(out text))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(
                    this,
                    "\u8CBC\u308A\u4ED8\u3051\u5185\u5BB9\u304C\u7A7A\u3067\u3059\u3002",
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                var result = HistoryShareService.ImportFromJson(text, sharedLogs);
                RefreshStairListFromLatest();
                MessageBox.Show(
                    this,
                    string.Format(
                        "\u5171\u6709\u30C7\u30FC\u30BF\u3092\u53D6\u8FBC\u307F\u307E\u3057\u305F\u3002\n\n\u5BFE\u8C61: {0} \u4EF6\n\u8FFD\u52A0: {1} \u4EF6\n\u66F4\u65B0: {2} \u4EF6\n\u30B9\u30AD\u30C3\u30D7: {3} \u4EF6\n\u7121\u52B9: {4} \u4EF6",
                        result.TotalEntries,
                        result.Added,
                        result.Updated,
                        result.Skipped,
                        result.Invalid),
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    string.Format("\u53D6\u8FBC\u307F\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\n\n{0}", ex.Message),
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private bool TryShowHistoryImportDialog(out string text)
        {
            text = string.Empty;

            using (var form = new Form())
            using (var label = new Label())
            using (var textBox = new TextBox())
            using (var buttonOk = new Button())
            using (var buttonCancel = new Button())
            {
                form.Text = "\u5171\u6709\u5C65\u6B74\u306E\u53D6\u8FBC\u307F";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.SizableToolWindow;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(560, 380);
                form.MinimumSize = new Size(500, 320);
                form.Font = Font;

                label.AutoSize = false;
                label.Dock = DockStyle.Top;
                label.Height = 42;
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.Padding = new Padding(8, 6, 8, 0);
                label.Text = "\u4ED6\u306E\u4EBA\u304B\u3089\u53D7\u3051\u53D6\u3063\u305F\u5171\u6709\u6587\u5B57\u5217\u3092\u8CBC\u308A\u4ED8\u3051\u3066\u3001\u300C\u53D6\u8FBC\u307F\u300D\u3092\u62BC\u3057\u3066\u304F\u3060\u3055\u3044\u3002";

                textBox.Multiline = true;
                textBox.ScrollBars = ScrollBars.Both;
                textBox.WordWrap = false;
                textBox.Dock = DockStyle.Fill;
                textBox.Font = new Font("Consolas", 9f);

                var buttonPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 42,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8, 6, 8, 6)
                };

                buttonOk.Text = "\u53D6\u8FBC\u307F";
                buttonOk.Width = 88;
                buttonOk.DialogResult = DialogResult.OK;

                buttonCancel.Text = "\u30AD\u30E3\u30F3\u30BB\u30EB";
                buttonCancel.Width = 88;
                buttonCancel.DialogResult = DialogResult.Cancel;

                buttonPanel.Controls.Add(buttonOk);
                buttonPanel.Controls.Add(buttonCancel);

                form.Controls.Add(textBox);
                form.Controls.Add(buttonPanel);
                form.Controls.Add(label);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;

                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                text = textBox.Text;
                return true;
            }
        }

        private void Button7_Click(object sender, EventArgs e)
        {
            if (!sharedLogs.Any())
            {
                MessageBox.Show(
                    this,
                    "\u5171\u6709\u30C7\u30FC\u30BF\u306F\u3042\u308A\u307E\u305B\u3093\u3002",
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var deleted = sharedLogs.Count;
            sharedLogs.Clear();
            RefreshStairListFromLatest();
            MessageBox.Show(
                this,
                string.Format("\u5171\u6709\u30C7\u30FC\u30BF\u3092\u524A\u9664\u3057\u307E\u3057\u305F\u3002\n\n{0} \u30DE\u30C3\u30D7", deleted),
                "\u30E1\u30C3\u30BB\u30FC\u30B8",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void CheckBoxShowTerrain_CheckedChanged(object sender, EventArgs e)
        {
            RefreshMiniMap();
        }
    }
}

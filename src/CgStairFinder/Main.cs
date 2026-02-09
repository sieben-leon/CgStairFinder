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
        private static readonly IDictionary<string, IList<MapPin>> localPins = new Dictionary<string, IList<MapPin>>(StringComparer.OrdinalIgnoreCase);
        private static readonly IDictionary<string, IList<MapPin>> sharedPins = new Dictionary<string, IList<MapPin>>(StringComparer.OrdinalIgnoreCase);

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
        private Button buttonManagePins;
        private Button buttonMiniMapOnly;
        private TableLayoutPanel mapOptionPanel;
        private CheckBox checkBoxGameOrientation;
        private Label labelMiniMapHint;
        private MiniMapOverlayController miniMapOverlayController;

        private enum StairListItemType
        {
            Stair,
            Pin,
            Message
        }

        private sealed class StairListItem
        {
            public StairListItemType ItemType { get; set; }
            public StairType? StairType { get; set; }
            public MapPin Pin { get; set; }
            public string MapCode { get; set; }
            public bool IsShared { get; set; }
            public string Text { get; set; }

            public override string ToString()
            {
                return Text ?? string.Empty;
            }
        }

        public Main()
        {
            InitializeComponent();
        }

        private void Main_Load(object sender, EventArgs e)
        {
            LoadLocalPins();
            InitializeMiniMapInteractions();
            InitializeMiniMapOverlayController();
            EnsurePinManageButton();
            EnsureMiniMapOnlyButton();
            EnsureMapOptionPanel();
            UpdateGameOrientationAvailability();
            EnsureMiniMapHintLabel();
            SetCgDirDisplayText();
            CgListReload(true);
        }

        private void InitializeMiniMapOverlayController()
        {
            if (miniMapOverlayController != null)
            {
                return;
            }

            miniMapOverlayController = new MiniMapOverlayController(listBox1.Font);
            miniMapOverlayController.RestoreRequested += MiniMapOverlayController_RestoreRequested;
            miniMapOverlayController.OverlayClosed += MiniMapOverlayController_OverlayClosed;
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
            tableLayoutPanel2.Controls.Add(labelMiniMapHint, 0, 6);
            tableLayoutPanel2.SetColumnSpan(labelMiniMapHint, 2);
        }

        private void EnsurePinManageButton()
        {
            if (buttonManagePins != null || flowLayoutPanelPin == null)
            {
                return;
            }

            buttonManagePins = new Button
            {
                Text = "ピン管理",
                Size = new Size(74, 32),
                Margin = new Padding(0, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249)
            };
            buttonManagePins.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            buttonManagePins.Click += ButtonManagePins_Click;
            flowLayoutPanelPin.Controls.Add(buttonManagePins);
        }

        private void EnsureMiniMapOnlyButton()
        {
            if (flowLayoutPanel2 == null || button1 == null)
            {
                return;
            }

            if (buttonMiniMapOnly == null)
            {
                buttonMiniMapOnly = new Button
                {
                    Text = "ミニマムビュー",
                    Size = new Size(118, 32),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(241, 245, 249),
                    Margin = new Padding(3, 3, 0, 0)
                };
                buttonMiniMapOnly.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
                buttonMiniMapOnly.Click += ButtonMiniMapOnly_Click;
            }

            button1.Margin = new Padding(3, 3, 0, 3);
            flowLayoutPanel2.SuspendLayout();
            flowLayoutPanel2.Controls.Clear();
            flowLayoutPanel2.FlowDirection = FlowDirection.RightToLeft;
            flowLayoutPanel2.WrapContents = true;
            flowLayoutPanel2.Controls.Add(buttonMiniMapOnly);
            flowLayoutPanel2.Controls.Add(button1);
            flowLayoutPanel2.SetFlowBreak(buttonMiniMapOnly, true);
            flowLayoutPanel2.ResumeLayout();
        }

        private void ButtonMiniMapOnly_Click(object sender, EventArgs e)
        {
            ToggleMiniMapOverlayMode();
        }

        private bool IsMiniMapOverlayActive()
        {
            return miniMapOverlayController != null && miniMapOverlayController.IsActive;
        }

        private void ToggleMiniMapOverlayMode()
        {
            if (IsMiniMapOverlayActive())
            {
                DisableMiniMapOverlayMode();
                return;
            }

            EnableMiniMapOverlayMode();
        }

        private void EnableMiniMapOverlayMode()
        {
            InitializeMiniMapOverlayController();
            miniMapOverlayController.Show(pictureBoxMap);
            pictureBoxMap.Visible = false;
            SyncMiniMapOverlayListFromMain();
            RefreshMiniMap();
            Hide();
        }

        private void DisableMiniMapOverlayMode()
        {
            if (miniMapOverlayController != null)
            {
                miniMapOverlayController.Hide();
            }

            pictureBoxMap.Visible = true;
            if (!Visible)
            {
                Show();
            }

            Activate();
            RefreshMiniMap();
        }

        private void MiniMapOverlayController_RestoreRequested(object sender, EventArgs e)
        {
            DisableMiniMapOverlayMode();
        }

        private void MiniMapOverlayController_OverlayClosed(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            pictureBoxMap.Visible = true;
            if (!Visible)
            {
                Show();
                Activate();
            }

            RefreshMiniMap();
        }

        private void EnsureMapOptionPanel()
        {
            if (mapOptionPanel != null || flowLayoutPanel1 == null || checkBoxShowTerrain == null)
            {
                return;
            }

            mapOptionPanel = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(6, 0, 0, 0),
                Padding = new Padding(0)
            };
            mapOptionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mapOptionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mapOptionPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            checkBoxGameOrientation = new CheckBox
            {
                AutoSize = true,
                Text = "ゲーム内向き",
                Checked = false,
                Margin = new Padding(0, 0, 0, 0),
                UseVisualStyleBackColor = true
            };
            checkBoxGameOrientation.CheckedChanged += CheckBoxGameOrientation_CheckedChanged;

            flowLayoutPanel1.Controls.Remove(checkBoxShowTerrain);
            checkBoxShowTerrain.Margin = new Padding(0, 7, 0, 0);

            mapOptionPanel.Controls.Add(checkBoxShowTerrain, 0, 0);
            mapOptionPanel.Controls.Add(checkBoxGameOrientation, 0, 1);
            flowLayoutPanel1.Controls.Add(mapOptionPanel);
        }

        private void UpdateGameOrientationAvailability()
        {
            if (checkBoxGameOrientation == null || checkBoxShowTerrain == null)
            {
                return;
            }

            var enabled = checkBoxShowTerrain.Checked;
            checkBoxGameOrientation.Enabled = enabled;
            if (!enabled && checkBoxGameOrientation.Checked)
            {
                checkBoxGameOrientation.Checked = false;
            }
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
                !IsMiniMapOverlayActive() &&
                latestEast.HasValue &&
                latestSouth.HasValue &&
                miniMapZoom > MiniMapZoomMin + 0.001f &&
                movedFromCenter;
        }

        private void LoadLocalPins()
        {
            localPins.Clear();
            foreach (var entry in LocalPinStore.Load())
            {
                localPins[entry.Key] = entry.Value;
            }
        }

        private void SaveLocalPins()
        {
            LocalPinStore.Save(localPins);
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
            SyncMiniMapOverlayListFromMain();
            latestMapData = null;
            latestEast = null;
            latestSouth = null;
            latestMapPath = null;
            miniMapZoom = MiniMapZoomMin;
            miniMapPanX = 0f;
            miniMapPanY = 0f;
            miniMapDragging = false;
            DisableMiniMapOverlayMode();
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

            miniMapOverlayController?.ClearImage();
        }

        private void RenderMiniMap(CgMapStairFinder.CgMapData mapData, int? east, int? south)
        {
            var overlayPictureBox = miniMapOverlayController == null ? null : miniMapOverlayController.ActivePictureBox;
            var targetPictureBox = overlayPictureBox ?? pictureBoxMap;
            if (targetPictureBox == null || targetPictureBox.IsDisposed)
            {
                targetPictureBox = pictureBoxMap;
            }

            if (mapData == null || mapData.Width <= 0 || mapData.Height <= 0 ||
                targetPictureBox.ClientSize.Width <= 0 || targetPictureBox.ClientSize.Height <= 0)
            {
                ClearMiniMap();
                return;
            }

            var miniMapPins = GetCurrentMapPinsForMiniMap();
            var bitmap = MiniMapRenderer.Render(
                targetPictureBox.ClientSize,
                mapData,
                east,
                south,
                miniMapPins,
                checkBoxGameOrientation != null && checkBoxGameOrientation.Checked,
                checkBoxShowTerrain.Checked,
                miniMapDragging,
                miniMapZoom,
                latestMapPath ?? string.Empty,
                miniMapPanX,
                miniMapPanY);

            var oldImage = targetPictureBox.Image;
            targetPictureBox.Image = bitmap;
            oldImage?.Dispose();

            if (ReferenceEquals(targetPictureBox, pictureBoxMap))
            {
                pictureBoxMap.Visible = true;
                miniMapOverlayController?.ClearImage();
            }
            else
            {
                pictureBoxMap.Visible = false;
                oldImage = pictureBoxMap.Image;
                pictureBoxMap.Image = null;
                oldImage?.Dispose();
            }
        }

        private static string GetDirection(int east, int south, CgStair stair)
        {
            // CG's map axes are visually rotated on screen, so rotate by -45 deg
            // to make "east" point to ↗ in the stair list.
            var r = Math.Atan2(stair.East - east, stair.South - south) / Math.PI * 180 - 45;
            if (r <= -180)
            {
                r += 360;
            }
            else if (r > 180)
            {
                r -= 360;
            }

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
                listBox1.Items.Add(new StairListItem
                {
                    ItemType = StairListItemType.Stair,
                    StairType = stair.Type,
                    MapCode = mapCode,
                    IsShared = false,
                    Text = BuildStairListText(stair, isSelectedWindow, east, south, false)
                });
            }

            DetectLog sharedLog;
            if (!string.IsNullOrWhiteSpace(mapCode) && sharedLogs.TryGetValue(mapCode, out sharedLog))
            {
                foreach (var stair in sharedLog.CgStairs ?? Enumerable.Empty<CgStair>())
                {
                    listBox1.Items.Add(new StairListItem
                    {
                        ItemType = StairListItemType.Stair,
                        StairType = stair.Type,
                        MapCode = mapCode,
                        IsShared = true,
                        Text = BuildStairListText(stair, isSelectedWindow, east, south, true)
                    });
                }
            }

            foreach (var pin in MapPinCollectionService.GetMapPins(localPins, mapCode).OrderBy(x => x.East).ThenBy(x => x.South).ThenBy(x => x.Title))
            {
                listBox1.Items.Add(new StairListItem
                {
                    ItemType = StairListItemType.Pin,
                    Pin = pin,
                    MapCode = mapCode,
                    IsShared = false,
                    Text = BuildPinListText(pin, isSelectedWindow, east, south, false)
                });
            }

            foreach (var pin in MapPinCollectionService.GetMapPins(sharedPins, mapCode).OrderBy(x => x.East).ThenBy(x => x.South).ThenBy(x => x.Title))
            {
                listBox1.Items.Add(new StairListItem
                {
                    ItemType = StairListItemType.Pin,
                    Pin = pin,
                    MapCode = mapCode,
                    IsShared = true,
                    Text = BuildPinListText(pin, isSelectedWindow, east, south, true)
                });
            }

            if (listBox1.Items.Count == 0)
            {
                listBox1.Items.Add(new StairListItem
                {
                    ItemType = StairListItemType.Message,
                    Text = "\u968E\u6BB5\u30FB\u30D4\u30F3\u304C\u898B\u3064\u304B\u308A\u307E\u305B\u3093\u3067\u3057\u305F\u3002"
                });
            }

            SyncMiniMapOverlayListFromMain();
        }

        private void SyncMiniMapOverlayListFromMain()
        {
            miniMapOverlayController?.SyncList(listBox1.Items);
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

        private static string BuildPinListText(MapPin pin, bool isSelectedWindow, int? east, int? south, bool isShared)
        {
            if (!isSelectedWindow || !east.HasValue || !south.HasValue)
            {
                return MapPinCollectionService.BuildPinListText(pin, isShared);
            }

            var prefix = isShared ? "\uFF0A" : string.Empty;
            var direction = GetDirection(east.Value, south.Value, pin.East, pin.South);
            return string.Format("{0}\u6771{1}\u3001\u5357{2} {3} -- {4}", prefix, pin.East, pin.South, direction, pin.Title);
        }

        private static string GetDirection(int east, int south, int targetEast, int targetSouth)
        {
            // CG's map axes are visually rotated on screen, so rotate by -45 deg
            // to make "east" point to ↗ in the stair list.
            var r = Math.Atan2(targetEast - east, targetSouth - south) / Math.PI * 180 - 45;
            if (r <= -180)
            {
                r += 360;
            }
            else if (r > 180)
            {
                r -= 360;
            }

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

        private IEnumerable<MapPin> GetCurrentMapPinsForMiniMap()
        {
            var mapCode = string.IsNullOrWhiteSpace(latestMapPath) ? null : Path.GetFileName(latestMapPath);
            if (string.IsNullOrWhiteSpace(mapCode))
            {
                return Enumerable.Empty<MapPin>();
            }

            return MapPinCollectionService.GetMapPins(localPins, mapCode)
                .Concat(MapPinCollectionService.GetMapPins(sharedPins, mapCode))
                .Where(x => x != null)
                .GroupBy(x => new { x.East, x.South, x.Title })
                .Select(x => x.First())
                .ToList();
        }

        private void RefreshStairListFromLatest()
        {
            if (latestMapData == null && string.IsNullOrWhiteSpace(latestMapPath))
            {
                listBox1.Items.Clear();
                SyncMiniMapOverlayListFromMain();
                return;
            }

            var mapCode = string.IsNullOrWhiteSpace(latestMapPath) ? null : Path.GetFileName(latestMapPath);
            var localStairs = latestMapData?.Stairs ?? (IList<CgStair>)new List<CgStair>();
            var isSelectedWindow = comboBox1.SelectedIndex > 0;
            PopulateStairList(mapCode, isSelectedWindow, latestEast, latestSouth, localStairs);
        }

        private string ResolveMapNameForCurrentMap(string currentMapCode, string snapshotMapName, string previousMapCode, bool mapChanged, bool isSelectedWindow)
        {
            var normalized = (snapshotMapName ?? string.Empty).Trim();
            if (!isSelectedWindow || !mapChanged || string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            var previousMapName = GetKnownMapName(previousMapCode);
            if (!string.IsNullOrWhiteSpace(previousMapName) &&
                string.Equals(normalized, previousMapName, StringComparison.Ordinal))
            {
                var currentKnownName = GetKnownMapName(currentMapCode);
                return string.IsNullOrWhiteSpace(currentKnownName) ? string.Empty : currentKnownName;
            }

            return normalized;
        }

        private static string ChooseStoredMapName(string preferredMapName, DetectLog existingLog)
        {
            var normalized = (preferredMapName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }

            if (existingLog != null && !string.IsNullOrWhiteSpace(existingLog.MapName))
            {
                return existingLog.MapName;
            }

            return string.Empty;
        }

        private static string GetKnownMapName(string mapCode)
        {
            if (string.IsNullOrWhiteSpace(mapCode))
            {
                return string.Empty;
            }

            DetectLog knownLog;
            if (logs.TryGetValue(mapCode, out knownLog) && !string.IsNullOrWhiteSpace(knownLog.MapName))
            {
                return knownLog.MapName;
            }

            if (sharedLogs.TryGetValue(mapCode, out knownLog) && !string.IsNullOrWhiteSpace(knownLog.MapName))
            {
                return knownLog.MapName;
            }

            return string.Empty;
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
                    if ((mapFile == null || !mapFile.Exists) && !string.IsNullOrWhiteSpace(latestMapPath))
                    {
                        var fallbackFile = new FileInfo(latestMapPath);
                        if (fallbackFile.Exists)
                        {
                            mapFile = fallbackFile;
                        }
                    }
                    east = snapshot.East;
                    south = snapshot.South;
                }
                else
                {
                    mapFile = CgClientReader.GetLatestMapFile(Settings.Default.cgDir);
                }

                if (mapFile == null || !mapFile.Exists)
                {
                    if (isSelectedWindow)
                    {
                        if (string.IsNullOrWhiteSpace(latestMapPath))
                        {
                            var initialFallback = CgClientReader.GetLatestMapFile(Settings.Default.cgDir);
                            if (initialFallback != null && initialFallback.Exists)
                            {
                                mapFile = initialFallback;
                            }
                            else
                            {
                                return;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        throw new Exception("\u30DE\u30C3\u30D7\u30D5\u30A1\u30A4\u30EB\u3092\u8AAD\u307F\u53D6\u308C\u307E\u305B\u3093\u3002");
                    }
                }

                var currentMapCode = mapFile.Name;
                var currentMapPath = mapFile.FullName;
                var previousMapCode = string.IsNullOrWhiteSpace(latestMapPath) ? null : Path.GetFileName(latestMapPath);
                var mapChanged = !string.Equals(latestMapPath, currentMapPath, StringComparison.OrdinalIgnoreCase);

                mapName = ResolveMapNameForCurrentMap(currentMapCode, mapName, previousMapCode, mapChanged, isSelectedWindow);
                Text = string.IsNullOrWhiteSpace(mapName) ? currentMapCode : mapName;
                SetMapPathLabel(currentMapPath);

                var mapData = new CgMapStairFinder(mapFile).GetMapData();
                if (mapChanged)
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
                    DetectLog existingLog;
                    logs.TryGetValue(currentMapCode, out existingLog);
                    logs[currentMapCode] = new DetectLog
                    {
                        MapCode = currentMapCode,
                        MapRelativePath = GetRelativeMapPath(mapFile),
                        MapName = ChooseStoredMapName(mapName, existingLog),
                        CgStairs = cgStairs,
                        DetectTime = DateTime.Now
                    };
                }

                PopulateStairList(currentMapCode, isSelectedWindow, east, south, cgStairs);
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

            var item = ((ListBox)sender).Items[e.Index] as StairListItem;
            var text = item?.ToString() ?? ((ListBox)sender).Items[e.Index].ToString();
            var fillColor = GetListItemFillColor(item);

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

        private static Color GetListItemFillColor(StairListItem item)
        {
            if (item == null)
            {
                return Color.White;
            }

            if (item.ItemType == StairListItemType.Pin)
            {
                return Color.FromArgb(243, 232, 255);
            }

            if (item.ItemType != StairListItemType.Stair || !item.StairType.HasValue)
            {
                return Color.White;
            }

            switch (item.StairType.Value)
            {
                case StairType.Up:
                    return Color.FromArgb(220, 252, 231);
                case StairType.Down:
                    return Color.FromArgb(254, 226, 226);
                case StairType.Jump:
                    return Color.FromArgb(226, 232, 240);
                default:
                    return Color.White;
            }
        }

        private void ListBox_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            var index = listBox1.IndexFromPoint(e.Location);
            if (index < 0 || index >= listBox1.Items.Count)
            {
                return;
            }

            var item = listBox1.Items[index] as StairListItem;
            if (item?.ItemType != StairListItemType.Pin || item.Pin == null)
            {
                return;
            }

            ShowPinDetailDialog(item.MapCode, item.Pin, item.IsShared);
        }

        private void ShowPinDetailDialog(string mapCode, MapPin pin, bool isShared)
        {
            if (pin == null)
            {
                return;
            }

            var action = PinDetailDialog.Show(this, Font, pin, isShared);
            if (action == PinDetailDialogAction.SaveLocal)
            {
                var normalizedMapCode = MapPinCollectionService.NormalizeMapCode(mapCode);
                if (string.IsNullOrWhiteSpace(normalizedMapCode))
                {
                    MessageBox.Show(
                        this,
                        "マップ情報が取得できないため、ローカル保存できませんでした。",
                        "メッセージ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                var normalizedPin = MapPin.Normalize(pin);
                if (normalizedPin == null)
                {
                    MessageBox.Show(
                        this,
                        "ピン情報が不正なため、ローカル保存できませんでした。",
                        "メッセージ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                MapPinCollectionService.AddOrUpdatePin(localPins, normalizedMapCode, normalizedPin);
                SaveLocalPins();
                RefreshStairListFromLatest();
                RefreshMiniMap();
                return;
            }

            if (action != PinDetailDialogAction.Delete)
            {
                return;
            }

            if (!TryDeletePin(mapCode, pin, isShared))
            {
                MessageBox.Show(
                    this,
                    "ピンを削除できませんでした。",
                    "メッセージ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!isShared)
            {
                SaveLocalPins();
            }

            RefreshStairListFromLatest();
            RefreshMiniMap();
        }

        private bool TryDeletePin(string mapCode, MapPin pin, bool isShared)
        {
            mapCode = MapPinCollectionService.NormalizeMapCode(mapCode);
            var source = isShared ? sharedPins : localPins;
            return MapPinCollectionService.RemovePin(source, mapCode, pin);
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

        private void Button8_Click(object sender, EventArgs e)
        {
            string mapCode;
            int initialEast;
            int initialSouth;
            if (!TryGetCurrentMapContext(out mapCode, out initialEast, out initialSouth))
            {
                mapCode = string.IsNullOrWhiteSpace(latestMapPath) ? null : Path.GetFileName(latestMapPath);
                initialEast = latestEast ?? 0;
                initialSouth = latestSouth ?? 0;
            }

            mapCode = MapPinCollectionService.NormalizeMapCode(mapCode);

            if (string.IsNullOrWhiteSpace(mapCode))
            {
                MessageBox.Show(
                    this,
                    "現在のマップを取得できません。検出開始後に再度お試しください。",
                    "メッセージ",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            MapPin newPin;
            if (!PinCreateDialog.TryShow(this, Font, mapCode, initialEast, initialSouth, out newPin))
            {
                return;
            }

            MapPinCollectionService.AddOrUpdatePin(localPins, mapCode, newPin);
            SaveLocalPins();
            RefreshStairListFromLatest();
        }

        private void ButtonManagePins_Click(object sender, EventArgs e)
        {
            ShowPinManagerDialog();
        }

        private void ShowPinManagerDialog()
        {
            using (var form = new Form())
            using (var table = new TableLayoutPanel())
            using (var list = new ListBox())
            using (var buttonPanel = new FlowLayoutPanel())
            using (var buttonDelete = new Button())
            using (var buttonClose = new Button())
            {
                form.Text = "ピン管理";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.SizableToolWindow;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(430, 320);
                form.Font = Font;

                table.Dock = DockStyle.Fill;
                table.Padding = new Padding(10);
                table.ColumnCount = 1;
                table.RowCount = 2;
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                table.RowStyles.Add(new RowStyle());

                list.Dock = DockStyle.Fill;
                list.IntegralHeight = false;
                table.Controls.Add(list, 0, 0);

                buttonPanel.Dock = DockStyle.Fill;
                buttonPanel.FlowDirection = FlowDirection.RightToLeft;
                buttonPanel.WrapContents = false;

                buttonClose.Text = "閉じる";
                buttonClose.Width = 88;
                buttonClose.DialogResult = DialogResult.Cancel;

                buttonDelete.Text = "削除";
                buttonDelete.Width = 88;
                buttonDelete.Click += (s, e) =>
                {
                    var selected = list.SelectedItem as LocalPinManagerItem;
                    if (selected == null || selected.Pin == null || string.IsNullOrWhiteSpace(selected.MapCode))
                    {
                        return;
                    }

                    if (!MapPinCollectionService.RemovePin(localPins, selected.MapCode, selected.Pin))
                    {
                        MessageBox.Show(
                            form,
                            "選択したピンを削除できませんでした。",
                            "メッセージ",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        return;
                    }

                    SaveLocalPins();
                    ReloadPinManagerList(list, buttonDelete);
                    RefreshStairListFromLatest();
                    RefreshMiniMap();
                };

                buttonPanel.Controls.Add(buttonClose);
                buttonPanel.Controls.Add(buttonDelete);
                table.Controls.Add(buttonPanel, 0, 1);

                form.Controls.Add(table);
                form.CancelButton = buttonClose;

                ReloadPinManagerList(list, buttonDelete);
                form.ShowDialog(this);
            }
        }

        private void ReloadPinManagerList(ListBox list, Control deleteButton)
        {
            if (list == null)
            {
                return;
            }

            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var item in GetLocalPinManagerItems())
                {
                    list.Items.Add(item);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            if (deleteButton != null)
            {
                deleteButton.Enabled = list.Items.Count > 0;
            }
        }

        private IEnumerable<LocalPinManagerItem> GetLocalPinManagerItems()
        {
            return MapPinCollectionService.BuildLocalPinManagerItems(localPins);
        }

        private bool TryGetCurrentMapContext(out string mapCode, out int east, out int south)
        {
            mapCode = null;
            east = 0;
            south = 0;

            if (comboBox1.SelectedIndex > 0)
            {
                var process = comboBox1.SelectedItem as Process;
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            CgClientMapSnapshot snapshot;
                            string errorMessage;
                            if (CgClientReader.TryReadSnapshot(process, Settings.Default.cgDir, out snapshot, out errorMessage))
                            {
                                east = snapshot.East;
                                south = snapshot.South;
                                mapCode = snapshot.MapFile?.Name;
                                if (!string.IsNullOrWhiteSpace(mapCode))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(latestMapPath))
            {
                mapCode = Path.GetFileName(latestMapPath);
            }

            if (latestEast.HasValue)
            {
                east = latestEast.Value;
            }

            if (latestSouth.HasValue)
            {
                south = latestSouth.Value;
            }

            return !string.IsNullOrWhiteSpace(mapCode) && latestEast.HasValue && latestSouth.HasValue;
        }

        private void Button5_Click(object sender, EventArgs e)
        {
            var candidates = BuildShareCandidates();
            if (!candidates.Any())
            {
                MessageBox.Show("\u8A18\u9332\u304C\u3042\u308A\u307E\u305B\u3093\u3002", "\u30E1\u30C3\u30BB\u30FC\u30B8", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ShareSelectionResult selection;
            if (!ShareSelectionDialog.TryShow(this, Font, candidates, out selection))
            {
                return;
            }

            var selectedLogs = selection.SelectedLogs ?? new List<DetectLog>();
            var includeMapFiles = selection.IncludeMapFiles;
            var includePins = selection.IncludePins;
            if (!selectedLogs.Any())
            {
                MessageBox.Show(
                    this,
                    "\u5171\u6709\u5BFE\u8C61\u304C\u9078\u629E\u3055\u308C\u3066\u3044\u307E\u305B\u3093\u3002",
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "\u5171\u6709\u30C7\u30FC\u30BF\u306E\u51FA\u529B";
                dialog.Filter = "\u5171\u6709\u30D5\u30A1\u30A4\u30EB (*.cgshare)|*.cgshare|JSON (*.json)|*.json|All files (*.*)|*.*";
                dialog.DefaultExt = "cgshare";
                dialog.AddExtension = true;
                dialog.FileName = string.Format("cgshare_{0:yyyyMMdd_HHmmss}.cgshare", DateTime.Now);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var exportResult = HistoryShareService.ExportCompressed(
                        dialog.FileName,
                        selectedLogs,
                        includeMapFiles,
                        TryLoadMapDatFile,
                        includePins,
                        TryLoadPinsForShare);

                    var detail = string.Format(
                        "共有データを出力しました。\n\n対象: {0} マップ\n{1}",
                        exportResult.ExportedEntries,
                        dialog.FileName);
                    if (includeMapFiles)
                    {
                        detail += string.Format(
                            "\n\n.dat 同梱: {0} 件\n.dat 未検出: {1} 件",
                            exportResult.ExportedMapFiles,
                            exportResult.MissingMapFiles);
                    }

                    if (includePins)
                    {
                        detail += "\n\nピン情報を同梱しました。";
                    }

                    MessageBox.Show(
                        this,
                        detail,
                        "\u30E1\u30C3\u30BB\u30FC\u30B8",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        string.Format("\u5171\u6709\u30C7\u30FC\u30BF\u306E\u51FA\u529B\u306B\u5931\u6557\u3057\u307E\u3057\u305F\u3002\n\n{0}", ex.Message),
                        "\u30E1\u30C3\u30BB\u30FC\u30B8",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void Button6_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "共有データの取込";
                dialog.Filter = "共有ファイル (*.cgshare;*.json)|*.cgshare;*.json|All files (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    bool overwriteExistingMapFiles;
                    if (!TryAskMapFileOverwriteOption(out overwriteExistingMapFiles))
                    {
                        return;
                    }

                    var wasRunning = timer1.Enabled;
                    if (wasRunning)
                    {
                        timer1.Stop();
                    }

                    var currentMapPathBeforeImport = GetCurrentMapPathBeforeImport();
                    HistoryShareService.ImportResult result;
                    try
                    {
                        result = HistoryShareService.ImportCompressed(
                            dialog.FileName,
                            sharedLogs,
                            sharedPins,
                            GetMapDirectoryPath(),
                            overwriteExistingMapFiles);
                    }
                    finally
                    {
                        if (wasRunning)
                        {
                            timer1.Start();
                        }
                    }

                    var touchedCurrentMap = TryTouchMapFileTimestamp(currentMapPathBeforeImport);
                    RefreshStairListFromLatest();
                    var detail = string.Format(
                        "共有データを取込しました。\n\n対象: {0} 件\n追加: {1} 件\n更新: {2} 件\nスキップ: {3} 件\n無効: {4} 件",
                        result.TotalEntries,
                        result.Added,
                        result.Updated,
                        result.Skipped,
                        result.Invalid);

                    if (result.TotalMapFiles > 0)
                    {
                        detail += string.Format(
                            "\n\n.dat 同梱: {0} 件\n.dat 新規配置: {1} 件\n.dat 上書き: {2} 件\n.dat 既存スキップ: {3} 件\n.dat 無効: {4} 件\n.dat 失敗: {5} 件",
                            result.TotalMapFiles,
                            result.ImportedMapFiles,
                            result.OverwrittenMapFiles,
                            result.SkippedMapFiles,
                            result.InvalidMapFiles,
                            result.FailedMapFiles);
                    }

                    if (result.PinMaps > 0)
                    {
                        detail += string.Format(
                            "\n\n共有ピン: {0} マップ / {1} 件",
                            result.PinMaps,
                            result.ImportedPins);
                    }

                    if (touchedCurrentMap)
                    {
                        detail += "\n\n現在マップの .dat 更新日時を更新し、最新判定が切り替わらないようにしました。";
                    }

                    MessageBox.Show(
                        this,
                        detail,
                        "メッセージ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        string.Format("取込に失敗しました。\n\n{0}", ex.Message),
                        "メッセージ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private List<ShareLogItem> BuildShareCandidates()
        {
            var result = new List<ShareLogItem>();
            var latestLocal = logs.Values
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.MapCode))
                .GroupBy(x => x.MapCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.DetectTime).First());
            result.AddRange(latestLocal.Select(x => new ShareLogItem { Log = x, IsSharedSource = false }));

            foreach (var shared in sharedLogs.Values.Where(x => x != null && !string.IsNullOrWhiteSpace(x.MapCode)))
            {
                if (result.Any(x => string.Equals(x.Log.MapCode, shared.MapCode, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.Add(new ShareLogItem { Log = shared, IsSharedSource = true });
            }

            return result
                .OrderBy(x => x.Log.MapCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string GetMapDirectoryPath()
        {
            var cgDir = Settings.Default.cgDir;
            if (string.IsNullOrWhiteSpace(cgDir))
            {
                return null;
            }

            try
            {
                return Path.Combine(cgDir, "map");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        private bool TryAskMapFileOverwriteOption(out bool overwriteExistingMapFiles)
        {
            overwriteExistingMapFiles = false;
            var choice = MessageBox.Show(
                this,
                ".dat 同梱データを含む場合、既存の同名 .dat を上書きしますか？\n\nはい: 上書きする\nいいえ: 上書きしない\nキャンセル: 取込中止",
                "取込オプション",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
            {
                return false;
            }

            overwriteExistingMapFiles = choice == DialogResult.Yes;
            return true;
        }

        private static string NormalizeRelativeMapPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            var normalized = relativePath.Trim().Replace('/', '\\').TrimStart('\\');
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

            var parts = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToArray();
            if (parts.Length == 0 || parts.Any(x => x == "." || x == ".."))
            {
                return null;
            }

            if (parts.Any(x => x.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                return null;
            }

            return string.Join("\\", parts);
        }

        private string GetRelativeMapPath(FileInfo mapFile)
        {
            if (mapFile == null)
            {
                return string.Empty;
            }

            var mapDir = GetMapDirectoryPath();
            if (string.IsNullOrWhiteSpace(mapDir))
            {
                return mapFile.Name;
            }

            try
            {
                var root = Path.GetFullPath(mapDir).TrimEnd('\\') + "\\";
                var full = Path.GetFullPath(mapFile.FullName);
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeRelativeMapPath(full.Substring(root.Length)) ?? mapFile.Name;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
            }

            return mapFile.Name;
        }

        private string GetCurrentMapPathBeforeImport()
        {
            if (!string.IsNullOrWhiteSpace(latestMapPath))
            {
                try
                {
                    if (File.Exists(latestMapPath))
                    {
                        return latestMapPath;
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                }
            }

            if (comboBox1.SelectedIndex > 0)
            {
                var process = comboBox1.SelectedItem as Process;
                if (process != null && !process.HasExited)
                {
                    CgClientMapSnapshot snapshot;
                    string errorMessage;
                    if (CgClientReader.TryReadSnapshot(process, Settings.Default.cgDir, out snapshot, out errorMessage))
                    {
                        var mapFile = snapshot?.MapFile;
                        if (mapFile != null)
                        {
                            try
                            {
                                if (mapFile.Exists)
                                {
                                    return mapFile.FullName;
                                }
                            }
                            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                            {
                            }
                        }
                    }
                }
            }

            return null;
        }

        private bool TryTouchMapFileTimestamp(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            try
            {
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow);
                return true;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException ||
                ex is PathTooLongException)
            {
                return false;
            }
        }

        private HistoryShareService.MapFileExportItem TryLoadMapDatFile(DetectLog log)
        {
            if (log == null)
            {
                return null;
            }

            var mapDir = GetMapDirectoryPath();
            if (string.IsNullOrWhiteSpace(mapDir) || !Directory.Exists(mapDir))
            {
                return null;
            }

            var relativePath = NormalizeRelativeMapPath(log.MapRelativePath);
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                try
                {
                    var preferredPath = Path.Combine(mapDir, relativePath);
                    if (File.Exists(preferredPath))
                    {
                        return new HistoryShareService.MapFileExportItem
                        {
                            RelativePath = relativePath,
                            Data = File.ReadAllBytes(preferredPath)
                        };
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                }
            }

            var fileName = NormalizeRelativeMapPath(log.MapCode);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            try
            {
                var match = new DirectoryInfo(mapDir)
                    .GetFiles(Path.GetFileName(fileName), SearchOption.AllDirectories)
                    .OrderByDescending(x => x.LastWriteTime)
                    .FirstOrDefault();
                if (match == null || !match.Exists)
                {
                    return null;
                }

                return new HistoryShareService.MapFileExportItem
                {
                    RelativePath = GetRelativeMapPath(match),
                    Data = File.ReadAllBytes(match.FullName)
                };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        private IEnumerable<MapPin> TryLoadPinsForShare(string mapCode)
        {
            if (string.IsNullOrWhiteSpace(mapCode))
            {
                return Enumerable.Empty<MapPin>();
            }

            var result = new List<MapPin>();
            result.AddRange(MapPinCollectionService.GetMapPins(localPins, mapCode));
            result.AddRange(MapPinCollectionService.GetMapPins(sharedPins, mapCode));

            return result
                .Select(MapPin.Normalize)
                .Where(x => x != null)
                .GroupBy(x => string.Format("{0}:{1}:{2}:{3}", x.East, x.South, x.Title, x.Detail), StringComparer.Ordinal)
                .Select(g => g.First())
                .ToList();
        }

        private void Button7_Click(object sender, EventArgs e)
        {
            if (!sharedLogs.Any() && !sharedPins.Any())
            {
                MessageBox.Show(
                    this,
                    "\u5171\u6709\u30C7\u30FC\u30BF\u306F\u3042\u308A\u307E\u305B\u3093\u3002",
                    "\u30E1\u30C3\u30BB\u30FC\u30B8",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var deleted = sharedLogs.Keys
                .Union(sharedPins.Keys, StringComparer.OrdinalIgnoreCase)
                .Count();
            sharedLogs.Clear();
            sharedPins.Clear();
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
            UpdateGameOrientationAvailability();
            RefreshMiniMap();
        }

        private void CheckBoxGameOrientation_CheckedChanged(object sender, EventArgs e)
        {
            RefreshMiniMap();
        }
    }
}

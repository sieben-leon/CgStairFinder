using System;
using System.Drawing;
using System.Windows.Forms;

namespace CgStairFinder
{
    internal sealed class MiniMapOverlayController : IDisposable
    {
        private const int OverlayClickThreshold = 4;
        private const int MinMiniMapSize = 180;
        private const int MinListHeight = 80;
        private const int DefaultListHeight = 260;
        private const int ResizeHandleSize = 14;

        private readonly Font listFont;
        private readonly int listItemHeight;
        private readonly DrawItemEventHandler listDrawItemHandler;

        private Form overlayForm;
        private TableLayoutPanel overlayLayout;
        private PictureBox overlayPictureBox;
        private ListBox overlayListBox;
        private Panel overlayResizeHandle;
        private bool overlayDragging;
        private bool overlayResizing;
        private bool overlayMoved;
        private bool suppressOverlayResizeEvent;
        private Point overlayDragStartCursor;
        private Point overlayDragStartForm;
        private Point overlayResizeStartCursor;
        private Size overlayResizeStartSize;
        private Point? lastOverlayLocation;
        private Size? lastOverlayClientSize;

        public event EventHandler RestoreRequested;
        public event EventHandler OverlayClosed;
        public event EventHandler OverlayResized;

        public MiniMapOverlayController(Font listFont, int listItemHeight, DrawItemEventHandler listDrawItemHandler)
        {
            this.listFont = listFont ?? SystemFonts.DefaultFont;
            this.listItemHeight = Math.Max(14, listItemHeight);
            this.listDrawItemHandler = listDrawItemHandler;
        }

        public bool IsActive
        {
            get
            {
                return overlayForm != null &&
                       !overlayForm.IsDisposed &&
                       overlayForm.Visible &&
                       overlayPictureBox != null &&
                       !overlayPictureBox.IsDisposed;
            }
        }

        public PictureBox ActivePictureBox
        {
            get
            {
                return IsActive ? overlayPictureBox : null;
            }
        }

        public void Show(PictureBox sourcePictureBox)
        {
            EnsureOverlayForm(sourcePictureBox);
            if (overlayForm == null || overlayForm.IsDisposed)
            {
                return;
            }

            if (!overlayForm.Visible)
            {
                overlayForm.Show();
            }

            overlayForm.BringToFront();
        }

        public void Hide()
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                overlayForm.Close();
            }
        }

        public void ClearImage()
        {
            if (overlayPictureBox == null || overlayPictureBox.IsDisposed)
            {
                return;
            }

            var oldImage = overlayPictureBox.Image;
            overlayPictureBox.Image = null;
            oldImage?.Dispose();
        }

        public void SyncList(ListBox.ObjectCollection items)
        {
            if (overlayListBox == null || overlayListBox.IsDisposed)
            {
                return;
            }

            overlayListBox.BeginUpdate();
            overlayListBox.Items.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    overlayListBox.Items.Add(item);
                }
            }

            overlayListBox.EndUpdate();
        }

        public void Dispose()
        {
            Hide();
        }

        private void EnsureOverlayForm(PictureBox sourcePictureBox)
        {
            if (overlayForm != null && !overlayForm.IsDisposed && overlayPictureBox != null && !overlayPictureBox.IsDisposed)
            {
                return;
            }

            var sourceWidth = sourcePictureBox == null ? 280 : sourcePictureBox.Width;
            var sourceHeight = sourcePictureBox == null ? 180 : sourcePictureBox.Height;
            var miniMapSize = Math.Max(MinMiniMapSize, Math.Max(sourceWidth, sourceHeight));
            var initialSize = lastOverlayClientSize ?? new Size(miniMapSize, miniMapSize + DefaultListHeight);
            var normalizedSize = NormalizeOverlayClientSize(initialSize);

            overlayPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(249, 251, 255),
                BorderStyle = BorderStyle.None
            };
            overlayPictureBox.MouseDown += OverlayMouseDown;
            overlayPictureBox.MouseMove += OverlayMouseMove;
            overlayPictureBox.MouseUp += OverlayMouseUp;

            overlayListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BackColor = Color.FromArgb(249, 251, 255),
                BorderStyle = BorderStyle.None,
                Font = listFont,
                DrawMode = listDrawItemHandler == null ? DrawMode.Normal : DrawMode.OwnerDrawFixed,
                ItemHeight = listItemHeight
            };
            if (listDrawItemHandler != null)
            {
                overlayListBox.DrawItem += listDrawItemHandler;
            }

            overlayListBox.MouseDown += OverlayMouseDown;
            overlayListBox.MouseMove += OverlayMouseMove;
            overlayListBox.MouseUp += OverlayMouseUp;

            overlayLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            overlayLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            overlayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, normalizedSize.Width));
            overlayLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            overlayLayout.Controls.Add(overlayPictureBox, 0, 0);
            overlayLayout.Controls.Add(overlayListBox, 0, 1);
            overlayLayout.MouseDown += OverlayMouseDown;
            overlayLayout.MouseMove += OverlayMouseMove;
            overlayLayout.MouseUp += OverlayMouseUp;

            overlayResizeHandle = new Panel
            {
                Size = new Size(ResizeHandleSize, ResizeHandleSize),
                BackColor = Color.FromArgb(226, 232, 240),
                Cursor = Cursors.SizeNWSE,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            overlayResizeHandle.MouseDown += OverlayResizeHandle_MouseDown;
            overlayResizeHandle.MouseMove += OverlayResizeHandle_MouseMove;
            overlayResizeHandle.MouseUp += OverlayResizeHandle_MouseUp;

            overlayForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                BackColor = Color.FromArgb(249, 251, 255),
                ClientSize = normalizedSize
            };
            overlayForm.Controls.Add(overlayLayout);
            overlayForm.Controls.Add(overlayResizeHandle);
            overlayForm.MouseDown += OverlayMouseDown;
            overlayForm.MouseMove += OverlayMouseMove;
            overlayForm.MouseUp += OverlayMouseUp;
            overlayForm.Resize += OverlayForm_Resize;
            overlayForm.FormClosed += OverlayForm_FormClosed;

            if (lastOverlayLocation.HasValue)
            {
                overlayForm.Location = lastOverlayLocation.Value;
            }
            else if (sourcePictureBox != null)
            {
                var miniMapScreenPos = sourcePictureBox.PointToScreen(Point.Empty);
                overlayForm.Location = new Point(miniMapScreenPos.X + 18, miniMapScreenPos.Y + 18);
            }

            UpdateOverlayLayoutAndNotify(false);
        }

        private void OverlayForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                lastOverlayLocation = overlayForm.Location;
                lastOverlayClientSize = overlayForm.ClientSize;
            }

            overlayDragging = false;
            overlayResizing = false;
            overlayMoved = false;

            if (overlayPictureBox != null && !overlayPictureBox.IsDisposed)
            {
                var oldImage = overlayPictureBox.Image;
                overlayPictureBox.Image = null;
                oldImage?.Dispose();
                overlayPictureBox.Dispose();
            }

            overlayPictureBox = null;
            overlayLayout = null;
            overlayListBox = null;
            overlayResizeHandle = null;
            overlayForm = null;
            OverlayClosed?.Invoke(this, EventArgs.Empty);
        }

        private void OverlayForm_Resize(object sender, EventArgs e)
        {
            UpdateOverlayLayoutAndNotify(true);
        }

        private void OverlayResizeHandle_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || overlayForm == null)
            {
                return;
            }

            overlayResizing = true;
            overlayDragging = false;
            overlayMoved = false;
            overlayResizeStartCursor = Cursor.Position;
            overlayResizeStartSize = overlayForm.ClientSize;
        }

        private void OverlayResizeHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!overlayResizing || overlayForm == null)
            {
                return;
            }

            var cursorPos = Cursor.Position;
            var dx = cursorPos.X - overlayResizeStartCursor.X;
            var dy = cursorPos.Y - overlayResizeStartCursor.Y;
            var requestedSize = new Size(overlayResizeStartSize.Width + dx, overlayResizeStartSize.Height + dy);
            var normalizedSize = NormalizeOverlayClientSize(requestedSize);
            if (overlayForm.ClientSize != normalizedSize)
            {
                overlayForm.ClientSize = normalizedSize;
            }
        }

        private void OverlayResizeHandle_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            overlayResizing = false;
        }

        private void OverlayMouseDown(object sender, MouseEventArgs e)
        {
            if (overlayResizing || e.Button != MouseButtons.Left || overlayForm == null)
            {
                return;
            }

            overlayDragging = true;
            overlayMoved = false;
            overlayDragStartCursor = Cursor.Position;
            overlayDragStartForm = overlayForm.Location;
        }

        private void OverlayMouseMove(object sender, MouseEventArgs e)
        {
            if (overlayResizing || !overlayDragging || overlayForm == null)
            {
                return;
            }

            var cursorPos = Cursor.Position;
            var dx = cursorPos.X - overlayDragStartCursor.X;
            var dy = cursorPos.Y - overlayDragStartCursor.Y;
            if (Math.Abs(dx) >= OverlayClickThreshold || Math.Abs(dy) >= OverlayClickThreshold)
            {
                overlayMoved = true;
            }

            overlayForm.Location = new Point(overlayDragStartForm.X + dx, overlayDragStartForm.Y + dy);
        }

        private void OverlayMouseUp(object sender, MouseEventArgs e)
        {
            if (overlayResizing)
            {
                return;
            }

            if (e.Button == MouseButtons.Left && overlayDragging && !overlayMoved)
            {
                RestoreRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            overlayDragging = false;
            overlayMoved = false;
        }

        private Size NormalizeOverlayClientSize(Size requested)
        {
            var width = Math.Max(MinMiniMapSize, requested.Width);
            var height = Math.Max(width + MinListHeight, requested.Height);
            return new Size(width, height);
        }

        private void UpdateOverlayLayoutAndNotify(bool notify)
        {
            if (overlayForm == null || overlayForm.IsDisposed || overlayLayout == null || overlayLayout.RowStyles.Count < 1)
            {
                return;
            }

            var normalizedSize = NormalizeOverlayClientSize(overlayForm.ClientSize);
            if (overlayForm.ClientSize != normalizedSize)
            {
                if (suppressOverlayResizeEvent)
                {
                    return;
                }

                suppressOverlayResizeEvent = true;
                overlayForm.ClientSize = normalizedSize;
                suppressOverlayResizeEvent = false;
            }

            overlayLayout.RowStyles[0].Height = overlayForm.ClientSize.Width;

            if (overlayResizeHandle != null && !overlayResizeHandle.IsDisposed)
            {
                overlayResizeHandle.Location = new Point(
                    Math.Max(0, overlayForm.ClientSize.Width - overlayResizeHandle.Width - 2),
                    Math.Max(0, overlayForm.ClientSize.Height - overlayResizeHandle.Height - 2));
                overlayResizeHandle.BringToFront();
            }

            if (notify && !suppressOverlayResizeEvent)
            {
                OverlayResized?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

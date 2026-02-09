using System;
using System.Drawing;
using System.Windows.Forms;

namespace CgStairFinder
{
    internal sealed class MiniMapOverlayController : IDisposable
    {
        private const int OverlayClickThreshold = 4;

        private readonly Font listFont;

        private Form overlayForm;
        private PictureBox overlayPictureBox;
        private ListBox overlayListBox;
        private bool overlayDragging;
        private bool overlayMoved;
        private Point overlayDragStartCursor;
        private Point overlayDragStartForm;

        public event EventHandler RestoreRequested;
        public event EventHandler OverlayClosed;

        public MiniMapOverlayController(Font listFont)
        {
            this.listFont = listFont ?? SystemFonts.DefaultFont;
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
                    overlayListBox.Items.Add(item == null ? string.Empty : item.ToString());
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
            var miniMapSize = Math.Max(280, Math.Max(sourceWidth, sourceHeight));
            var overlayWidth = miniMapSize;
            var overlayHeight = miniMapSize + 260;

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
                Font = listFont
            };
            overlayListBox.MouseDown += OverlayMouseDown;
            overlayListBox.MouseMove += OverlayMouseMove;
            overlayListBox.MouseUp += OverlayMouseUp;

            var overlayLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            overlayLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            overlayLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, miniMapSize));
            overlayLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            overlayLayout.Controls.Add(overlayPictureBox, 0, 0);
            overlayLayout.Controls.Add(overlayListBox, 0, 1);
            overlayLayout.MouseDown += OverlayMouseDown;
            overlayLayout.MouseMove += OverlayMouseMove;
            overlayLayout.MouseUp += OverlayMouseUp;

            overlayForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                TopMost = true,
                BackColor = Color.FromArgb(249, 251, 255),
                ClientSize = new Size(overlayWidth, overlayHeight)
            };
            overlayForm.Controls.Add(overlayLayout);
            overlayForm.MouseDown += OverlayMouseDown;
            overlayForm.MouseMove += OverlayMouseMove;
            overlayForm.MouseUp += OverlayMouseUp;
            overlayForm.FormClosed += OverlayForm_FormClosed;

            if (sourcePictureBox != null)
            {
                var miniMapScreenPos = sourcePictureBox.PointToScreen(Point.Empty);
                overlayForm.Location = new Point(miniMapScreenPos.X + 18, miniMapScreenPos.Y + 18);
            }
        }

        private void OverlayForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            overlayDragging = false;
            overlayMoved = false;

            if (overlayPictureBox != null && !overlayPictureBox.IsDisposed)
            {
                var oldImage = overlayPictureBox.Image;
                overlayPictureBox.Image = null;
                oldImage?.Dispose();
                overlayPictureBox.Dispose();
            }

            overlayPictureBox = null;
            overlayListBox = null;
            overlayForm = null;
            OverlayClosed?.Invoke(this, EventArgs.Empty);
        }

        private void OverlayMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || overlayForm == null)
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
            if (!overlayDragging || overlayForm == null)
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
            if (e.Button == MouseButtons.Left && overlayDragging && !overlayMoved)
            {
                RestoreRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            overlayDragging = false;
            overlayMoved = false;
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace CgStairFinder
{
    internal enum PinDetailDialogAction
    {
        None = 0,
        Delete = 1,
        SaveLocal = 2
    }

    internal static class PinDetailDialog
    {
        public static PinDetailDialogAction Show(IWin32Window owner, Font font, MapPin pin, bool isShared)
        {
            if (pin == null)
            {
                return PinDetailDialogAction.None;
            }

            var action = PinDetailDialogAction.None;
            using (var form = new Form())
            using (var table = new TableLayoutPanel())
            using (var labelCoord = new Label())
            using (var labelTitle = new Label())
            using (var textDetail = new TextBox())
            using (var buttonPanel = new FlowLayoutPanel())
            using (var buttonDelete = new Button())
            using (var buttonSaveLocal = new Button())
            using (var buttonClose = new Button())
            {
                form.Text = isShared ? "共有ピン" : "ローカルピン";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(420, 250);
                form.Font = font;

                table.Dock = DockStyle.Fill;
                table.Padding = new Padding(10);
                table.ColumnCount = 1;
                table.RowCount = 4;
                table.RowStyles.Add(new RowStyle());
                table.RowStyles.Add(new RowStyle());
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                table.RowStyles.Add(new RowStyle());

                labelCoord.AutoSize = true;
                labelCoord.Text = string.Format("座標: 東{0}、南{1}", pin.East, pin.South);
                table.Controls.Add(labelCoord, 0, 0);

                labelTitle.AutoSize = true;
                labelTitle.Margin = new Padding(3, 6, 3, 3);
                labelTitle.Text = string.Format("タイトル: {0}", pin.Title);
                table.Controls.Add(labelTitle, 0, 1);

                textDetail.Multiline = true;
                textDetail.ReadOnly = true;
                textDetail.ScrollBars = ScrollBars.Vertical;
                textDetail.Dock = DockStyle.Fill;
                textDetail.Text = string.IsNullOrWhiteSpace(pin.Detail) ? "（詳細なし）" : pin.Detail;
                table.Controls.Add(textDetail, 0, 2);

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
                    action = PinDetailDialogAction.Delete;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };

                buttonPanel.Controls.Add(buttonClose);
                if (isShared)
                {
                    buttonSaveLocal.Text = "ローカル保存";
                    buttonSaveLocal.Width = 104;
                    buttonSaveLocal.Click += (s, e) =>
                    {
                        action = PinDetailDialogAction.SaveLocal;
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    };
                    buttonPanel.Controls.Add(buttonSaveLocal);
                }

                buttonPanel.Controls.Add(buttonDelete);
                table.Controls.Add(buttonPanel, 0, 3);

                form.Controls.Add(table);
                form.CancelButton = buttonClose;

                if (form.ShowDialog(owner) != DialogResult.OK)
                {
                    return PinDetailDialogAction.None;
                }
            }

            return action;
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace CgStairFinder
{
    internal static class PinCreateDialog
    {
        public static bool TryShow(IWin32Window owner, Font font, string mapCode, int initialEast, int initialSouth, out MapPin pin)
        {
            pin = null;

            using (var form = new Form())
            using (var table = new TableLayoutPanel())
            using (var labelMap = new Label())
            using (var labelEast = new Label())
            using (var labelSouth = new Label())
            using (var labelTitle = new Label())
            using (var labelDetail = new Label())
            using (var inputEast = new NumericUpDown())
            using (var inputSouth = new NumericUpDown())
            using (var inputTitle = new TextBox())
            using (var inputDetail = new TextBox())
            using (var buttons = new FlowLayoutPanel())
            using (var buttonOk = new Button())
            using (var buttonCancel = new Button())
            {
                form.Text = "ピンを追加";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(420, 280);
                form.Font = font;

                table.Dock = DockStyle.Fill;
                table.Padding = new Padding(10);
                table.ColumnCount = 2;
                table.RowCount = 6;
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                table.RowStyles.Add(new RowStyle());
                table.RowStyles.Add(new RowStyle());
                table.RowStyles.Add(new RowStyle());
                table.RowStyles.Add(new RowStyle());
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                table.RowStyles.Add(new RowStyle());

                labelMap.Text = string.Format("マップ: {0}", mapCode);
                labelMap.AutoSize = true;
                labelMap.Dock = DockStyle.Fill;
                table.Controls.Add(labelMap, 0, 0);
                table.SetColumnSpan(labelMap, 2);

                labelEast.Text = "東";
                labelEast.TextAlign = ContentAlignment.MiddleLeft;
                labelEast.Dock = DockStyle.Fill;
                inputEast.Minimum = 0;
                inputEast.Maximum = 9999;
                inputEast.Value = Math.Min(9999, Math.Max(0, initialEast));
                inputEast.Width = 120;
                table.Controls.Add(labelEast, 0, 1);
                table.Controls.Add(inputEast, 1, 1);

                labelSouth.Text = "南";
                labelSouth.TextAlign = ContentAlignment.MiddleLeft;
                labelSouth.Dock = DockStyle.Fill;
                inputSouth.Minimum = 0;
                inputSouth.Maximum = 9999;
                inputSouth.Value = Math.Min(9999, Math.Max(0, initialSouth));
                inputSouth.Width = 120;
                table.Controls.Add(labelSouth, 0, 2);
                table.Controls.Add(inputSouth, 1, 2);

                labelTitle.AutoSize = true;
                labelTitle.Text = "タイトル\r\n(5文字)";
                labelTitle.TextAlign = ContentAlignment.MiddleLeft;
                labelTitle.Dock = DockStyle.Fill;
                inputTitle.MaxLength = 5;
                inputTitle.Dock = DockStyle.Fill;
                table.Controls.Add(labelTitle, 0, 3);
                table.Controls.Add(inputTitle, 1, 3);

                labelDetail.Text = "詳細";
                labelDetail.TextAlign = ContentAlignment.MiddleLeft;
                labelDetail.Dock = DockStyle.Fill;
                inputDetail.Multiline = true;
                inputDetail.ScrollBars = ScrollBars.Vertical;
                inputDetail.Dock = DockStyle.Fill;
                table.Controls.Add(labelDetail, 0, 4);
                table.Controls.Add(inputDetail, 1, 4);

                buttons.Dock = DockStyle.Fill;
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.WrapContents = false;

                buttonOk.Text = "保存";
                buttonOk.Width = 88;
                buttonOk.DialogResult = DialogResult.OK;

                buttonCancel.Text = "キャンセル";
                buttonCancel.Width = 88;
                buttonCancel.DialogResult = DialogResult.Cancel;

                buttons.Controls.Add(buttonOk);
                buttons.Controls.Add(buttonCancel);
                table.Controls.Add(buttons, 0, 5);
                table.SetColumnSpan(buttons, 2);

                form.Controls.Add(table);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;

                if (form.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }

                var normalized = MapPin.Normalize(new MapPin
                {
                    East = (int)inputEast.Value,
                    South = (int)inputSouth.Value,
                    Title = inputTitle.Text,
                    Detail = inputDetail.Text
                });

                if (normalized == null)
                {
                    MessageBox.Show(
                        owner,
                        "タイトルを1〜5文字で入力してください。",
                        "メッセージ",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return false;
                }

                pin = normalized;
                return true;
            }
        }
    }
}

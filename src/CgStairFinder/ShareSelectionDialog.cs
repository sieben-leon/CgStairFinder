using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CgStairFinder
{
    internal sealed class ShareSelectionResult
    {
        public List<DetectLog> SelectedLogs { get; set; } = new List<DetectLog>();
        public bool IncludeMapFiles { get; set; }
        public bool IncludePins { get; set; }
    }

    internal static class ShareSelectionDialog
    {
        private sealed class MapNameKeywordCandidate
        {
            public MapNameKeywordCandidate(string displayText, string searchText)
            {
                DisplayText = displayText ?? string.Empty;
                SearchText = searchText ?? string.Empty;
            }

            public string DisplayText { get; }

            public string SearchText { get; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        public static bool TryShow(
            IWin32Window owner,
            Font font,
            IList<ShareLogItem> candidates,
            out ShareSelectionResult result)
        {
            result = null;
            if (candidates == null)
            {
                return false;
            }

            using (var form = new Form())
            using (var label = new Label())
            using (var filterPanel = new FlowLayoutPanel())
            using (var labelRecent = new Label())
            using (var inputRecentMinutes = new NumericUpDown())
            using (var labelRecentSuffix = new Label())
            using (var labelKeyword = new Label())
            using (var inputKeyword = new ComboBox())
            using (var buttonSelectVisible = new Button())
            using (var buttonUnselectVisible = new Button())
            using (var labelStatus = new Label())
            using (var checkedList = new CheckedListBox())
            using (var checkIncludeMapFiles = new CheckBox())
            using (var checkIncludePins = new CheckBox())
            using (var buttonOk = new Button())
            using (var buttonCancel = new Button())
            {
                form.Text = "共有対象の選択";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.SizableToolWindow;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(520, 420);
                form.MinimumSize = new Size(460, 320);
                form.Font = font;

                label.AutoSize = false;
                label.Dock = DockStyle.Top;
                label.Height = 36;
                label.Padding = new Padding(8, 6, 8, 0);
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.Text = "共有するマップを選択してください。";

                filterPanel.AutoSize = false;
                filterPanel.Dock = DockStyle.Top;
                filterPanel.Height = 58;
                filterPanel.Padding = new Padding(8, 2, 8, 2);
                filterPanel.FlowDirection = FlowDirection.LeftToRight;
                filterPanel.WrapContents = true;

                labelRecent.AutoSize = true;
                labelRecent.Margin = new Padding(0, 8, 4, 0);
                labelRecent.Text = "直近";

                inputRecentMinutes.Minimum = 0;
                inputRecentMinutes.Maximum = 999;
                inputRecentMinutes.Value = 0;
                inputRecentMinutes.Width = 60;
                inputRecentMinutes.Margin = new Padding(0, 4, 4, 0);

                labelRecentSuffix.AutoSize = true;
                labelRecentSuffix.Margin = new Padding(0, 8, 10, 0);
                labelRecentSuffix.Text = "分以内";

                labelKeyword.AutoSize = true;
                labelKeyword.Margin = new Padding(0, 8, 4, 0);
                labelKeyword.Text = "マップ名";

                inputKeyword.DropDownStyle = ComboBoxStyle.DropDown;
                inputKeyword.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                inputKeyword.AutoCompleteSource = AutoCompleteSource.ListItems;
                inputKeyword.Width = 180;
                inputKeyword.Margin = new Padding(0, 4, 10, 0);
                foreach (var candidate in BuildMapNameKeywordCandidates(candidates))
                {
                    inputKeyword.Items.Add(candidate);
                }

                var isApplyingKeywordCandidate = false;
                Action<MapNameKeywordCandidate> applyKeywordCandidate = selectedCandidate =>
                {
                    if (isApplyingKeywordCandidate)
                    {
                        return;
                    }

                    if (selectedCandidate == null)
                    {
                        return;
                    }

                    isApplyingKeywordCandidate = true;
                    try
                    {
                        inputKeyword.Text = selectedCandidate.SearchText;
                        inputKeyword.SelectionStart = inputKeyword.Text.Length;
                        inputKeyword.SelectionLength = 0;
                        inputKeyword.DroppedDown = false;
                    }
                    finally
                    {
                        isApplyingKeywordCandidate = false;
                    }
                };

                inputKeyword.SelectionChangeCommitted += (s, e) =>
                {
                    var selectedCandidate = inputKeyword.SelectedItem as MapNameKeywordCandidate;
                    if (selectedCandidate == null)
                    {
                        return;
                    }

                    // ComboBox内部の選択反映後にキーワード文字列へ置き換える。
                    form.BeginInvoke((Action)(() => applyKeywordCandidate(selectedCandidate)));
                };

                buttonSelectVisible.Text = "全選択";
                buttonSelectVisible.Width = 72;
                buttonSelectVisible.Margin = new Padding(0, 3, 4, 0);

                buttonUnselectVisible.Text = "全解除";
                buttonUnselectVisible.Width = 72;
                buttonUnselectVisible.Margin = new Padding(0, 3, 0, 0);

                filterPanel.Controls.Add(labelRecent);
                filterPanel.Controls.Add(inputRecentMinutes);
                filterPanel.Controls.Add(labelRecentSuffix);
                filterPanel.Controls.Add(labelKeyword);
                filterPanel.Controls.Add(inputKeyword);
                filterPanel.Controls.Add(buttonSelectVisible);
                filterPanel.Controls.Add(buttonUnselectVisible);

                labelStatus.AutoSize = false;
                labelStatus.Dock = DockStyle.Top;
                labelStatus.Height = 22;
                labelStatus.Padding = new Padding(8, 0, 8, 0);
                labelStatus.TextAlign = ContentAlignment.MiddleLeft;
                labelStatus.ForeColor = Color.FromArgb(100, 116, 139);

                checkedList.Dock = DockStyle.Fill;
                checkedList.CheckOnClick = true;
                checkedList.HorizontalScrollbar = true;

                checkIncludeMapFiles.AutoSize = true;
                checkIncludeMapFiles.Dock = DockStyle.Bottom;
                checkIncludeMapFiles.Padding = new Padding(8, 4, 8, 2);
                checkIncludeMapFiles.Text = "選択したマップの .dat も同梱する（ファイルサイズ増）";
                checkIncludeMapFiles.Checked = false;

                checkIncludePins.AutoSize = true;
                checkIncludePins.Dock = DockStyle.Bottom;
                checkIncludePins.Padding = new Padding(8, 2, 8, 2);
                checkIncludePins.Text = "ピン情報も共有する";
                checkIncludePins.Checked = false;

                var buttonPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 42,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8, 6, 8, 6)
                };

                buttonOk.Text = "次へ";
                buttonOk.Width = 88;
                buttonOk.DialogResult = DialogResult.OK;

                buttonCancel.Text = "キャンセル";
                buttonCancel.Width = 88;
                buttonCancel.DialogResult = DialogResult.Cancel;

                buttonPanel.Controls.Add(buttonOk);
                buttonPanel.Controls.Add(buttonCancel);

                var checkedState = candidates.ToDictionary(x => x, x => false);
                var isRefreshing = false;

                Func<ShareLogItem, bool> matchFilter = item =>
                {
                    if (item == null || item.Log == null)
                    {
                        return false;
                    }

                    var minutes = (int)inputRecentMinutes.Value;
                    if (minutes > 0)
                    {
                        var threshold = DateTime.Now.AddMinutes(-minutes);
                        if (item.Log.DetectTime < threshold)
                        {
                            return false;
                        }
                    }

                    var keyword = (inputKeyword.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(keyword))
                    {
                        var mapName = item.Log.MapName ?? string.Empty;
                        if (mapName.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0)
                        {
                            return false;
                        }
                    }

                    return true;
                };

                Action<bool> refreshVisibleItems = syncCheckedStateFromList =>
                {
                    isRefreshing = true;
                    try
                    {
                        if (syncCheckedStateFromList)
                        {
                            for (var i = 0; i < checkedList.Items.Count; i++)
                            {
                                var existing = checkedList.Items[i] as ShareLogItem;
                                if (existing != null)
                                {
                                    checkedState[existing] = checkedList.GetItemChecked(i);
                                }
                            }
                        }

                        checkedList.Items.Clear();
                        var visibleCount = 0;
                        foreach (var candidate in candidates)
                        {
                            if (!matchFilter(candidate))
                            {
                                continue;
                            }

                            visibleCount++;
                            bool isChecked;
                            if (!checkedState.TryGetValue(candidate, out isChecked))
                            {
                                isChecked = false;
                                checkedState[candidate] = false;
                            }

                            checkedList.Items.Add(candidate, isChecked);
                        }

                        var selectedCount = checkedState.Count(x => x.Value);
                        labelStatus.Text = string.Format(
                            "表示: {0} / 全件: {1}    選択: {2}",
                            visibleCount,
                            candidates.Count,
                            selectedCount);
                    }
                    finally
                    {
                        isRefreshing = false;
                    }
                };

                checkedList.ItemCheck += (s, e) =>
                {
                    if (isRefreshing || e.Index < 0 || e.Index >= checkedList.Items.Count)
                    {
                        return;
                    }

                    var item = checkedList.Items[e.Index] as ShareLogItem;
                    if (item == null)
                    {
                        return;
                    }

                    checkedState[item] = e.NewValue == CheckState.Checked;
                    form.BeginInvoke((Action)(() => refreshVisibleItems(false)));
                };

                inputRecentMinutes.ValueChanged += (s, e) => refreshVisibleItems(true);
                inputKeyword.TextChanged += (s, e) => refreshVisibleItems(true);

                buttonSelectVisible.Click += (s, e) =>
                {
                    foreach (var item in checkedList.Items.Cast<ShareLogItem>())
                    {
                        checkedState[item] = true;
                    }

                    refreshVisibleItems(false);
                };

                buttonUnselectVisible.Click += (s, e) =>
                {
                    foreach (var item in checkedList.Items.Cast<ShareLogItem>())
                    {
                        checkedState[item] = false;
                    }

                    refreshVisibleItems(false);
                };

                form.Controls.Add(checkedList);
                form.Controls.Add(checkIncludePins);
                form.Controls.Add(checkIncludeMapFiles);
                form.Controls.Add(buttonPanel);
                form.Controls.Add(labelStatus);
                form.Controls.Add(filterPanel);
                form.Controls.Add(label);
                form.AcceptButton = buttonOk;
                form.CancelButton = buttonCancel;
                refreshVisibleItems(false);

                if (form.ShowDialog(owner) != DialogResult.OK)
                {
                    return false;
                }

                result = new ShareSelectionResult
                {
                    SelectedLogs = candidates
                        .Where(x => checkedState.ContainsKey(x) && checkedState[x])
                        .Select(x => x.Log)
                        .ToList(),
                    IncludeMapFiles = checkIncludeMapFiles.Checked,
                    IncludePins = checkIncludePins.Checked
                };
                return true;
            }
        }

        private static IEnumerable<MapNameKeywordCandidate> BuildMapNameKeywordCandidates(IList<ShareLogItem> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return Enumerable.Empty<MapNameKeywordCandidate>();
            }

            return candidates
                .Where(x => x?.Log != null && !string.IsNullOrWhiteSpace(x.Log.MapName))
                .Select(x =>
                {
                    var mapName = (x.Log.MapName ?? string.Empty).Trim();
                    return new
                    {
                        Display = ReplaceDigitsWithCircle(mapName),
                        Search = ExtractPrefixBeforeFirstDigit(mapName)
                    };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Display) &&
                            !string.IsNullOrWhiteSpace(x.Search) &&
                            x.Display.IndexOf('\u3007') >= 0)
                .GroupBy(x => x.Display, StringComparer.CurrentCultureIgnoreCase)
                .Where(g => g.Count() >= 5)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g =>
                {
                    var selectedSearch = g
                        .GroupBy(x => x.Search, StringComparer.CurrentCultureIgnoreCase)
                        .OrderByDescending(x => x.Count())
                        .ThenBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase)
                        .Select(x => x.Key)
                        .FirstOrDefault() ?? string.Empty;
                    return new MapNameKeywordCandidate(g.Key, selectedSearch);
                })
                .ToList();
        }

        private static string ReplaceDigitsWithCircle(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                return string.Empty;
            }

            var buffer = new List<char>(mapName.Length);
            var previousIsDigit = false;
            foreach (var ch in mapName)
            {
                if (char.IsDigit(ch))
                {
                    if (!previousIsDigit)
                    {
                        buffer.Add('\u3007');
                    }

                    previousIsDigit = true;
                    continue;
                }

                previousIsDigit = false;
                buffer.Add(ch);
            }

            return new string(buffer.ToArray()).Trim();
        }

        private static string ExtractPrefixBeforeFirstDigit(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                return string.Empty;
            }

            for (var i = 0; i < mapName.Length; i++)
            {
                if (char.IsDigit(mapName[i]))
                {
                    return mapName.Substring(0, i).TrimEnd();
                }
            }

            return string.Empty;
        }
    }
}

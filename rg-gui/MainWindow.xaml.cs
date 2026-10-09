using FramePFX.Themes;
using Ookii.Dialogs.Wpf;
using Peter;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using static rg_gui.RipGrepWrapper;

namespace rg_gui
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const double DEFAULT_MAINWINDOW_LEFT = 0;
        private const double DEFAULT_MAINWINDOW_TOP = 0;
        private const double DEFAULT_MAINWINDOW_WIDTH = 800;
        private const double DEFAULT_MAINWINDOW_HEIGHT = 450;
        private const int DEFAULT_MAINWINDOW_STATE = 0;

        private const string DEFAULT_BASEPATH = "";
        private const string DEFAULT_INCLUDEFILES = "";
        private const string DEFAULT_EXCLUDEFILES = "";
        private const string DEFAULT_CONTAININGTEXT = "";

        private const bool DEFAULT_CASESENSITIVE = false;
        private const bool DEFAULT_RECURSIVE = true;
        private const bool DEFAULT_REGULAREXPRESSION = false;

        private const string DEFAULT_FILEENCODING = "Auto";

        private const int DEFAULT_MAXFILESIZE = 0;
        private const string DEFAULT_MAXFILESIZEUNIT = "None";

        private const int DEFAULT_MAXSEARCHTERMS = 10;

        private const int HIGHLIGHT_COLORS_COUNT = 4;

        private const double GRID_SPLITTER_WIDTH = 5.0;

        private string m_currentInput = string.Empty;
        private string? m_currentSuggestion = string.Empty;
        private string m_currentText = string.Empty;
        private int m_selectionStart;
        private int m_selectionLength;
        private IEnumerable<string> m_folderSuggestionValues = Enumerable.Empty<string>();

        private int m_maxSearchTerms;

        private const ThemeType DEFAULT_THEME = ThemeType.Dark;
        private ThemeType m_currentTheme;

        private const bool DEFAULT_MULTIPLEHIGHLIGHTCOLORS = true;
        private bool m_multipleHighlightColors = DEFAULT_MULTIPLEHIGHLIGHTCOLORS;

        private const int DEFAULT_MAXLINEHIGHLIGHTS = 100;
        private int m_maxLineHighlights = DEFAULT_MAXLINEHIGHLIGHTS;

        private const int DEFAULT_CONTEXTLINESBEFORE = 2;
        private int m_contextLinesBefore = DEFAULT_CONTEXTLINESBEFORE;

        private const int DEFAULT_CONTEXTLINESAFTER = 2;
        private int m_contextLinesAfter = DEFAULT_CONTEXTLINESAFTER;

        private string m_fileViewerPath;
        private string m_fileViewerArgs;

        public class FileSearchResult
        {
            public string Path { get; }

            public string Filename { get; }

            // Null if the file's dates couldn't be read.
            public DateTime? Modified { get; }

            public DateTime? Created { get; }

            public FileSearchResult(string path, string filename, DateTime? modified = null, DateTime? created = null)
            {
                Path = path;
                Filename = filename;
                Modified = modified;
                Created = created;
            }
        }

        public class ResultLine
        {
            // Null for separator rows.
            public int? Line { get; }

            public string Content { get; }

            // Context lines (and separators) are shown dimmed and without highlighting.
            public bool IsContext { get; }

            public bool IsSeparator => Line == null;

            // Separator rows sort between the blocks they separate.
            public double SortKey { get; }

            public ResultLine(int line, string content, bool isContext = false)
            {
                Line = line;
                Content = content;
                IsContext = isContext;
                SortKey = line;
            }

            private ResultLine(double sortKey)
            {
                Content = "…";
                IsContext = true;
                SortKey = sortKey;
            }

            public static ResultLine Separator(double sortKey) => new(sortKey);
        }

        private CancellationTokenSource? m_cancellationTokenSource;

        // Incremented whenever the result lines are replaced, so a slow file read can tell it's out of date.
        private int m_resultLinesVersion;

        private FileEncoding m_searchEncoding = FileEncoding.Auto;

        private readonly RipGrepWrapper m_ripGrepWrapper;

        public RangeObservableCollection<FileSearchResult> FileResultItems { get; } = new();
        public RangeObservableCollection<ResultLine> ResultLineItems { get; } = new();

        public MainWindow(string? basePath, string? includeFiles, string? excludeFiles, string? containingText)
        {
            InitializeComponent();

            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            Left = double.TryParse(config.AppSettings.Settings["MainWindowLeft"]?.Value, out var left) ? left : DEFAULT_MAINWINDOW_LEFT;
            Top = double.TryParse(config.AppSettings.Settings["MainWindowTop"]?.Value, out var top) ? top : DEFAULT_MAINWINDOW_TOP;
            Width = double.TryParse(config.AppSettings.Settings["MainWindowWidth"]?.Value, out var width) ? width : DEFAULT_MAINWINDOW_WIDTH;
            Height = double.TryParse(config.AppSettings.Settings["MainWindowHeight"]?.Value, out var height) ? height : DEFAULT_MAINWINDOW_HEIGHT;
            WindowState = int.TryParse(config.AppSettings.Settings["MainWindowState"]?.Value, out var windowState) ? WindowState : DEFAULT_MAINWINDOW_STATE;

            txtBasePath.Text = basePath ?? config.AppSettings.Settings["BasePath"]?.Value ?? DEFAULT_BASEPATH;
            txtIncludeFiles.Text = includeFiles ?? config.AppSettings.Settings["IncludeFiles"]?.Value ?? DEFAULT_INCLUDEFILES;
            txtExcludeFiles.Text = excludeFiles ?? config.AppSettings.Settings["ExcludeFiles"]?.Value ?? DEFAULT_EXCLUDEFILES;
            txtContainingText.Text = containingText ?? config.AppSettings.Settings["ContainingText"]?.Value ?? DEFAULT_CONTAININGTEXT;
            chkCaseSensitive.IsChecked = bool.TryParse(config.AppSettings.Settings["CaseSensitive"]?.Value, out var caseSensitive) ? caseSensitive : DEFAULT_CASESENSITIVE;
            chkRecursive.IsChecked = bool.TryParse(config.AppSettings.Settings["Recursive"]?.Value, out var recursive) ? recursive : DEFAULT_RECURSIVE;
            chkRegularExpression.IsChecked = bool.TryParse(config.AppSettings.Settings["RegularExpression"]?.Value, out var regularExpression) ? regularExpression : DEFAULT_REGULAREXPRESSION;

            var gridFileResultsWidthStr = config.AppSettings.Settings["GridFileResultsWidth"]?.Value;
            var gridSplitterWidthStr = config.AppSettings.Settings["GridSplitterWidth"]?.Value;
            var gridResultLinesWidthStr = config.AppSettings.Settings["GridResultLinesWidth"]?.Value;

            var gridLengthConverter = new GridLengthConverter();

            if (gridFileResultsWidthStr != null && gridSplitterWidthStr != null && gridResultLinesWidthStr != null)
            {
                var gridFileResultsWidth = (GridLength?)gridLengthConverter.ConvertFromString(gridFileResultsWidthStr);
                var gridSplitterWidth = (GridLength?)gridLengthConverter.ConvertFromString(gridSplitterWidthStr);
                var gridResultLinesWidth = (GridLength?)gridLengthConverter.ConvertFromString(gridResultLinesWidthStr);

                // Sanity check the column widths before restoring them.
                if (gridFileResultsWidth != null && gridSplitterWidth != null && gridResultLinesWidth != null &&
                    (gridFileResultsWidth.Value.Value + gridSplitterWidth.Value.Value + gridResultLinesWidth.Value.Value) < Width &&
                    gridSplitterWidth.Value.Value == GRID_SPLITTER_WIDTH)
                {
                    gridResults.ColumnDefinitions[0].Width = (GridLength)gridFileResultsWidth;
                    gridResults.ColumnDefinitions[1].Width = (GridLength)gridSplitterWidth;
                    gridResults.ColumnDefinitions[2].Width = (GridLength)gridResultLinesWidth;
                }
            }

            var fileListSort = config.AppSettings.Settings["FileListSort"]?.Value;
            if (!string.IsNullOrWhiteSpace(fileListSort))
            {
                RestoreFileListSort(fileListSort);
            }

            var fileEncoding = cmbEncoding.FindName(config.AppSettings.Settings["FileEncoding"]?.Value ?? DEFAULT_FILEENCODING);
            if (fileEncoding != null)
            {
                cmbEncoding.SelectedItem = fileEncoding;
            }
            else
            {
                cmbEncoding.SelectedIndex = 0;
            }

            txtMaxFileSize.Text = (int.TryParse(config.AppSettings.Settings["MaxFileSize"]?.Value, out var maxFileSize) ? maxFileSize : DEFAULT_MAXFILESIZE).ToString();
            var maxFileSizeUnit = cmbFileSizeUnit.FindName(config.AppSettings.Settings["MaxFileSizeUnit"]?.Value ?? DEFAULT_MAXFILESIZEUNIT);
            if (maxFileSizeUnit != null)
            {
                cmbFileSizeUnit.SelectedItem = maxFileSizeUnit;
            }
            else
            {
                cmbFileSizeUnit.SelectedIndex = 0;
            }

            m_currentTheme = Enum.TryParse<ThemeType>(config.AppSettings.Settings["Theme"]?.Value, out var themeName) ? themeName : DEFAULT_THEME;
            ThemesController.SetTheme(m_currentTheme);

            var ripgrepPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty, "rg.exe");
            if (!File.Exists(ripgrepPath))
            {
                MessageBox.Show("rg.exe not found in installation path.", "Error");
                throw new Exception("rg.exe not found in installation path.");
            }

            m_maxSearchTerms = int.TryParse(config.AppSettings.Settings["MaxSearchTerms"]?.Value, out var maxSearchTerms) ? maxSearchTerms : DEFAULT_MAXSEARCHTERMS;
            m_multipleHighlightColors = bool.TryParse(config.AppSettings.Settings["MultipleHighlightColors"]?.Value, out var multipleHighlightColors) ? multipleHighlightColors : DEFAULT_MULTIPLEHIGHLIGHTCOLORS;

            m_maxLineHighlights = int.TryParse(config.AppSettings.Settings["MaxLineHighlights"]?.Value, out var maxLineHighlights) ? maxLineHighlights : DEFAULT_MAXLINEHIGHLIGHTS;

            m_contextLinesBefore = int.TryParse(config.AppSettings.Settings["ContextLinesBefore"]?.Value, out var contextLinesBefore) && contextLinesBefore >= 0 ? contextLinesBefore : DEFAULT_CONTEXTLINESBEFORE;
            m_contextLinesAfter = int.TryParse(config.AppSettings.Settings["ContextLinesAfter"]?.Value, out var contextLinesAfter) && contextLinesAfter >= 0 ? contextLinesAfter : DEFAULT_CONTEXTLINESAFTER;

            m_ripGrepWrapper = new RipGrepWrapper(ripgrepPath);
            m_ripGrepWrapper.FileFound += OnFileAdded;

            m_fileViewerPath = config.AppSettings.Settings["FileViewerPath"]?.Value ?? string.Empty;
            m_fileViewerArgs = config.AppSettings.Settings["FileViewerArgs"]?.Value ?? string.Empty;
        }

        // Restores a sort order saved as e.g. "Modified:Descending" or "Path:Ascending,Filename:Ascending".
        private void RestoreFileListSort(string fileListSort)
        {
            var sortDescriptions = new List<SortDescription>();
            foreach (var item in fileListSort.Split(','))
            {
                var parts = item.Split(':');
                if (parts.Length != 2 || !Enum.TryParse<ListSortDirection>(parts[1], out var direction) || !gridFileResults.Columns.Any(x => x.SortMemberPath == parts[0]))
                {
                    // Keep the default sort order if the setting isn't valid.
                    return;
                }

                sortDescriptions.Add(new SortDescription(parts[0], direction));
            }

            var collectionViewSource = (CollectionViewSource)FindResource("FileResultItemsCollectionViewSource");
            collectionViewSource.SortDescriptions.Clear();
            foreach (var sortDescription in sortDescriptions)
            {
                collectionViewSource.SortDescriptions.Add(sortDescription);
            }

            // The column sort directions decide which way the next header click sorts.
            foreach (var column in gridFileResults.Columns)
            {
                column.SortDirection = sortDescriptions.Where(x => x.PropertyName == column.SortMemberPath).Select(x => (ListSortDirection?)x.Direction).FirstOrDefault();
            }
        }

        private static void SetConfigValue(Configuration config, string key, string value)
        {
            if (config.AppSettings.Settings[key] != null)
            {
                config.AppSettings.Settings[key].Value = value;
            }
            else
            {
                config.AppSettings.Settings.Add(key, value);
            }
        }

        private void OnClosing(object? sender, EventArgs e)
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            if (WindowState != WindowState.Minimized)
            {
                SetConfigValue(config, "MainWindowLeft", Left.ToString());
                SetConfigValue(config, "MainWindowTop", Top.ToString());
                SetConfigValue(config, "MainWindowWidth", Width.ToString());
                SetConfigValue(config, "MainWindowHeight", Height.ToString());
                SetConfigValue(config, "MainWindowState", ((int)WindowState).ToString());

                var gridLengthConverter = new GridLengthConverter();
                var gridFileResultsWidthStr = gridLengthConverter.ConvertToString(gridResults.ColumnDefinitions[0].Width);
                var gridSplitterWidthStr = gridLengthConverter.ConvertToString(gridResults.ColumnDefinitions[1].Width);
                var gridResultLinesWidthStr = gridLengthConverter.ConvertToString(gridResults.ColumnDefinitions[2].Width);

                if (gridFileResultsWidthStr != null && gridSplitterWidthStr != null && gridResultLinesWidthStr != null)
                {
                    SetConfigValue(config, "GridFileResultsWidth", gridFileResultsWidthStr);
                    SetConfigValue(config, "GridSplitterWidth", gridSplitterWidthStr);
                    SetConfigValue(config, "GridResultLinesWidth", gridResultLinesWidthStr);
                }
                else
                {
                    // Unable to get the current values for some reason.  Save default values instead.
                    SetConfigValue(config, "GridFileResultsWidth", "*");
                    SetConfigValue(config, "GridSplitterWidth", GRID_SPLITTER_WIDTH.ToString("N0"));
                    SetConfigValue(config, "GridResultLinesWidth", "*");
                }
            }

            SetConfigValue(config, "BasePath", txtBasePath.Text);
            SetConfigValue(config, "IncludeFiles", txtIncludeFiles.Text);
            SetConfigValue(config, "ExcludeFiles", txtExcludeFiles.Text);
            SetConfigValue(config, "ContainingText", txtContainingText.Text);
            SetConfigValue(config, "CaseSensitive", (chkCaseSensitive.IsChecked ?? DEFAULT_CASESENSITIVE).ToString());
            SetConfigValue(config, "Recursive", (chkRecursive.IsChecked ?? DEFAULT_RECURSIVE).ToString());
            SetConfigValue(config, "RegularExpression", (chkRegularExpression.IsChecked ?? DEFAULT_REGULAREXPRESSION).ToString());

            SetConfigValue(config, "FileListSort", string.Join(",", gridFileResults.Items.SortDescriptions.Select(x => $"{x.PropertyName}:{x.Direction}")));

            SetConfigValue(config, "FileEncoding", ((ComboBoxItem)cmbEncoding.SelectedItem).Name);
            SetConfigValue(config, "MaxFileSize", txtMaxFileSize.Text);
            SetConfigValue(config, "MaxFileSizeUnit", ((ComboBoxItem)cmbFileSizeUnit.SelectedItem).Name);
            SetConfigValue(config, "Theme", m_currentTheme.ToString());
            SetConfigValue(config, "MultipleHighlightColors", m_multipleHighlightColors.ToString());
            SetConfigValue(config, "MaxLineHighlights", m_maxLineHighlights.ToString());
            SetConfigValue(config, "ContextLinesBefore", m_contextLinesBefore.ToString());
            SetConfigValue(config, "ContextLinesAfter", m_contextLinesAfter.ToString());

            SetConfigValue(config, "FileViewerPath", m_fileViewerPath);
            SetConfigValue(config, "FileViewerArgs", m_fileViewerArgs);

            config.Save();

            ConfigurationManager.RefreshSection("appSettings");
        }

        private void OnFileAdded(object? sender, (string path, string filename) result)
        {
            DateTime? modified = null;
            DateTime? created = null;
            try
            {
                var fileInfo = new FileInfo(Path.Combine(result.path, result.filename));
                if (fileInfo.Exists)
                {
                    modified = fileInfo.LastWriteTime;
                    created = fileInfo.CreationTime;
                }
            }
            catch (Exception)
            {
            }

            Application.Current.Dispatcher.Invoke(delegate
            {
                // Ensure the same result won't be added multiple times.
                if (!FileResultItems.Any(x => x.Path == result.path && x.Filename == result.filename))
                {
                    FileResultItems.Add(new FileSearchResult(result.path, result.filename, modified, created));
                    txtFileListStatus.Text = $"Found {FileResultItems.Count} files.";
                }
            });
        }

        private void gridFileResults_MouseDown(object? sender, MouseEventArgs e)
        {
            if ((e.RightButton == MouseButtonState.Pressed && !SystemParameters.SwapButtons) || (e.LeftButton == MouseButtonState.Pressed && SystemParameters.SwapButtons))
            {
                var selectedFiles = new List<FileInfo>();

                foreach (var selectedItem in gridFileResults.SelectedItems)
                {
                    if (selectedItem is FileSearchResult fileSearchResult)
                    {
                        selectedFiles.Add(new FileInfo(Path.Combine(fileSearchResult.Path, fileSearchResult.Filename)));
                    }
                }

                if (selectedFiles.Any())
                {
                    var point = PointToScreen(e.MouseDevice.GetPosition(this));

                    var shellContextMenu = new ShellContextMenu();
                    shellContextMenu.ShowContextMenu(selectedFiles, new System.Drawing.Point((int)point.X, (int)point.Y));
                }
            }
        }

        private void gridFileResults_Sorting(object sender, DataGridSortingEventArgs e)
        {
            // Like Explorer, the first click on a date column shows the newest files first.
            // The DataGrid reverses the current direction, so mark it ascending to get descending.
            if (e.Column.SortDirection == null && (e.Column.SortMemberPath == nameof(FileSearchResult.Modified) || e.Column.SortMemberPath == nameof(FileSearchResult.Created)))
            {
                e.Column.SortDirection = ListSortDirection.Ascending;
            }
        }

        private void gridFileResults_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                if (e.AddedItems[0] is FileSearchResult addedItem)
                {
                    ShowResultLines(addedItem);
                }
            }
        }

        private async void ShowResultLines(FileSearchResult file)
        {
            // Scroll gridResultLines back to left end.
            GetScrollViewer(gridResultLines)?.ScrollToLeftEnd();

            ResultLineItems.Reset(Enumerable.Empty<ResultLine>());
            var version = ++m_resultLinesVersion;

            var lineResults = m_ripGrepWrapper.FileResults.Where(x => x.Key.path == file.Path && x.Key.filename == file.Filename)
                .Select(x => (lineNumber: x.Key.lineNumber, lineResult: x.Value))
                .OrderBy(x => x.lineNumber)
                .ToList();

            if (m_contextLinesBefore == 0 && m_contextLinesAfter == 0)
            {
                foreach (var lineResult in lineResults)
                {
                    ResultLineItems.Add(new ResultLine(lineResult.lineNumber, GetColorizedString(lineResult.lineResult.LineContent, lineResult.lineResult.TermResults).Trim()));
                }

                txtResultLineStatus.Text = $"{ResultLineItems.Count} lines matched.";
                return;
            }

            txtResultLineStatus.Text = "Loading...";

            var filePath = Path.Combine(file.Path, file.Filename);
            var linesBefore = m_contextLinesBefore;
            var linesAfter = m_contextLinesAfter;
            var useGbk = m_searchEncoding == FileEncoding.GBK;

            List<ResultLine> rows;
            string? error;
            try
            {
                (rows, error) = await Task.Run(() => GetResultLinesWithContext(filePath, lineResults, linesBefore, linesAfter, useGbk));
            }
            catch (Exception)
            {
                (rows, error) = (GetResultLinesWithoutContext(lineResults), "unexpected error");
            }

            // Another file was selected (or a new search started) while this one was loading.
            if (version != m_resultLinesVersion)
            {
                return;
            }

            ResultLineItems.Reset(rows);

            txtResultLineStatus.Text = error == null
                ? $"{lineResults.Count} lines matched."
                : $"{lineResults.Count} lines matched. No context lines: {error}.";
        }

        private (List<ResultLine> rows, string? error) GetResultLinesWithContext(string filePath, List<(int lineNumber, LineResult lineResult)> lineResults, int linesBefore, int linesAfter, bool useGbk)
        {
            var (lines, error) = ContextLineReader.ReadLines(filePath, useGbk);

            if (lines != null && !ContextLineReader.MatchesFile(lines, lineResults.Select(x => (x.lineNumber, x.lineResult.LineContent))))
            {
                (lines, error) = (null, "file has changed since the search");
            }

            if (lines == null)
            {
                return (GetResultLinesWithoutContext(lineResults), error);
            }

            var matches = lineResults.ToDictionary(x => x.lineNumber, x => x.lineResult);
            var rows = new List<ResultLine>();

            foreach (var (start, end) in ContextLineReader.GetBlocks(matches.Keys, linesBefore, linesAfter, lines.Length))
            {
                if (rows.Count > 0)
                {
                    rows.Add(ResultLine.Separator(start - 0.5));
                }

                for (var line = start; line <= end; line++)
                {
                    // Only trim the end, so indentation lines up between match and context rows.
                    if (matches.TryGetValue(line, out var lineResult))
                    {
                        rows.Add(new ResultLine(line, GetColorizedString(lineResult.LineContent, lineResult.TermResults).TrimEnd()));
                    }
                    else
                    {
                        rows.Add(new ResultLine(line, EscapeString(lines[line - 1].TrimEnd()), isContext: true));
                    }
                }
            }

            return (rows, null);
        }

        private List<ResultLine> GetResultLinesWithoutContext(List<(int lineNumber, LineResult lineResult)> lineResults)
        {
            return lineResults.Select(x => new ResultLine(x.lineNumber, GetColorizedString(x.lineResult.LineContent, x.lineResult.TermResults).Trim())).ToList();
        }

        private void grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F3)
            {
                return;
            }

            OpenFileViewer();
        }

        private void grid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGridRow row)
            {
                row.IsSelected = true;
                row.Focus();
            }
        }

        private void grid_RequestBringIntoViewHandler(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void gridResultLines_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (string.IsNullOrEmpty(m_fileViewerPath) || string.IsNullOrEmpty(m_fileViewerArgs))
            {
                e.Handled = true;
            }

            // Separator rows have no line to open.
            if (gridResultLines.SelectedItems.Count > 0 && gridResultLines.SelectedItems[gridResultLines.SelectedItems.Count - 1] is ResultLine { IsSeparator: true })
            {
                e.Handled = true;
            }
        }

        private static ScrollViewer? GetScrollViewer(UIElement? element)
        {
            if (element == null)
            {
                return null;
            }

            ScrollViewer? result = null;
            for (var i = 0; result == null && i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (child is ScrollViewer scrollViewer)
                {
                    result = scrollViewer;
                }
                else
                {
                    result = GetScrollViewer(child as UIElement);
                }
            }
            return result;
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if ((txtBasePath.Text.IndexOfAny(Path.GetInvalidPathChars()) != -1) || !Directory.Exists(txtBasePath.Text))
            {
                MessageBox.Show("Invalid \"In Folder\" path.", "Error");
                return;
            }

            // based on https://stackoverflow.com/questions/66598956/how-to-stop-a-method-triggered-by-button-click-in-wpf

            if (m_cancellationTokenSource != null)
            {
                return;
            }

            // Based on https://stackoverflow.com/questions/52194058/regex-with-escaped-double-quotes
            var searchTerms = Regex.Matches(txtContainingText.Text, @"""[^""\\]*(?:\\.[^""\\]*)*""|([^\s])+|[^\s""]+");
            if (searchTerms.Count < 1)
            {
                return;
            }

            // Sanity check -- allow minimum of one search term.
            if (m_maxSearchTerms < 1)
            {
                m_maxSearchTerms = 1;
            }

            if (searchTerms.Count > m_maxSearchTerms)
            {
                MessageBox.Show($"Search text contains more than {m_maxSearchTerms} terms.");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            btnStart.IsEnabled = false;
            btnCancel.IsEnabled = true;
            btnSettings.IsEnabled = false;
            var cancellationTokenSource = new CancellationTokenSource();
            m_cancellationTokenSource = cancellationTokenSource;

            ResultLineItems.Reset(Enumerable.Empty<ResultLine>());
            m_resultLinesVersion++;
            txtFileListStatus.Text = string.Empty;
            txtResultLineStatus.Text = string.Empty;

            m_ripGrepWrapper.Clear();

            var startPath = txtBasePath.Text;
            if (startPath.EndsWith(Path.DirectorySeparatorChar))
            {
                startPath = startPath.TrimEnd(Path.DirectorySeparatorChar);
            }

            try
            {
                var searchParameters = new SearchParameters
                {
                    StartPath = startPath,
                    SearchStrings = searchTerms.Cast<Match>().Select(x => x.Value),
                    IgnoreCase = !(chkCaseSensitive.IsChecked ?? false),
                    Recursive = chkRecursive.IsChecked ?? true,
                    IncludePatterns = txtIncludeFiles.Text,
                    ExcludePatterns = txtExcludeFiles.Text,
                    RegularExpression = chkRegularExpression.IsChecked ?? false,
                    Encoding = (FileEncoding)cmbEncoding.SelectedIndex,
                    MaxFileSize = int.Parse(txtMaxFileSize.Text),
                    MaxFileSizeUnit = (MaxFileSizeUnit)cmbFileSizeUnit.SelectedIndex,
                };

                FileResultItems.Reset(Enumerable.Empty<FileSearchResult>());

                // Context lines are read with the encoding used for the search.
                m_searchEncoding = searchParameters.Encoding;

                await m_ripGrepWrapper.Search(searchParameters, cancellationTokenSource.Token);
            }
            finally
            {
                btnCancel.IsEnabled = false;
                btnStart.IsEnabled = true;
                btnSettings.IsEnabled = true;

                m_cancellationTokenSource = null;
                cancellationTokenSource.Cancel();
            }

            stopwatch.Stop();
            txtFileListStatus.Text = $"Found {FileResultItems.Count} files.  Took {stopwatch.Elapsed.TotalSeconds:0.00} seconds.";
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog()
            {
                Description = "Select folder",
                UseDescriptionForTitle = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this).GetValueOrDefault())
            {
                txtBasePath.Text = dialog.SelectedPath;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            m_cancellationTokenSource?.Cancel();
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = this,
                Theme = m_currentTheme.GetName(),
                MaxSearchTerms = m_maxSearchTerms,
                Multicolor = m_multipleHighlightColors,
                MaxLineHighlights = m_maxLineHighlights,
                ContextLinesBefore = m_contextLinesBefore,
                ContextLinesAfter = m_contextLinesAfter,
                FileViewerPath = m_fileViewerPath,
                FileViewerArgs = m_fileViewerArgs
            };

            if (settingsWindow.ShowDialog() == true)
            {
                m_currentTheme = Enum.Parse<ThemeType>(settingsWindow.Theme);
                ThemesController.SetTheme(m_currentTheme);
                m_maxSearchTerms = settingsWindow.MaxSearchTerms;
                m_multipleHighlightColors = settingsWindow.Multicolor;
                m_maxLineHighlights = settingsWindow.MaxLineHighlights;
                m_contextLinesBefore = settingsWindow.ContextLinesBefore;
                m_contextLinesAfter = settingsWindow.ContextLinesAfter;
                m_fileViewerPath = settingsWindow.FileViewerPath;
                m_fileViewerArgs = settingsWindow.FileViewerArgs;

                // Redisplay the selected file so new settings take effect right away.
                if (gridFileResults.SelectedItem is FileSearchResult selectedFile)
                {
                    ShowResultLines(selectedFile);
                }
            }
        }

        private void txtContainingText_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;

                if (btnStart.IsEnabled)
                {
                    btnStart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
        }

        private void txtBasePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFolderSuggestionValues();

            // Based on https://learn.microsoft.com/en-us/answers/questions/840981/auto-complete-for-textbox-in-wpf-(mvvm)
            var input = txtBasePath.Text;
            if (input.Length > m_currentInput.Length && input != m_currentSuggestion)
            {
                m_currentSuggestion = m_folderSuggestionValues.FirstOrDefault(x => x.StartsWith(input, StringComparison.CurrentCultureIgnoreCase));
                if (m_currentSuggestion != null)
                {
                    m_currentText = m_currentSuggestion;
                    m_selectionStart = input.Length;
                    m_selectionLength = m_currentSuggestion.Length - input.Length;

                    txtBasePath.Text = m_currentText;
                    txtBasePath.Select(m_selectionStart, m_selectionLength);
                }
            }
            m_currentInput = input;
        }

        private void UpdateFolderSuggestionValues()
        {
            var input = txtBasePath.Text;

            if (input.EndsWith(Path.DirectorySeparatorChar) && Directory.Exists(input))
            {
                m_folderSuggestionValues = Directory.GetDirectories(input);
            }
        }

        private void txtMaxFileSize_TextChanged(object sender, TextChangedEventArgs e)
        {
            var input = txtMaxFileSize.Text;
            txtMaxFileSize.Text = new string(input.Where(c => char.IsDigit(c)).ToArray());
        }

        private void cmbFileSizeUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            txtMaxFileSize.IsEnabled = (cmbFileSizeUnit.SelectedIndex != 0);
        }

        private void openInFileViewer_Click(object sender, RoutedEventArgs e)
        {
            OpenFileViewer();
        }

        private string GetColorizedString(string source, IEnumerable<TermResult> termResults)
        {
            // Algorithm for calculating highlightRanges was written with assistance from GitHub Copilot AI (GPT-4.1).
            var segmentEdges = new List<int>();
            foreach (var termResult in termResults)
            {
                segmentEdges.Add(termResult.Start);
                segmentEdges.Add(termResult.End + 1);  // +1, next place a segment can start is immediately after the current one.
            }
            var sortedEdges = segmentEdges.Distinct().OrderBy(x => x).ToList();

            var highlightResults = new List<TermResult>();
            TermResult? previous = null;

            for (var i = 0; i < sortedEdges.Count - 1 && highlightResults.Count < m_maxLineHighlights; i++)
            {
                var segmentStart = sortedEdges[i];
                var segmentEnd = sortedEdges[i + 1] - 1;  // -1, this segment ends immediately before the next one begins.

                // Get termResult with the lowest TermIndex overlapping this segment, if any.  This determines the color for this segment.
                var termResult = termResults.Where(x => segmentStart >= x.Start && segmentEnd <= x.End).OrderBy(x => x.TermIndex).FirstOrDefault();
                if (termResult != null)
                {
                    // See if we should expand the previous segment or add a new one.
                    if (previous?.End == segmentStart - 1 && previous?.TermIndex == termResult.TermIndex)
                    {
                        // Previous result is adjacent to the current one, but TermIndex hasn't changed.  Expand previous range.
                        previous.End = segmentEnd;
                    }
                    else
                    {
                        previous = new TermResult(segmentStart, segmentEnd, termResult.TermIndex);
                        highlightResults.Add(previous);
                    }
                }
            }

            var stringBuilder = new StringBuilder();

            var startingIndex = 0;
            foreach (var highlightResult in highlightResults)
            {
                if (startingIndex != highlightResult.Start)
                {
                    stringBuilder.Append(EscapeString(source.Substring(startingIndex, highlightResult.Start - startingIndex)));
                }

                var colorIndex = m_multipleHighlightColors ? highlightResult.TermIndex % HIGHLIGHT_COLORS_COUNT : 0;

                stringBuilder.Append($"<c{colorIndex}>");
                stringBuilder.Append(EscapeString(source.Substring(highlightResult.Start, highlightResult.End - highlightResult.Start + 1)));
                stringBuilder.Append($"</c{colorIndex}>");

                startingIndex = highlightResult.End + 1;
            }

            stringBuilder.Append(EscapeString(source.Substring(startingIndex)));

            return stringBuilder.ToString();
        }

        private static string EscapeString(string source)
        {
            return source.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private void OpenFileViewer()
        {
            if (gridFileResults.SelectedItems.Count <= 0 || gridResultLines.SelectedItems.Count <= 0)
            {
                return;
            }

            var resultFile = gridFileResults.SelectedItems[gridFileResults.SelectedItems.Count - 1] as FileSearchResult;
            var resultLine = gridResultLines.SelectedItems[gridResultLines.SelectedItems.Count - 1] as ResultLine;
            if (resultFile == null || resultLine == null || resultLine.IsSeparator)
            {
                return;
            }

            if (!string.IsNullOrEmpty(m_fileViewerPath) && File.Exists(m_fileViewerPath) && !string.IsNullOrEmpty(m_fileViewerArgs) && m_fileViewerArgs.Contains("$FILE"))
            {
                var args = m_fileViewerArgs
                    .Replace("$FILE", $"\"{Path.Combine(resultFile.Path, resultFile.Filename)}\"")
                    .Replace("$LINE", resultLine.Line.ToString());

                var processStartInfo = new ProcessStartInfo()
                {
                    FileName = m_fileViewerPath,
                    Arguments = args,
                    UseShellExecute = false
                };

                using var process = new Process()
                {
                    StartInfo = processStartInfo
                };
                process.Start();
            }
        }
    }
}

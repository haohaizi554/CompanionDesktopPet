using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.UI;

public partial class DeveloperModeWindow : Window
{
    private readonly Action<DialogueLine> _speak;
    private CorpusFolder? _folder;
    private CorpusFolder? _pendingFolder;
    private int _selectionAttempts;

    public DeveloperModeWindow(CorpusFolder root, Action<DialogueLine> speak)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(speak);
        InitializeComponent();
        _speak = speak;
        CorpusTree.ItemsSource = new[] { root };
        Loaded += (_, _) =>
        {
            FitDirectoryColumn(root);
            ApplyPendingSelection();
        };
    }

    public void SelectFolder(CorpusFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        _pendingFolder = folder;
        if (IsLoaded)
        {
            ApplyPendingSelection();
        }
    }

    private void FitDirectoryColumn(CorpusFolder root)
    {
        var probe = new TextBlock
        {
            FontFamily = CorpusTree.FontFamily,
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap
        };
        var widest = 0d;
        MeasureFolder(root, 0, probe, ref widest);
        DirectoryColumn.Width = new GridLength(Math.Max(200, widest + 28));
    }

    private static void MeasureFolder(CorpusFolder folder, int depth, TextBlock probe, ref double widest)
    {
        probe.Text = folder.Header;
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        widest = Math.Max(widest, probe.DesiredSize.Width + (depth * 16) + 36);
        foreach (var child in folder.Children)
        {
            MeasureFolder(child, depth + 1, probe, ref widest);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyPendingSelection()
    {
        if (_pendingFolder is not { } folder)
        {
            return;
        }

        var path = new List<CorpusFolder>();
        foreach (CorpusFolder root in CorpusTree.Items)
        {
            if (TryFind(root, folder, path))
            {
                break;
            }

            path.Clear();
        }

        if (path.Count == 0)
        {
            return;
        }

        ItemsControl parent = CorpusTree;
        TreeViewItem? container = null;
        foreach (var node in path)
        {
            parent.UpdateLayout();
            container = parent.ItemContainerGenerator.ContainerFromItem(node) as TreeViewItem;
            if (container is null)
            {
                if (_selectionAttempts++ < 6)
                {
                    Dispatcher.BeginInvoke(ApplyPendingSelection, DispatcherPriority.Loaded);
                }

                return;
            }

            container.IsExpanded = true;
            container.UpdateLayout();
            parent = container;
        }

        container!.IsSelected = true;
        container.BringIntoView();
        _pendingFolder = null;
        _selectionAttempts = 0;
    }

    private static bool TryFind(CorpusFolder node, CorpusFolder target, List<CorpusFolder> path)
    {
        path.Add(node);
        if (ReferenceEquals(node, target))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryFind(child, target, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private void CorpusTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CorpusFolder folder)
        {
            _folder = folder;
            RefreshLines();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshLines();

    private void RefreshLines()
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        FolderTitleText.Text = _folder?.Title ?? "全库";
        var source = _folder?.Lines ?? [];
        var query = SearchBox.Text.Trim();
        IEnumerable<DialogueLine> lines = source;
        if (query.Length > 0)
        {
            lines = source.Where(line =>
                line.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                || line.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || line.TopicId.Contains(query, StringComparison.OrdinalIgnoreCase)
                || line.SemanticGroup.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var rows = lines.Select(line => new CorpusLineRow(line)).ToArray();
        LineList.ItemsSource = rows;
        LineCountText.Text = $"{rows.Length} 条";
        if (LineList.SelectedItem is null && rows.Length > 0)
        {
            LineList.SelectedIndex = 0;
        }
    }

    private void LineList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineList.SelectedItem is CorpusLineRow row)
        {
            LineDetailText.Text = row.Detail;
            LineDetailText.ToolTip = row.Detail;
        }
        else
        {
            const string hint = "选中一句，这里会记下它的编号和语气。";
            LineDetailText.Text = hint;
            LineDetailText.ToolTip = hint;
        }
    }

    private void LineList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => SpeakSelectedLine();

    private void SpeakLine_Click(object sender, RoutedEventArgs e) => SpeakSelectedLine();

    private void SpeakSelectedLine()
    {
        if (LineList.SelectedItem is CorpusLineRow row)
        {
            _speak(row.Line);
        }
    }

    private sealed class CorpusLineRow
    {
        public CorpusLineRow(DialogueLine line)
        {
            Line = line;
            Display = line.Text;
            Meta = $"{CorpusLabels.Category(line.Category)}  ·  {line.Tone}";
            Detail =
                $"{line.Id}    {line.TopicId} / {line.SemanticGroup}    语气 {line.Tone}    权重 {line.Weight}    冷却 {line.CooldownHours} 小时    打断 {line.InterruptionCost}    触发 {line.Trigger}";
        }

        public DialogueLine Line { get; }

        public string Display { get; }

        public string Meta { get; }

        public string Detail { get; }
    }
}

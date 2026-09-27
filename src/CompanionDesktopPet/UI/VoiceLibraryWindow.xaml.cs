using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.UI;

internal partial class VoiceLibraryWindow : Window
{
    private readonly VoiceLibraryFolder _root;
    private VoiceLibraryFolder? _folder;
    private VoiceLibraryFolder? _pendingFolder;
    private SoundPlayer? _player;
    private int _selectionAttempts;

    internal VoiceLibraryWindow(VoiceLibraryFolder root)
    {
        ArgumentNullException.ThrowIfNull(root);
        InitializeComponent();
        _root = root;
        LibraryTree.ItemsSource = new[] { root };
        Closed += (_, _) => StopPlayback();
        Loaded += (_, _) =>
        {
            FitDirectoryColumn(root);
            ApplyPendingSelection();
        };
    }

    internal void SelectFolder(VoiceLibraryFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        _pendingFolder = folder;
        if (IsLoaded)
        {
            ApplyPendingSelection();
        }
    }

    private void FitDirectoryColumn(VoiceLibraryFolder root)
    {
        var probe = new TextBlock
        {
            FontFamily = LibraryTree.FontFamily,
            FontSize = 13,
            TextWrapping = TextWrapping.NoWrap
        };
        var widest = 0d;
        MeasureFolder(root, 0, probe, ref widest);
        DirectoryColumn.Width = new GridLength(Math.Max(160, widest + 28));
    }

    private static void MeasureFolder(VoiceLibraryFolder folder, int depth, TextBlock probe, ref double widest)
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

        var path = new List<VoiceLibraryFolder>();
        foreach (VoiceLibraryFolder root in LibraryTree.Items)
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

        ItemsControl parent = LibraryTree;
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

    private static bool TryFind(VoiceLibraryFolder node, VoiceLibraryFolder target, List<VoiceLibraryFolder> path)
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

    private void LibraryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is VoiceLibraryFolder folder)
        {
            _folder = folder;
            RefreshClips();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshClips();

    private void RefreshClips()
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        FolderTitleText.Text = _folder?.Title ?? _root.Title;
        var source = _folder?.Clips ?? _root.Clips;
        var query = SearchBox.Text.Trim();
        IEnumerable<VoiceLibraryClip> clips = source;
        if (query.Length > 0)
        {
            clips = source.Where(clip =>
                clip.Prompt.Contains(query, StringComparison.OrdinalIgnoreCase)
                || clip.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var rows = clips.ToArray();
        ClipList.ItemsSource = rows;
        ClipCountText.Text = $"{rows.Length} 条";
        if (ClipList.SelectedItem is null && rows.Length > 0)
        {
            ClipList.SelectedIndex = 0;
        }
    }

    private void ClipList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClipList.SelectedItem is VoiceLibraryClip clip)
        {
            ClipDetailText.Text = $"{clip.Id}    {clip.Meta}    {clip.Prompt}";
        }
        else
        {
            ClipDetailText.Text = "选中一条，这里会记下编号、语气和原文。";
        }
    }

    private void ClipList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PlaySelected();

    private void PlayClip_Click(object sender, RoutedEventArgs e) => PlaySelected();

    private void PlaySelected()
    {
        if (ClipList.SelectedItem is not VoiceLibraryClip clip)
        {
            return;
        }

        if (!File.Exists(clip.AudioPath))
        {
            ClipDetailText.Text = $"{clip.Id} 的参考音频不在旁边。";
            return;
        }

        try
        {
            StopPlayback();
            _player = new SoundPlayer(clip.AudioPath);
            _player.Play();
            ClipDetailText.Text = $"正在听 {clip.Id}。{clip.Meta}    {clip.Prompt}";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or FileNotFoundException)
        {
            ClipDetailText.Text = $"{clip.Id} 这段参考放不出来。";
        }
    }

    private void StopPlayback()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
    }
}

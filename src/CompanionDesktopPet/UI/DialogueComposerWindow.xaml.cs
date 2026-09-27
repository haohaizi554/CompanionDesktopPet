using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using CompanionDesktopPet.Services;

namespace CompanionDesktopPet.UI;

internal partial class DialogueComposerWindow : Window
{
    private const int ExtendedStyle = -20;
    private const int ToolWindow = 0x00000080;

    private bool _allowClose;
    private bool _imeComposing;
    private bool _toolStyled;

    public DialogueComposerWindow()
    {
        InitializeComponent();
        Input.AddHandler(
            TextCompositionManager.PreviewTextInputStartEvent,
            new TextCompositionEventHandler((_, _) => _imeComposing = true),
            handledEventsToo: true);
        Input.AddHandler(
            TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler((_, _) => _imeComposing = false),
            handledEventsToo: true);
        SourceInitialized += (_, _) =>
        {
            if (_toolStyled)
            {
                return;
            }

            _toolStyled = true;
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, ExtendedStyle);
            if ((style.ToInt64() & ToolWindow) == 0)
            {
                SetWindowLongPtr(handle, ExtendedStyle, new IntPtr(style.ToInt64() | ToolWindow));
            }
        };
    }

    public event EventHandler? SendRequested;

    public string Draft => Input.Text;

    public bool HasDraft => Input.Text.Length > 0;

    public void SetDraft(string text) => Input.Text = text;

    public void ClearDraft() => Input.Text = string.Empty;

    public void FocusInput()
    {
        if (!IsKeyboardFocusWithin)
        {
            Activate();
        }

        Input.Focus();
    }

    public Size MeasureSurface()
    {
        if (Surface.ActualWidth > 1 && Surface.ActualHeight > 1)
        {
            return new Size(Surface.ActualWidth, Surface.ActualHeight);
        }

        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Surface.DesiredSize.Width > 1 ? Surface.DesiredSize.Width : ActualWidth;
        var height = Surface.DesiredSize.Height > 1 ? Surface.DesiredSize.Height : ActualHeight;
        if (width < 1)
        {
            width = 316;
        }

        if (height < 1)
        {
            height = 96;
        }

        return new Size(width, height);
    }

    public void ApplySide(DialoguePlacementSide side)
    {
        ArrowUp.Visibility = side == DialoguePlacementSide.Below ? Visibility.Visible : Visibility.Collapsed;
        ArrowDown.Visibility = side == DialoguePlacementSide.Above ? Visibility.Visible : Visibility.Collapsed;
        ArrowLeft.Visibility = side == DialoguePlacementSide.Right ? Visibility.Visible : Visibility.Collapsed;
        ArrowRight.Visibility = side == DialoguePlacementSide.Left ? Visibility.Visible : Visibility.Collapsed;
    }

    public void CloseForShutdown()
    {
        _allowClose = true;
        if (IsLoaded)
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    private void Send_Click(object sender, RoutedEventArgs e) =>
        SendRequested?.Invoke(this, EventArgs.Empty);

    private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (_imeComposing || ImeCaret.IsComposing(Input))
        {
            return;
        }

        e.Handled = true;
        SendRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        Hint.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InputMenu_Opened(object sender, RoutedEventArgs e)
    {
        var selected = Input.SelectionLength > 0;
        CutItem.IsEnabled = selected && !Input.IsReadOnly;
        CopyItem.IsEnabled = selected;
        try
        {
            PasteItem.IsEnabled = Clipboard.ContainsText();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            PasteItem.IsEnabled = true;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}

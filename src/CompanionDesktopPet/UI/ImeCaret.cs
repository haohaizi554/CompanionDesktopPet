using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace CompanionDesktopPet.UI;

internal static class ImeCaret
{
    private const int GcsCompStr = 0x0008;
    private const int CfsPoint = 0x0020;
    private const int CfsExclude = 0x0080;

    public static void MoveTo(TextBox box)
    {
        if (PresentationSource.FromVisual(box) is not HwndSource source)
        {
            return;
        }

        var caret = box.GetRectFromCharacterIndex(box.CaretIndex);
        if (caret.IsEmpty)
        {
            caret = new Rect(0, Math.Max(0, box.ActualHeight - 2), 1, 2);
        }

        var screen = box.PointToScreen(new Point(caret.Left, caret.Bottom));
        var point = new PointNative
        {
            X = (int)Math.Round(screen.X),
            Y = (int)Math.Round(screen.Y)
        };
        if (!ScreenToClient(source.Handle, ref point))
        {
            return;
        }

        var context = ImmGetContext(source.Handle);
        if (context == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var composition = new CompositionForm
            {
                Style = CfsPoint,
                Current = point
            };
            ImmSetCompositionWindow(context, ref composition);
            var candidate = new CandidateForm
            {
                Index = 0,
                Style = CfsExclude,
                Current = point,
                Area = new RectNative
                {
                    Left = point.X,
                    Top = point.Y - (int)Math.Max(18, caret.Height),
                    Right = point.X + Math.Max(1, (int)Math.Round(caret.Width)),
                    Bottom = point.Y
                }
            };
            ImmSetCandidateWindow(context, ref candidate);
        }
        finally
        {
            ImmReleaseContext(source.Handle, context);
        }
    }

    public static bool IsComposing(TextBox box)
    {
        if (PresentationSource.FromVisual(box) is not HwndSource source)
        {
            return false;
        }

        var context = ImmGetContext(source.Handle);
        if (context == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return ImmGetCompositionString(context, GcsCompStr, IntPtr.Zero, 0) > 0;
        }
        finally
        {
            ImmReleaseContext(source.Handle, context);
        }
    }

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hwnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr context);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetCompositionWindow(IntPtr context, ref CompositionForm form);

    [DllImport("imm32.dll")]
    private static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);

    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionString(IntPtr context, int index, IntPtr buffer, int length);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref PointNative point);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm
    {
        public int Style;
        public PointNative Current;
        public RectNative Area;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CandidateForm
    {
        public int Index;
        public int Style;
        public PointNative Current;
        public RectNative Area;
    }
}

using ProjectEye.Core;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ProjectEye.Views
{
    /// <summary>
    /// LongRestWindow.xaml 的交互逻辑
    /// 长休息倒计时窗口，显示在所属屏幕右下角，鼠标穿透且不抢占焦点
    /// </summary>
    public partial class LongRestWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int SW_SHOWNOACTIVATE = 4;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        private bool isWarmed;
        private bool isWarming;

        public LongRestWindow()
        {
            InitializeComponent();
            SourceInitialized += LongRestWindow_SourceInitialized;
            Loaded += LongRestWindow_Loaded;
        }

        private void LongRestWindow_SourceInitialized(object sender, EventArgs e)
        {
            if (!isWarming)
            {
                PlaceOnScreen();
            }
        }

        private void LongRestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!isWarming)
            {
                PlaceOnScreen();
            }
        }

        /// <summary>
        /// 在屏幕外用正常不透明度完成第一次 Show/Hide。
        /// 透明预热不会真正绘制，同一轮消息里连续 Show/Hide/Show 也不会上屏。
        /// </summary>
        public void WarmUp()
        {
            if (isWarmed)
            {
                return;
            }
            isWarming = true;
            ShowActivated = true;
            Left = -10000;
            Top = -10000;
            Opacity = 1;
            Show();
            Hide();
            ShowActivated = false;
            isWarming = false;
            isWarmed = true;
            PlaceOnScreen();
        }

        /// <summary>
        /// 定位到所属屏幕工作区右下角
        /// </summary>
        public void PlaceOnScreen()
        {
            var deviceName = Tag as string;
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            if (!string.IsNullOrEmpty(deviceName))
            {
                foreach (var s in System.Windows.Forms.Screen.AllScreens)
                {
                    if (s.DeviceName == deviceName)
                    {
                        screen = s;
                        break;
                    }
                }
            }
            var size = WindowManager.GetSize(screen);
            var margin = WindowManager.ToDips(16, size.XDPI);
            Left = WindowManager.ToDips(screen.WorkingArea.Right, size.XDPI) - Width - margin;
            Top = WindowManager.ToDips(screen.WorkingArea.Bottom, size.YDPI) - Height - margin;
        }

        /// <summary>
        /// 强制显示窗口（不抢焦点）
        /// </summary>
        public void EnsureShown()
        {
            Opacity = 1;
            Visibility = Visibility.Visible;
            PlaceOnScreen();
            Topmost = false;
            Topmost = true;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                RECT rect;
                GetWindowRect(hwnd, out rect);
                LogHelper.Warning($"[LongRest] EnsureShown Left={Left:0} Top={Top:0} W={Width} H={Height} IsVisible={IsVisible} Opacity={Opacity} hwndVisible={IsWindowVisible(hwnd)} rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})");
            }
            //样式放到下一帧再设，避免挡住分层窗口的首次合成
            Dispatcher.BeginInvoke(new Action(ApplyClickThrough), DispatcherPriority.ApplicationIdle);
        }

        private void ApplyClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }
    }
}

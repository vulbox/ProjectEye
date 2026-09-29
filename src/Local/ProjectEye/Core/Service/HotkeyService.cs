using ProjectEye.Core.Models.Options;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ProjectEye.Core.Service
{
    /// <summary>
    /// 全局热键服务
    /// 用于注册系统级热键，实现任何时间通过热键立即进入休息
    /// </summary>
    public class HotkeyService : IService
    {
        private const int WM_HOTKEY = 0x0312;
        /// <summary>
        /// 立即休息热键ID
        /// </summary>
        private const int HOTKEY_ID_RESET = 0x0001;
        /// <summary>
        /// 长休息倒计时跳过（显隐）热键ID
        /// </summary>
        private const int HOTKEY_ID_SKIP = 0x0002;

        #region win32
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        #endregion

        #region 热键修饰键
        private const uint MOD_ALT = 0x1;
        private const uint MOD_CONTROL = 0x2;
        private const uint MOD_SHIFT = 0x4;
        private const uint MOD_WIN = 0x8;
        #endregion

        private readonly ConfigService config;
        private readonly MainService main;
        private readonly RestService reset;
        private readonly LongRestService longRest;
        private HwndSource source;
        private IntPtr hwnd = IntPtr.Zero;
        private readonly HwndSourceHook hwndSourceHook;
        private bool isRegistered;
        private bool isSkipRegistered;

        public HotkeyService(App app,
            ConfigService config,
            MainService main,
            RestService reset,
            LongRestService longRest)
        {
            this.config = config;
            this.main = main;
            this.reset = reset;
            this.longRest = longRest;
            hwndSourceHook = new HwndSourceHook(WndProc);
            app.Exit += new ExitEventHandler(app_Exit);
            this.longRest.RunningChanged += new EventHandler(longRest_RunningChanged);
        }

        public void Init()
        {
            //创建一个隐藏的窗口，用于接收热键消息
            Window hookWindow = new Window();
            hookWindow.Width = 0;
            hookWindow.Height = 0;
            hookWindow.ShowInTaskbar = false;
            hookWindow.WindowStyle = WindowStyle.None;
            hookWindow.Visibility = Visibility.Hidden;
            hookWindow.SourceInitialized += new EventHandler(hookWindow_SourceInitialized);
            hookWindow.Show();

            //热键配置更改时重新注册
            config.Changed += new EventHandler(config_Changed);
        }

        private void hookWindow_SourceInitialized(object sender, EventArgs e)
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper((Window)sender).Handle);
            hwnd = source.Handle;
            source.AddHook(hwndSourceHook);
            Register();
        }

        private void config_Changed(object sender, EventArgs e)
        {
            var oldOptions = sender as OptionsModel;
            if (oldOptions.KeyboardShortcuts.ResetNow != config.options.KeyboardShortcuts.ResetNow)
            {
                //热键配置更改，重新注册
                Register();
            }
            if (oldOptions.KeyboardShortcuts.NoReset != config.options.KeyboardShortcuts.NoReset)
            {
                UpdateSkipHotkey();
            }
        }

        private void longRest_RunningChanged(object sender, EventArgs e)
        {
            UpdateSkipHotkey();
        }

        #region 注册热键
        /// <summary>
        /// 注册立即休息全局热键
        /// </summary>
        private void Register()
        {
            //先取消之前的注册
            Unregister();
            var hotkey = config.options.KeyboardShortcuts.ResetNow;
            if (string.IsNullOrEmpty(hotkey))
            {
                return;
            }
            uint modifiers, vk;
            if (TryParseHotkey(hotkey, out modifiers, out vk))
            {
                if (RegisterHotKey(hwnd, HOTKEY_ID_RESET, modifiers, vk))
                {
                    isRegistered = true;
                    Debug.WriteLine("全局热键注册成功：" + hotkey);
                }
                else
                {
                    LogHelper.Warning("全局热键注册失败，可能已被其他程序占用：" + hotkey);
                }
            }
            else
            {
                LogHelper.Warning("全局热键配置无法解析：" + hotkey);
            }
        }

        /// <summary>
        /// 取消热键注册
        /// </summary>
        private void Unregister()
        {
            if (isRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_RESET);
                isRegistered = false;
            }
        }

        /// <summary>
        /// 长休息倒计时进行中时注册跳过快捷键（仅此时占用全局热键，避免平时抢走按键）
        /// </summary>
        private void UpdateSkipHotkey()
        {
            UnregisterSkip();
            if (!longRest.IsRunning || hwnd == IntPtr.Zero)
            {
                return;
            }
            var hotkey = config.options.KeyboardShortcuts.NoReset;
            if (string.IsNullOrEmpty(hotkey))
            {
                return;
            }
            uint modifiers, vk;
            if (TryParseHotkey(hotkey, out modifiers, out vk))
            {
                if (RegisterHotKey(hwnd, HOTKEY_ID_SKIP, modifiers, vk))
                {
                    isSkipRegistered = true;
                    Debug.WriteLine("长休息跳过热键注册成功：" + hotkey);
                }
                else
                {
                    LogHelper.Warning("长休息跳过热键注册失败，可能已被其他程序占用：" + hotkey);
                }
            }
            else
            {
                LogHelper.Warning("长休息跳过热键配置无法解析：" + hotkey);
            }
        }

        private void UnregisterSkip()
        {
            if (isSkipRegistered)
            {
                UnregisterHotKey(hwnd, HOTKEY_ID_SKIP);
                isSkipRegistered = false;
            }
        }
        #endregion

        #region 解析热键配置字符串
        /// <summary>
        /// 解析热键配置字符串（如：CTRL + ALT + R）
        /// </summary>
        /// <param name="hotkey">热键配置字符串</param>
        /// <param name="modifiers">修饰键</param>
        /// <param name="vk">虚拟键码</param>
        /// <returns>解析成功返回true</returns>
        private static bool TryParseHotkey(string hotkey, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            Key? key = null;
            var parts = hotkey.Split(new string[] { "+" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var name = part.Trim();
                switch (name.ToUpper())
                {
                    case "CTRL":
                        modifiers |= MOD_CONTROL;
                        break;
                    case "ALT":
                        modifiers |= MOD_ALT;
                        break;
                    case "SHIFT":
                        modifiers |= MOD_SHIFT;
                        break;
                    case "WIN":
                        modifiers |= MOD_WIN;
                        break;
                    default:
                        if (key == null)
                        {
                            try
                            {
                                key = (Key)Enum.Parse(typeof(Key), name, true);
                            }
                            catch
                            {
                                return false;
                            }
                        }
                        else
                        {
                            //存在多个非修饰键
                            return false;
                        }
                        break;
                }
            }
            if (key == null || key == Key.None)
            {
                return false;
            }
            vk = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
            return vk != 0;
        }
        #endregion

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_HOTKEY:
                    if (wParam.ToInt32() == HOTKEY_ID_RESET)
                    {
                        //休息进行中时忽略
                        if (!reset.IsRunning && !longRest.IsRunning)
                        {
                            //立即进入休息
                            main.ResetNow();
                        }
                    }
                    else if (wParam.ToInt32() == HOTKEY_ID_SKIP)
                    {
                        //长休息倒计时中：隐藏/恢复右下角窗口，后台继续计时
                        longRest.ToggleCornerWindow();
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private void app_Exit(object sender, ExitEventArgs e)
        {
            Unregister();
            UnregisterSkip();
            if (source != null)
            {
                source.RemoveHook(hwndSourceHook);
                source.Dispose();
            }
        }
    }
}

using ProjectEye.ViewModels;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Input;

namespace ProjectEye.Core.Service
{
    public class KeyboardShortcutsService : IService
    {
        private Dictionary<string, Command> keycommands;
        public KeyboardShortcutsService()
        {
            keycommands = new Dictionary<string, Command>();
        }
        /// <summary>
        /// 设置快捷键命令
        /// </summary>
        /// <param name="key"></param>
        /// <param name="command"></param>
        public void Set(string key, Command command)
        {
            if (keycommands.ContainsKey(key))
            {
                keycommands[key] = command;
            }
            else
            {
                keycommands.Add(key, command);
            }
        }
        public void Init()
        {

        }
        /// <summary>
        /// 执行快捷键命令
        /// </summary>
        /// <param name="keyEventArgs"></param>
        public void Execute(KeyEventArgs keyEventArgs, object parameter = null)
        {
            //Alt组合键时需要使用SystemKey获取真实按键
            var key = keyEventArgs.Key == Key.System ? keyEventArgs.SystemKey : keyEventArgs.Key;
            //按下纯修饰键时不执行
            if (IsModifierKey(key))
            {
                return;
            }
            string keyStr = FormatKey(key, keyEventArgs.KeyboardDevice.Modifiers);
            if (keycommands.ContainsKey(keyStr))
            {
                Debug.WriteLine("执行快捷键命令：" + keyStr);
                keycommands[keyStr].Execute(parameter);
            }
        }

        /// <summary>
        /// 指示按键是否为修饰键
        /// </summary>
        public static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin;
        }

        /// <summary>
        /// 按规范格式生成快捷键字符串（CTRL + ALT + SHIFT + WIN + Key）
        /// </summary>
        public static string FormatKey(Key key, ModifierKeys modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                parts.Add("CTRL");
            }
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                parts.Add("ALT");
            }
            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                parts.Add("SHIFT");
            }
            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
            {
                parts.Add("WIN");
            }
            parts.Add(key.ToString());
            return string.Join(" + ", parts);
        }
    }
}

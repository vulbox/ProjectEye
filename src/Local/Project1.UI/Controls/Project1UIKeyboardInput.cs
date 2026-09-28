
using Project1.UI.Controls.Commands;
using Project1.UI.Controls.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Project1.UI.Controls
{
    public class Project1UIKeyboardInput : Control
    {
        public string Placeholder
        {
            get { return (string)GetValue(PlaceholderProperty); }
            set { SetValue(PlaceholderProperty, value); }
        }

        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.Register("Placeholder", typeof(string), typeof(Project1UIKeyboardInput), new PropertyMetadata(""));

        public string KeyText
        {
            get { return (string)GetValue(KeyTextProperty); }
            set { SetValue(KeyTextProperty, value); }
        }

        public static readonly DependencyProperty KeyTextProperty =
            DependencyProperty.Register("KeyText", typeof(string), typeof(Project1UIKeyboardInput), new PropertyMetadata(""));
        public Project1UIKeyboardInput()
        {
            this.DefaultStyleKey = typeof(Project1UIKeyboardInput);
            this.CommandBindings.Add(new CommandBinding(Project1UIInputCommands.ClearTextCommand, OnClearTextCommand));

        }

        private void OnClearTextCommand(object sender, ExecutedRoutedEventArgs e)
        {
            KeyText = string.Empty;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            //Alt组合键时需要使用SystemKey获取真实按键
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            //按下纯修饰键时不录入
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }
            //按规范格式录入（CTRL + ALT + SHIFT + WIN + Key）
            var parts = new List<string>();
            var modifiers = e.KeyboardDevice.Modifiers;
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
            KeyText = string.Join(" + ", parts);
        }

    }
}

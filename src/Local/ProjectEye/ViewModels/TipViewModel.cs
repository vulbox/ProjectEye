using Newtonsoft.Json;
using Project1.UI.Controls;
using Project1.UI.Controls.Models;
using Project1.UI.Cores;
using ProjectEye.Core;
using ProjectEye.Core.Service;
using ProjectEye.Models;
using ProjectEye.Models.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfAnimatedGif;

namespace ProjectEye.ViewModels
{
    public class TipViewModel : TipModel, IViewModel
    {
        public string ScreenName { get; set; }
        public Window WindowInstance { get; set; }

        /// <summary>
        /// 休息命令
        /// </summary>
        public Command resetCommand { get; set; }
        /// <summary>
        /// 跳过命令
        /// </summary>
        public Command busyCommand { get; set; }

        private readonly RestService reset;
        private readonly SoundService sound;
        private readonly ConfigService config;
        private readonly StatisticService statistic;
        private readonly MainService main;
        private readonly KeyboardShortcutsService keyboardShortcuts;
        private readonly PreAlertService preAlert;
        private readonly ThemeService theme;
        private readonly LongRestService longRest;

        public event ViewModelEventHandler ChangedEvent;

        public TipViewModel(RestService reset,
            SoundService sound,
            ConfigService config,
            StatisticService statistic,
            MainService main,
            App app,
            KeyboardShortcutsService keyboardShortcuts,
            PreAlertService preAlert,
            ThemeService theme,
            LongRestService longRest)
        {
            this.reset = reset;
            this.reset.TimeChanged += new RestEventHandler(timeChanged);
            this.reset.RestCompleted += new RestEventHandler(resetCompleted);

            this.sound = sound;
            this.config = config;
            this.config.Changed += config_Changed;

            this.longRest = longRest;
            this.longRest.PromptShown += new RestEventHandler(longRest_PromptShown);
            this.longRest.PromptClosed += new RestEventHandler(longRest_PromptClosed);


            resetCommand = new Command(new Action<object>(resetCommand_action));
            busyCommand = new Command(new Action<object>(busyCommand_action));

            this.statistic = statistic;

            this.main = main;
            this.keyboardShortcuts = keyboardShortcuts;
            this.preAlert = preAlert;
            this.theme = theme;
            theme.OnChangedTheme += Theme_OnChangedTheme;
            ChangedEvent += TipViewModel_ChangedEvent;
            main.OnHandleTimeout += Main_OnHandleTimeout;
            LoadConfig();

        }

        private void Main_OnHandleTimeout(object service, int msg)
        {
            if (config.options.Behavior.IsHandleTimeoutRest)
            {
                //  进入休息状态
                resetCommand_action(null);
            }
            else
            {
                //关闭窗口
                WindowManager.Hide("TipWindow");
                //进入离开状态
                main.OnLeave();
            }
        }

        private void Theme_OnChangedTheme(string OldThemeName, string NewThemeName)
        {
            CreateUI();
        }

        private void TipViewModel_ChangedEvent()
        {
            CreateUI();
            //WindowInstance.Activated += WindowInstance_Activated;
            WindowInstance.KeyUp += WindowInstance_KeyUp;
            (WindowInstance as Project1UIWindow).OnWShow += TipViewModel_OnWShow;
        }

        private void TipViewModel_OnWShow(object sender, EventArgs e)
        {
            UpdateVariable();
            if (!config.options.Style.IsThruTipWindow)
            {
                WindowInstance.Focus();
            }
            HandleAutoAction();
        }

        private void WindowInstance_KeyUp(object sender, KeyEventArgs e)
        {
            //处理执行快捷键命令
            keyboardShortcuts.Execute(e);
        }

        private void WindowInstance_Activated(object sender, EventArgs e)
        {
            //UpdateVariable();
            //var window = sender as Window;
            //window.Focus();
            //HandleAutoAction();
        }

        private void UpdateVariable()
        {
            try
            {
                COUNTDOWN = config.options.General.RestTime;
                //提醒间隔变量（长休息提示时显示累计工作时长）
                T = longRest.IsWaitingPrompt
                    ? (config.options.General.WarnTime * config.options.General.LongRestCount).ToString()
                    : config.options.General.WarnTime.ToString();
                //当前时间
                TIME = DateTime.Now.ToString();
                //年
                Y = DateTime.Now.ToString("yyyy");
                //月
                M = DateTime.Now.ToString("MM");
                //日
                D = DateTime.Now.ToString("dd");
                //时
                H = DateTime.Now.ToString("HH");
                //分
                MINUTES = DateTime.Now.ToString("mm");
                //今日用眼时长
                TWT = statistic.GetTodayData().WorkingTime.ToString();
                //今日休息时长
                TRT = statistic.GetTodayData().ResetTime.ToString();
                //今日跳过次数
                TSC = statistic.GetTodayData().SkipCount.ToString();
            }
            catch (Exception ex)
            {
                LogHelper.Error("UpdateVariable Fail,Exception message:" + ex.Message);
            }
        }
        private void CreateUI()
        {
            var container = new Grid();
            string uiFilePath = $"UI\\{config.options.Style.Theme.ThemeName}_{ScreenName}.json";
            var data = JsonConvert.DeserializeObject<UIDesignModel>(FileHelper.Read(uiFilePath));
            if (data == null)
            {
                data = theme.GetCreateDefaultTipWindowUI(config.options.Style.Theme.ThemeName, ScreenName);

                FileHelper.Write(uiFilePath, JsonConvert.SerializeObject(data));
            }
            var containerBG = new Border();
            containerBG.Width = Double.NaN;
            containerBG.Height = Double.NaN;
            containerBG.SetValue(Grid.ZIndexProperty, -1);
            containerBG.Background = data.ContainerAttr.Background;
            containerBG.Opacity = data.ContainerAttr.Opacity;
            container.Children.Add(containerBG);
            foreach (var element in data.Elements)
            {
                var ttf = new TranslateTransform()
                {
                    X = element.X,
                    Y = element.Y
                };
                switch (element.Type)
                {
                    case Project1.UI.Controls.Enums.DesignItemType.Text:
                        var textElement = CreateTextElemenet(element);
                        textElement.RenderTransform = ttf;
                        container.Children.Add(textElement);
                        break;
                    case Project1.UI.Controls.Enums.DesignItemType.Button:
                        var buttonElement = CreateButtonElement(element);
                        buttonElement.RenderTransform = ttf;
                        container.Children.Add(buttonElement);
                        break;
                    case Project1.UI.Controls.Enums.DesignItemType.Image:
                        var imageElement = new Image();
                        imageElement.HorizontalAlignment = HorizontalAlignment.Left;
                        imageElement.VerticalAlignment = VerticalAlignment.Top;
                        imageElement.RenderTransform = ttf;
                        imageElement.Width = element.Width;
                        imageElement.Height = element.Height;
                        imageElement.Opacity = element.Opacity;
                        imageElement.Stretch = Stretch.UniformToFill;


                        try
                        {
                            var image = new BitmapImage();
                            image.BeginInit();
                            image.UriSource = new Uri(element.Image, UriKind.RelativeOrAbsolute);
                            image.EndInit();
                            ImageBehavior.SetAnimatedSource(imageElement, image);
                        }
                        catch
                        {
                            imageElement.Source = BitmapImager.Load("pack://application:,,,/Project1.UI;component/Assets/Images/sunglasses.png");

                        }

                        //var imageElement = new Image();
                        //imageElement.HorizontalAlignment = HorizontalAlignment.Left;
                        //imageElement.VerticalAlignment = VerticalAlignment.Top;
                        //imageElement.RenderTransform = ttf;
                        //imageElement.Width = element.Width;
                        //imageElement.Height = element.Height;
                        //imageElement.Opacity = element.Opacity;
                        //imageElement.Stretch = Stretch.Fill;
                        //try
                        //{
                        //    //imageElement.Source = new BitmapImage(new Uri(element.Image, UriKind.RelativeOrAbsolute));
                        //    imageElement.Source = BitmapImager.Load(element.Image);

                        //}
                        //catch
                        //{
                        //    imageElement.Source = BitmapImager.Load("pack://application:,,,/Project1.UI;component/Assets/Images/sunglasses.png");
                        //    //imageElement.Source = new BitmapImage(new Uri("pack://application:,,,/Project1.UI;component/Assets/Images/sunglasses.png", UriKind.RelativeOrAbsolute));
                        //}
                        container.Children.Add(imageElement);
                        break;
                }
            }



            WindowInstance.Content = container;
        }

        private Project1UIButton CreateButtonElement(ElementModel element)
        {
            var button = new Project1UIButton();
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.VerticalAlignment = VerticalAlignment.Top;
            button.Width = element.Width;
            button.Height = element.Height;
            button.FontSize = element.FontSize;
            button.Opacity = element.Opacity;
            button.FontWeight = element.IsTextBold ? FontWeights.Bold : FontWeights.Normal;
            if (element.Style != null)
            {
                var res = WindowInstance.TryFindResource(element.Style);
                if (res != null)
                {
                    button.Style = res as Style;
                }
                else
                {
                    res = WindowInstance.TryFindResource("default");
                    if (res != null)
                    {
                        button.Style = res as Style;
                    }
                }
            }

            button.Content = element.Text;
            var binding = new Binding();
            binding.Source = this;
            binding.Mode = BindingMode.OneWay;
            binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
            binding.Path = new PropertyPath("TakeButtonVisibility");

            switch (element.Command)
            {
                case "rest":
                    button.Command = resetCommand;
                    BindingOperations.SetBinding(button, Button.VisibilityProperty, binding);
                    break;
                case "break":
                    button.Command = busyCommand;
                    BindingOperations.SetBinding(button, Button.VisibilityProperty, binding);
                    break;
            }

            return button;
        }
        private TextBlock CreateTextElemenet(ElementModel element)
        {
            var textBlock = new TextBlock();
            textBlock.HorizontalAlignment = HorizontalAlignment.Left;
            textBlock.VerticalAlignment = VerticalAlignment.Top;
            textBlock.Width = element.Width;
            textBlock.Height = element.Height;
            textBlock.Opacity = element.Opacity;
            textBlock.FontSize = element.FontSize;
            textBlock.FontWeight = element.IsTextBold ? FontWeights.Bold : FontWeights.Normal;
            textBlock.Foreground = element.TextColor;
            textBlock.TextWrapping = TextWrapping.Wrap;
            TextAlignment textAlignment = TextAlignment.Left;
            switch (element.TextAlignment)
            {
                case 0:
                    textAlignment = TextAlignment.Left;
                    break;
                case 1:
                    textAlignment = TextAlignment.Center;
                    break;
                case 2:
                    textAlignment = TextAlignment.Right;
                    break;
            }
            textBlock.TextAlignment = textAlignment;
            if (!Regex.IsMatch(element.Text, @"\{(.*?)\}"))
            {
                textBlock.Text = element.Text;
            }
            else
            {
                if (Regex.IsMatch(element.Text, @"\{countdown\}"))
                {
                    //带有倒计时变量
                    BindingOperations.SetBinding(textBlock, TextBlock.VisibilityProperty, new Binding()
                    {
                        Source = this,
                        Path = new PropertyPath("CountDownVisibility"),
                        Mode = BindingMode.OneWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                }
                //带有变量的文本
                var ms = Regex.Matches(element.Text, @"\{(.*?)\}");
                int startIndex = 0;
                for (int i = 0; i < ms.Count; i++)
                {
                    var item = ms[i];
                    string text = string.Empty;
                    if (startIndex != item.Index)
                    {
                        text = element.Text.Substring(startIndex, item.Index - startIndex);
                    }
                    startIndex = startIndex + text.Length + item.Value.Length;
                    if (text != string.Empty)
                    {
                        textBlock.Inlines.Add(text);
                    }
                    var variable = new Run();
                    string matchVariable = item.Value.Replace("{", "").Replace("}", "");
                    BindingOperations.SetBinding(variable, Run.TextProperty, new Binding()
                    {
                        Source = this,
                        Path = new PropertyPath(matchVariable.ToUpper()),
                        Mode = BindingMode.OneWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged

                    });
                    textBlock.Inlines.Add(variable);
                    if (i == ms.Count - 1)
                    {
                        //最后一个
                        textBlock.Inlines.Add(element.Text.Substring(startIndex));
                    }
                }

            }
            return textBlock;
        }


        //加载配置
        private void LoadConfig()
        {
            //创建快捷键命令
            if (!string.IsNullOrEmpty(config.options.KeyboardShortcuts.Reset))
            {
                keyboardShortcuts.Set(config.options.KeyboardShortcuts.Reset, resetCommand);
            }
            if (!string.IsNullOrEmpty(config.options.KeyboardShortcuts.NoReset))
            {
                keyboardShortcuts.Set(config.options.KeyboardShortcuts.NoReset, busyCommand);
            }
            //动画开关
            IsAnimation = config.options.Style.IsAnimation;

            //动画类型
            WindowAnimationType = config.options.Style.TipWindowAnimation.AnimationType;

            //鼠标穿透
            IsThruWindow = config.options.Style.IsThruTipWindow;

        }

        //配置文件被修改时
        private void config_Changed(object sender, EventArgs e)
        {
            LoadConfig();
        }

        private void resetCompleted(object sender, int timed)
        {
            //休息结束
            Init();
            //播放提示音
            if (config.options.General.Sound)
            {
                sound.Play();
            }
            //重启计时
            main.ReStart();
        }

        private void Init()
        {
            COUNTDOWN = 20;
            CountDownVisibility = System.Windows.Visibility.Hidden;
            TakeButtonVisibility = System.Windows.Visibility.Visible;
        }

        private void resetCommand_action(object obj)
        {
            main.StopBusyListener();
            if (longRest.IsWaitingPrompt)
            {
                //长休息提示：开始长休息
                longRest.Start();
                return;
            }
            if (longRest.IsRunning)
            {
                //长休息倒计时进行中，忽略普通休息指令（多显示器窗口重复触发或全局热键触发）
                return;
            }
            CountDownVisibility = System.Windows.Visibility.Visible;
            TakeButtonVisibility = System.Windows.Visibility.Hidden;
            reset.Start();
            if (config.options.General.Data)
            {
                statistic.Add(StatisticType.ResetTime, config.options.General.RestTime);
                statistic.Add(StatisticType.RestCount, 1);
                statistic.SetLastRestTime();
            }
        }
        private void busyCommand_action(object obj)
        {
            main.StopBusyListener();
            //中断休息倒计时，避免计时器仍在运行导致全局热键失效
            reset.Stop();
            if (longRest.IsWaitingPrompt)
            {
                //长休息提示：跳过长休息
                longRest.Skip();
                return;
            }
            if (config.options.General.Data)
            {
                statistic.Add(StatisticType.SkipCount, 1);
            }
            main.ReStart();
            WindowManager.Hide("TipWindow");
        }
        private void timeChanged(object sender, int timed)
        {
            COUNTDOWN = timed;

        }

        private void longRest_PromptShown(object sender, int timed)
        {
            //重置提示窗口状态，确保休息/跳过按钮可见
            Init();
            //长休息提示显示时更新窗口变量
            UpdateVariable();
            //休息按钮加上（长休息）标注
            SetRestButtonText("好的（长休息）");
        }

        private void longRest_PromptClosed(object sender, int timed)
        {
            //恢复休息按钮原始文字
            SetRestButtonText(originalRestButtonText);
            originalRestButtonText = null;
        }

        #region 长休息提示按钮文字
        /// <summary>
        /// 长休息提示时休息按钮的原始文字
        /// </summary>
        private string originalRestButtonText;

        /// <summary>
        /// 设置所有提示窗口中休息按钮的文字（长休息提示时加上（长休息）标注）
        /// </summary>
        private void SetRestButtonText(string text)
        {
            var windows = WindowManager.GetWindows("TipWindow");
            if (windows == null)
            {
                return;
            }
            foreach (var window in windows)
            {
                foreach (var button in FindVisualChildren<Project1UIButton>(window))
                {
                    if (button.Command == resetCommand)
                    {
                        if (originalRestButtonText == null)
                        {
                            originalRestButtonText = button.Content as string;
                        }
                        button.Content = text;
                    }
                }
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                yield break;
            }
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                var typed = child as T;
                if (typed != null)
                {
                    yield return typed;
                }
                foreach (var grandChild in FindVisualChildren<T>(child))
                {
                    yield return grandChild;
                }
            }
        }
        #endregion

        ///// <summary>
        ///// 窗口监听
        ///// </summary>
        //private void WindowsListener()
        //{
        //    var windows = WindowManager.GetWindows("TipWindow");
        //    foreach (var window in windows)
        //    {
        //        window.Activated += Window_Activated;
        //        window.KeyDown += Window_KeyDown;
        //    }
        //}




        /// <summary>
        /// 处理自动操作
        /// </summary>
        private void HandleAutoAction()
        {
            //全局热键强制休息
            if (main.IsForceReset)
            {
                //清除标记，避免多显示器窗口重复触发
                main.ClearForceReset();
                resetCommand_action(null);
                return;
            }
            //长休息提示：按用户的自动休息设置直接开始长休息
            if (longRest.IsWaitingPrompt)
            {
                if (!config.options.Style.IsTipAsk || config.options.Behavior.IsDisabledSkip)
                {
                    longRest.Start();
                }
                return;
            }
            //询问
            if (!config.options.Style.IsTipAsk)
            {
                //进入休息
                resetCommand_action(null);
                return;
            }
            //预提醒
            if (config.options.Style.IsPreAlert)
            {
                if (preAlert.PreAlertAction == PreAlertAction.Goto)
                {
                    //进入休息
                    resetCommand_action(null);
                    return;
                }
            }
            //禁用跳过休息
            if (config.options.Behavior.IsDisabledSkip)
            {
                //进入休息
                resetCommand_action(null);
                return;
            }

        }

        public void OnChanged()
        {
            ChangedEvent?.Invoke();
        }
    }
}

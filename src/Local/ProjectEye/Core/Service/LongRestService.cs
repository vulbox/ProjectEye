using ProjectEye.Views;
using System;
using System.Windows;
using System.Windows.Threading;

namespace ProjectEye.Core.Service
{
    /// <summary>
    /// 长休息 Service
    /// 每完成一定次数的休息提醒（跳过也算）后触发一次长休息，
    /// 先弹出全屏提示窗口，用户选择休息后在屏幕右下角显示倒计时，期间暂停常规休息提醒计时
    /// </summary>
    public class LongRestService : IService
    {
        /// <summary>
        /// 倒计时计时器
        /// </summary>
        private readonly DispatcherTimer timer;
        /// <summary>
        /// 倒计时剩余秒数
        /// </summary>
        private int timed = 0;
        /// <summary>
        /// 已计数的休息提醒次数
        /// </summary>
        private int counted = 0;
        /// <summary>
        /// 计数日期（跨天清零）
        /// </summary>
        private DateTime countDate = DateTime.Now.Date;
        /// <summary>
        /// 右下角倒计时窗口实例（每块屏幕一个）
        /// </summary>
        private readonly System.Collections.Generic.List<Window> cornerWindows = new System.Collections.Generic.List<Window>();
        /// <summary>
        /// 右下角窗口是否按业务逻辑处于显示状态（不依赖 WPF IsVisible，首次 Show 时它会误报）
        /// </summary>
        private bool isCornerShown;

        private readonly ConfigService config;
        private readonly MainService main;
        private readonly SoundService sound;
        private readonly StatisticService statistic;

        /// <summary>
        /// 倒计时更改时发生
        /// </summary>
        public event RestEventHandler TimeChanged;
        /// <summary>
        /// 长休息提示显示时发生
        /// </summary>
        public event RestEventHandler PromptShown;
        /// <summary>
        /// 长休息提示结束时发生（开始休息、跳过或取消）
        /// </summary>
        public event RestEventHandler PromptClosed;
        /// <summary>
        /// 长休息倒计时开始或结束时发生（用于注册/注销跳过快捷键）
        /// </summary>
        public event EventHandler RunningChanged;

        public LongRestService(
            ConfigService config,
            MainService main,
            SoundService sound,
            StatisticService statistic,
            App app)
        {
            this.config = config;
            this.main = main;
            this.sound = sound;
            this.statistic = statistic;

            //初始化计时器
            timer = new DispatcherTimer();
            timer.Tick += new EventHandler(timer_Tick);
            timer.Interval = new TimeSpan(0, 0, 1);

            //用户离开时取消未处理的长休息提示
            main.OnLeaveEvent += new MainService.MainEventHandler(main_OnLeaveEvent);
            //到点提醒显示前判断是否轮到长休息（第N次到点直接作为长休息提示）
            main.OnBeforeTipShow += new MainService.TipShowEventHandler(main_OnBeforeTipShow);
            //程序退出时清理
            app.Exit += new ExitEventHandler(app_Exit);
        }

        public void Init()
        {
            //启动后预热右下角窗口：第一次 Show 必须在真正休息前完成，且不能跟随后的 Show 挤在同一轮消息里
            Application.Current.Dispatcher.BeginInvoke(new Action(EnsureCornerWindows), DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// 指示长休息提示是否显示中（等待用户选择）
        /// </summary>
        public bool IsWaitingPrompt { get; private set; }
        /// <summary>
        /// 指示长休息倒计时是否进行中
        /// </summary>
        public bool IsRunning
        {
            get { return timer.IsEnabled; }
        }
        /// <summary>
        /// 当前已计数的休息提醒次数
        /// </summary>
        public int Counted
        {
            get { return counted; }
        }

        /// <summary>
        /// 到点提醒计数一次（休息或跳过均计数），达到触发次数时返回true（本次提醒应作为长休息）
        /// </summary>
        public bool Add()
        {
            if (!config.options.General.IsLongRest || IsRunning || IsWaitingPrompt)
            {
                return false;
            }
            //跨天清零
            if (countDate != DateTime.Now.Date)
            {
                countDate = DateTime.Now.Date;
                counted = 0;
            }
            counted++;
            return counted >= config.options.General.LongRestCount;
        }

        private void main_OnBeforeTipShow(object sender, MainService.TipShowEventArgs e)
        {
            //第N次到点提醒直接作为长休息提示显示
            if (Add())
            {
                e.Handled = true;
                Prompt();
            }
        }

        /// <summary>
        /// 显示长休息提示窗口（复用全屏TipWindow，TipWindow显示时主服务会暂停工作计时）
        /// </summary>
        public void Prompt()
        {
            if (IsRunning)
            {
                //倒计时进行中再次触发，只恢复右下角窗口，不重新计时
                ShowCornerWindow();
                return;
            }
            IsWaitingPrompt = true;
            counted = 0;
            WindowManager.Show("TipWindow");
            //通知提示窗口更新变量（跳过路径下窗口未重新显示，OnWShow不会触发）
            PromptShown?.Invoke(this, 0);
            //繁忙超时监听（与普通休息提示一致）
            main.StartBusyListener();
        }

        /// <summary>
        /// 开始长休息倒计时
        /// </summary>
        public void Start()
        {
            if (IsRunning)
            {
                //已在休息中，只恢复右下角窗口，剩余时间继续
                ShowCornerWindow();
                return;
            }
            LogHelper.Warning($"[LongRest] 开始长休息：LongRestTime={config.options.General.LongRestTime}分钟，timed={config.options.General.LongRestTime * 60}秒");
            IsWaitingPrompt = false;
            OnPromptClosed();
            timed = config.options.General.LongRestTime * 60;
            //标记长休息进行中，主服务将保持工作计时暂停
            main.IsLongRestRunning = true;
            //关闭全屏提示，显示右下角倒计时
            WindowManager.Hide("TipWindow");
            ShowCornerWindow();
            OnTimeChanged();
            timer.Start();
            OnRunningChanged();
        }

        /// <summary>
        /// 切换右下角倒计时窗口显隐。隐藏后后台继续计时，再次调用则恢复显示，整段仍计为一次长休息。
        /// </summary>
        public void ToggleCornerWindow()
        {
            if (!IsRunning)
            {
                return;
            }
            if (isCornerShown)
            {
                HideCornerWindow();
            }
            else
            {
                ShowCornerWindow();
            }
        }

        /// <summary>
        /// 跳过长休息（用户在提示窗口选择跳过时调用）
        /// </summary>
        public void Skip()
        {
            IsWaitingPrompt = false;
            OnPromptClosed();
            counted = 0;
            WindowManager.Hide("TipWindow");
            main.ReStart();
        }

        /// <summary>
        /// 取消未处理的长休息提示（用户离开等场景）
        /// </summary>
        public void Cancel()
        {
            if (IsWaitingPrompt)
            {
                IsWaitingPrompt = false;
                OnPromptClosed();
                counted = 0;
            }
        }

        private void main_OnLeaveEvent(object sender, int msg)
        {
            //用户离开时取消未处理的长休息提示
            Cancel();
        }

        private void app_Exit(object sender, ExitEventArgs e)
        {
            timer.Stop();
            main.IsLongRestRunning = false;
            OnRunningChanged();
            foreach (var window in cornerWindows)
            {
                window.Hide();
            }
        }

        /// <summary>
        /// 长休息结束
        /// </summary>
        private void End()
        {
            LogHelper.Warning("[LongRest] 长休息倒计时结束，隐藏右下角窗口");
            timer.Stop();
            main.IsLongRestRunning = false;
            OnRunningChanged();
            HideCornerWindow();
            counted = 0;
            if (config.options.General.Data)
            {
                //长休息时长计入今日休息时长
                statistic.Add(StatisticType.ResetTime, config.options.General.LongRestTime * 60);
            }
            //休息结束提示音
            if (config.options.General.Sound)
            {
                sound.Play();
            }
            //重启工作计时
            main.ReStart();
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            if (timed <= 1)
            {
                End();
            }
            else
            {
                timed--;
                OnTimeChanged();
            }
        }

        #region 右下角倒计时窗口
        /// <summary>
        /// 创建并预热右下角窗口（每块屏幕一个）
        /// </summary>
        private void EnsureCornerWindows()
        {
            if (cornerWindows.Count > 0)
            {
                return;
            }
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var window = WindowManager.CreateWindowInScreen("LongRestWindow", screen);
                if (window != null)
                {
                    window.Tag = screen.DeviceName;
                    var corner = window as LongRestWindow;
                    if (corner != null)
                    {
                        corner.WarmUp();
                    }
                    cornerWindows.Add(window);
                }
            }
        }

        /// <summary>
        /// 显示右下角倒计时窗口。真正的 Show 放到下一轮消息，对齐 Ctrl+Q 第二次能显示的路径。
        /// </summary>
        private void ShowCornerWindow()
        {
            EnsureCornerWindows();
            isCornerShown = true;
            Application.Current.Dispatcher.BeginInvoke(new Action(ShowCornerWindowNow), DispatcherPriority.ApplicationIdle);
        }

        private void ShowCornerWindowNow()
        {
            if (!isCornerShown)
            {
                return;
            }
            foreach (var window in cornerWindows)
            {
                window.Show();
                var corner = window as LongRestWindow;
                if (corner != null)
                {
                    corner.EnsureShown();
                }
            }
            LogHelper.Warning($"[LongRest] 右下角窗口Show调用完成，共{cornerWindows.Count}个");
        }
        /// <summary>
        /// 隐藏右下角倒计时窗口
        /// </summary>
        private void HideCornerWindow()
        {
            foreach (var window in cornerWindows)
            {
                window.Hide();
            }
            isCornerShown = false;
        }
        #endregion

        private void OnTimeChanged()
        {
            TimeChanged?.Invoke(this, timed);
        }

        private void OnPromptClosed()
        {
            PromptClosed?.Invoke(this, 0);
        }

        private void OnRunningChanged()
        {
            RunningChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

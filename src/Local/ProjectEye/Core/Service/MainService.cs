using Microsoft.Win32;
using ProjectEye.Core.Models.Options;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace ProjectEye.Core.Service
{
    /// <summary>
    /// Main Service
    /// </summary>
    public class MainService : IService
    {
        /// <summary>
        /// 用眼计时器
        /// </summary>
        private DispatcherTimer work_timer;
        System.Diagnostics.Stopwatch workTimerStopwatch;
        /// <summary>
        /// 空闲检测计时器，用于检测用户离开和回来
        /// </summary>
        private DispatcherTimer idle_timer;
        /// <summary>
        /// 用户离开判定阈值（键鼠无输入时长）
        /// </summary>
        private readonly TimeSpan leaveThreshold = new TimeSpan(0, 3, 0);
        /// <summary>
        /// 指示用户是否处于离开状态
        /// </summary>
        private bool isUserAway;
        /// <summary>
        /// 本次离开的开始时刻
        /// </summary>
        private DateTime? leaveStartTime;
        /// <summary>
        /// 繁忙检测，用于检测用户在休息提示界面是否超时不操作
        /// </summary>
        private DispatcherTimer busy_timer;
        /// <summary>
        /// 用眼计时，用于定时统计和保存用户的用眼时长
        /// </summary>
        private DispatcherTimer useeye_timer;
        /// <summary>
        /// 日期更改计时，用于处理日期变化
        /// </summary>
        private DispatcherTimer date_timer;
        /// <summary>
        /// 日期更改计时重置标记
        /// </summary>
        private bool isDateTimerReset;
        /// <summary>
        /// 预提醒操作
        /// </summary>
        private PreAlertAction preAlertAction;
        #region Service
        private readonly ScreenService screen;
        private readonly ConfigService config;
        private readonly CacheService cache;
        private readonly StatisticService statistic;
        private readonly ThemeService theme;
        private readonly SystemResourcesService systemResources;
        #endregion

        #region win32
        //[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        //public static extern IntPtr GetForegroundWindow();
        //[DllImport("user32", SetLastError = true)]
        //public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        #endregion

        #region Event
        public delegate void MainEventHandler(object service, int msg);
        /// <summary>
        /// 用户离开时发生
        /// </summary>
        public event MainEventHandler OnLeaveEvent;
        ///// <summary>
        ///// 用户回来时发生
        ///// </summary>
        //public event MainEventHandler OnComeBackEvent;
        /// <summary>
        /// 计时器重启时发生
        /// </summary>
        public event MainEventHandler OnReStartTimer;
        /// <summary>
        /// 到达休息时间时发生（不管是否进入休息状态）
        /// </summary>
        public event MainEventHandler OnReset;
        /// <summary>
        /// 计时器停止时发生
        /// </summary>
        public event MainEventHandler OnPause;
        /// <summary>
        /// 计时器启动时发生
        /// </summary>
        public event MainEventHandler OnStart;
        /// <summary>
        /// 加载语言完成时发生
        /// </summary>
        public event MainEventHandler OnLoadedLanguage;
        /// <summary>
        /// 提示休息后超时未处理时发生
        /// </summary>
        public event MainEventHandler OnHandleTimeout;
        #endregion
        public MainService(App app,
            ScreenService screen,
            ConfigService config,
            CacheService cache,
            StatisticService statistic,
            ThemeService theme,
            SystemResourcesService systemResources)
        {
            this.screen = screen;
            this.config = config;
            this.cache = cache;
            this.statistic = statistic;
            this.theme = theme;
            this.systemResources = systemResources;

            app.Exit += new ExitEventHandler(app_Exit);
            SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(OnPowerModeChanged);
        }

        #region 初始化
        public void Init()
        {
            //关闭暂不提醒
            config.options.General.Noreset = false;
            //关闭番茄时钟模式
            config.options.General.IsTomatoMode = false;

            //初始化用眼计时器
            work_timer = new DispatcherTimer();
            work_timer.Tick += new EventHandler(timer_Tick);
            work_timer.Interval = new TimeSpan(0, config.options.General.WarnTime, 0);
            workTimerStopwatch = new Stopwatch();
            //初始化空闲检测计时器
            idle_timer = new DispatcherTimer();
            idle_timer.Tick += new EventHandler(idle_timer_Tick);
            idle_timer.Interval = new TimeSpan(0, 1, 0);
            //初始化繁忙计时器
            busy_timer = new DispatcherTimer();
            busy_timer.Tick += new EventHandler(busy_timer_Tick);
            busy_timer.Interval = new TimeSpan(0, 0, 30);
            //初始化用眼统计计时器
            useeye_timer = new DispatcherTimer();
            useeye_timer.Tick += new EventHandler(useeye_timer_Tick);
            useeye_timer.Interval = new TimeSpan(0, 10, 0);

            date_timer = new DispatcherTimer();
            date_timer.Tick += new EventHandler(date_timer_Tick);
            /****调试模式代码****/
#if DEBUG
            //每5秒检测一次键鼠空闲（仅提高检测频率，判定阈值仍为3分钟）
            idle_timer.Interval = new TimeSpan(0, 0, 5);
#endif

            CreateTipWindows();

            UpdateDateTimer();

            Start();

            config.Changed += Config_Changed;


            //加载语言
            HandleLanguageChanged();
        }
        #endregion

        private void HandleLanguageChanged()
        {
            var language = new ResourceDictionary { Source = new Uri($"/ProjectEye;component/Resources/Language/{config.options.Style.Language.Value}.xaml", UriKind.RelativeOrAbsolute) };

            var mds = System.Windows.Application.Current.Resources.MergedDictionaries;
            var loadedLanguage = mds.Where(m => m.Source.OriginalString.Contains("Language")).FirstOrDefault();
            if (loadedLanguage != null)
            {
                mds.Remove(loadedLanguage);
            }
            mds.Add(language);
            systemResources.Init();
            OnLoadedLanguage?.Invoke(this, 0);
        }

        private void Config_Changed(object sender, EventArgs e)
        {
            var oldOptions = sender as OptionsModel;
            if (oldOptions.General.LeaveListener != config.options.General.LeaveListener)
            {
                //离开监听开关更改，即时生效
                if (config.options.General.LeaveListener)
                {
                    idle_timer.Start();
                }
                else
                {
                    idle_timer.Stop();
                    if (isUserAway)
                    {
                        //正处于离开状态时关闭监听，恢复计时
                        isUserAway = false;
                        FlushLeaveTime();
                        DoStart(false);
                    }
                }
                Debug.WriteLine("离开监听开关更改：" + config.options.General.LeaveListener);
            }
            if (oldOptions.Style.IsThruTipWindow != config.options.Style.IsThruTipWindow)
            {
                //鼠标穿透被打开
                CreateTipWindows();
                Debug.WriteLine("鼠标穿透更改，重新创建窗口");
            }
            if (oldOptions.General.IsTomatoMode != config.options.General.IsTomatoMode)
            {
                if (config.options.General.IsTomatoMode)
                {
                    //番茄时钟模式打开
                    DoStop(false);
                    Debug.WriteLine("番茄模式已启动，关闭计时休息提醒模式");
                }
                else
                {
                    DoStart(false);
                    Debug.WriteLine("番茄模式已关闭，恢复计时休息提醒模式");
                }
            }
            HandleLanguageChanged();
        }
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    //电脑休眠
                    Pause();
                    break;
                case PowerModes.Resume:
                    //电脑恢复
                    Start();
                    break;
            }
        }

        private void busy_timer_Tick(object sender, EventArgs e)
        {
            Debug.WriteLine("用户超过20秒未处理");
            //用户超过20秒未处理
            busy_timer.Stop();

            OnHandleTimeout?.Invoke(this, 0);
           
        }

        private void idle_timer_Tick(object sender, EventArgs e)
        {
            var idleTime = Win32APIHelper.GetIdleTime();
            if (!isUserAway)
            {
                if (idleTime >= leaveThreshold)
                {
                    //键鼠无输入超过阈值，用户离开了电脑
                    OnLeave();
                }
            }
            else if (idleTime < idle_timer.Interval)
            {
                //自上次检测以来有键鼠输入，用户回来了
                Debug.WriteLine("用户回来了");
                isUserAway = false;
                //把本次离开时长计入统计
                FlushLeaveTime();
                DoStart(false);
            }
        }

        private void date_timer_Tick(object sender, EventArgs e)
        {

            date_timer.Stop();
            if (!isDateTimerReset)
            {
                //重置统计时间
                statistic.StatisticUseEyeData();
                //延迟2分钟后重置timer
                isDateTimerReset = true;
                date_timer.Interval = new TimeSpan(0, 2, 0);
                date_timer.Start();
            }
            else
            {
                UpdateDateTimer();
            }
        }

        #region 获取下一次休息剩余分钟数
        public double GetRestCountdownMinutes()
        {
            return work_timer.Interval.TotalMinutes - workTimerStopwatch.Elapsed.TotalMinutes;
        }
        #endregion

        #region 把进行中的离开时长计入统计
        /// <summary>
        /// 把进行中的离开时长计入统计（用户回来或程序退出时调用）
        /// </summary>
        private void FlushLeaveTime()
        {
            if (leaveStartTime.HasValue && config.options.General.Data)
            {
                statistic.Add(StatisticType.LeaveTime,
                    Math.Round((DateTime.Now - leaveStartTime.Value).TotalMinutes, 2));
            }
            leaveStartTime = null;
        }
        #endregion

        #region 获取今日累计离开分钟数
        /// <summary>
        /// 获取今日累计离开分钟数（已入库 + 正在进行的离开段）
        /// </summary>
        public double GetTodayLeaveMinutes()
        {
            double total = statistic.GetTodayData()?.LeaveTime ?? 0;
            if (isUserAway && leaveStartTime.HasValue)
            {
                total += (DateTime.Now - leaveStartTime.Value).TotalMinutes;
            }
            return total;
        }
        #endregion

        #region 获取距上次休息的分钟数
        /// <summary>
        /// 获取距上次休息的分钟数
        /// </summary>
        /// <returns>返回null表示本次运行还没有休息过</returns>
        public double? GetMinutesSinceLastRest()
        {
            var lastRestTime = statistic.LastRestTime;
            if (lastRestTime.HasValue)
            {
                return (DateTime.Now - lastRestTime.Value).TotalMinutes;
            }
            return null;
        }
        #endregion

        #region 获取当前用眼计时已经过的分钟数
        /// <summary>
        /// 获取当前用眼计时已经过的分钟数（用于距上次休息的回退显示）
        /// </summary>
        public double GetWorkTimerElapsedMinutes()
        {
            return workTimerStopwatch.Elapsed.TotalMinutes;
        }
        #endregion

        #region 获取提醒计时是否在运行
        public bool IsWorkTimerRun()
        {
            return work_timer.IsEnabled && !config.options.General.Noreset;
        }
        #endregion

        #region 到达统计时间
        private void useeye_timer_Tick(object sender, EventArgs e)
        {
            StatisticData();
        }
        #endregion

        #region 统计数据
        private void StatisticData()
        {
            if (config.options.General.Data)
            {
                //更新用眼时长
                statistic.StatisticUseEyeData();
                //数据持久化
                statistic.Save();
            }
        }
        #endregion

        #region 结束繁忙超时监听
        /// <summary>
        /// 结束繁忙超时监听
        /// </summary>
        public void StopBusyListener()
        {
            if (busy_timer.IsEnabled)
            {
                busy_timer.Stop();
            }
        }
        #endregion

        #region 进入离开状态
        /// <summary>
        /// 进入离开状态
        /// </summary>
        public void OnLeave()
        {
            Debug.WriteLine("用户离开了");
            WindowManager.Hide("TipWindow");
            isUserAway = true;
            //记录本次离开开始时刻用于统计离开时长
            leaveStartTime = DateTime.Now;
            //停止所有服务
            DoStop();
            //空闲检测继续运行以监听用户回来
            idle_timer.Start();
            //事件响应
            OnLeaveEvent?.Invoke(this, 0);
        }
        #endregion

        #region 停止主进程。退出程序时调用
        /// <summary>
        /// 停止主进程。退出程序时调用
        /// </summary>
        public void Exit()
        {
            if (config.options.General.Data)
            {
                //补记进行中的离开时长
                FlushLeaveTime();
                //更新用眼时长
                statistic.StatisticUseEyeData();
                //数据持久化
                statistic.Save();
            }

            screen.Dispose();
            DoStop();
            WindowManager.Close("TipWindow");
        }
        #endregion

        #region 启动主服务
        public void Start()
        {
            DoStart();
        }
        #endregion

        #region 暂停主服务
        /// <summary>
        /// 暂停主服务
        /// </summary>
        /// <param name="isStopStatistic">是否停止用眼统计，默认true</param>
        public void Pause(bool isStopStatistic = true)
        {
            DoStop(isStopStatistic);
            OnPause?.Invoke(this, 0);
        }
        #endregion

        #region 设置提醒间隔时间
        /// <summary>
        /// 设置提醒间隔时间，如果与当前计时器不一致时将重启计时
        /// </summary>
        /// <param name="minutes">间隔时间（分钟）</param>
        /// <returns>重启时返回TRUE</returns>
        public bool SetWarnTime(int minutes)
        {
            if (work_timer.Interval.TotalMinutes != minutes)
            {
                Debug.WriteLine(work_timer.Interval.TotalMinutes + "," + minutes);
                work_timer.Interval = new TimeSpan(0, minutes, 0);
                if (!config.options.General.Noreset)
                {
                    ReStart();
                    return true;
                }
                return false;
            }
            return false;
        }
        #endregion

        #region 重新启动计时
        /// <summary>
        /// 重新启动休息计时
        /// </summary>
        public void ReStart()
        {
            if (!config.options.General.Noreset)
            {
                Debug.WriteLine("重新启动休息计时");
                DoStop();
                DoStart();
                OnReStartTimer?.Invoke(this, 0);
            }
        }

        #endregion

        #region 启动计时实际操作
        private void DoStart(bool isHard = true)
        {
            //允许硬启动，否则只有在关闭暂不提醒和番茄时钟模式时才允许启动工作计时
            if (isHard || !config.options.General.Noreset && !config.options.General.IsTomatoMode)
            {
                //休息提醒
                work_timer.Start();
                workTimerStopwatch.Restart();
            }
            //空闲检测（受离开监听开关控制）
            if (config.options.General.LeaveListener)
            {
                idle_timer.Start();
            }
            //数据统计
            if (config.options.General.Data)
            {
                //重置用眼计时
                statistic.ResetStatisticTime();
                //用眼统计
                useeye_timer.Start();
            }
            OnStart?.Invoke(this, 0);
        }
        #endregion

        #region 停止计时实际操作
        private void DoStop(bool isHard = true)
        {
            //统计数据
            StatisticData();
            work_timer.Stop();
            workTimerStopwatch.Stop();
            if (isHard)
            {
                useeye_timer.Stop();
                idle_timer.Stop();
            }
            busy_timer.Stop();
        }
        #endregion

        #region 立即进入休息
        /// <summary>
        /// 强制休息标记（全局热键触发）
        /// </summary>
        private bool isForceReset;
        /// <summary>
        /// 立即进入休息，无视询问等设置
        /// </summary>
        public void ResetNow()
        {
            //设置强制休息标记
            isForceReset = true;
            //结束繁忙超时监听
            StopBusyListener();
            //显示提示窗口并直接进入休息
            WindowManager.Show("TipWindow");
        }
        /// <summary>
        /// 指示是否为强制休息（全局热键触发）
        /// </summary>
        public bool IsForceReset
        {
            get { return isForceReset; }
        }
        /// <summary>
        /// 清除强制休息标记
        /// </summary>
        public void ClearForceReset()
        {
            isForceReset = false;
        }
        #endregion

        #region 显示休息提示窗口
        /// <summary>
        /// 显示休息提示窗口
        /// </summary>
        private void ShowTipWindow()
        {
            if (config.options.Style.IsPreAlert && preAlertAction == PreAlertAction.Break)
            {
                //预提醒设置跳过
                statistic.Add(StatisticType.SkipCount, 1);
                ReStartWorkTimerWatch();
            }
            else
            {
                if (IsBreakReset())
                {
                    statistic.Add(StatisticType.SkipCount, 1);
                    ReStartWorkTimerWatch();
                }
                else
                {
                    busy_timer.Start();
                    WindowManager.Show("TipWindow");
                }
            }
        }
        #endregion

        #region 提示窗口显示时 Event
        private void isVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var window = sender as Window;
            if (window.IsVisible)
            {
                //显示提示窗口时停止计时
                work_timer.Stop();
                workTimerStopwatch.Stop();

                window.Focus();
            }
            else
            {
                //隐藏时继续计时
                work_timer.Start();
                workTimerStopwatch.Restart();
            }
        }
        #endregion

        #region 用眼到达设定时间 Event
        private void timer_Tick(object sender, EventArgs e)
        {
            ShowTipWindow();
            OnReset?.Invoke(this, 0);
        }
        #endregion

        #region 程序退出 Event
        private void app_Exit(object sender, ExitEventArgs e)
        {
            Exit();
        }
        #endregion

        #region 设置预提醒状态
        public void SetPreAlertAction(PreAlertAction preAlertAction)
        {
            this.preAlertAction = preAlertAction;
        }
        #endregion

        #region 是否跳过本次休息
        /// <summary>
        /// 是否跳过本次休息
        /// </summary>
        /// <returns>true跳过，false不跳过</returns>
        public bool IsBreakReset()
        {
            if (!config.options.General.Noreset)
            {
                //深色主题切换判断
                theme.HandleDarkMode();

                //0.全屏跳过判断
                if (config.options.Behavior.IsFullScreenBreak)
                {
                    var info = Win32APIHelper.GetFocusWindowInfo();
                    if (info.IsFullScreen)
                    {
                        return true;
                    }
                }

                //1.进程跳过判断
                if (config.options.Behavior.IsBreakProgressList)
                {
                    Process[] processes = Process.GetProcesses();
                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (config.options.Behavior.BreakProgressList.Contains(process.ProcessName))
                            {
                                return true;
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                return false;
            }

            return true;
        }
        #endregion

        #region 重置测量时间
        public void ReStartWorkTimerWatch()
        {
            workTimerStopwatch.Restart();
        }
        #endregion

        #region 创建全屏提示窗口
        /// <summary>
        /// 创建全屏提示窗口
        /// </summary>
        public void CreateTipWindows()
        {
            //关闭
            WindowManager.Close("TipWindow");
            //在所有屏幕上创建全屏提示窗口
            var tipWindow = WindowManager.GetCreateWindow("TipWindow", true);

            foreach (var window in tipWindow)
            {
                window.IsVisibleChanged += new DependencyPropertyChangedEventHandler(isVisibleChanged);
            }
        }
        #endregion

        #region 更新日期更改计时时间
        /// <summary>
        /// 更新日期更改计时时间
        /// </summary>
        private void UpdateDateTimer()
        {

            DateTime now = DateTime.Now;
            DateTime morrow = new DateTime(now.Year, now.Month, now.Day, 23, 59, 0);
            int diffseconds = (int)morrow.Subtract(now).TotalSeconds;
            if (diffseconds < 0)
            {
                diffseconds = 0;
            }
            date_timer.Interval = new TimeSpan(0, 0, diffseconds);
            date_timer.Start();
            isDateTimerReset = false;
        }
        #endregion
    }
}

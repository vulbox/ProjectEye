using ProjectEye.Core;
using ProjectEye.Core.Service;
using ProjectEye.Models;
using System;
using System.Windows;

namespace ProjectEye.ViewModels
{
    public class LongRestViewModel : LongRestModel, IViewModel
    {
        public string ScreenName { get; set; }
        public Window WindowInstance { get; set; }

        public event ViewModelEventHandler ChangedEvent;

        private readonly LongRestService longRest;

        public LongRestViewModel(LongRestService longRest)
        {
            this.longRest = longRest;
            this.longRest.TimeChanged += new RestEventHandler(longRest_TimeChanged);
        }

        private void longRest_TimeChanged(object sender, int timed)
        {
            //倒计时文本 mm:ss
            CountdownText = $"{timed / 60:D2}:{timed % 60:D2}";
        }

        public void OnChanged()
        {
            ChangedEvent?.Invoke();
        }
    }
}

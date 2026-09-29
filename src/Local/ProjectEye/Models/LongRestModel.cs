namespace ProjectEye.Models
{
    public class LongRestModel : UINotifyPropertyChanged
    {
        private string countdownText_ = "";
        /// <summary>
        /// 倒计时文本（mm:ss）
        /// </summary>
        public string CountdownText
        {
            get { return countdownText_; }
            set
            {
                countdownText_ = value;
                OnPropertyChanged();
            }
        }
    }
}

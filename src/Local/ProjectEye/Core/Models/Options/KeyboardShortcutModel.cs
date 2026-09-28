using System.Xml.Serialization;

namespace ProjectEye.Core.Models.Options
{
    [XmlRootAttribute("KeyboardShortcut")]
    public class KeyboardShortcutModel
    {
        /// <summary>
        /// 休息快捷键
        /// </summary>
        public string Reset { get; set; } = "";
        /// <summary>
        /// 不休息快捷键
        /// </summary>
        public string NoReset { get; set; } = "";
        /// <summary>
        /// 立即休息全局热键
        /// </summary>
        public string ResetNow { get; set; } = "";

    }
}

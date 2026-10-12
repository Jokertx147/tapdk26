using System.Windows;
using TheAirline.Models.Airliners;

namespace TheAirline.GraphicsModel.UserControlModel.PopUpWindowsModel
{
    /// <summary>
    ///     Shows the picture and the specifications of an airliner type.
    /// </summary>
    public partial class PopUpAirlinerTypeInfo : PopUpWindow
    {
        public PopUpAirlinerTypeInfo(AirlinerType type)
        {
            Type = type;
            DataContext = this;

            InitializeComponent();

            Title = type.Name;
        }

        public AirlinerType Type { get; }

        public string Produced
        {
            get
            {
                return string.Format("{0} - {1}", Type.Produced.From.Year,
                    Type.Produced.To.Year >= 2100 ? "present" : Type.Produced.To.Year.ToString());
            }
        }

        public static void ShowPopUp(AirlinerType type)
        {
            var window = new PopUpAirlinerTypeInfo(type);
            window.ShowDialog();
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

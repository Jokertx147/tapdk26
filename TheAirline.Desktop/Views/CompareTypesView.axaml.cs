using Avalonia.Controls;
using Avalonia.Input;
using TheAirline.Presentation;

namespace TheAirline.Desktop.Views
{
    public partial class CompareTypesView : UserControl
    {
        public CompareTypesView()
        {
            InitializeComponent();
        }

        private void OnChoiceDoubleTapped(object sender, TappedEventArgs e)
        {
            var vm = DataContext as CompareTypesViewModel;
            if (vm?.AddCommand.CanExecute(null) == true) vm.AddCommand.Execute(null);
        }
    }
}

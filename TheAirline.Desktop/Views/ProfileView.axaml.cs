using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TheAirline.Presentation;

namespace TheAirline.Desktop.Views
{
    public partial class ProfileView : UserControl
    {
        public ProfileView()
        {
            InitializeComponent();
        }

        private async void OnPickFile(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ProfileViewModel;
            var top = TopLevel.GetTopLevel(this);
            if (vm == null || top == null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Pick a logo picture",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Pictures") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
                }
            });
            var file = files?.FirstOrDefault();
            string path = file?.TryGetLocalPath();
            if (path != null) vm.UseLogoFile(path);
        }

        private void OnLogoDoubleTapped(object sender, TappedEventArgs e)
        {
            var vm = DataContext as ProfileViewModel;
            if (vm?.UseLogoCommand.CanExecute(null) == true) vm.UseLogoCommand.Execute(null);
        }
    }
}

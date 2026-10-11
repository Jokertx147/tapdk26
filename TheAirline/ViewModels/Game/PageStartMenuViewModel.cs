using System;
using System.Windows;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using TaskDialogInterop;
using TheAirline.Infrastructure.Services;
using TheAirline.Models.General;

namespace TheAirline.ViewModels.Game
{
    public class PageStartMenuViewModel : BindableBase
    {
        private readonly IRegionManager _regionManager;
        private readonly ITaskDialogService _taskDialog;
        private Window _parent;

        public PageStartMenuViewModel(IRegionManager regionManager, ITaskDialogService taskDialog)
        {
            _regionManager = regionManager;
            _taskDialog = taskDialog;
            NavigateCommand = new DelegateCommand<Uri>(Navigate);
            LoadGameCommand = new DelegateCommand(LoadGame);
            ExitCommand = new DelegateCommand<Window>(ExitGame);
        }

        public DelegateCommand<Uri> NavigateCommand { get; }
        public DelegateCommand<Window> ExitCommand { get; }
        public DelegateCommand LoadGameCommand { get; }
        public Uri NewGameUri => new Uri("/PageStartData", UriKind.Relative);
        public Uri LoadGameUri => new Uri("/PageLoadGame", UriKind.Relative);
        public Uri SettingsUri => new Uri("/PageSettings", UriKind.Relative);
        public Uri CreditsUri => new Uri("/PageCredits", UriKind.Relative);

        // the load screen is one of the older Page screens, shown over the whole window
        private void LoadGame()
        {
            try
            {
                Infrastructure.GameDataLoader.EnsureLoaded();
                GUIModel.HelpersModel.PageNavigator.NavigateTo(new GUIModel.PagesModel.OptionsPageModel.PageLoadGame());
            }
            catch (Exception e)
            {
                MessageBox.Show("Could not open the load screen:\n" + e, "TheAirline");
            }
        }

        private void Navigate(Uri view)
        {
            _regionManager.RequestNavigate("MainContentRegion", view);
        }

        private void ExitGame(Window window)
        {
            _parent = window;

            var opts = new TaskDialogOptions
            {
                Owner = window,
                Title = Translator.GetInstance().GetString("MessageBox", "1003"),
                MainInstruction = Translator.GetInstance().GetString("MessageBox", "1003", "message"),
                MainIcon = VistaTaskDialogIcon.Warning,
                CommonButtons = TaskDialogCommonButtons.YesNo
            };

            _taskDialog.ShowTaskDialog(opts, ExitResult);
        }

        private void ExitResult(TaskDialogResult results)
        {
            if (results.Result == TaskDialogSimpleResult.Yes)
            {
                _parent.Close();
            }
        }
    }
}
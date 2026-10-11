using System.Windows;
using Prism.Ioc;
using TheAirline.Infrastructure;
using TheAirline.Infrastructure.Services;
using TheAirline.Views.Game;

/*!
 * /brief Namespace of the project
 */

namespace TheAirline
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public sealed partial class App
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<AppState>();
            containerRegistry.Register<ITaskDialogService, TaskDialogService>();

            //the pages the main window navigates to, registered under the names used in navigation URIs such as "/PageStartMenu"
            RegisterPage<PageHeader>(containerRegistry);
            RegisterPage<PageSelectLanguage>(containerRegistry);
            RegisterPage<PageStartMenu>(containerRegistry);
            RegisterPage<PageSettings>(containerRegistry);
            RegisterPage<PageCredits>(containerRegistry);
            RegisterPage<PageNewGame>(containerRegistry);
            RegisterPage<PageStartData>(containerRegistry);
            RegisterPage<PageAirlineData>(containerRegistry);
        }

        private static void RegisterPage<TPage>(IContainerRegistry containerRegistry)
        {
            containerRegistry.Register(typeof (object), typeof (TPage), typeof (TPage).Name);
        }
    }
}

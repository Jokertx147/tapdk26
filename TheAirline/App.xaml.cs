using System;
using System.Globalization;
using System.Windows;
using Prism.Ioc;
using TheAirline.Helpers;
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
        //Command line tool: TheAirline.exe --set-money <save name without .sav> <amount>
        protected override void OnStartup(StartupEventArgs e)
        {
            StartupLog.Write("---- start ---- (build of " + System.IO.File.GetLastWriteTime(System.Reflection.Assembly.GetExecutingAssembly().Location) + ")");
            DispatcherUnhandledException += (s, args) => StartupLog.Write("UI thread exception: " + args.Exception);
            AppDomain.CurrentDomain.UnhandledException +=
                (s, args) => StartupLog.Write("Unhandled exception: " + args.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException +=
                (s, args) => StartupLog.Write("Unobserved task exception: " + args.Exception);

            if (e.Args.Length >= 1 && e.Args[0] == "--set-money")
            {
                double amount;
                if (e.Args.Length != 3 ||
                    !double.TryParse(e.Args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                {
                    MessageBox.Show("Usage: TheAirline.exe --set-money <save name> <amount>", "Edit save");
                }
                else
                {
                    string result;
                    try
                    {
                        result = SaveEditor.SetMoney(e.Args[1], amount);
                    }
                    catch (Exception ex)
                    {
                        result = "Editing the save failed: " + ex.Message;
                        StartupLog.Write("set-money failed: " + ex);
                    }

                    StartupLog.Write("set-money: " + result);
                    MessageBox.Show(result, "Edit save");
                }

                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

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

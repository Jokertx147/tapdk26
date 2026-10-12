using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TheAirline.Infrastructure;
using TheAirline.Models.General;

namespace TheAirline.Helpers.Workers
{
    //the worker class for updating game object (non-graphics)
    public class GameObjectWorker : INotifyPropertyChanged
    {
        #region Static Fields

        private static GameObjectWorker _instance;

        #endregion

        #region Fields

        private readonly BackgroundWorker _worker;

        private bool _isPaused;

        #endregion

        #region Constructors and Destructors

        private GameObjectWorker()
        {
            _worker = new BackgroundWorker {WorkerReportsProgress = true, WorkerSupportsCancellation = true};

            _worker.DoWork += bw_DoWork;
            //bw.ProgressChanged += new ProgressChangedEventHandler(bw_ProgressChanged);
            //this.Worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bw_RunWorkerCompleted);
            IsPaused = false;
            IsFinish = false;
            IsError = false;
        }

        #endregion

        #region Public Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Public Properties

        public bool IsError { get; set; }

        public bool IsFinish { get; set; }

        public bool IsPaused
        {
            get { return _isPaused; }
            set
            {
                _isPaused = value;
                NotifyPropertyChanged("IsPaused");
            }
        }

        #endregion

        #region Public Methods and Operators

        public static GameObjectWorker GetInstance()
        {
            return _instance ?? (_instance = new GameObjectWorker());
        }

        //cancels the worker
        public void Cancel()
        {
            _worker.CancelAsync();

            while (_worker.IsBusy && !IsFinish)
            {
            }
        }

        //asks the worker to stop without waiting for it
        public void RequestCancel()
        {
            _worker.CancelAsync();
        }

        public bool IsBusy()
        {
            return _worker.IsBusy;
        }

        //pause the worker
        public void Pause()
        {
            Cancel();

            IsPaused = true;
        }

        //restarts the worker
        public void Restart()
        {
            Start();

            IsPaused = false;
        }

        //starts the worker
        public void Start()
        {
            if (!_worker.IsBusy)
            {
                IsPaused = false;

                _worker.RunWorkerAsync();
            }
        }

        //starts the worker paused
        public void StartPaused()
        {
            if (!_worker.IsBusy)
            {
                IsPaused = true;

                _worker.RunWorkerAsync();
            }
        }

        #endregion

        #region Methods

        private void NotifyPropertyChanged(String propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        //returns if the worker is paused

        //the target length of a turn in milliseconds: at the Normal speed one game month takes 90 seconds,
        //the speed setting scales that (Slowest 0.6x ... Fastest 3x)
        private static long GetTurnDuration()
        {
            const double secondsPerMonth = 90;
            const double normalSpeed = 1500;

            GameObject game = GameObject.GetInstance();
            double daysInMonth = DateTime.DaysInMonth(game.GameTime.Year, game.GameTime.Month);

            double turnsPerMonth = game.DayRoundEnabled
                ? daysInMonth
                : daysInMonth * 24 * 60 / Math.Max(1, Infrastructure.Settings.GetInstance().MinutesPerTurn);

            double speedFactor = normalSpeed / Math.Max(1, (int)Infrastructure.Settings.GetInstance().GameSpeed);

            return (long) (secondsPerMonth * 1000 / turnsPerMonth / speedFactor);
        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            int failures = 0;

            while (!IsPaused && !_worker.CancellationPending)
            {
                try
                {
                    IsFinish = false;

                    var sw = new Stopwatch();

                    sw.Start();
                    GameObjectHelpers.SimulateTurn();
                    sw.Stop();

                    failures = 0;

                    long waittime = GetTurnDuration() - sw.ElapsedMilliseconds;

                    if (waittime > 0)
                    {
                        Thread.Sleep((int) waittime);
                    }
                }
                catch (Exception ex)
                {
                    failures++;

                    // a failing turn used to loop without any wait, so the clock raced ahead while nothing was simulated
                    StartupLog.Write(string.Format(
                        "Game turn failed ({0} in a row) at game time {1}: {2}",
                        failures,
                        GameObject.GetInstance().GameTime,
                        ex));

                    IsError = true;

                    if (failures >= 3)
                    {
                        IsPaused = true;

                        StartupLog.Write("Game paused because the turn failed repeatedly");

                        var app = System.Windows.Application.Current;
                        app?.Dispatcher.BeginInvoke(
                            new Action(
                                () =>
                                System.Windows.MessageBox.Show(
                                    "The game was paused because a turn failed several times in a row.\n\n"
                                    + ex.GetType().Name + ": " + ex.Message
                                    + "\n\nDetails are in startup.log in your Documents\\theairlineproject folder.",
                                    "TheAirline")));
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }
            }

            IsFinish = true;
        }

        private void bw_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (_worker.CancellationPending)
            {
                Console.WriteLine(@"Canceled!");

                IsFinish = true;
            }

            else if (e.Error != null)
            {
                //this.Cancelled = true;

                Console.WriteLine(@"Error: " + e.Error.Message);

                var lCurrentStack = new StackTrace(true);

                var file = new StreamWriter(AppSettings.GetCommonApplicationDataPath() + "\\theairlinepause.log");
                file.WriteLine(lCurrentStack.ToString());
                file.WriteLine("------------ERROR MESSAGE--------------");
                file.WriteLine(e.Error.Message);
                file.WriteLine(e.Error.StackTrace);
                file.Close();

                IsError = true;
            }

            IsFinish = true;
        }

        #endregion

        //returns the instance
    }
}
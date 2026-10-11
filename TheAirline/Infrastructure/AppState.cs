using System;
using System.Linq;
using TheAirline.Db;
using TheAirline.Infrastructure.Enums;
using WPFLocalizeExtension.Engine;

namespace TheAirline.Infrastructure
{
    public sealed class AppState : IDisposable
    {
        private readonly Models.General.Settings _settings;

        public AppState()
        {
            //SQLite replaces SQL Server Compact: create the database and a default settings row on first run
            Context.Database.EnsureCreated();
            DatabaseSeeder.Seed(Context);
            _settings = Context.Settings.FirstOrDefault();
            if (_settings == null)
            {
                _settings = new Models.General.Settings();
                Context.Settings.Add(_settings);
                Context.SaveChanges();
            }

            var dict = LocalizeDictionary.Instance;
            dict.IncludeInvariantCulture = false;
        }

        public string Language
        {
            get { return _settings.Language; }
            set { _settings.Language = value; }
        }

        public ScreenMode Mode
        {
            get { return _settings.Mode; }
            set { _settings.Mode = value; }
        }

        public AirlineContext Context { get; } = new AirlineContext();

        public void Dispose()
        {
            Context.SaveChanges();
            Context.Dispose();
        }

        public void SaveState()
        {
            Context.SaveChanges();
        }
    }
}
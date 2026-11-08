using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TheAirline.Models.General;

namespace TheAirline.Presentation
{
    public class NewsRow : ObservableObject
    {
        private bool _read;

        public NewsRow(News news)
        {
            News = news;
            _read = news.IsRead;
            Date = DateFormatter.Date(news.Date);
            Type = TypeName(news.Type);
            Subject = news.Subject;
        }

        public News News { get; }
        public string Date { get; }
        public string Type { get; }
        public string Subject { get; }
        public string Marker => _read ? "  " : "● ";
        public double Weight => _read ? 0.6 : 1.0;

        public bool IsRead
        {
            get { return _read; }
            set
            {
                if (SetProperty(ref _read, value))
                {
                    News.IsRead = value;
                    OnPropertyChanged(nameof(Marker));
                    OnPropertyChanged(nameof(Weight));
                }
            }
        }

        public static string TypeName(News.NewsType type)
        {
            switch (type)
            {
                case News.NewsType.AirportNews: return "Airports";
                case News.NewsType.FlightNews: return "Flights";
                case News.NewsType.FleetNews: return "Fleet";
                case News.NewsType.AirlineNews: return "Airlines";
                case News.NewsType.AllianceNews: return "Alliances";
                case News.NewsType.AirlinerNews: return "Aircraft";
                case News.NewsType.RivalNews: return "Rivals";
                default: return "General";
            }
        }
    }

    /// <summary>The news log of the game: read, filter and clear the messages.</summary>
    public class NewsViewModel : ObservableObject
    {
        private const string AllTypes = "All";
        private const int MaxRows = 500;

        private string _filter = AllTypes;
        private bool _unreadOnly;
        private NewsRow _selected;
        private int _seenCount = -1, _seenUnread = -1;

        private readonly Func<object, bool> _openLink;

        /// <param name="openLink">Opens what a message points to (a rival alert's route on the competitor screen); false when it can't.</param>
        public NewsViewModel(Func<object, bool> openLink = null)
        {
            _openLink = openLink;
            OpenLinkCommand = new RelayCommand(OpenLink);
            Types = new List<string> { AllTypes };
            Types.AddRange(Enum.GetValues(typeof(News.NewsType)).Cast<News.NewsType>().Select(NewsRow.TypeName));
            MarkAllReadCommand = new RelayCommand(MarkAllRead);
            DeleteSelectedCommand = new RelayCommand(DeleteSelected);
            DeleteReadCommand = new RelayCommand(DeleteRead);
            Reload();
        }

        public ObservableCollection<NewsRow> Rows { get; } = new ObservableCollection<NewsRow>();
        public List<string> Types { get; }
        public ICommand MarkAllReadCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand DeleteReadCommand { get; }
        public string Count { get; private set; }
        public int Unread { get; private set; }
        public string Body => _selected?.News.Body ?? "";
        public string BodyTitle => _selected == null ? "" : _selected.Date + "  |  " + _selected.Type + "  |  " + _selected.Subject;
        public bool HasSelection => _selected != null;
        public ICommand OpenLinkCommand { get; }

        /// <summary>The selected message points somewhere (a rival alert: the route on the competitor screen).</summary>
        public bool HasLink => _openLink != null && (_selected?.News.ActionObject is TheAirline.Helpers.RivalLink || _selected?.News.ActionObject is TheAirline.Helpers.ReportLink);

        public string LinkText => _selected?.News.ActionObject is TheAirline.Helpers.ReportLink ? "Open the report" : "Show in Competitors";

        /// <summary>Said under the message when its link no longer leads anywhere.</summary>
        public string LinkNote { get; private set; } = "";

        private void OpenLink()
        {
            if (!HasLink) return;
            bool ok = _openLink(_selected.News.ActionObject);
            LinkNote = ok ? "" : "You no longer fly this route.";
            OnPropertyChanged(nameof(LinkNote));
        }

        public string Filter
        {
            get { return _filter; }
            set { if (SetProperty(ref _filter, value ?? AllTypes)) Reload(true); }
        }

        public bool UnreadOnly
        {
            get { return _unreadOnly; }
            set { if (SetProperty(ref _unreadOnly, value)) Reload(true); }
        }

        public NewsRow Selected
        {
            get { return _selected; }
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    if (value != null && !value.IsRead)
                    {
                        value.IsRead = true;
                        UpdateCounts();
                    }
                    OnPropertyChanged(nameof(Body));
                    OnPropertyChanged(nameof(BodyTitle));
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(HasLink));
                    OnPropertyChanged(nameof(LinkText));
                    LinkNote = "";
                    OnPropertyChanged(nameof(LinkNote));
                }
            }
        }

        private static List<News> Snapshot()
        {
            var box = GameObject.GetInstance()?.NewsBox;
            if (box == null) return new List<News>();
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { return box.GetNews().ToList(); }
                catch (InvalidOperationException) { }
                catch (ArgumentException) { }
            }
            return new List<News>();
        }

        /// <summary>Called on every screen refresh: only rebuilds when messages arrived or were removed.</summary>
        public void Poll()
        {
            var all = Snapshot();
            int unread = all.Count(n => !n.IsRead);
            if (all.Count != _seenCount || unread != _seenUnread)
            {
                Reload(true);
            }
        }

        public void Reload(bool force = true)
        {
            var all = Snapshot();
            _seenCount = all.Count;
            _seenUnread = all.Count(n => !n.IsRead);

            var keep = _selected?.News;
            var list = all.Where(
                n => (_filter == AllTypes || NewsRow.TypeName(n.Type) == _filter) && (!_unreadOnly || !n.IsRead))
                          .OrderByDescending(n => n.Date)
                          .Take(MaxRows)
                          .Select(n => new NewsRow(n))
                          .ToList();

            Rows.Clear();
            foreach (var r in list) Rows.Add(r);
            _selected = Rows.FirstOrDefault(r => r.News == keep);
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(Body));
            OnPropertyChanged(nameof(BodyTitle));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasLink));
            Count = Rows.Count + " of " + all.Count + " messages";
            OnPropertyChanged(nameof(Count));
            UpdateCounts();
        }

        private void UpdateCounts()
        {
            var box = GameObject.GetInstance()?.NewsBox;
            Unread = Snapshot().Count(n => !n.IsRead);
            _seenUnread = Unread;
            if (box != null) box.HasUnreadNews = Unread > 0;
            OnPropertyChanged(nameof(Unread));
        }

        private void MarkAllRead()
        {
            foreach (var r in Rows) r.IsRead = true;
            UpdateCounts();
            if (_unreadOnly) Reload();
        }

        private void DeleteSelected()
        {
            if (_selected == null) return;
            GameObject.GetInstance().NewsBox.RemoveNews(_selected.News);
            Reload();
        }

        private void DeleteRead()
        {
            var box = GameObject.GetInstance().NewsBox;
            foreach (var n in Snapshot().Where(n => n.IsRead)) box.RemoveNews(n);
            Reload();
        }
    }
}

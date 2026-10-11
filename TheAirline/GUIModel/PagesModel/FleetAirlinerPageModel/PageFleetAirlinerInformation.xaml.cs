using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TheAirline.GraphicsModel.UserControlModel.MessageBoxModel;
using TheAirline.GraphicsModel.UserControlModel.PopUpWindowsModel;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.Helpers;
using TheAirline.Models.Airliners;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Finances;
using TheAirline.Models.Pilots;
using TheAirline.Views.Airline;

namespace TheAirline.GUIModel.PagesModel.FleetAirlinerPageModel
{
    /// <summary>
    ///     Interaction logic for PageFleetAirlinerInformation.xaml
    /// </summary>
    public partial class PageFleetAirlinerInformation : Page
    {
        #region Constructors and Destructors

        public PageFleetAirlinerInformation(FleetAirlinerMVVM airliner)
        {
            Airliner = airliner;

            InRoute = Airliner.Airliner.Status != FleetAirliner.AirlinerStatus.Stopped;

            DataContext = Airliner;
            Loaded += PageFleetAirlinerInformation_Loaded;

            InitializeComponent();
        }

        #endregion

        #region Public Properties

        public FleetAirlinerMVVM Airliner { get; set; }

        public Boolean InRoute { get; set; }

        #endregion

        #region Methods

        private void PageFleetAirlinerInformation_Loaded(object sender, RoutedEventArgs e)
        {
            Airliner.Classes.CollectionChanged -= Classes_CollectionChanged;
            Airliner.Classes.CollectionChanged += Classes_CollectionChanged;
            BuildCabinStrip();

            foreach (AirlinerClassMVVM aClass in Airliner.Classes)
            {
                foreach (AirlinerFacilityMVVM aFacility in aClass.Facilities)
                {
                    AirlinerFacility facility =
                        Airliner.Airliner.Airliner.GetAirlinerClass(aClass.Type).GetFacility(aFacility.Type);
                    aFacility.SelectedFacility = facility;
                }
            }
        }

        private readonly List<Action> _stripRefreshers = new List<Action>();

        private void Classes_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            BuildCabinStrip();
        }

        private static int CabinOrder(AirlinerClass.ClassType type)
        {
            switch (type)
            {
                case AirlinerClass.ClassType.FirstClass:
                    return 0;
                case AirlinerClass.ClassType.BusinessClass:
                    return 1;
                default:
                    return 2;
            }
        }

        private static Brush CabinBrush(AirlinerClass.ClassType type)
        {
            switch (type)
            {
                case AirlinerClass.ClassType.FirstClass:
                    return new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B));
                case AirlinerClass.ClassType.BusinessClass:
                    return new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x6B));
                default:
                    return new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5));
            }
        }

        //moves seats between two classes by changing the seat counts the sliders use (economy follows automatically)
        private static void MoveSeats(AirlinerClassMVVM left, AirlinerClassMVVM right, int move)
        {
            bool rightIsEconomy = right.Type == AirlinerClass.ClassType.EconomyClass;

            if (rightIsEconomy)
            {
                int target = Math.Max(1, Math.Min(left.MaxSeats, left.Seating + move));
                if (target != left.Seating)
                {
                    left.Seating = target;
                }
                return;
            }

            // both are changeable classes: lower the shrinking one first so the limits stay valid
            move = Math.Max(1 - left.Seating, Math.Min(right.Seating - 1, move));
            if (move > 0)
            {
                right.Seating -= move;
                left.Seating += move;
            }
            else if (move < 0)
            {
                left.Seating += move;
                right.Seating -= move;
            }
        }

        //builds the cabin strip: the classes as blocks, first class left, economy right, with draggable dividers
        private void BuildCabinStrip()
        {
            foreach (Action undo in _stripRefreshers)
            {
                undo();
            }
            _stripRefreshers.Clear();

            cabinStrip.Children.Clear();
            cabinStrip.ColumnDefinitions.Clear();

            List<AirlinerClassMVVM> ordered = Airliner.Classes.OrderBy(c => CabinOrder(c.Type)).ToList();
            if (ordered.Count == 0)
            {
                return;
            }

            var labels = new List<TextBlock>();
            var columns = new List<ColumnDefinition>();

            for (int i = 0; i < ordered.Count; i++)
            {
                var column = new ColumnDefinition { MinWidth = 24 };
                cabinStrip.ColumnDefinitions.Add(column);
                columns.Add(column);

                var label = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                labels.Add(label);

                var block = new Border
                {
                    Background = CabinBrush(ordered[i].Type),
                    Child = label,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(1)
                };
                Grid.SetColumn(block, i * 2);
                cabinStrip.Children.Add(block);

                if (i < ordered.Count - 1)
                {
                    cabinStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                }
            }

            Action refresh = () =>
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    labels[i].Text = string.Format("{0} {1}", ordered[i].Type.ToString().Replace("Class", ""), ordered[i].Seating);
                    columns[i].Width = new GridLength(Math.Max(1, ordered[i].Seating), GridUnitType.Star);
                }
            };

            foreach (AirlinerClassMVVM aClass in ordered)
            {
                AirlinerClassMVVM captured = aClass;
                PropertyChangedEventHandler handler = (o, args) =>
                {
                    if (args.PropertyName == "Seating")
                    {
                        refresh();
                    }
                };
                captured.PropertyChanged += handler;
                _stripRefreshers.Add(() => captured.PropertyChanged -= handler);
            }

            refresh();

            for (int i = 0; i < ordered.Count - 1; i++)
            {
                AirlinerClassMVVM left = ordered[i];
                AirlinerClassMVVM right = ordered[i + 1];

                bool canMove = left.ChangeableSeats
                               && (right.Type == AirlinerClass.ClassType.EconomyClass || right.ChangeableSeats);

                var thumb = new Thumb
                {
                    Background = canMove ? Brushes.White : Brushes.Gray,
                    IsEnabled = canMove,
                    Cursor = System.Windows.Input.Cursors.SizeWE,
                    ToolTip = canMove ? "Drag to move seats" : "These seats cannot be changed"
                };
                Grid.SetColumn(thumb, i * 2 + 1);
                cabinStrip.Children.Add(thumb);

                double carried = 0;
                thumb.DragDelta += (o, args) =>
                {
                    double total = ordered.Sum(c => c.Seating);
                    double usable = Math.Max(1, cabinStrip.ActualWidth - 8 * (ordered.Count - 1));

                    carried += args.HorizontalChange * total / usable;
                    int move = (int)Math.Truncate(carried);
                    if (move == 0)
                    {
                        return;
                    }

                    carried -= move;
                    MoveSeats(left, right, move);
                };
            }
        }

        private void btnAddClass_Click(object sender, RoutedEventArgs e)
        {
            var cbClasses = new ComboBox();
            cbClasses.SetResourceReference(StyleProperty, "ComboBoxTransparentStyle");
            cbClasses.ItemTemplate = Resources["AirlinerClassItem"] as DataTemplate;
            cbClasses.HorizontalAlignment = HorizontalAlignment.Left;
            cbClasses.Width = 200;

            foreach (AirlinerClass.ClassType type in Enum.GetValues(typeof(AirlinerClass.ClassType)))
            {
                Boolean hasClass = Airliner.Classes.ToList().Exists(c => c.Type == type);
                if ((int)type <= GameObject.GetInstance().GameTime.Year && !hasClass)
                {
                    cbClasses.Items.Add(type);
                }
            }

            cbClasses.SelectedIndex = 0;

            AirlinerClassMVVM tClass = Airliner.Classes[0];

            if (PopUpSingleElement.ShowPopUp(
                Translator.GetInstance().GetString("PageFleetAirlinerInformation", "1011"),
                cbClasses) == PopUpSingleElement.ButtonSelected.OK && cbClasses.SelectedItem != null)
            {
                int maxseats;

                int maxCapacity;

                if (Airliner.Airliner.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger)
                {
                    maxCapacity = ((AirlinerPassengerType)Airliner.Airliner.Airliner.Type).MaxSeatingCapacity;
                }
                else
                {
                    maxCapacity = tClass.RegularSeatingCapacity;
                }

                if (Airliner.Classes.Count == 2)
                {
                    maxseats = maxCapacity - 1 - Airliner.Classes[1].RegularSeatingCapacity;
                }
                else
                {
                    maxseats = maxCapacity - 1;
                }

                var aClass = new AirlinerClassMVVM(
                    new AirlinerClass((AirlinerClass.ClassType)cbClasses.SelectedItem, 0),
                    1,
                    1,
                    maxseats,
                    true);

                foreach (AirlinerFacilityMVVM aFacility in aClass.Facilities)
                {
                    AirlinerFacility facility = AirlinerFacilities.GetBasicFacility(aFacility.Type);
                    aFacility.SelectedFacility = facility;
                }

                Airliner.Classes.Add(aClass);

                tClass.RegularSeatingCapacity -= aClass.RegularSeatingCapacity;

                tClass.Seating =
                    Convert.ToInt16(
                        Convert.ToDouble(tClass.RegularSeatingCapacity)
                        / tClass.Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat)
                            .First()
                            .SelectedFacility.SeatUses);
            }
        }

        private void btnAddPilot_Click(object sender, RoutedEventArgs e)
        {
            var cbPilots = new ComboBox();
            cbPilots.SetResourceReference(StyleProperty, "ComboBoxTransparentStyle");
            cbPilots.DisplayMemberPath = "Profile.Name";
            cbPilots.SelectedValuePath = "Profile.Name";
            cbPilots.HorizontalAlignment = HorizontalAlignment.Left;
            cbPilots.Width = 200;

            foreach (
                Pilot pilot in
                    Airliner.Airliner.Airliner.Airline.Pilots.Where(
                        p =>
                            p.Airliner == null
                            && p.Aircrafts.Contains(Airliner.Airliner.Airliner.Type.AirlinerFamily)))
            {
                cbPilots.Items.Add(pilot);
            }

            cbPilots.SelectedIndex = 0;

            AirlinerClassMVVM tClass = Airliner.Classes[0];

            if (PopUpSingleElement.ShowPopUp(
                Translator.GetInstance().GetString("PageFleetAirlinerInformation", "1013"),
                cbPilots) == PopUpSingleElement.ButtonSelected.OK && cbPilots.SelectedItem != null)
            {
                var pilot = (Pilot)cbPilots.SelectedItem;
                Airliner.addPilot(pilot);
            }
        }

        private void btnBuy_Click(object sender, RoutedEventArgs e)
        {
            if (Airliner.Airliner.Airliner.GetPrice() > GameObject.GetInstance().HumanAirline.Money)
            {
                WPFMessageBox.Show(
                    Translator.GetInstance().GetString("MessageBox", "2006"),
                    Translator.GetInstance().GetString("MessageBox", "2006", "message"),
                    WPFMessageBoxButtons.Ok);
            }
            else
            {
                WPFMessageBoxResult result = WPFMessageBox.Show(
                    Translator.GetInstance().GetString("MessageBox", "2007"),
                    string.Format(
                        Translator.GetInstance().GetString("MessageBox", "2007", "message"),
                        new ValueCurrencyConverter().Convert(Airliner.Airliner.Airliner.GetPrice())),
                    WPFMessageBoxButtons.YesNo);

                if (result == WPFMessageBoxResult.Yes)
                {
                    Airliner.buyAirliner();
                }
            }
        }
        private void btnOutlease_Click(object sender, RoutedEventArgs e)
        {
            WPFMessageBoxResult result = WPFMessageBox.Show(
                    Translator.GetInstance().GetString("MessageBox", "2016"),
                    string.Format(
                        Translator.GetInstance().GetString("MessageBox", "2016", "message"),
                        new ValueCurrencyConverter().Convert(Airliner.Airliner.Name)),
                    WPFMessageBoxButtons.YesNo);

            if (result == WPFMessageBoxResult.Yes)
            {
                Airliner.Airliner.Airliner.Status = Models.Airliners.Airliner.StatusTypes.Leasing;

                PageNavigator.NavigateTo(new PageAirline(Airliner.Airliner.Airliner.Airline));
            }
        }
        private void btnConvert_Click(object sender, RoutedEventArgs e)
        {
            double price =
                AirlinerHelpers.GetCargoConvertingPrice(Airliner.Airliner.Airliner.Type as AirlinerPassengerType);

            if (price > GameObject.GetInstance().HumanAirline.Money)
            {
                WPFMessageBox.Show(
                    Translator.GetInstance().GetString("MessageBox", "2015"),
                    string.Format(Translator.GetInstance().GetString("MessageBox", "2015", "message")),
                    WPFMessageBoxButtons.Ok);
            }
            else
            {
                WPFMessageBoxResult result = WPFMessageBox.Show(
                    Translator.GetInstance().GetString("MessageBox", "2014"),
                    string.Format(
                        Translator.GetInstance().GetString("MessageBox", "2014", "message"),
                        new ValueCurrencyConverter().Convert(price)),
                    WPFMessageBoxButtons.YesNo);

                if (result == WPFMessageBoxResult.Yes)
                {
                    Airliner.convertToCargo();

                    AirlineHelpers.AddAirlineInvoice(
                        Airliner.Airliner.Airliner.Airline,
                        GameObject.GetInstance().GameTime,
                        Invoice.InvoiceType.Purchases,
                        -price);
                }
            }
        }

        private void btnDeletePilot_Click(object sender, RoutedEventArgs e)
        {
            var pilot = (Pilot)((Button)sender).Tag;

            WPFMessageBoxResult result = WPFMessageBox.Show(
                Translator.GetInstance().GetString("MessageBox", "2125"),
                string.Format(Translator.GetInstance().GetString("MessageBox", "2125", "message"), pilot.Profile.Name),
                WPFMessageBoxButtons.YesNo);

            if (result == WPFMessageBoxResult.Yes)
            {
                Airliner.removePilot(pilot);
            }
        }

        private void btnEditHomebase_Click(object sender, RoutedEventArgs e)
        {
            var cbHomebase = new ComboBox();
            cbHomebase.SetResourceReference(StyleProperty, "ComboBoxTransparentStyle");
            cbHomebase.ItemTemplate = Application.Current.Resources["AirportCountryItem"] as DataTemplate;
            cbHomebase.HorizontalAlignment = HorizontalAlignment.Left;
            cbHomebase.Width = 200;

            foreach (
                Airport airport in
                    AirlineHelpers.GetHomebases(
                        GameObject.GetInstance().HumanAirline,
                        Airliner.Airliner.Airliner.Type))
            {
                cbHomebase.Items.Add(airport);
            }

            cbHomebase.SelectedIndex = 0;

            if (PopUpSingleElement.ShowPopUp(
                Translator.GetInstance().GetString("PageFleetAirlinerInformation", "1014"),
                cbHomebase) == PopUpSingleElement.ButtonSelected.OK && cbHomebase.SelectedItem != null)
            {
                var homebase = cbHomebase.SelectedItem as Airport;
                Airliner.Homebase = homebase;
            }
        }

        private void btnSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            Airliner.Airliner.Airliner.ClearAirlinerClasses();

            foreach (AirlinerClassMVVM aClass in Airliner.Classes)
            {
                var nClass = new AirlinerClass(aClass.Type, aClass.RegularSeatingCapacity);
                nClass.SeatingCapacity = aClass.Seating;

                foreach (AirlinerFacilityMVVM aFacility in aClass.Facilities)
                {
                    nClass.ForceSetFacility(aFacility.SelectedFacility);
                }

                Airliner.Airliner.Airliner.AddAirlinerClass(nClass);
            }
        }

        private void btnUndoChanges_Click(object sender, RoutedEventArgs e)
        {
            var allclasses = new List<AirlinerClassMVVM>(Airliner.Classes);

            foreach (
                AirlinerClassMVVM aClass in
                    allclasses.Where(c => !Airliner.Airliner.Airliner.Classes.Exists(ac => ac.Type == c.Type)))
            {
                Airliner.Classes.Remove(aClass);
            }

            foreach (AirlinerClassMVVM aClass in Airliner.Classes)
            {
                aClass.RegularSeatingCapacity =
                    Airliner.Airliner.Airliner.GetAirlinerClass(aClass.Type).RegularSeatingCapacity;
                aClass.Seating = Airliner.Airliner.Airliner.GetAirlinerClass(aClass.Type).SeatingCapacity;

                foreach (AirlinerFacilityMVVM aFacility in aClass.Facilities)
                {
                    AirlinerFacility facility =
                        Airliner.Airliner.Airliner.GetAirlinerClass(aClass.Type).GetFacility(aFacility.Type);
                    aFacility.SelectedFacility = facility;
                }
            }
        }

        private void slSeats_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var aClass = (AirlinerClassMVVM)((Slider)sender).Tag;

            if (aClass.Type != AirlinerClass.ClassType.EconomyClass && !aClass.ChangedFacility)
            {
                var diff = (int)(e.NewValue - e.OldValue);

                Airliner.Classes[0].RegularSeatingCapacity -= diff;
                Airliner.Classes[0].Seating =
                    Convert.ToInt16(
                        Convert.ToDouble(Airliner.Classes[0].RegularSeatingCapacity)
                        / Airliner.Classes[0].Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat)
                            .First()
                            .SelectedFacility.SeatUses);

                if (Airliner.Classes.Count == 3)
                {
                    if (Airliner.Classes[1] == aClass)
                    {
                        //this.Airliner.Classes[2].RegularSeatingCapacity -= diff;
                        Airliner.Classes[2].MaxSeats -=
                            Convert.ToInt16(
                                Convert.ToDouble(diff)
                                / Airliner.Classes[2].Facilities.Where(
                                    f => f.Type == AirlinerFacility.FacilityType.Seat).First().SelectedFacility.SeatUses);
                    }
                    else
                    {
                        //this.Airliner.Classes[1].RegularSeatingCapacity -= diff;
                        Airliner.Classes[1].MaxSeats -=
                            Convert.ToInt16(
                                Convert.ToDouble(diff)
                                / Airliner.Classes[2].Facilities.Where(
                                    f => f.Type == AirlinerFacility.FacilityType.Seat).First().SelectedFacility.SeatUses);
                    }
                }
            }
            /*
            if (aClass.Type != AirlinerClass.ClassType.Economy_Class)
            {
                double diff = (e.NewValue - e.OldValue);// *aClass.Facilities.Find(f => f.Type == AirlinerFacility.FacilityType.Seat).SelectedFacility.SeatUses;

              
                if (this.Airliner.Classes.Count == 3)
                {
                    if (this.Airliner.Classes[1] == aClass)
                    {
                       // this.Airliner.Classes[2].RegularSeatingCapacity -= Convert.ToInt16(diff);
                        this.Airliner.Classes[2].MaxSeats = Convert.ToInt16(Convert.ToDouble(this.Airliner.Classes[2].RegularSeatingCapacity) / this.Airliner.Classes[2].Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat).First().SelectedFacility.SeatUses);
           
                    }
                    
                    if (this.Airliner.Classes[2] == aClass)
                    {
                       // this.Airliner.Classes[1].RegularSeatingCapacity -= Convert.ToInt16(diff);
                        this.Airliner.Classes[1].MaxSeats = Convert.ToInt16(Convert.ToDouble(this.Airliner.Classes[1].RegularSeatingCapacity) / this.Airliner.Classes[1].Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat).First().SelectedFacility.SeatUses);
                    }
                }

                AirlinerClassMVVM tClass = this.Airliner.Classes[0];

                tClass.RegularSeatingCapacity -= Convert.ToInt16(diff);

                tClass.Seating = Convert.ToInt16(Convert.ToDouble(tClass.RegularSeatingCapacity) / tClass.Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat).First().SelectedFacility.SeatUses);
            }*/
        }

        #endregion
    }
}
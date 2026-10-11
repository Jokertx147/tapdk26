using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TheAirline.GraphicsModel.UserControlModel.MessageBoxModel;
using TheAirline.GraphicsModel.UserControlModel.PopUpWindowsModel;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.Infrastructure;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;
using TheAirline.GUIModel.PagesModel.FleetAirlinerPageModel;

namespace TheAirline.Views.Airline
{
    /// <summary>
    ///     Interaction logic for PageAirlineEditAirliners.xaml
    /// </summary>
    public partial class PageAirlineEditAirliners
    {
        #region Constructors and Destructors

        public PageAirlineEditAirliners(List<FleetAirliner> airliners)
        {
            Airliners = airliners;

            Loaded += PageAirlineEditAirliners_Loaded;

            DataContext = Airliners;

            Classes = new ObservableCollection<AirlinerClassMVVM>();
            Airliner = getMinimumAirliner();

            foreach (var aClass in Airliner.Airliner.Classes)
            {
                var maxCapacity = Airliner.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger ? ((AirlinerPassengerType) Airliner.Airliner.Type).MaxSeatingCapacity : 100;

                var changeable = Airliner.Airliner.Classes.IndexOf(aClass) > 0;

                int maxSeats;

                if (Airliner.Airliner.Classes.Count == 3)
                {
                    if (Airliner.Airliner.Classes.IndexOf(aClass) == 1)
                    {
                        maxSeats = maxCapacity - 1 - Airliner.Airliner.Classes[2].RegularSeatingCapacity;
                    }
                    else
                    {
                        maxSeats = maxCapacity - 1 - Airliner.Airliner.Classes[1].RegularSeatingCapacity;
                    }
                }
                else
                {
                    maxSeats = maxCapacity - 1;
                }

                var amClass = new AirlinerClassMVVM(
                    aClass,
                    aClass.SeatingCapacity,
                    aClass.RegularSeatingCapacity,
                    maxSeats,
                    changeable);
                Classes.Add(amClass);
            }

            InitializeComponent();
        }

        #endregion

        #region Public Properties

        public ObservableCollection<AirlinerClassMVVM> Classes { get; set; }

        #endregion

        #region Fields

        private readonly FleetAirliner Airliner;

        private readonly List<FleetAirliner> Airliners;

        #endregion

        //returns the minimum of the selected airliners

        #region Methods

        private void PageAirlineEditAirliners_Loaded(object sender, RoutedEventArgs e)
        {
            Classes.CollectionChanged -= Classes_CollectionChanged;
            Classes.CollectionChanged += Classes_CollectionChanged;
            BuildCabinStrip();

            var tab_main = UIHelpers.FindChild<TabControl>(Tag as Page, "tcMenu");

            if (tab_main != null)
            {
                var matchingItem =
                    tab_main.Items.Cast<TabItem>().FirstOrDefault(item => item.Tag.ToString() == "Airliners");

                matchingItem.Visibility = Visibility.Visible;

                matchingItem.IsSelected = true;
            }
        }

        private readonly List<Action> _stripRefreshers = new List<Action>();

        private void Classes_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
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

            List<AirlinerClassMVVM> ordered = Classes.OrderBy(c => CabinOrder(c.Type)).ToList();
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
                Boolean hasClass = Classes.ToList().Exists(c => c.Type == type);
                if ((int)type <= GameObject.GetInstance().GameTime.Year && !hasClass)
                {
                    cbClasses.Items.Add(type);
                }
            }

            cbClasses.SelectedIndex = 0;

            AirlinerClassMVVM tClass = Classes[0];

            if (PopUpSingleElement.ShowPopUp(
                Translator.GetInstance().GetString("PageFleetAirlinerInformation", "1011"),
                cbClasses) == PopUpSingleElement.ButtonSelected.OK && cbClasses.SelectedItem != null)
            {
                int maxseats;

                int maxCapacity;

                if (Airliner.Airliner.Type.TypeAirliner == AirlinerType.TypeOfAirliner.Passenger)
                {
                    maxCapacity = ((AirlinerPassengerType)Airliner.Airliner.Type).MaxSeatingCapacity;
                }
                else
                {
                    maxCapacity = tClass.RegularSeatingCapacity;
                }

                if (Classes.Count == 2)
                {
                    maxseats = maxCapacity - 1 - Classes[1].RegularSeatingCapacity;
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

                Classes.Add(aClass);

                tClass.RegularSeatingCapacity -= aClass.RegularSeatingCapacity;

                tClass.Seating =
                    Convert.ToInt16(
                        Convert.ToDouble(tClass.RegularSeatingCapacity)
                        / tClass.Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat)
                            .First()
                            .SelectedFacility.SeatUses);
            }
        }

        //removes a business or first class; its seats go back to economy (applied when the changes are saved)
        private void btnRemoveClass_Click(object sender, RoutedEventArgs e)
        {
            var aClass = (AirlinerClassMVVM)((Button)sender).Tag;

            if (aClass == null || aClass.Type == AirlinerClass.ClassType.EconomyClass || Classes.Count < 2)
            {
                return;
            }

            AirlinerClassMVVM economy = Classes[0];

            economy.RegularSeatingCapacity += aClass.RegularSeatingCapacity;
            economy.Seating =
                Convert.ToInt16(
                    Convert.ToDouble(economy.RegularSeatingCapacity)
                    / economy.Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat)
                        .First()
                        .SelectedFacility.SeatUses);

            Classes.Remove(aClass);

            // the class that is left may now use all the seats economy does not
            foreach (AirlinerClassMVVM other in Classes.Skip(1))
            {
                other.MaxSeats = other.MaxSeatsCapacity;
            }
        }

        private void slSeats_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var aClass = (AirlinerClassMVVM)((Slider)sender).Tag;

            if (aClass.Type != AirlinerClass.ClassType.EconomyClass && !aClass.ChangedFacility)
            {
                var diff = (int)(e.NewValue - e.OldValue);

                Classes[0].RegularSeatingCapacity -= diff;
                Classes[0].Seating =
                    Convert.ToInt16(
                        Convert.ToDouble(Classes[0].RegularSeatingCapacity)
                        / Classes[0].Facilities.Where(f => f.Type == AirlinerFacility.FacilityType.Seat)
                            .First()
                            .SelectedFacility.SeatUses);

                if (Classes.Count == 3)
                {
                    if (Classes[1] == aClass)
                    {
                        //Classes[2].RegularSeatingCapacity -= diff;
                        Classes[2].MaxSeats -=
                            Convert.ToInt16(
                                Convert.ToDouble(diff)
                                / Classes[2].Facilities.Where(
                                    f => f.Type == AirlinerFacility.FacilityType.Seat).First().SelectedFacility.SeatUses);
                    }
                    else
                    {
                        //Classes[1].RegularSeatingCapacity -= diff;
                        Classes[1].MaxSeats -=
                            Convert.ToInt16(
                                Convert.ToDouble(diff)
                                / Classes[2].Facilities.Where(
                                    f => f.Type == AirlinerFacility.FacilityType.Seat).First().SelectedFacility.SeatUses);
                    }
                }
            }
        }

        private void btnApply_Click(object sender, RoutedEventArgs e)
        {
            var result = WPFMessageBox.Show(
                Translator.GetInstance().GetString("MessageBox", "2128"),
                string.Format(Translator.GetInstance().GetString("MessageBox", "2128", "message")),
                WPFMessageBoxButtons.YesNo);

            if (result == WPFMessageBoxResult.Yes)
            {
                foreach (var airliner in Airliners)
                {
                    airliner.Airliner.ClearAirlinerClasses();

                    foreach (var aClass in Classes)
                    {
                        var newClass = new AirlinerClass(aClass.Type, aClass.Seating);

                        airliner.Airliner.AddAirlinerClass(newClass);

                        foreach (var facility in aClass.Facilities)
                        {
                            newClass.ForceSetFacility(facility.SelectedFacility);
                        }
                    }

                    var totalSeats = Classes.Sum(c => c.RegularSeatingCapacity);

                    var seatingDiff = ((AirlinerPassengerType) airliner.Airliner.Type).MaxSeatingCapacity - totalSeats;

                    var economyClass =
                        airliner.Airliner.Classes.Find(c => c.Type == AirlinerClass.ClassType.EconomyClass);
                    economyClass.RegularSeatingCapacity += seatingDiff;

                    var seatingFacility = economyClass.GetFacility(AirlinerFacility.FacilityType.Seat);

                    var extraSeats = (int) (seatingDiff/seatingFacility.SeatUses);

                    economyClass.SeatingCapacity += extraSeats;
                }
            }
        }

        private void btnLoadConfiguration_Click(object sender, RoutedEventArgs e)
        {
            var cbConfigurations = new ComboBox();
            cbConfigurations.SetResourceReference(StyleProperty, "ComboBoxTransparentStyle");
            cbConfigurations.SelectedValuePath = "Name";
            cbConfigurations.DisplayMemberPath = "Name";
            cbConfigurations.HorizontalAlignment = HorizontalAlignment.Left;
            cbConfigurations.Width = 200;

            foreach (
                AirlinerConfiguration confItem in
                    Configurations.GetConfigurations(Configuration.ConfigurationType.Airliner)
                        .Where(
                            a =>
                                ((AirlinerConfiguration) a).MinimumSeats <= Airliner.Airliner.GetTotalSeatCapacity()
                                && ((AirlinerConfiguration) a).Classes.Count
                                <= ((AirlinerPassengerType) Airliner.Airliner.Type).MaxAirlinerClasses))
            {
                cbConfigurations.Items.Add(confItem);
            }

            cbConfigurations.SelectedIndex = 0;

            if (PopUpSingleElement.ShowPopUp(
                Translator.GetInstance().GetString("PageAirlineWages", "1013"),
                cbConfigurations) == PopUpSingleElement.ButtonSelected.OK && cbConfigurations.SelectedItem != null)
            {
                Classes.Clear();

                var configuration = (AirlinerConfiguration) cbConfigurations.SelectedItem;

                foreach (var aClass in configuration.Classes)
                {
                    var nClass = new AirlinerClass(aClass.Type, aClass.SeatingCapacity);
                    var tClass = new AirlinerClassMVVM(
                        nClass,
                        aClass.SeatingCapacity,
                        aClass.RegularSeatingCapacity,
                        aClass.RegularSeatingCapacity);

                    foreach (var facility in aClass.GetFacilities())
                    {
                        tClass.Facilities.First(f => f.Type == facility.Type).SelectedFacility = facility;
                    }

                    foreach (
                        AirlinerFacility.FacilityType fType in Enum.GetValues(typeof (AirlinerFacility.FacilityType)))
                    {
                        if (!aClass.Facilities.Exists(f => f.Type == fType))
                        {
                            tClass.Facilities.First(f => f.Type == fType).SelectedFacility =
                                AirlinerFacilities.GetBasicFacility(fType);
                        }
                    }

                    Classes.Add(tClass);
                }
                var seatingDiff = ((AirlinerPassengerType) Airliner.Airliner.Type).MaxSeatingCapacity
                                  - configuration.MinimumSeats;

                var economyClass = Classes.First(
                    c => c.Type == AirlinerClass.ClassType.EconomyClass);

                economyClass.RegularSeatingCapacity += seatingDiff;

                var seatingFacility =
                    economyClass.Facilities.First(f => f.Type == AirlinerFacility.FacilityType.Seat).SelectedFacility;

                var extraSeats = (int) (seatingDiff/seatingFacility.SeatUses);

                economyClass.Seating += extraSeats;
            }
        }

        private void btnSaveConfiguration_Click(object sender, RoutedEventArgs e)
        {
            var totalServiceLevel = Classes.Sum(c => c.Facilities.Sum(f => f.SelectedFacility.ServiceLevel));
            var txtName = new TextBox
            {
                Width = 200,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                Text = $"Custom configuration ({Classes.Count} classes)",
                HorizontalAlignment = HorizontalAlignment.Left
            };

            if (PopUpSingleElement.ShowPopUp(
                Translator.GetInstance().GetString("PageAirlineEditAirliners", "1002"),
                txtName) == PopUpSingleElement.ButtonSelected.OK && txtName.Text.Trim().Length > 2)
            {
                var name = txtName.Text.Trim();
                var configuration = new AirlinerConfiguration(
                    name,
                    getMinimumAirliner().Airliner.GetTotalSeatCapacity(),
                    false);

                foreach (var type in Classes)
                {
                    var classConfiguration = new AirlinerClassConfiguration(
                        type.Type,
                        type.Seating,
                        type.RegularSeatingCapacity);

                    foreach (var facility in type.Facilities)
                    {
                        classConfiguration.AddFacility(facility.SelectedFacility);
                    }

                    configuration.AddClassConfiguration(classConfiguration);
                }

                Configurations.AddConfiguration(configuration);
            }
        }

        private FleetAirliner getMinimumAirliner()
        {
            var minAirliner = Airliners[0];

            foreach (var airliner in Airliners)
            {
                if (((AirlinerPassengerType) airliner.Airliner.Type).MaxAirlinerClasses
                    < ((AirlinerPassengerType) minAirliner.Airliner.Type).MaxAirlinerClasses)
                {
                    minAirliner = airliner;
                }
                else
                {
                    if (((AirlinerPassengerType) airliner.Airliner.Type).MaxAirlinerClasses
                        == ((AirlinerPassengerType) minAirliner.Airliner.Type).MaxAirlinerClasses
                        && airliner.Airliner.Classes[0].SeatingCapacity
                        < minAirliner.Airliner.Classes[0].SeatingCapacity)
                    {
                        minAirliner = airliner;
                    }
                }
            }

            return minAirliner;
        }

        #endregion
    }
}
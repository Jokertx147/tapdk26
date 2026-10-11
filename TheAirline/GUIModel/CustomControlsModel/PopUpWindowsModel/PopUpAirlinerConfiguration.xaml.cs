using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using TheAirline.GUIModel.HelpersModel;
using TheAirline.Models.Airliners;
using TheAirline.Models.General;

namespace TheAirline.GraphicsModel.UserControlModel.PopUpWindowsModel
{
    /// <summary>
    /// Interaction logic for PopUpAirlinerConfiguration.xaml
    /// </summary>
    public partial class PopUpAirlinerConfiguration : PopUpWindow
    {
        public static int MaxSeats;
        public static List<AirlinerClass.ClassType> FreeClasses;
        private List<AirlinerClass> Classes;
        private ListBox lbClasses;
        private readonly Dictionary<AirlinerClass, int[]> originalSeats = new Dictionary<AirlinerClass, int[]>();
        private Grid cabinStrip;
        private TextBlock cabinHint;
        private ContentControl lblNewClass;
        private AirlinerType Type;
        private AirlinerClass.ClassType CurrentClass;
        public static object ShowPopUp(Airliner airliner)
        {
            PopUpWindow window = new PopUpAirlinerConfiguration(airliner);
            window.ShowDialog();

            return window.Selected;
        }
          public static object ShowPopUp(AirlinerType airliner, List<AirlinerClass> classes)
        {
              
            PopUpWindow window = new PopUpAirlinerConfiguration(airliner,classes);
            window.ShowDialog();

            return window.Selected;
        }
       
        public PopUpAirlinerConfiguration(AirlinerType type, List<AirlinerClass> classes)
        {
            this.Classes = new List<AirlinerClass>(classes);
            
            this.Type = type;
          
            createPopUp();
        }
        public PopUpAirlinerConfiguration(Airliner airliner)
        {
          
            this.Classes = new List<AirlinerClass>();

            foreach (AirlinerClass aClass in airliner.Classes)
            {
                this.Classes.Add(aClass);
            }

            this.Type = airliner.Type;

            createPopUp();

        }
        //creates the pop up
        private void createPopUp()
        {
            InitializeComponent();

            // remember the seat counts so Cancel can undo the dragging
            foreach (AirlinerClass aClass in this.Classes)
            {
                originalSeats[aClass] = new[] { aClass.SeatingCapacity, aClass.RegularSeatingCapacity };
            }

            this.Title = "Airliner Configuration";

            this.Width = 520;

            this.Height = 260;

            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;


            StackPanel panelClasses = new StackPanel();

            cabinHint = new TextBlock
            {
                Text = "Drag the dividers to move seats between classes",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 3)
            };
            panelClasses.Children.Add(cabinHint);

            cabinStrip = new Grid { Height = 44, Margin = new Thickness(0, 0, 0, 6) };
            panelClasses.Children.Add(cabinStrip);

            lbClasses = new ListBox();
            lbClasses.ItemContainerStyleSelector = new ListBoxItemStyleSelector();
            lbClasses.ItemTemplate = this.Resources["ClassItem"] as DataTemplate;


            panelClasses.Children.Add(lbClasses);

            lblNewClass = new ContentControl();
            lblNewClass.Margin = new Thickness(0, 5, 0, 0);
            lblNewClass.ContentTemplate = this.Resources["NewClassItem"] as DataTemplate;
            lblNewClass.Content = AirlinerClass.ClassType.FirstClass;

            panelClasses.Children.Add(lblNewClass);



            WrapPanel panelButtons = new WrapPanel();
            panelButtons.Margin = new Thickness(0, 5, 0, 0);

            Button btnOk = new Button();
            btnOk.SetResourceReference(StyleProperty, "StandardButtonStyle");
            btnOk.Height = 16;
            btnOk.Width = Double.NaN;
            btnOk.HorizontalAlignment = HorizontalAlignment.Left;
            btnOk.Click += new RoutedEventHandler(btnOk_Click);
            btnOk.Content = "OK";
       
            panelButtons.Children.Add(btnOk);

            Button btnCancel = new Button();
            btnCancel.SetResourceReference(StyleProperty, "StandardButtonStyle");
            btnCancel.Height = 16;
            btnCancel.Width = Double.NaN;
            btnCancel.HorizontalAlignment = HorizontalAlignment.Left;
            btnCancel.Click += new RoutedEventHandler(btnCancel_Click);
            btnCancel.Margin = new Thickness(5, 0, 0, 0);
            btnCancel.Content = "Cancel";
         
            panelButtons.Children.Add(btnCancel);

            panelClasses.Children.Add(panelButtons);

            this.Content = panelClasses;

            showAirlinerClasses();

        }
        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pair in originalSeats)
            {
                pair.Key.SeatingCapacity = pair.Value[0];
                pair.Key.RegularSeatingCapacity = pair.Value[1];
            }

            this.Selected = null;
            this.Close();
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            
            this.Selected = this.Classes;
            this.Close();
        }
        //shows the airliner classes
        private void showAirlinerClasses()
        {
            lbClasses.Items.Clear();

            FreeClasses = new List<AirlinerClass.ClassType>();

            int i = 0;
            foreach (AirlinerClass aClass in this.Classes)
            {
                lbClasses.Items.Add(new AirlinerClassItem(aClass, i == this.Classes.Count - 1 && i > 0));

                i++;
            }

            int maxCapacity = ((AirlinerPassengerType)this.Type).MaxSeatingCapacity;
        

            foreach (AirlinerClass.ClassType type in Enum.GetValues(typeof(AirlinerClass.ClassType)))
            {
                if (!this.Classes.Exists(c => c.Type == type) && ((int)type <= GameObject.GetInstance().GameTime.Year))
                    FreeClasses.Add(type);
            }
            
            AirlinerClass.ClassType nextClass = FreeClasses.Count > 0 ?  FreeClasses[0] : AirlinerClass.ClassType.EconomyClass;

            MaxSeats = maxCapacity - this.Classes.Count;

            lblNewClass.Visibility = this.Classes.Count < ((AirlinerPassengerType)this.Type).MaxAirlinerClasses && FreeClasses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            lblNewClass.Content = nextClass;

            buildCabinStrip();
        }

        //the cabin runs first class (left) to economy (right)
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

        //builds the strip showing the classes as blocks with draggable dividers between them
        private void buildCabinStrip()
        {
            if (cabinStrip == null)
            {
                return;
            }

            cabinStrip.Children.Clear();
            cabinStrip.ColumnDefinitions.Clear();

            List<AirlinerClass> ordered = this.Classes.OrderBy(c => CabinOrder(c.Type)).ToList();
            var labels = new List<TextBlock>();
            var columns = new List<ColumnDefinition>();

            for (int i = 0; i < ordered.Count; i++)
            {
                AirlinerClass aClass = ordered[i];

                var column = new ColumnDefinition { Width = new GridLength(Math.Max(1, aClass.SeatingCapacity), GridUnitType.Star), MinWidth = 24 };
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

                var block = new Border { Background = CabinBrush(aClass.Type), Child = label, BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
                Grid.SetColumn(block, i * 2);
                cabinStrip.Children.Add(block);

                if (i < ordered.Count - 1)
                {
                    cabinStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                }
            }

            Action refreshLabels = () =>
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    labels[i].Text = string.Format("{0} {1}", ordered[i].Type.ToString().Replace("Class", ""), ordered[i].SeatingCapacity);
                    columns[i].Width = new GridLength(Math.Max(1, ordered[i].SeatingCapacity), GridUnitType.Star);
                }
            };

            refreshLabels();

            for (int i = 0; i < ordered.Count - 1; i++)
            {
                AirlinerClass left = ordered[i];
                AirlinerClass right = ordered[i + 1];

                var thumb = new Thumb { Cursor = System.Windows.Input.Cursors.SizeWE, Background = Brushes.White, ToolTip = "Drag to move seats" };
                Grid.SetColumn(thumb, i * 2 + 1);
                cabinStrip.Children.Add(thumb);

                double carried = 0;
                thumb.DragDelta += (o, args) =>
                {
                    double total = ordered.Sum(c => c.SeatingCapacity);
                    double usable = Math.Max(1, cabinStrip.ActualWidth - 8 * (ordered.Count - 1));

                    carried += args.HorizontalChange * total / usable;
                    int move = (int)Math.Truncate(carried);
                    if (move == 0)
                    {
                        return;
                    }

                    // keep at least one seat in each class
                    move = Math.Max(-(left.SeatingCapacity - 1), Math.Min(right.SeatingCapacity - 1, move));
                    carried -= move;

                    left.SeatingCapacity += move;
                    left.RegularSeatingCapacity += move;
                    right.SeatingCapacity -= move;
                    right.RegularSeatingCapacity -= move;

                    refreshLabels();
                    lbClasses.Items.Refresh();
                };
            }
        }

        private void ButtonAdd_Click(object sender, RoutedEventArgs e)
        {
            int seating = (int)((Button)sender).Tag;

              AirlinerClass aClass = new AirlinerClass(this.CurrentClass, seating);

            aClass.ForceSetFacility(this.Classes[0].GetFacility(AirlinerFacility.FacilityType.Audio));
            aClass.ForceSetFacility(this.Classes[0].GetFacility(AirlinerFacility.FacilityType.Seat));
            aClass.ForceSetFacility(this.Classes[0].GetFacility(AirlinerFacility.FacilityType.Video));
            
            this.Classes.Add(aClass);

            // chs, 2011-11-10 added so seat capacity is correctly calculated
            this.Classes[0].SeatingCapacity -= aClass.SeatingCapacity;
            this.Classes[0].RegularSeatingCapacity -= aClass.SeatingCapacity;

            showAirlinerClasses();
        }

        private void ButtonDelete_Click(object sender, RoutedEventArgs e)
        {
            AirlinerClass aClass = this.Classes[this.Classes.Count - 1];
            this.Classes.Remove(aClass);

            // chs, 2011-11-10 added so seat capacity is correctly calculated
            this.Classes[0].SeatingCapacity += aClass.RegularSeatingCapacity;
            this.Classes[0].RegularSeatingCapacity += aClass.RegularSeatingCapacity;

            showAirlinerClasses();
        }
        private void cbClasses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (((ComboBox)sender).SelectedItem!=null)
                this.CurrentClass = (AirlinerClass.ClassType)((ComboBox)sender).SelectedItem;
        }
        //the class for an airliner class item
        private class AirlinerClassItem
        {
            public AirlinerClass AirlinerClass { get; set; }
            public Boolean CanDelete { get; set; }
            public AirlinerClassItem(AirlinerClass aClass, Boolean canDelete)
            {
                this.AirlinerClass = aClass;
                this.CanDelete = canDelete;
            }
        }

       

    }
    //the converter for the "free" classes
    public class FreeClassesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ObservableCollection<AirlinerClass.ClassType> list = new ObservableCollection<AirlinerClass.ClassType>();
            foreach (AirlinerClass.ClassType type in PopUpAirlinerConfiguration.FreeClasses)
                 list.Add(type);
                  
            return list;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    //the converter for returning the amount of passengers
    public class NumberOfPassengersConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ObservableCollection<int> list = new ObservableCollection<int>();
             for (int i = 1; i < PopUpAirlinerConfiguration.MaxSeats; i++)
                list.Add(i + 1);
           return list;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
   

}
    
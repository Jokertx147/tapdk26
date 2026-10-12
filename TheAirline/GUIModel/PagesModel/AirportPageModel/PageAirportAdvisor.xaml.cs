using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using TheAirline.GUIModel.CustomControlsModel.PopUpWindowsModel;
using TheAirline.Helpers;

namespace TheAirline.GUIModel.PagesModel.AirportPageModel
{
    /// <summary>
    ///     Recommends the most lucrative new routes from an airport.
    /// </summary>
    public partial class PageAirportAdvisor : Page
    {
        public PageAirportAdvisor(AirportMVVM airport)
        {
            Airport = airport;

            int rank = 1;
            Advice = RouteAdvisor.GetAdvice(airport.Airport)
                                 .Select(a => new RankedAdvice { Rank = rank++, Item = a })
                                 .ToList();

            Heading = "Route advisory for " + airport.Airport.Profile.Name;
            NoResults = Advice.Count == 0;

            InitializeComponent();
        }

        private void lnkCreateRoute_Click(object sender, RoutedEventArgs e)
        {
            var advice = (RouteAdvice) ((Hyperlink) sender).Tag;

            if (PopUpAdvisedRoute.ShowPopUp(Airport.Airport, advice.Destination) && NavigationService != null)
            {
                NavigationService.Navigate(new PageAirportAdvisor(Airport) { Tag = Tag });
            }
        }

        public AirportMVVM Airport { get; set; }

        public List<RankedAdvice> Advice { get; set; }

        public string Heading { get; set; }

        public bool NoResults { get; set; }
    }

    public class RankedAdvice
    {
        public int Rank { get; set; }

        public RouteAdvice Item { get; set; }
    }
}

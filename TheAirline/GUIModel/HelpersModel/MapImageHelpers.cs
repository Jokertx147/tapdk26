using System;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheAirline.Helpers;
using TheAirline.Infrastructure;

namespace TheAirline.GUIModel.HelpersModel
{
    //builds the big world map image from the map tiles; the game logic asks for it through UserInterface.PrepareMaps
    public static class MapImageHelpers
    {
        public static void CreateBigImageCanvas()
        {
            const int zoom = 3;
            const int imageSize = 256;

            var panelMap = new Canvas();

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    string name = $@"{zoom}\{x}\{y}.png";

                    var imgMap = new Image
                        {
                            Width = imageSize,
                            Height = imageSize,
                            Source = new BitmapImage(
                                new Uri(AppSettings.GetDataPath() + "\\graphics\\maps\\" + name, UriKind.RelativeOrAbsolute))
                        };
                    RenderOptions.SetBitmapScalingMode(imgMap, BitmapScalingMode.HighQuality);

                    Canvas.SetTop(imgMap, y*imageSize);
                    Canvas.SetLeft(imgMap, x*imageSize);

                    panelMap.Children.Add(imgMap);
                }
            }

            GeneralHelpers.BigMapXaml = XamlWriter.Save(panelMap);
        }
    }
}

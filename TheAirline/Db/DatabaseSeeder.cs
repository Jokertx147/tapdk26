using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;

namespace TheAirline.Db
{
    /// <summary>
    ///     Fills a new, empty database with the lookup data the old SQL Server Compact file shipped with.
    /// </summary>
    public static class DatabaseSeeder
    {
        public static void Seed(AirlineContext context)
        {
            var changed = false;

            if (!context.Continents.Any())
            {
                // one shared Continent instance per name, in the order the seed file lists them
                var continents = new[]
                {
                    Migrations.Seeds.Continents.All, Migrations.Seeds.Continents.Asia,
                    Migrations.Seeds.Continents.Africa, Migrations.Seeds.Continents.Australia,
                    Migrations.Seeds.Continents.Europe, Migrations.Seeds.Continents.NorthAmerica,
                    Migrations.Seeds.Continents.SouthAmerica
                }.ToDictionary(c => c.Name);

                context.Continents.AddRange(continents.Values);

                if (!context.Regions.Any())
                {
                    var regions = typeof (Migrations.Seeds.Regions)
                        .GetProperties(BindingFlags.Public | BindingFlags.Static)
                        .Select(p => (Region) p.GetValue(null))
                        .Where(r => r.Name != "All Regions") // the view model adds its own "All Regions" entry
                        .ToList();

                    foreach (var region in regions)
                    {
                        region.Continent = continents[region.Continent.Name];
                    }

                    context.Regions.AddRange(regions);
                }
                changed = true;
            }

            if (!context.Difficulties.Any())
            {
                context.Difficulties.AddRange(new List<Difficulty>
                {
                    Level("Easy", 1.5, 0.75, 1.5, 1, 1.25, 5),
                    Level("Normal", 1, 1, 1.2, 1.1, 1, 2),
                    Level("Hard", 0.5, 1.25, 1, 1.2, 0.75, 1)
                });
                changed = true;
            }

            if (changed)
            {
                context.SaveChanges();
            }
        }

        private static Difficulty Level(string name, double money, double loan, double passengers, double price,
            double ai, double startData)
        {
            return new Difficulty
            {
                Name = name,
                MoneyLevel = money,
                LoanLevel = loan,
                PassengersLevel = passengers,
                PriceLevel = price,
                AiLevel = ai,
                StartDataLevel = startData
            };
        }
    }
}

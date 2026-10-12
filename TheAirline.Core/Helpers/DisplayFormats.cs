using System;
using System.Globalization;
using TheAirline.Infrastructure;
using TheAirline.Infrastructure.Enums;
using TheAirline.Models.Airports;
using TheAirline.Models.General;
using TheAirline.Models.General.Countries;
using Settings = TheAirline.Infrastructure.Settings;

namespace TheAirline.Helpers
{
    // Text and value formatting the game logic needs (money, airport codes, distances, current country).
    // The WPF converters of the same purpose wrap these.

    public class ValueCurrencyFormatter
    {
        // game values are in US dollars, so without a local currency they are shown as dollars whatever the language
        private static readonly CultureInfo BaseCurrencyCulture = CultureInfo.GetCultureInfo("en-US");

        #region Public Methods and Operators

        public object Convert(object value)
        {
            try
            {
                var v = double.Parse(value.ToString());

                if (GameObject.GetInstance().CurrencyCountry == null || double.IsInfinity(v))
                {
                    return string.Format(BaseCurrencyCulture, "{0:C}", value);
                }
                var currency =
                    GameObject.GetInstance().CurrencyCountry.GetCurrency(GameObject.GetInstance().GameTime);

                if (currency == null)
                {
                    if (Settings.GetInstance().CurrencyShorten)
                    {
                        if (v >= 1000000000 || v <= -1000000000)
                        {
                            return string.Format(
                                BaseCurrencyCulture,
                                "{0:C} {1}",
                                v/1000000000,
                                Translator.GetInstance().GetString("General", "2001"));
                        }
                        if (v >= 1000000 || v <= -1000000)
                        {
                            return string.Format(
                                BaseCurrencyCulture,
                                "{0:C} {1}",
                                v/1000000,
                                Translator.GetInstance().GetString("General", "2000"));
                        }
                        return string.Format(BaseCurrencyCulture, "{0:C}", value);
                    }
                    return string.Format(BaseCurrencyCulture, "{0:C}", value);
                }
                var currencyValue = v*currency.Exchange;

                if (Settings.GetInstance().CurrencyShorten)
                {
                    if (currencyValue >= 1000000 || currencyValue <= -1000000)
                    {
                        var sValue = currencyValue/1000000;
                        var sFormat = Translator.GetInstance().GetString("General", "2000");

                        if (currencyValue >= 1000000000 || currencyValue <= -1000000000)
                        {
                            sValue = currencyValue/1000000000;
                            sFormat = Translator.GetInstance().GetString("General", "2001");
                        }

                        if (currency.SymbolOnRight)
                        {
                            return string.Format("{0:#,0.##} {2} {1}", sValue, currency.Symbol, sFormat);
                        }
                        return string.Format("{1}{0:#,0.##} {2}", sValue, currency.Symbol, sFormat);
                    }
                    if (currency.SymbolOnRight)
                    {
                        return string.Format("{0:#,0.##} {1}", currencyValue, currency.Symbol);
                    }
                    return string.Format("{1}{0:#,0.##}", currencyValue, currency.Symbol);
                }
                if (currency.SymbolOnRight)
                {
                    return string.Format("{0:#,0.##} {1}", currencyValue, currency.Symbol);
                }
                return string.Format("{1}{0:#,0.##}", currencyValue, currency.Symbol);
            }
            catch (Exception)
            {
                return string.Format(BaseCurrencyCulture, "{0:C}", value);
            }
        }



        #endregion
    }

    public class CountryCurrentCountryResolver
    {
        #region Public Methods and Operators

        public object Convert(object value)
        {
            if (value is Union)
            {
                return value;
            }

            var country = (Country) value;

            if (country is TerritoryCountry)
            {
                return ((TerritoryCountry) country).MainCountry;
            }
            if (!(country is TemporaryCountry))
            {
                var tempCountry = TemporaryCountries.GetTemporaryCountry(
                    country,
                    GameObject.GetInstance().GameTime);

                if (tempCountry == null)
                {
                    return country;
                }
                if (tempCountry.GetCurrentCountry(GameObject.GetInstance().GameTime, country) == null)
                {
                    return country;
                }
                return tempCountry.GetCurrentCountry(GameObject.GetInstance().GameTime, country);
            }
            return ((TemporaryCountry) country).GetCurrentCountry(GameObject.GetInstance().GameTime, country);
            //return country is TemporaryCountry ? ((TemporaryCountry)country).getCurrentCountry(GameObject.GetInstance().GameTime) : country;
        }



        #endregion
    }

    public class AirportCodeFormatter
    {
        #region Public Methods and Operators

        public object Convert(object value)
        {
            if (value is Airport)
            {
                var airport = (Airport) value;

                if (Settings.GetInstance().AirportCodeDisplay == AirportCode.Iata)
                {
                    return airport.Profile.IATACode;
                }
                return airport.Profile.ICAOCode;
            }
            return "";
        }



        #endregion
    }

    public class SmallDistanceFormatter
    {
        #region Public Methods and Operators

        public object Convert(object value)
        {
            var v = double.Parse(value.ToString());

            if (AppSettings.GetInstance().GetLanguage().Unit == Language.UnitSystem.Metric)
            {
                return v + " m.";
            }
            return string.Format("{0:#,0} feet", MathHelpers.MeterToFeet(v));
        }


        #endregion
    }
}

using System;
using System.Globalization;
using System.Linq;
using TheAirline.Models.Airlines;

namespace TheAirline.Models.General.Countries
{
    //the class for handling the tail numbers for the country
    [Serializable]
    public class CountryTailNumber
    {
        public CountryTailNumber(Country country)
        {
            Country = country;
        }

        public CountryTailNumber()
        {
        }

        public int Id { get; set; }

        public string LastTailNumber { get; set; }

        public Country Country { get; set; }

        //returns if a tail number matches the country
        public bool IsMatch(string tailNumber)
        {
            // US registrations look like N001G4 (no dash), the older data format is N-0001
            if (IsUnitedStates && tailNumber != null && tailNumber.Length >= 2 && tailNumber[0] == 'N'
                && !tailNumber.Contains("-") && tailNumber.Skip(1).All(char.IsLetterOrDigit))
            {
                return true;
            }

            if (tailNumber == null || Country?.TailNumberFormat == null
                || !Country.TailNumberFormat.Contains("-") || !tailNumber.Contains("-"))
            {
                return false;
            }

            var countryID = Country.TailNumberFormat.Split('-')[0];
            var numberFormat = Country.TailNumberFormat.Split('-')[1];

            var length = 0; //Convert.ToInt16(numberFormat.Substring(numberFormat.Length - 1));

            var sIndex = numberFormat.IndexOf('s');
            var dIndex = numberFormat.IndexOf('d');


            if (sIndex != -1)
                length += Convert.ToInt16(numberFormat.Substring(sIndex + 1, 1));
            if (dIndex != -1)
                length += Convert.ToInt16(numberFormat.Substring(dIndex + 1, 1));

            var tailID = tailNumber.Split('-')[0];
            var tailFormat = tailNumber.Split('-')[1];

            return tailID == countryID && tailFormat.Length == length;
        }

        //returns the tail number for digits
        private string getTailNumber(int number, int digits)
        {
            var format = "{0:";
            for (var i = 0; i < digits; i++)
                format += "0";
            format += "}";
            return string.Format(format, number);
        }

        //returns the tail number for strings
        private string getTailNumber(string lastCode, int chars)
        {
            int i;
            if (lastCode == null)
            {
                var code = "";
                for (i = 0; i < chars; i++)
                    code += "A";
                return code;
            }
            i = 0;
            var found = false;
            while (!found && i < chars)
            {
                if (lastCode[lastCode.Length - 1 - i] < 'Z')
                    found = true;
                else
                    i++;
            }

            var replaceChar = lastCode[lastCode.Length - 1 - i];
            replaceChar++;

            if (i == 0)
                return lastCode.Substring(0, chars - i - 1) + replaceChar + lastCode.Substring(chars - i);
            var postfix = "";
            for (var j = 0; j < i; j++)
                postfix += "A";

            return lastCode.Substring(0, chars - i - 1) + replaceChar + postfix;
        }

        private bool IsUnitedStates
        {
            get { return Country?.TailNumberFormat != null && Country.TailNumberFormat.StartsWith("N-"); }
        }

        private static readonly object UsTailLock = new object();
        private static readonly System.Collections.Generic.Dictionary<string, int> UsTailCounters =
            new System.Collections.Generic.Dictionary<string, int>();

        //returns the next tail number for an airline: US airlines get N + a 3 digit number + the airline's two letter code (N001G4, N002G4 ...)
        public string GetNextTailNumber(Airline airline)
        {
            string code = airline?.Profile?.IATACode;

            if (!IsUnitedStates || string.IsNullOrWhiteSpace(code))
            {
                return GetNextTailNumber();
            }

            lock (UsTailLock)
            {
                var pattern = new System.Text.RegularExpressions.Regex("^N(\\d{3,})" + System.Text.RegularExpressions.Regex.Escape(code) + "$");

                int highest;
                UsTailCounters.TryGetValue(code, out highest);

                foreach (string tail in TheAirline.Models.Airliners.Airliners.GetAllAirliners().Select(a => a.TailNumber)
                    .Concat(airline.Fleet.ToList().Select(f => f.Airliner?.TailNumber)))
                {
                    if (tail == null)
                    {
                        continue;
                    }

                    var match = pattern.Match(tail);
                    int number;
                    if (match.Success && int.TryParse(match.Groups[1].Value, out number) && number > highest)
                    {
                        highest = number;
                    }
                }

                highest++;
                UsTailCounters[code] = highest;

                return "N" + highest.ToString("000", CultureInfo.InvariantCulture) + code;
            }
        }

        //returns the next tail number
        public string GetNextTailNumber()
        {
            if (!Country.TailNumberFormat.Contains("-"))
                return "";

            try
            {
                var countryID = Country.TailNumberFormat.Split('-')[0];
                var numberFormat = Country.TailNumberFormat.Split('-')[1];

                int length = Convert.ToInt16(numberFormat.Substring(numberFormat.Length - 1));

                if (numberFormat.Contains("\\s") && numberFormat.Contains("\\d"))
                {
                    int dLenght = Convert.ToInt16(numberFormat.Substring(numberFormat.IndexOf('d') + 1, 1));
                    int sLenght = Convert.ToInt16(numberFormat.Substring(numberFormat.IndexOf('s') + 1, 1));
                    if (LastTailNumber == null)
                    {
                        if (dLenght < sLenght)
                            LastTailNumber = countryID + "-" + getTailNumber(0, dLenght) + getTailNumber(null, sLenght);
                        else
                            LastTailNumber = countryID + "-" + getTailNumber(null, sLenght) + getTailNumber(0, dLenght);
                    }
                    else
                    {
                        var t = LastTailNumber.Split('-')[1].Substring(0, dLenght);
                        var s = LastTailNumber.Split('-')[1].Substring(sLenght, dLenght);
                        var postfix = LastTailNumber.Split('-')[1];
                        var lastCode = dLenght < sLenght
                            ? LastTailNumber.Split('-')[1].Substring(dLenght, sLenght)
                            : LastTailNumber.Split('-')[1].Substring(0, sLenght);

                        int lastNumber = dLenght < sLenght ? Convert.ToInt16(t) : Convert.ToInt16(s);
                        var number = lastNumber + 1;

                        var nLenght = number.ToString(CultureInfo.InvariantCulture).Length;

                        var sNumber = nLenght > dLenght ? getTailNumber(lastCode, sLenght) : lastCode;
                        var dNumber = nLenght > dLenght ? getTailNumber(0, dLenght) : getTailNumber(number, dLenght);
                        var code = getTailNumber(lastCode, sLenght);

                        if (dLenght < sLenght)
                            LastTailNumber = countryID + "-" + dNumber + sNumber;
                        else
                            LastTailNumber = countryID + "-" + sNumber + dNumber;
                    }
                }
                if (numberFormat.Contains("\\d") && !numberFormat.Contains("\\s"))
                {
                    int number;
                    if (LastTailNumber == null)
                        number = 0;
                    else
                        number = Convert.ToInt32(LastTailNumber.Split('-')[1]) + 1;
                    /*
                    string format = countryID + "-{0:";
                    for (int i = 0; i < length; i++)
                        format += "0";
                    format += "}";
                     * */
                    LastTailNumber = countryID + "-" + getTailNumber(number, length);
                }
                if (numberFormat.Contains("\\s") && !numberFormat.Contains("\\d"))
                {
                    LastTailNumber = countryID + "-" +
                                     getTailNumber(
                                         LastTailNumber == null ? LastTailNumber : LastTailNumber.Split('-')[1], length);
                    /*
                    if (LastTailNumber == null)
                    {
                        string code = countryID + "-";
                        for (int i = 0; i < length; i++)
                            code += "A";
                        this.LastTailNumber = code;
                    }
                    else
                    {
                        string lastCode = this.LastTailNumber.Split('-')[1];


                        int i = 0;
                        Boolean found = false;
                        while (!found && i < length)
                        {
                            if (lastCode[lastCode.Length - 1 - i] < 'Z')
                                found = true;
                            else
                                i++;
                        }
                   
              
                        char replaceChar = lastCode[lastCode.Length - 1 - i];
                        replaceChar++;

                        string newCode = lastCode.Substring(0, length - i - 1) + replaceChar + lastCode.Substring(length - i);

                        this.LastTailNumber = countryID + "-" + newCode;
                    }
                     * */
                }
            }
            catch (Exception)
            {
                return "";
            }

            return LastTailNumber;
        }
    }
}
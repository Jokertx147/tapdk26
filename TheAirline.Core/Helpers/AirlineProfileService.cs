using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheAirline.Infrastructure;
using TheAirline.Models.Airlines;
using TheAirline.Models.General;
using TheAirline.Models.Routes;

namespace TheAirline.Helpers
{
    /// <summary>Changes to the player's airline profile: name, CEO, color, story, route focus and logo.</summary>
    public static class AirlineProfileService
    {
        public static string LogoFolder => Path.Combine(AppSettings.GetDataPath(), "graphics", "airlinelogos");

        /// <summary>Where logos the player picks from his own files are copied, so the save keeps working when the file moves.</summary>
        public static string CustomLogoFolder
        {
            get
            {
                string path = Path.Combine(AppSettings.GetCommonApplicationDataPath(), "logos");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp" };

        /// <summary>The logo file shown for the airline now, or null when there is none.</summary>
        public static string GetLogo(Airline airline)
        {
            var p = airline?.Profile;
            if (p == null) return null;
            int year = GameObject.GetInstance()?.GameTime.Year ?? DateTime.Now.Year;
            var logos = p.Logos ?? new List<AirlineLogo>();
            var logo = logos.LastOrDefault(l => l.FromYear <= year && l.ToYear >= year && File.Exists(l.Path))
                       ?? logos.LastOrDefault(l => File.Exists(l.Path));
            if (logo != null) return logo.Path;

            foreach (var name in new[] { p.LogoName, p.IATACode, "default" })
            {
                if (string.IsNullOrEmpty(name)) continue;
                string f = Path.Combine(LogoFolder, name + ".png");
                if (File.Exists(f)) return f;
            }
            return null;
        }

        /// <summary>The logos that come with the game (file names without extension), filtered by text.</summary>
        public static List<string> BuiltInLogos(string filter)
        {
            if (!Directory.Exists(LogoFolder)) return new List<string>();
            string s = (filter ?? "").Trim();
            return Directory.GetFiles(LogoFolder, "*.png")
                            .Select(Path.GetFileNameWithoutExtension)
                            .Where(n => s.Length == 0 || n.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                            .OrderBy(n => n.Length).ThenBy(n => n)
                            .ToList();
        }

        public static string BuiltInLogoPath(string name)
        {
            return Path.Combine(LogoFolder, name + ".png");
        }

        /// <summary>Uses the logo file from now on (all years), replacing the airline's logos.</summary>
        private static void UseLogo(Airline airline, string path)
        {
            airline.Profile.Logos = new List<AirlineLogo> { new AirlineLogo(path) };
        }

        public static bool SetBuiltInLogo(Airline airline, string name, out string message)
        {
            string path = BuiltInLogoPath(name ?? "");
            if (!File.Exists(path))
            {
                message = "That logo was not found.";
                return false;
            }
            UseLogo(airline, path);
            message = "The logo is now " + name + ".";
            return true;
        }

        /// <summary>Copies a picture from the player's files and uses it as the logo.</summary>
        public static bool SetCustomLogo(Airline airline, string file, out string message)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                message = "The file was not found.";
                return false;
            }
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (!ImageExtensions.Contains(ext))
            {
                message = "Pick a PNG, JPG or BMP picture.";
                return false;
            }
            if (new FileInfo(file).Length > 5*1024*1024)
            {
                message = "The picture is larger than 5 MB. Pick a smaller one.";
                return false;
            }
            string code = Regex.Replace(airline.Profile.IATACode ?? "airline", "[^A-Za-z0-9]", "");
            string target = Path.Combine(CustomLogoFolder, code + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext);
            File.Copy(file, target, true);
            UseLogo(airline, target);
            message = "Your own logo is in use. A copy is kept in " + CustomLogoFolder + ".";
            return true;
        }

        /// <summary>Goes back to the logo the airline started with.</summary>
        public static void ResetLogo(Airline airline)
        {
            string name = string.IsNullOrEmpty(airline.Profile.LogoName) ? airline.Profile.IATACode : airline.Profile.LogoName;
            string path = BuiltInLogoPath(name);
            UseLogo(airline, File.Exists(path) ? path : BuiltInLogoPath("default"));
        }

        /// <summary>Accepts #RRGGBB, RRGGBB or a color name such as Red; returns null when the text is not a color.</summary>
        public static string NormalizeColor(string text)
        {
            string s = (text ?? "").Trim();
            if (s.Length == 0) return null;
            if (Regex.IsMatch(s, "^#?[0-9A-Fa-f]{6}$")) return "#" + s.TrimStart('#').ToUpperInvariant();
            if (Regex.IsMatch(s, "^[A-Za-z]+$")) return char.ToUpperInvariant(s[0]) + s.Substring(1);
            return null;
        }

        public static readonly List<string> ColorChoices = new List<string>
        {
            "#C62828", "#E53935", "#AD1457", "#6A1B9A", "#283593", "#1565C0", "#0277BD", "#00838F",
            "#00695C", "#2E7D32", "#558B2F", "#F9A825", "#EF6C00", "#D84315", "#4E342E", "#37474F", "#000000", "#FFFFFF"
        };

        /// <summary>Saves the editable fields. Returns false with a message when something is not valid.</summary>
        public static bool Save(Airline airline, string name, string ceo, string color, string narrative, Route.RouteType focus, out string message)
        {
            name = (name ?? "").Trim();
            if (name.Length < 2)
            {
                message = "The name needs at least 2 letters.";
                return false;
            }
            if (name.Length > 60)
            {
                message = "The name can be at most 60 letters.";
                return false;
            }
            if (Airlines.GetAllAirlines().Any(a => a != airline && string.Equals(a.Profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                message = "Another airline is already called " + name + ".";
                return false;
            }
            string c = NormalizeColor(color);
            if (!string.IsNullOrWhiteSpace(color) && c == null)
            {
                message = "The color should look like #1565C0 or a name such as Blue.";
                return false;
            }

            // the kind (passenger or cargo) only changes while the airline has no routes and no aircraft
            string kindNote = AirlineTypeRules.KindChangeBlocker(airline, focus);
            bool newKind = kindNote == null && AirlineTypeRules.IsCargoAirline(airline) != (focus == Route.RouteType.Cargo);

            var p = airline.Profile;
            var changes = new List<string>();
            if (p.Name != name) changes.Add("name");
            if ((p.CEO ?? "") != (ceo ?? "").Trim()) changes.Add("CEO");
            if (c != null && p.Color != c) changes.Add("color");
            if ((p.Narrative ?? "") != (narrative ?? "").Trim()) changes.Add("story");
            if (newKind) changes.Add(focus == Route.RouteType.Cargo ? "now a cargo airline" : "now a passenger airline");

            p.Name = name;
            p.CEO = (ceo ?? "").Trim();
            if (c != null) p.Color = c;
            p.Narrative = (narrative ?? "").Trim();
            if (newKind) airline.AirlineRouteFocus = focus;

            message = changes.Count == 0 ? "Nothing changed." : "Saved: " + string.Join(", ", changes) + ".";
            if (kindNote != null) message += " " + kindNote;
            return true;
        }
    }
}

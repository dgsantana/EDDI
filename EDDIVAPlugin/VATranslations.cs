﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EDDIVAPlugin
{
    /// <summary>Translations for VoiceAttack</summary>
    public class VATranslations
    {
        /// <summary>Fix up ship models</summary>
        public static String ShipModel(string model)
        {
            switch(model)
            {
                case "Cobra Mk. III":
                    return "Cobra Mark 3";
                case "Cobra Mk. IV":
                    return "Cobra Mark 4";
                case "Viper Mk. III":
                    return "Viper Mark 3";
                case "Viper Mk. IV":
                    return "Viper Mark 4";
                default:
                    return model;
            }
        }

        /// <summary>Fix up power names</summary>
        public static string Power(string power)
        {
            switch (power)
            {
                case "Aisling Duval":
                    return "Ashling Du-val";
                case "Arissa Lavigny-Duval":
                    return "Arissa Lavigny Du-val";
                case "Denton Patreus":
                    return "Denton Patreyus";
                case "Edmund Mahon":
                    return "Edmund Mahonn";
                case "Zemina Torval":
                    return "Zemeena Torvalll";
                default:
                    return power;


            }
        }

        private static Regex DIGITS = new Regex(@"\d{3,}");
        private static Regex DECIMAL_DIGITS = new Regex(@"( point )(\d{2,})");
        // Regular expression to locate generated star systems
        private static Regex SECTOR = new Regex("(.*) ([A-Za-z][A-Za-z]-[A-Za-z] .*)");
        /// <summary>Fix up star system names</summary>
        public static string StarSystem(string starSystem)
        {
            // Common star catalogues
            if (starSystem.StartsWith("HIP "))
            {
                starSystem = starSystem.Replace("HIP ", "Hip ");
            }
            if (starSystem.StartsWith("L ")
                || starSystem.StartsWith("LFT ")
                || starSystem.StartsWith("LHS ")
                || starSystem.StartsWith("LTT ")
                || starSystem.StartsWith("NLTT ")
                || starSystem.StartsWith("LPM ")
                || starSystem.StartsWith("PPM ")
                || starSystem.StartsWith("ADS ")
                || starSystem.StartsWith("HR ")
                || starSystem.StartsWith("HD ")
                )
            {
                starSystem = starSystem.Replace("-", "dash ");
            }
            if (starSystem.StartsWith("Gliese "))
            {
                starSystem = starSystem.Replace(".", " point ");
            }

            // Generated star systems
            if (SECTOR.IsMatch(starSystem))
            {
                // Need to handle the pieces before and after the sector marker separately
                Match Match = SECTOR.Match(starSystem);

                // Fix common names
                string firstPiece = Match.Groups[1].Value
                    .Replace("Col ", "Coll ")
                    .Replace("R CrA ", "R CRA ")
                    .Replace("Tr ", "TR ")
                    .Replace("Skull and Crossbones Neb. ", "Skull and Crossbones ")
                    .Replace("(", "").Replace(")", "");

                string secondPiece = Match.Groups[2].Value.Replace("-", " dash ");

                starSystem = firstPiece + " " + secondPiece;
            }

            // Star systems with +/- (and sometimes .)
            if (starSystem.StartsWith("2MASS ")
                || starSystem.StartsWith("AC ")
                || starSystem.StartsWith("AG") // Note no space
                || starSystem.StartsWith("BD")
                || starSystem.StartsWith("CFBDSIR ")
                || starSystem.StartsWith("CXOONC ")
                || starSystem.StartsWith("CXOU ")
                || starSystem.StartsWith("CSI") // Note no space
                || starSystem.StartsWith("Csi") // Note no space
                || starSystem.StartsWith("IDS ")
                || starSystem.StartsWith("LF ")
                || starSystem.StartsWith("MJD95 ")
                || starSystem.StartsWith("SDSS ")
                || starSystem.StartsWith("UGCS ")
                || starSystem.StartsWith("WISE ")
                || starSystem.StartsWith("XTE ")
                )
            {
                starSystem = starSystem.Replace("Csi ", "CSI ")
                                       .Replace("WISE ", "Wise ")
                                       .Replace("2MASS ", "2 mass ")
                                       .Replace("+", " plus ")
                                       .Replace("-", " minus ")
                                       .Replace(".", " point ");
            }

            // Fix up digit strings.  
            // Any digits after a decimal point are broken in to individual digits
            starSystem = DECIMAL_DIGITS.Replace(starSystem, match => match.Groups[1].Value + string.Join<char>(" ", match.Groups[2].Value));
            // Any string of more than two digits is broken up in to individual digits
            return DIGITS.Replace(starSystem, match => string.Join<char>(" ", match.Value));
        }
    }
}
using EDDI;
using EddiDataDefinitions;
using EddiEvents;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using EDDI.Core;
using Utilities;

namespace EddiJournalMonitor
{
    public partial class JournalMonitor : LogMonitor, IEDDIMonitor
    {
        private static readonly Regex JsonRegex = new Regex(@"^{.*}$");

        public JournalMonitor() : base(GetSavedGamesDir(), @"^Journal.*\.[0-9\.]+\.log$", result =>
        ForwardJournalEntry(result, EDDI.Core.Eddi.Instance.EventHandler)) { }

        public static void ForwardJournalEntry(string line, Action<Event> callback)
        {
            var events = ParseJournalEntry(line);
            if (events == null)
            {
                Logging.Warn("No events found....");
                return;
            }
            foreach (var loggedEvent in events)
            {
                callback(loggedEvent);
            }
        }

        private static string NpcSpeechBy(string from, string message)
        {
            string by;
            if (message.StartsWith("$AmbushedPilot_"))
            {
                by = "Ambushed pilot";
            }
            else if (message.StartsWith("$BountyHunter"))
            {
                by = "Bounty hunter";
            }
            else if (message.StartsWith("$CapShip") || message.StartsWith("$FEDCapShip"))
            {
                by = "Capital ship";
            }
            else if (message.StartsWith("$CargoHunter"))
            {
                by = "Cargo hunter"; // Mission specific
            }
            else if (message.StartsWith("$Commuter"))
            {
                by = "Civilian pilot";
            }
            else if (message.StartsWith("$ConvoyExplorers"))
            {
                by = "Exploration convoy";
            }
            else if (message.StartsWith("$ConvoyWedding"))
            {
                by = "Wedding convoy";
            }
            else if (message.StartsWith("$CruiseLiner"))
            {
                by = "Cruise liner";
            }
            else if (message.StartsWith("$Escort"))
            {
                by = "Escort";
            }
            else if (message.StartsWith("$Hitman"))
            {
                by = "Hitman";
            }
            else if (message.StartsWith("$Messenger"))
            {
                by = "Messenger";
            }
            else if (message.StartsWith("$Military"))
            {
                by = "Military";
            }
            else if (message.StartsWith("$Miner"))
            {
                by = "Miner";
            }
            else if (message.StartsWith("$PassengerHunter"))
            {
                by = "Passenger hunter"; // Mission specific
            }
            else if (message.StartsWith("$PassengerLiner"))
            {
                by = "Passenger liner";
            }
            else if (message.StartsWith("$Pirate"))
            {
                by = "Pirate";
            }
            else if (message.StartsWith("$Police"))
            {
                // Police messages appear to be re-used by bounty hunters.  Check from to see if it really is police
                by = from.Contains("Police") ? "Police" : "Bounty hunter";
            }
            else if (message.StartsWith("$PowersAssassin"))
            {
                by = "Rival power's agent";  // Power play specific
            }
            else if (message.StartsWith("$PowersPirate"))
            {
                by = "Rival power's agent"; // Power play specific
            }
            else if (message.StartsWith("$PowersSecurity"))
            {
                by = "Rival power's agent"; // Power play specific
            }
            else if (message.StartsWith("$Propagandist"))
            {
                by = "Propagandist";
            }
            else if (message.StartsWith("$Protester"))
            {
                by = "Protester";
            }
            else if (message.StartsWith("$Refugee"))
            {
                by = "Refugee";
            }
            else if (message.StartsWith("$Smuggler"))
            {
                by = "Civilian pilot";  // We shouldn't recognize a smuggler without a cargo scan
            }
            else if (message.StartsWith("$StarshipOne"))
            {
                by = "Starship One";
            }
            else if (message.Contains("_SearchandRescue_"))
            {
                by = "Search and rescue";
            }
            else
            {
                by = "NPC";
            }
            return by;
        }

        // Be sensible with health - round it unless it's very low
        private static decimal SensibleHealth(decimal health)
        {
            return (health < 10 ? Math.Round(health, 1) : Math.Round(health));
        }

        public string MonitorName => "Journal monitor";

        public string MonitorVersion => "1.0.0";

        public string MonitorDescription =>
            "Monitor Elite: Dangerous' journal.log for many common events.  This should not be disabled unless you are sure you know what you are doing, as it will result in many functions inside EDDI no longer working";

        public bool IsRequired => true;

        public bool NeedsStart => true;

        public void Reload() { }

        public UserControl ConfigurationTabItem()
        {
            return null;
        }

        private static string GetSavedGamesDir()
        {
            int result = NativeMethods.SHGetKnownFolderPath(new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4"), 0, new IntPtr(0), out var path);
            if (result >= 0)
            {
                return Marshal.PtrToStringUni(path) + @"\Frontier Developments\Elite Dangerous";
            }

            throw new ExternalException("Failed to find the saved games directory.", result);
        }

        internal class NativeMethods
        {
            [DllImport("Shell32.dll")]
            internal static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)]Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
        }

        // Helpers for parsing json
        private static decimal GetDecimal(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            return GetDecimal(key, val);
        }

        private static decimal GetDecimal(string key, object val)
        {
            switch (val)
            {
                case null:
                    throw new ArgumentNullException("Expected value for " + key + " not present");
                case long l:
                    return l;
                case double d:
                    return (decimal)d;
            }

            throw new ArgumentException("Unparseable value for " + key);
        }

        private static decimal? GetOptionalDecimal(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            return GetOptionalDecimal(key, val);
        }

        private static decimal? GetOptionalDecimal(string key, object val)
        {
            switch (val)
            {
                case null:
                    return null;
                case long _:
                    return (long?)val;
                case double _:
                    return (decimal?)(double?)val;
            }

            throw new ArgumentException("Unparseable value for " + key);
        }

        private static int GetInt(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            return GetInt(key, val);
        }

        private static int GetInt(string key, object val)
        {
            switch (val)
            {
                case long _:
                    return (int)(long)val;
                case int _:
                    return (int)val;
            }

            throw new ArgumentException("Unparseable value for " + key);
        }

        private static int? GetOptionalInt(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            return GetOptionalInt(key, val);
        }

        private static int? GetOptionalInt(string key, object val)
        {
            switch (val)
            {
                case null:
                    return null;
                case long _:
                    return (int?)(long?)val;
                case int _:
                    return (int?)val;
            }

            throw new ArgumentException("Unparseable value for " + key);
        }

        private static long GetLong(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            return GetLong(key, val);
        }

        private static long GetLong(string key, object val)
        {
            if (val is long l)
            {
                return l;
            }
            throw new ArgumentException("Unparseable value for " + key);
        }

        private static long? GetOptionalLong(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            switch (val)
            {
                case null:
                    return null;
                case long _:
                    return (long?)val;
            }

            throw new ArgumentException($"Expected value of type long for key {key}, instead got value of type {data.GetType().FullName}");
        }

        private static bool GetBool(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            return GetBool(key, val);
        }

        private static bool GetBool(string key, object val)
        {
            if (val == null)
                throw new ArgumentNullException("Expected value for " + key + " not present");
            return (bool)val;
        }

        private static bool? GetOptionalBool(IDictionary<string, object> data, string key)
        {
            if (data.TryGetValue(key, out var val))
                return val as bool?;

            return null;
        }

        private static string GetString(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            return val as string;
        }

        private static Superpower GetAllegiance(IDictionary<string, object> data, string key)
        {
            data.TryGetValue(key, out var val);
            // FD sends "" rather than null; fix that here
            if (((string)val) == "") { val = null; }
            return Superpower.From((string)val);
        }

        private static string GetFaction(IDictionary<string, object> data, string key)
        {
            string faction = GetString(data, key);
            // Might be a superpower...
            Superpower superpowerFaction = Superpower.From(faction);
            return superpowerFaction != null ? superpowerFaction.name : faction;
        }

        public void PreHandle(Event ev)
        {
        }

        public void PostHandle(Event ev)
        {
        }

        public void HandleProfile(JObject profile)
        {
        }

        public IDictionary<string, object> GetVariables()
        {
            return null;
        }

        private static string GetRole(IDictionary<string, object> data, string key)
        {
            var role = GetString(data, key);
            if (role == "FireCon")
                role = "Gunner";
            else if (role == "FighterCon")
                role = "Fighter";

            return role;
        }
    }
}

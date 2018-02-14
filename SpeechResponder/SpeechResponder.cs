using System.Collections.Generic;
using Cottle.Values;
using EddiSpeechService;
using Utilities;
using Newtonsoft.Json;
using EddiEvents;
using EDDI;
using System.Windows.Controls;
using System;
using System.Text.RegularExpressions;
using System.IO;
using EddiDataDefinitions;
using EddiShipMonitor;
using EDDI.Core;

namespace EddiSpeechResponder
{
    /// <summary>
    /// A responder that responds to events with a speech
    /// </summary>
    public class SpeechResponder : IEDDIResponder
    {
        // The file to log speech
        public static readonly string LogFile = Constants.DATA_DIR + @"\speechresponder.out";

        private ScriptResolver _scriptResolver;

        private bool _subtitles;

        private bool _subtitlesOnly;

        private int _beaconScanCount = 0;

        public string ResponderName => "Speech responder";

        public string ResponderVersion => "1.0.0";

        public string ResponderDescription => "Respond to events with scripted speech based on the information in the event. Not all events have scripted responses. If a script response is empty, its 'Test' and 'View' buttons are disabled.";

        public SpeechResponder()
        {
            var configuration = SpeechResponderConfiguration.FromFile();
            Personality personality = null;
            if (configuration?.Personality != null)
            {
                personality = Personality.FromName(configuration.Personality);
            }
            if (personality == null)
            { 
                personality = Personality.Default();
            }
            _scriptResolver = new ScriptResolver(personality.Scripts);
            _subtitles = configuration.Subtitles;
            _subtitlesOnly = configuration.SubtitlesOnly;
            Logging.Info("Initialised " + ResponderName + " " + ResponderVersion);
        }

        /// <summary>
        /// Change the personality for the speech responder
        /// </summary>
        /// <returns>true if the speech responder is now using the new personality, otherwise false</returns>
        public bool SetPersonality(string newPersonality)
        {
            SpeechResponderConfiguration configuration = SpeechResponderConfiguration.FromFile();
            if (newPersonality == configuration.Personality)
            {
                // Already set to this personality
                return true;
            }

            // Ensure that this personality exists
            Personality personality = Personality.FromName(newPersonality);
            if (personality != null)
            {
                // Yes it does; use it
                configuration.Personality = newPersonality;
                configuration.ToFile();
                _scriptResolver = new ScriptResolver(personality.Scripts);
                Logging.Debug("Changed personality to " + newPersonality);
                return true;
            }

            // No it does not; ignore it
            return false;
        }

        public bool Start()
        {
            return true;
        }

        public void Stop()
        {
        }

        public void Reload()
        {
            var configuration = SpeechResponderConfiguration.FromFile();
            var personality = Personality.FromName(configuration.Personality);
            if (personality == null)
            {
                Logging.Warn("Failed to find named personality; falling back to default");
                personality = Personality.Default();
                configuration.Personality = personality.Name;
                configuration.ToFile();
            }
            _scriptResolver = new ScriptResolver(personality.Scripts);
            _subtitles = configuration.Subtitles;
            _subtitlesOnly = configuration.SubtitlesOnly;
            Logging.Debug($"Reloaded {ResponderName} {ResponderVersion}");
        }

        public void Handle(Event theEvent)
        {
            Logging.Debug("Received event " + JsonConvert.SerializeObject(theEvent));

            // By default we say things unless we've been told not to
            var sayOutLoud = true;
            if (Eddi.Instance.State.TryGetValue("speechresponder_quiet", out var tmp))
            {
                if (tmp is bool b)
                {
                    sayOutLoud = !b;
                }
            }

            switch (theEvent)
            {
                case NavBeaconScanEvent @event:
                    _beaconScanCount = @event.numbodies;
                    Logging.Debug($"beaconScanCount = {_beaconScanCount}");
                    break;
                case StarScannedEvent _:
                case BodyScannedEvent _:
                case BeltScannedEvent _:
                    if (_beaconScanCount > 0)
                    {
                        _beaconScanCount--;
                        Logging.Debug($"beaconScanCount = {_beaconScanCount}");
                        return;
                    }
                    if (theEvent is BeltScannedEvent)
                    {
                        // We ignore belt clusters
                        return;
                    }

                    break;
                case CommunityGoalEvent _:
                    // Disable speech from the community goal event for the time being.
                    return;
            }


            Say(_scriptResolver, ((ShipMonitor)Eddi.Instance.ObtainMonitor("Ship monitor")).GetCurrentShip(), theEvent.type, theEvent, null, null, null, sayOutLoud);
        }

        // Say something with the default resolver
        public void Say(Ship ship, string scriptName, Event theEvent = null, int? priority = null, string voice = null, bool? wait = null, bool sayOutLoud = true)
        {
            Say(_scriptResolver, ship, scriptName, theEvent, priority, voice, null, sayOutLoud);
        }

        // Say something with a custom resolver
        public void Say(ScriptResolver resolver, Ship ship, string scriptName, Event theEvent = null, int? priority = null, string voice = null, bool? wait = null, bool sayOutLoud = true)
        {
            var dict = CreateVariables(theEvent);
            var speech = resolver.Resolve(scriptName, dict);
            if (speech == null) return;
            if (_subtitles)
            {
                // Log a tidied version of the speech
                Log(Regex.Replace(speech, "<.*?>", string.Empty));
            }
            if (sayOutLoud && !(_subtitles && _subtitlesOnly))
            {
                SpeechService.Instance.Say(ship, speech, wait ?? true, priority ?? resolver.Priority(scriptName), voice);
            }
        }

        // Create Cottle variables from the EDDI information
        private static Dictionary<string, Cottle.Value> CreateVariables(Event theEvent = null)
        {
            var dict =
                new Dictionary<string, Cottle.Value>
                {
                    ["vehicle"] = Eddi.Instance.Vehicle,
                    ["environment"] = Eddi.Instance.Environment
                };


            if (Eddi.Instance.Cmdr != null)
                dict["cmdr"] = new ReflectionValue(Eddi.Instance.Cmdr);

            if (Eddi.Instance.HomeStarSystem != null)
                dict["homesystem"] = new ReflectionValue(Eddi.Instance.HomeStarSystem);

            if (Eddi.Instance.HomeStation != null)
                dict["homestation"] = new ReflectionValue(Eddi.Instance.HomeStation);

            if (Eddi.Instance.CurrentStarSystem != null)
                dict["system"] = new ReflectionValue(Eddi.Instance.CurrentStarSystem);

            if (Eddi.Instance.LastStarSystem != null)
                dict["lastsystem"] = new ReflectionValue(Eddi.Instance.LastStarSystem);

            if (Eddi.Instance.CurrentStation != null)
                dict["station"] = new ReflectionValue(Eddi.Instance.CurrentStation);

            if (theEvent != null)
                dict["event"] = new ReflectionValue(theEvent);

            if (Eddi.Instance.State != null)
            {
                dict["state"] = ScriptResolver.BuildState();
                Logging.Debug("State is " + JsonConvert.SerializeObject(Eddi.Instance.State));
            }

            // Obtain additional variables from each monitor
            foreach (var monitor in Eddi.Instance.Monitors)
            {
                var monitorVariables = monitor.GetVariables();
                if (monitorVariables == null) continue;

                foreach (var key in monitorVariables.Keys)
                {
                    if (monitorVariables[key] == null)
                        dict.Remove(key);
                    else
                        dict[key] = new ReflectionValue(monitorVariables[key]);
                }
            }

            return dict;
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }

        private static readonly object LogLock = new object();
        private static void Log(string speech)
        {
            lock (LogLock)
            {
                try
                {
                    using (var file = new StreamWriter(LogFile, true))
                    {
                        file.WriteLine(speech);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to write speech", ex);
                }
            }
        }
    }
}

using Cottle.Builtins;
using Cottle.Documents;
using Cottle.Functions;
using Cottle.Settings;
using Cottle.Stores;
using Cottle.Values;
using EDDI;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiShipMonitor;
using EddiSpeechService;
using GalnetMonitor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Utilities;

namespace EddiSpeechResponder
{
    public class ScriptResolver
    {
        private readonly Dictionary<string, Script> _scripts = new Dictionary<string, Script>();
        private readonly Random _random;
        private readonly CustomSetting _setting;

        public static object Instance { get; set; }

        public ScriptResolver(Dictionary<string, Script> scripts)
        {
            _random = new Random();
            if (scripts != null) { _scripts = scripts; }

            _setting = new CustomSetting {Trimmer = BuiltinTrimmers.CollapseBlankCharacters};
        }

        public string Resolve(string name, Dictionary<string, Cottle.Value> vars, bool master = true)
        {
            return Resolve(name, BuildStore(vars), master);
        }

        public int Priority(string name)
        {
            _scripts.TryGetValue(name, out var script);
            return script?.Priority ?? 5;
        }

        public string Resolve(string name, BuiltinStore store, bool master = true)
        {
            Logging.Debug("Resolving script " + name);
            _scripts.TryGetValue(name, out var script);
            if (script?.Value == null)
            {
                Logging.Debug("No script");
                return null;
            }
            Logging.Debug("Found script");
            if (script.Enabled == false)
            {
                Logging.Debug("Script disabled");
                return null;
            }

            return ResolveScript(script.Value, store, master);
        }

        /// <summary>
        /// Resolve a script with an existing store
        /// </summary>
        public string ResolveScript(string script, BuiltinStore store, bool master = true)
        {
            try
            {
                // Before we start, we remove the context for master scripts.
                // This means that scripts without context still work as expected
                if (master)
                {
                    EDDI.Core.Eddi.Instance.State["eddi_context_last_subject"] = null;
                    EDDI.Core.Eddi.Instance.State["eddi_context_last_action"] = null;
                }

                var document = new SimpleDocument(script, _setting);
                var result = document.Render(store);
                // Tidy up the output script
                result = Regex.Replace(result, " +", " ").Replace(" ,", ",").Replace(" .", ".").Trim();
                Logging.Debug("Turned script " + script + " in to speech " + result);
                result = result.Trim() == "" ? null : result.Trim();

                if (master && result != null)
                {
                    string stored = result;
                    // Remove any leading pause
                    if (stored.StartsWith("<break"))
                    {
                        string pattern = "^<break[^>]*>";
                        string replacement = "";
                        Regex rgx = new Regex(pattern);
                        stored = rgx.Replace(stored, replacement);
                    }

                    EDDI.Core.Eddi.Instance.State["eddi_context_last_speech"] = stored;
                    if (EDDI.Core.Eddi.Instance.State.TryGetValue("eddi_context_last_subject", out var lastSubject))
                    {
                        if (lastSubject != null)
                        {
                            string csLastSubject = ((string)lastSubject).ToLowerInvariant().Replace(" ", "_");
                            EDDI.Core.Eddi.Instance.State["eddi_context_last_speech_" + csLastSubject] = stored;
                        }
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                Logging.Warn("Failed to resolve script: " + e.ToString());
                return "There is a problem with the script: " + e.Message.Replace("'", "");
            }
        }

        /// <summary>
        /// Build a store from a list of variables
        /// </summary>
        private BuiltinStore BuildStore(Dictionary<string, Cottle.Value> vars)
        {
            BuiltinStore store = new BuiltinStore();

            // TODO fetch this from configuration
            bool useIcao = SpeechServiceConfiguration.FromFile().EnableIcao;

            // Function to call another script
            store["F"] = new NativeFunction((values) => new ScriptResolver(_scripts).Resolve(values[0].AsString, store, false), 1);

            // Translation functions
            store["P"] = new NativeFunction((values) =>
            {
                string val = values[0].AsString;
                string translation = val;
                if (translation == val)
                {
                    translation = Translations.Body(val, useIcao);
                }
                if (translation == val)
                {
                    translation = Translations.StarSystem(val, useIcao);
                }
                if (translation == val)
                {
                    translation = Translations.Faction(val);
                }
                if (translation == val)
                {
                    translation = Translations.Power(val);
                }
                if (translation == val)
                {
                    Ship ship = ShipDefinitions.FromModel(val);
                    if (ship != null && ship.EDID > 0)
                    {
                        translation = ship.SpokenModel();
                    }
                }
                if (translation == val)
                {
                    Ship ship = ShipDefinitions.FromEDModel(val);
                    if (ship != null && ship.EDID > 0)
                    {
                        translation = ship.SpokenModel();
                    }
                }
                return translation;
            }, 1);

            // Boolean constants
            store["true"] = true;
            store["false"] = false;

            // Helper functions
            store["OneOf"] = new NativeFunction((values) => new ScriptResolver(_scripts).ResolveScript(values[_random.Next(values.Count)].AsString, store, false));

            store["Occasionally"] = new NativeFunction((values) =>
            {
                if (_random.Next((int)values[0].AsNumber) == 0)
                {
                    return new ScriptResolver(_scripts).ResolveScript(values[1].AsString, store, false);
                }

                return "";
            }, 2);

            store["Humanise"] = new NativeFunction((values) => Translations.Humanize(values[0].AsNumber), 1);

            store["List"] = new NativeFunction((values) =>
            {
                string output = String.Empty;
                const string localisedAnd = "and";
                if (values.Count == 1)
                {
                    foreach (KeyValuePair<Cottle.Value, Cottle.Value> value in values[0].Fields)
                    {
                        string valueString = value.Value.ToString();
                        if (value.Key == 0)
                        {
                            output = valueString;
                        }
                        else if (value.Key < (values[0].Fields.Count - 1))
                        {
                            output = $"{output}, {valueString}";
                        }
                        else
                        {
                            output = $"{output}, {localisedAnd} {valueString}";
                        }
                    }
                }
                return output;
            }, 1);

            store["Pause"] = new NativeFunction((values) => @"<break time=""" + values[0].AsNumber + @"ms"" />", 1);

            store["Play"] = new NativeFunction((values) => @"<audio src=""" + values[0].AsString + @""" />", 1);

            store["Spacialise"] = new NativeFunction((values) =>
            {
                string entree = values[0].AsString;
                if (entree == "")
                { return ""; }
                string sortie = "";
                string upperSortie = "";
                foreach (char c in entree)
                {
                    sortie = sortie + c + " ";
                }
                upperSortie = sortie.ToUpper();
                return upperSortie;

            }, 1);

            store["Emphasize"] = new NativeFunction((values) =>
            {
                switch (values.Count)
                {
                    case 1:
                        return @"<emphasis level =""strong"">" + values[0].AsString + @"</emphasis>";
                    case 2:
                        return @"<emphasis level =""" + values[1].AsString + @""">" + values[0].AsString + @"</emphasis>";
                    default:
                        return "The Emphasize function is used improperly. Please review the documentation for correct usage.";
                }
            }, 1, 2);

            store["SpeechPitch"] = new NativeFunction((values) =>
            {
                string text = values[0].AsString;
                string pitch = "default";
                if (values.Count == 1 || string.IsNullOrEmpty(values[1].AsString))
                {
                    return text;
                }
                else if (values.Count == 2)
                {
                    pitch = values[1].AsString;
                    return @"<prosody pitch=""" + pitch + @""">" + text + "</prosody>";
                }
                else
                {
                    return "The SpeechPitch function is used improperly. Please review the documentation for correct usage.";
                }
            }, 1, 2);

            store["SpeechRate"] = new NativeFunction((values) =>
            {
                string text = values[0].AsString;
                string rate = "default";
                if (values.Count == 1 || string.IsNullOrEmpty(values[1].AsString))
                {
                    return text;
                }
                else if (values.Count == 2)
                {
                    rate = values[1].AsString;
                    return @"<prosody rate=""" + rate + @""">" + text + "</prosody>";
                }
                else
                {
                    return "The SpeechRate function is used improperly. Please review the documentation for correct usage.";
                }
            }, 1, 2);

            store["SpeechVolume"] = new NativeFunction((values) =>
            {
                string text = values[0].AsString;
                string volume = "default";
                if (values.Count == 1 || string.IsNullOrEmpty(values[1].AsString))
                {
                    return text;
                }
                else if (values.Count == 2)
                {
                    volume = values[1].AsString;
                    return @"<prosody volume=""" + volume + @""">" + text + "</prosody>";
                }
                else
                {
                    return "The SpeechVolume function is used improperly. Please review the documentation for correct usage.";
                }
            }, 1, 2);

            store["StartsWithVowel"] = new NativeFunction((values) =>
            {
                string entree = values[0].AsString;
                if (entree == "")
                { return ""; }

                char[] vowels = { 'a', 'à', 'â', 'ä', 'e', 'ê', 'é', 'è', 'ë', 'i', 'î', 'ï', 'o', 'ô', 'ö', 'u', 'ù', 'û', 'ü', 'œ', 'y' };
                char firstCharacter = entree.ToLower().ToCharArray().ElementAt(0);
                Boolean result = vowels.Contains(firstCharacter);

                return result;

            }, 1);

            //
            // Commander-specific functions
            //
            store["ShipName"] = new NativeFunction((values) =>
            {
                int? localId = (values.Count == 0 ? (int?)null : (int)values[0].AsNumber);
                string model = (values.Count == 2 ? values[1].AsString : null);
                Ship ship = FindShip(localId, model);
                string result = (ship == null ? "your ship" : ship.SpokenName());
                return result;
            }, 0, 2);

            store["ShipCallsign"] = new NativeFunction((values) =>
            {
                int? localId = (values.Count == 0 ? (int?)null : (int)values[0].AsNumber);
                Ship ship = FindShip(localId, null);

                string result;
                if (ship != null)
                {
                    if (EDDI.Core.Eddi.Instance.Cmdr != null && EDDI.Core.Eddi.Instance.Cmdr.name != null)
                    {
                        // Obtain the first three characters
                        string chars = new Regex("[^a-zA-Z0-9]").Replace(EDDI.Core.Eddi.Instance.Cmdr.name, "").ToUpperInvariant().Substring(0, 3);
                        result = ship.SpokenManufacturer() + " " + Translations.ICAO(chars);
                    }
                    else
                    {
                        if (ship.SpokenManufacturer() == null)
                        {
                            result = "unidentified ship";
                        }
                        else
                        {
                            result = "unidentified " + ship.SpokenManufacturer() + " " + ship.SpokenModel();
                        }
                    }
                }
                else
                {
                    result = "unidentified ship";
                }
                return result;
            }, 0, 1);

            //
            // Obtain definition objects for various items
            //

            store["SecondsSince"] = new NativeFunction((values) =>
            {
                long? date = (long?)values[0].AsNumber;
                long now = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

                return now - date;
            }, 1);

            store["ICAO"] = new NativeFunction((values) =>
            {
                // Turn a string in to an ICAO definition
                string value = values[0].AsString;
                if (string.IsNullOrEmpty(value))
                {
                    return "";
                }

                // Remove anything that isn't alphanumeric
                Logging.Warn("value is " + value);
                value = value.ToUpperInvariant().Replace("[^A-Z0-9]", "");
                Logging.Warn("value is " + value);

                // Translate to ICAO
                return Translations.ICAO(value);
            }, 1);

            store["ShipDetails"] = new NativeFunction((values) =>
            {
                Ship result = ShipDefinitions.FromModel(values[0].AsString);
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["CombatRatingDetails"] = new NativeFunction((values) =>
            {
                CombatRating result = CombatRating.FromName(values[0].AsString);
                if (result == null)
                {
                    result = CombatRating.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["TradeRatingDetails"] = new NativeFunction((values) =>
            {
                TradeRating result = TradeRating.FromName(values[0].AsString) ?? TradeRating.FromEDName(values[0].AsString);
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["ExplorationRatingDetails"] = new NativeFunction((values) =>
            {
                ExplorationRating result = ExplorationRating.FromName(values[0].AsString);
                if (result == null)
                {
                    result = ExplorationRating.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["EmpireRatingDetails"] = new NativeFunction((values) =>
            {
                EmpireRating result = EmpireRating.FromName(values[0].AsString);
                if (result == null)
                {
                    result = EmpireRating.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["FederationRatingDetails"] = new NativeFunction((values) =>
            {
                FederationRating result = FederationRating.FromName(values[0].AsString);
                if (result == null)
                {
                    result = FederationRating.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["SystemDetails"] = new NativeFunction((values) =>
            {
                StarSystem result = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(values[0].AsString, true);
                SetSystemDistanceFromHome(result);
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["BodyDetails"] = new NativeFunction((values) =>
            {
                StarSystem system;
                if (values.Count == 1 || string.IsNullOrEmpty(values[1].AsString))
                {
                    // Current system
                    system = EDDI.Core.Eddi.Instance.CurrentStarSystem;
                }
                else
                {
                    // Named system
                    system = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(values[1].AsString, true);
                }
                Body result = system != null && system.bodies != null ? system.bodies.FirstOrDefault(v => v.name == values[0].AsString) : null;
                if (result != null && result.type == "Star" && result.chromaticity == null)
                {
                    // Need to set our internal extras for the star
                    result.setStellarExtras();
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1, 2);

            store["StationDetails"] = new NativeFunction((values) =>
            {
                if (values.Count == 0)
                {
                    return null;
                }
                StarSystem system;
                if (values.Count == 1)
                {
                    // Current system
                    system = EDDI.Core.Eddi.Instance.CurrentStarSystem;
                }
                else
                {
                    // Named system
                    system = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(values[1].AsString, true);
                }
                Station result = system != null && system.stations != null ? system.stations.FirstOrDefault(v => v.name == values[0].AsString) : null;
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1, 2);

            store["SuperpowerDetails"] = new NativeFunction((values) =>
            {
                Superpower result = Superpower.FromName(values[0].AsString);
                if (result == null)
                {
                    result = Superpower.From(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["StateDetails"] = new NativeFunction((values) =>
            {
                State result = State.FromName(values[0].AsString);
                if (result == null)
                {
                    result = State.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["EconomyDetails"] = new NativeFunction((values) =>
            {
                Economy result = Economy.FromName(values[0].AsString);
                if (result == null)
                {
                    result = Economy.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["GovernmentDetails"] = new NativeFunction((values) =>
            {
                Government result = Government.FromName(values[0].AsString);
                if (result == null)
                {
                    result = Government.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["SecurityLevelDetails"] = new NativeFunction((values) =>
            {
                SecurityLevel result = SecurityLevel.FromName(values[0].AsString);
                if (result == null)
                {
                    result = SecurityLevel.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["MaterialDetails"] = new NativeFunction((values) =>
            {
                if (string.IsNullOrEmpty(values[0].AsString))
                {
                    return new ReflectionValue(new object());
                }

                Material result = Material.FromName(values[0].AsString);
                if (result == null)
                {
                    result = Material.FromEDName(values[0].AsString);
                }
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["BlueprintDetails"] = new NativeFunction((values) =>
            {
                BlueprintMaterials result = BlueprintMaterials.FromName(values[0].AsString);
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["GalnetNewsArticle"] = new NativeFunction((values) =>
            {
                News result = GalnetSqLiteRepository.Instance.GetArticle(values[0].AsString);
                return (result == null ? new ReflectionValue(new object()) : new ReflectionValue(result));
            }, 1);

            store["GalnetNewsArticles"] = new NativeFunction((values) =>
            {
                List<News> results = null;
                if (values.Count == 0)
                {
                    // Obtain all unread articles
                    results = GalnetSqLiteRepository.Instance.GetArticles();
                }
                else if (values.Count == 1)
                {
                    // Obtain all unread news of a given category
                    results = GalnetSqLiteRepository.Instance.GetArticles(values[0].AsString);
                }
                else if (values.Count == 2)
                {
                    // Obtain all news of a given category
                    results = GalnetSqLiteRepository.Instance.GetArticles(values[0].AsString, values[1].AsBoolean);
                }
                return (results == null ? new ReflectionValue(new List<News>()) : new ReflectionValue(results));
            }, 0, 2);

            store["GalnetNewsMarkRead"] = new NativeFunction((values) =>
            {
                News result = GalnetSqLiteRepository.Instance.GetArticle(values[0].AsString);
                if (result != null)
                {
                    GalnetSqLiteRepository.Instance.MarkRead(result);
                }
                return "";
            }, 1);

            store["GalnetNewsMarkUnread"] = new NativeFunction((values) =>
            {
                News result = GalnetSqLiteRepository.Instance.GetArticle(values[0].AsString);
                if (result != null)
                {
                    GalnetSqLiteRepository.Instance.MarkUnread(result);
                }
                return "";
            }, 1);
            
            store["GalnetNewsDelete"] = new NativeFunction((values) =>
            {
                News result = GalnetSqLiteRepository.Instance.GetArticle(values[0].AsString);
                if (result != null)
                {
                    GalnetSqLiteRepository.Instance.DeleteNews(result);
                }
                return "";
            }, 1);
            
            store["Distance"] = new NativeFunction((values) =>
            {
                return (decimal)Math.Round(Math.Sqrt(Math.Pow((double)(values[0].AsNumber - values[3].AsNumber), 2)
                                                                      + Math.Pow((double)(values[1].AsNumber - values[4].AsNumber), 2)
                                                                      + Math.Pow((double)(values[2].AsNumber - values[5].AsNumber), 2)), 2);
            }, 6);

            store["Log"] = new NativeFunction((values) =>
            {
                Logging.Info(values[0].AsString);
                return "";
            }, 1);

            store["SetState"] = new NativeFunction((values) =>
            {
                string name = values[0].AsString.ToLowerInvariant().Replace(" ", "_");
                Cottle.Value value = values[1];
                if (value.Type == Cottle.ValueContent.Boolean)
                {
                    EDDI.Core.Eddi.Instance.State[name] = value.AsBoolean;
                    store["state"] = BuildState();
                }
                else if (value.Type == Cottle.ValueContent.Number)
                {
                    EDDI.Core.Eddi.Instance.State[name] = value.AsNumber;
                    store["state"] = BuildState();
                }
                else if (value.Type == Cottle.ValueContent.String)
                {
                    EDDI.Core.Eddi.Instance.State[name] = value.AsString;
                    store["state"] = BuildState();
                }
                // Ignore other possibilities
                return "";
            }, 2);

            // Variables
            foreach (KeyValuePair<string, Cottle.Value> entry in vars)
            {
                store[entry.Key] = entry.Value;
            }

            return store;
        }

        public static Dictionary<Cottle.Value, Cottle.Value> BuildState()
        {
            if (EDDI.Core.Eddi.Instance.State == null)
                return null;

            var state = new Dictionary<Cottle.Value, Cottle.Value>();
            foreach (string key in EDDI.Core.Eddi.Instance.State.Keys)
            {
                object value = EDDI.Core.Eddi.Instance.State[key];
                if (value == null)
                {
                    continue;
                }
                Type valueType = value.GetType();
                if (valueType == typeof(string))
                {
                    state[key] = (string)value;
                }
                else if (valueType == typeof(int))
                {
                    state[key] = (int)value;
                }
                else if (valueType == typeof(bool))
                {
                    state[key] = (bool)value;
                }
                else if (valueType == typeof(decimal))
                {
                    state[key] = (decimal)value;
                }
            }
            return state;
        }

        private static Ship FindShip(int? localId, string model)
        {
            Ship ship = null;
            if (localId == null)
            {
                // No local ID so take the current ship
                ship = ((ShipMonitor)EDDI.Core.Eddi.Instance.ObtainMonitor("Ship monitor")).GetCurrentShip();
            }
            else
            {
                // Find the ship with the given local ID
                ship = ((ShipMonitor)EDDI.Core.Eddi.Instance.ObtainMonitor("Ship monitor")).GetShip(localId);
            }

            if (ship == null)
            {
                // Provide a basic ship based on the model template
                ship = ShipDefinitions.FromModel(model);
                if (ship == null)
                {
                    ship = ShipDefinitions.FromEDModel(model);
                }
            }
            return ship;
        }

        private void SetSystemDistanceFromHome(StarSystem system)
        {
            if (EDDI.Core.Eddi.Instance.HomeStarSystem != null && EDDI.Core.Eddi.Instance.HomeStarSystem.x != null && system.x != null)
            {
                system.distancefromhome = (decimal)Math.Round(Math.Sqrt(Math.Pow((double)(system.x - EDDI.Core.Eddi.Instance.HomeStarSystem.x), 2)
                                                                      + Math.Pow((double)(system.y - EDDI.Core.Eddi.Instance.HomeStarSystem.y), 2)
                                                                      + Math.Pow((double)(system.z - EDDI.Core.Eddi.Instance.HomeStarSystem.z), 2)), 2);
                Logging.Debug("Distance from home is " + system.distancefromhome);
            }
        }
    }
}

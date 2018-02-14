using CredentialManagement;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiSpeechService;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Utilities;

namespace EddiCompanionAppService
{
    public class CompanionAppService
    {
        private const string BASE_URL = "https://companion.orerve.net";
        private const string ROOT_URL = "/";
        private const string LOGIN_URL = "/user/login";
        private const string CONFIRM_URL = "/user/confirm";
        private const string PROFILE_URL = "/profile";
        private const string MARKET_URL = "/market";
        private const string SHIPYARD_URL = "/shipyard";

        // We cache the profile to avoid spamming the service
        private Profile _cachedProfile;
        private DateTime _cachedProfileExpires;

        public enum State
        {
            NEEDS_LOGIN,
            NEEDS_CONFIRMATION,
            READY
        };
        public State CurrentState;

        public CompanionAppCredentials Credentials;

        private static CompanionAppService _instance;

        private static readonly object InstanceLock = new object();
        public static CompanionAppService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (InstanceLock)
                    {
                        if (_instance == null)
                        {
                            Logging.Debug("No companion API instance: creating one");
                            _instance = new CompanionAppService();
                        }
                    }
                }
                return _instance;
            }
        }
        private CompanionAppService()
        {
            Credentials = CompanionAppCredentials.FromFile();

            // Need to work out our current state.

            //If we're missing username and password then we need to log in again
            if (string.IsNullOrEmpty(Credentials.email) || string.IsNullOrEmpty(getPassword()))
            {
                CurrentState = State.NEEDS_LOGIN;
            }
            else if (string.IsNullOrEmpty(Credentials.machineId) || string.IsNullOrEmpty(Credentials.machineToken))
            {
                CurrentState = State.NEEDS_LOGIN;
            }
            else
            {
                // Looks like we're ready but test it to find out
                CurrentState = State.READY;
                try
                {
                    Profile();
                }
                catch (EliteDangerousCompanionAppException ex)
                {
                    Logging.Warn("Failed to obtain profile: " + ex.ToString());
                }
            }
        }

        ///<summary>Log in.  Throws an exception if it fails</summary>
        public void Login()
        {
            if (CurrentState != State.NEEDS_LOGIN)
            {
                // Shouldn't be here
                throw new EliteDangerousCompanionAppIllegalStateException("Service in incorrect state to login (" + CurrentState + ")");
            }

            HttpWebRequest request = GetRequest(BASE_URL + LOGIN_URL);

            // Send the request
            request.ContentType = "application/x-www-form-urlencoded";
            request.Method = "POST";
            string encodedUsername = WebUtility.UrlEncode(Credentials.email);
            string encodedPassword = WebUtility.UrlEncode(getPassword());
            byte[] data = Encoding.UTF8.GetBytes("email=" + encodedUsername + "&password=" + encodedPassword);
            request.ContentLength = data.Length;
            using (Stream dataStream = request.GetRequestStream())
            {
                dataStream.Write(data, 0, data.Length);
            }

            using (HttpWebResponse response = GetResponse(request))
            {
                if (response == null)
                {
                    throw new EliteDangerousCompanionAppException("Failed to contact API server");
                }
                if (response.StatusCode == HttpStatusCode.Found && response.Headers["Location"] == CONFIRM_URL)
                {
                    CurrentState = State.NEEDS_CONFIRMATION;
                }
                else if (response.StatusCode == HttpStatusCode.Found && response.Headers["Location"] == ROOT_URL)
                {
                    CurrentState = State.READY;
                }
                else
                {
                    throw new EliteDangerousCompanionAppAuthenticationException("Username or password incorrect");
                }
            }
        }

        ///<summary>Confirm a login.  Throws an exception if it fails</summary>
        public void Confirm(string code)
        {
            if (CurrentState != State.NEEDS_CONFIRMATION)
            {
                // Shouldn't be here
                throw new EliteDangerousCompanionAppIllegalStateException("Service in incorrect state to confirm login (" + CurrentState + ")");
            }

            HttpWebRequest request = GetRequest(BASE_URL + CONFIRM_URL);

            request.ContentType = "application/x-www-form-urlencoded";
            request.Method = "POST";
            string encodedCode = WebUtility.UrlEncode(code);
            byte[] data = Encoding.UTF8.GetBytes("code=" + encodedCode);
            request.ContentLength = data.Length;
            using (Stream dataStream = request.GetRequestStream())
            {
                dataStream.Write(data, 0, data.Length);
            }

            using (HttpWebResponse response = GetResponse(request))
            {
                if (response == null)
                {
                    throw new EliteDangerousCompanionAppException("Failed to contact API server");
                }

                if (response.StatusCode == HttpStatusCode.Found && response.Headers["Location"] == ROOT_URL)
                {
                    CurrentState = State.READY;
                }
                else if (response.StatusCode == HttpStatusCode.Found && response.Headers["Location"] == LOGIN_URL)
                {
                    CurrentState = State.NEEDS_LOGIN;
                    throw new EliteDangerousCompanionAppAuthenticationException("Confirmation code incorrect or expired");
                }
            }
        }

        /// <summary>
        /// Log out of the companion API and remove local credentials
        /// </summary>
        public void Logout()
        {
            // Remove everything other than the local email address
            Credentials = CompanionAppCredentials.FromFile();
            Credentials.machineToken = null;
            Credentials.machineId = null;
            Credentials.appId = null;
            setPassword(null);
            Credentials.ToFile();
            CurrentState = State.NEEDS_LOGIN;
        }

        public Profile Profile(bool forceRefresh = false)
        {
            Logging.Debug("Entered");
            if (CurrentState != State.READY)
            {
                // Shouldn't be here
                Logging.Debug("Service in incorrect state to provide profile (" + CurrentState + ")");
                Logging.Debug("Leaving");
                throw new EliteDangerousCompanionAppIllegalStateException("Service in incorrect state to provide profile (" + CurrentState + ")");
            }
            if ((!forceRefresh) && _cachedProfileExpires > DateTime.Now)
            {
                // return the cached version
                Logging.Debug("Returning cached profile");
                Logging.Debug("Leaving");
                return _cachedProfile;
            }

            string data = ObtainProfile(BASE_URL + PROFILE_URL);

            if (data == null || data == "Profile unavailable")
            {
                // Happens if there is a problem with the API.  Logging in again might clear this...
                Relogin();
                if (CurrentState != State.READY)
                {
                    // No luck; give up
                    SpeechService.Instance.Say(null, "Access to Frontier API has been lost.  Please update your information in Eddi's Frontier API tab to re-establish the connection.", false);
                    Logout();
                }
                else
                {
                    // Looks like login worked; try again
                    data = ObtainProfile(BASE_URL + PROFILE_URL);

                    if (data == null || data == "Profile unavailable")

                    {
                        // No luck with a relogin; give up
                        SpeechService.Instance.Say(null, "Access to Frontier API has been lost.  Please update your information in Eddi's Frontier API tab to re-establish the connection.", false);
                        Logout();
                        throw new EliteDangerousCompanionAppException("Failed to obtain data from Frontier server (" + CurrentState + ")");
                    }
                }
            }

            try
            {
                _cachedProfile = ProfileFromJson(data);
            }
            catch (JsonException ex)
            {
                Logging.Error("Failed to parse companion profile", ex);
                _cachedProfile = null;
            }

            if (_cachedProfile != null)
            {
                _cachedProfileExpires = DateTime.Now.AddSeconds(30);
                Logging.Debug("Profile is " + JsonConvert.SerializeObject(_cachedProfile));
            }

            Logging.Debug("Leaving");
            return _cachedProfile;
        }

        public Profile Station(string systemName)
        {
            Logging.Debug("Entered");
            if (CurrentState != State.READY)
            {
                // Shouldn't be here
                Logging.Debug("Service in incorrect state to provide station data (" + CurrentState + ")");
                Logging.Debug("Leaving");
                throw new EliteDangerousCompanionAppIllegalStateException("Service in incorrect state to provide station data (" + CurrentState + ")");
            }

            try
            {
                Logging.Debug("Getting station market data");
                string market = ObtainProfile(BASE_URL + MARKET_URL);
                market = "{\"lastStarport\":" + market + "}";
                JObject marketJson = JObject.Parse(market);
                string lastStarport = (string)marketJson["lastStarport"]["name"];

                _cachedProfile.CurrentStarSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(systemName);
                _cachedProfile.LastStation = _cachedProfile.CurrentStarSystem.stations.Find(s => s.name == lastStarport);
                if (_cachedProfile.LastStation == null)
                {
                    // Don't have a station so make one up
                    _cachedProfile.LastStation = new Station();
                    _cachedProfile.LastStation.name = lastStarport;
                }
                _cachedProfile.LastStation.systemname = systemName;

                if (_cachedProfile.LastStation.hasmarket ?? false)
                {
                    _cachedProfile.LastStation.economies = EconomiesFromProfile(marketJson);
                    _cachedProfile.LastStation.commodities = CommoditiesFromProfile(marketJson);
                    _cachedProfile.LastStation.prohibited = ProhibitedCommoditiesFromProfile(marketJson);
                }

                if (_cachedProfile.LastStation.hasoutfitting ?? false)
                {
                    Logging.Debug("Getting station outfitting data");
                    string outfitting = ObtainProfile(BASE_URL + SHIPYARD_URL);
                    outfitting = "{\"lastStarport\":" + outfitting + "}";
                    JObject outfittingJson = JObject.Parse(outfitting);
                    _cachedProfile.LastStation.outfitting = OutfittingFromProfile(outfittingJson);
                }

                if (_cachedProfile.LastStation.hasshipyard ?? false)
                {
                    Logging.Debug("Getting station shipyard data");
                    Thread.Sleep(5000);
                    string shipyard = ObtainProfile(BASE_URL + SHIPYARD_URL);
                    shipyard = "{\"lastStarport\":" + shipyard + "}";
                    JObject shipyardJson = JObject.Parse(shipyard);
                    _cachedProfile.LastStation.shipyard = ShipyardFromProfile(shipyardJson);
                }
            }
            catch (JsonException ex)
            {
                Logging.Error("Failed to parse companion station data", ex);
            }

            Logging.Debug("Station is " + JsonConvert.SerializeObject(_cachedProfile));
            Logging.Debug("Leaving");
            return _cachedProfile;
        }


        private string ObtainProfile(string url)
        {
            HttpWebRequest request = GetRequest(url);
            using (HttpWebResponse response = GetResponse(request))
            {
                if (response == null)
                {
                    Logging.Debug("Failed to contact API server");
                    Logging.Debug("Leaving");
                    throw new EliteDangerousCompanionAppException("Failed to contact API server");
                }

                if (response.StatusCode == HttpStatusCode.Found && response.Headers["Location"] == LOGIN_URL)
                {
                    return null;
                }

                return GetResponseData(response);
            }
        }

        /**
         * Try to relogin if there is some issue that requires it.
         * Throws an exception if it failed to log in.
         */
        private void Relogin()
        {
            // Need to log in again.
            CurrentState = State.NEEDS_LOGIN;
            Login();
            if (CurrentState != State.READY)
            {
                Logging.Debug("Service in incorrect state to provide profile (" + CurrentState + ")");
                Logging.Debug("Leaving");
                throw new EliteDangerousCompanionAppIllegalStateException("Service in incorrect state to provide profile (" + CurrentState + ")");
            }
        }

        /**
         * Obtain the response data from an HTTP web response
         */
        private string GetResponseData(HttpWebResponse response)
        {
            // Obtain and parse our response
            var encoding = response.CharacterSet == ""
                    ? Encoding.UTF8
                    : Encoding.GetEncoding(response.CharacterSet);

            Logging.Debug("Reading response");
            using (var stream = response.GetResponseStream())
            {
                var reader = new StreamReader(stream, encoding);
                string data = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(data))
                {
                    Logging.Warn("No data returned");
                    return null;
                }
                Logging.Debug("Data is " + data);
                return data;
            }
        }

        // Set up a request with the correct parameters for talking to the companion app
        private HttpWebRequest GetRequest(string url)
        {
            Logging.Debug("Entered");

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            CookieContainer cookieContainer = new CookieContainer();
            AddCompanionAppCookie(cookieContainer, Credentials);
            AddMachineIdCookie(cookieContainer, Credentials);
            AddMachineTokenCookie(cookieContainer, Credentials);
            request.CookieContainer = cookieContainer;
            request.AllowAutoRedirect = false;
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 7_1_2 like Mac OS X) AppleWebKit/537.51.2 (KHTML, like Gecko) Mobile/11D257";

            Logging.Debug("Leaving");
            return request;
        }

        // Obtain a response, ensuring that we obtain the response's cookies
        private HttpWebResponse GetResponse(WebRequest request)
        {
            Logging.Debug("Entered");
            Logging.Debug("Requesting " + request.RequestUri);

            HttpWebResponse response;
            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException wex)
            {
                Logging.Warn("Failed to obtain response, error code " + wex.Status);
                return null;
            }
            Logging.Debug("Response is " + JsonConvert.SerializeObject(response));
            UpdateCredentials(response);
            Credentials.ToFile();

            Logging.Debug("Leaving");
            return response;
        }

        private void UpdateCredentials(WebResponse response)
        {
            Logging.Debug("Entered");

            // Obtain the cookies from the raw information available to us
            string cookieHeader = response.Headers[HttpResponseHeader.SetCookie];
            if (cookieHeader != null)
            {
                Match companionAppMatch = Regex.Match(cookieHeader, @"CompanionApp=([^;]+)");
                if (companionAppMatch.Success)
                {
                    Credentials.appId = companionAppMatch.Groups[1].Value;
                }
                Match machineIdMatch = Regex.Match(cookieHeader, @"mid=([^;]+)");
                if (machineIdMatch.Success)
                {
                    Credentials.machineId = machineIdMatch.Groups[1].Value;
                }
                Match machineTokenMatch = Regex.Match(cookieHeader, @"mtk=([^;]+)");
                if (machineTokenMatch.Success)
                {
                    Credentials.machineToken = machineTokenMatch.Groups[1].Value;
                }
            }

            Logging.Debug("Leaving");
        }

        private static void AddCompanionAppCookie(CookieContainer cookies, CompanionAppCredentials credentials)
        {
            if (cookies != null && credentials.appId != null)
            {
                var appCookie = new Cookie
                {
                    Domain = "companion.orerve.net",
                    Path = "/",
                    Name = "CompanionApp",
                    Value = credentials.appId,
                    Secure = false
                };
                cookies.Add(appCookie);
            }
        }

        private static void AddMachineIdCookie(CookieContainer cookies, CompanionAppCredentials credentials)
        {
            if (cookies != null && credentials.machineId != null)
            {
                var machineIdCookie = new Cookie();
                machineIdCookie.Domain = "companion.orerve.net";
                machineIdCookie.Path = "/";
                machineIdCookie.Name = "mid";
                machineIdCookie.Value = credentials.machineId;
                machineIdCookie.Secure = true;
                // The expiry is embedded in the cookie value
                if (credentials.machineId.IndexOf("%7C") == -1)
                {
                    machineIdCookie.Expires = DateTime.Now.AddDays(7);
                }
                else
                {
                    string expiryseconds = credentials.machineId.Substring(0, credentials.machineId.IndexOf("%7C"));
                    DateTime expiryDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                    try
                    {
                        expiryDateTime = expiryDateTime.AddSeconds(Convert.ToInt64(expiryseconds));
                        machineIdCookie.Expires = expiryDateTime;
                    }
                    catch (Exception)
                    {
                        Logging.Warn("Failed to handle machine id expiry seconds " + expiryseconds);
                        machineIdCookie.Expires = DateTime.Now.AddDays(7);
                    }
                }
                cookies.Add(machineIdCookie);
            }
        }

        private static void AddMachineTokenCookie(CookieContainer cookies, CompanionAppCredentials credentials)
        {
            if (cookies != null && credentials.machineToken != null)
            {
                var machineTokenCookie = new Cookie
                {
                    Domain = "companion.orerve.net",
                    Path = "/",
                    Name = "mtk",
                    Value = credentials.machineToken,
                    Secure = true
                };
                // The expiry is embedded in the cookie value
                if (credentials.machineToken.IndexOf("%7C") == -1)
                {
                    machineTokenCookie.Expires = DateTime.Now.AddDays(7);
                }
                else
                {
                    string expiryseconds = credentials.machineToken.Substring(0, credentials.machineToken.IndexOf("%7C"));
                    DateTime expiryDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                    try
                    {
                        expiryDateTime = expiryDateTime.AddSeconds(Convert.ToInt64(expiryseconds));
                        machineTokenCookie.Expires = expiryDateTime;
                    }
                    catch (Exception)
                    {
                        Logging.Warn("Failed to handle machine token expiry seconds " + expiryseconds);
                        machineTokenCookie.Expires = DateTime.Now.AddDays(7);
                    }
                }
                cookies.Add(machineTokenCookie);
            }
        }

        /// <summary>Create a  profile given the results from a /profile call</summary>
        public static Profile ProfileFromJson(string data)
        {
            Logging.Debug("Entered");
            Profile profile = null;
            if (!string.IsNullOrEmpty(data))
            {
                profile = ProfileFromJson(JObject.Parse(data));
                AugmentCmdrInfo(profile.Cmdr);
            }
            Logging.Debug("Leaving");
            return profile;
        }

        /// <summary>Create a profile given the results from a /profile call</summary>
        public static Profile ProfileFromJson(JObject json)
        {
            Logging.Debug("Entered");
            Profile profile = new Profile();
            profile.json = json;

            if (json["commander"] != null)
            {
                Commander commander = new Commander
                {
                    name = (string)json["commander"]["name"],
                    combatrating = CombatRating.FromRank((int)json["commander"]["rank"]["combat"]),
                    traderating = TradeRating.FromRank((int)json["commander"]["rank"]["trade"]),
                    explorationrating = ExplorationRating.FromRank((int)json["commander"]["rank"]["explore"]),
                    cqcrating = CQCRating.FromRank((int)json["commander"]["rank"]["cqc"]),
                    empirerating = EmpireRating.FromRank((int)json["commander"]["rank"]["empire"]),
                    federationrating = FederationRating.FromRank((int)json["commander"]["rank"]["federation"]),
                    credits = (long)json["commander"]["credits"],
                    debt = (long)json["commander"]["debt"]
                };

                profile.Cmdr = commander;

                string systemName = json["lastSystem"] == null ? null : (string)json["lastSystem"]["name"];
                if (systemName != null)
                {
                    profile.CurrentStarSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(systemName);
                }

                if (json["lastStarport"] != null)
                {
                    profile.LastStation = profile.CurrentStarSystem.stations.Find(s => s.name == (string)json["lastStarport"]["name"]);
                    if (profile.LastStation == null)
                    {
                        // Don't have a station so make one up
                        profile.LastStation = new Station();
                        profile.LastStation.name = (string)json["lastStarport"]["name"];
                    }

                    profile.LastStation.systemname = profile.CurrentStarSystem.name;
                }
            }

            Logging.Debug("Leaving");
            return profile;
        }

        private static void AugmentCmdrInfo(Commander cmdr)
        {
            Logging.Debug("Entered");
            //if (cmdr != null)
            //{
            //    CommanderConfiguration cmdrConfiguration = CommanderConfiguration.FromFile();
            //    if (cmdrConfiguration.PhoneticName == null || cmdrConfiguration.PhoneticName.Trim().Length == 0)
            //    {
            //        cmdr.phoneticname = null;
            //    }
            //    else
            //    {
            //        cmdr.phoneticname = cmdrConfiguration.PhoneticName;
            //    }
            //}
            Logging.Debug("Leaving");
        }

        // Obtain the list of outfitting modules from the profile
        public static List<Module> OutfittingFromProfile(dynamic json)
        {
            List<Module> modules = new List<Module>();

            if (json["lastStarport"] != null && json["lastStarport"]["modules"] != null)
            {
                foreach (dynamic o in json["lastStarport"]["modules"])
                {
                    dynamic moduleJson = o.Value;
                    // Not interested in paintjobs, decals, ...
                    if (moduleJson["category"] == "weapon" || moduleJson["category"] == "module" || moduleJson["category"] == "utility")
                    {
                        Module module = ModuleDefinitions.ModuleFromEliteID((long)moduleJson["id"]);
                        if (module.name == null)
                        {
                            // Unknown module; log an error so that we can update the definitions
                            Logging.Report("No definition for outfitting module", moduleJson.ToString(Formatting.None));
                            // Set the name from the JSON
                            module.EDName = (string)moduleJson["name"];
                        }
                        module.price = moduleJson["cost"];
                        modules.Add(module);
                    }
                }
            }

            return modules;
        }

        // Obtain the list of station economies from the profile
        public static List<CompanionAppEconomy> EconomiesFromProfile(dynamic json)
        {
            List<CompanionAppEconomy> economies = new List<CompanionAppEconomy>();

            if (json["lastStarport"] != null && json["lastStarport"]["economies"] != null)
            {
                foreach (dynamic o in json["lastStarport"]["economies"])
                {
                    dynamic economyJson = o.Value;
                    CompanionAppEconomy economy = new CompanionAppEconomy
                    {
                        name = (string)economyJson["name"],
                        proportion = (decimal)economyJson["proportion"]
                    };

                    economies.Add(economy);
                }
            }

            Logging.Debug("Economies are " + JsonConvert.SerializeObject(economies));
            return economies;
        }

        // Obtain the list of prohibited commodities from the profile
        public static List<string> ProhibitedCommoditiesFromProfile(dynamic json)
        {
            List<string> prohibitedCommodities = new List<string>();

            if (json["lastStarport"] != null && json["lastStarport"]["prohibited"] != null)
            {
                foreach (dynamic prohibitedcommodity in json["lastStarport"]["prohibited"])
                {
                    string pc = (string)prohibitedcommodity.Value;
                    if (pc != null)
                    {
                        prohibitedCommodities.Add(pc);
                    }
                }
            }

            Logging.Debug("Prohibited Commodities are " + JsonConvert.SerializeObject(prohibitedCommodities));
            return prohibitedCommodities;
        }

        // Obtain the list of commodities from the profile
        public static List<Commodity> CommoditiesFromProfile(dynamic json)
        {
            List<Commodity> commodities = new List<Commodity>();

            if (json["lastStarport"] != null && json["lastStarport"]["commodities"] != null)
            {
                List<Commodity> commodityErrors = new List<Commodity>();
                foreach (dynamic o in json["lastStarport"]["commodities"])
                {
                    dynamic commodityJson = o.Value;
                    Commodity commodity = new Commodity();
                    Commodity eddiCommodity = CommodityDefinitions.CommodityFromEliteID((long)o["id"]);
                    if (eddiCommodity == null)
                    {
                        // If we fail to identify the commodity by EDID, try using the EDName.
                        eddiCommodity = CommodityDefinitions.FromName((string)o["name"]);
                    }

                    commodity.EDName = (string)o["name"];
                    commodity.name = (string)o["locName"];
                    commodity.category = ((string)o["categoryname"]).Trim();
                    commodity.avgprice = (int)o["meanPrice"];
                    commodity.buyprice = (int)o["buyPrice"];
                    commodity.stock = (int)o["stock"];
                    commodity.stockbracket = (dynamic)o["stockBracket"];
                    commodity.sellprice = (int)o["sellPrice"];
                    commodity.demand = (int)o["demand"];
                    commodity.demandbracket = (dynamic)o["demandBracket"];

                    List<string> StatusFlags = new List<string>();
                    foreach (dynamic statusFlag in o["statusFlags"])
                    {
                        StatusFlags.Add((string)statusFlag);
                    }
                    commodity.StatusFlags = StatusFlags;
                    commodities.Add(commodity);

                    if (eddiCommodity == null || eddiCommodity.EDName != commodity.EDName || eddiCommodity.name != commodity.name
                        || eddiCommodity.category != commodity.category)
                    {
                        if (eddiCommodity.name != "Limpet")
                        {
                            commodityErrors.Add(commodity);
                        }
                    }
                }

                if (commodityErrors.Any())
                {
                    Logging.Report("Commodity definition errors: " + JsonConvert.SerializeObject(commodityErrors));
                }
            }

            return commodities;
        }

        // Obtain the list of ships available at the station from the profile
        public static List<Ship> ShipyardFromProfile(JObject json)
        {
            List<Ship> ships = new List<Ship>();

            if (json["lastStarport"] != null && json["lastStarport"]["ships"] != null)
            {
                foreach (var o in json["lastStarport"]["ships"]["shipyard_list"])
                {
                    Ship ship = ShipDefinitions.FromEliteID((long)o["id"]);
                    if (ship.EDName != null)
                    {
                        ship.value = (long)o["basevalue"];
                        ships.Add(ship);
                    }
                }

                foreach (var o in json["lastStarport"]["ships"]["unavailable_list"])
                {
                    Ship ship = ShipDefinitions.FromEliteID((long)o["id"]);
                    if (ship.EDName != null)
                    {
                        ship.value = (long)o["basevalue"];
                        ships.Add(ship);
                    }
                }
            }

            return ships;
        }

        public void setPassword(string password)
        {
            using (var credential = new Credential())
            {
                credential.Password = password;
                credential.Target = @"EDDIFDevApi";
                credential.Type = CredentialType.Generic;
                credential.PersistanceType = PersistanceType.Enterprise;
                credential.Save();
            }
        }

        private string getPassword()
        {
            using (var credential = new Credential())
            {
                credential.Target = @"EDDIFDevApi";
                credential.Load();
                return credential.Password;
            }
        }
    }
}

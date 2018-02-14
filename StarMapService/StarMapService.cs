using EddiDataDefinitions;
using EddiDataProviderService;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Serializers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Utilities;

namespace EddiStarMapService
{
    /// <summary> Talk to the Elite: Dangerous Star Map service </summary>
    public class StarMapService
    {
        // Use en-US everywhere to ensure that we don't use , rather than . for our separator
        private static readonly CultureInfo EnUsCulture = new CultureInfo("en-US");

        private readonly string _commanderName;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        public StarMapService(string apiKey, string commanderName, string baseUrl="https://www.edsm.net/")
        {
            _apiKey = apiKey;
            _commanderName = commanderName;
            _baseUrl = baseUrl;
        }

        public void SendStarMapLog(DateTime timestamp, string systemName, decimal? x, decimal? y, decimal? z)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-logs-v1/set-log", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("systemName", systemName);
            request.AddParameter("dateVisited", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            request.AddParameter("fromSoftware", Constants.EDDI_NAME);
            request.AddParameter("fromSoftwareVersion", Constants.EDDI_VERSION);
            if (x.HasValue)
                request.AddParameter("x", ((decimal) x).ToString("0.000", EnUsCulture));

            if (y.HasValue)
                request.AddParameter("y", ((decimal) y).ToString("0.000", EnUsCulture));

            if (z.HasValue)
                request.AddParameter("z", ((decimal) z).ToString("0.000", EnUsCulture));

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug($"Sending data to EDSM: {client.BuildUri(request).AbsoluteUri}");
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    var response = clientResponse.Data;
                    Logging.Debug("Data sent to EDSM");
                    if (response.Msgnum != 100)
                        Logging.Warn($"EDSM responded with {response.Msg}");
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendCredits(decimal credits, decimal loan)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-commander-v1/set-credits", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("balance", credits);
            request.AddParameter("loan", loan);
            request.AddParameter("fromSoftware", Constants.EDDI_NAME);
            request.AddParameter("fromSoftwareVersion", Constants.EDDI_VERSION);

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug("Sending data to EDSM: " + client.BuildUri(request).AbsoluteUri);
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    var response = clientResponse.Data;
                    Logging.Debug("Data sent to EDSM");
                    if (response.Msgnum != 100)
                        Logging.Warn("EDSM responded with " + response.Msg);
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendRanks(int combat, int combatProgress,
            int trade, int tradeProgress,
            int exploration, int explorationProgress,
            int cqc, int cqcProgress,
            int federation, int federationProgress,
            int empire, int empireProgress)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-commander-v1/set-ranks", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("Combat", combat + ";" + combatProgress);
            request.AddParameter("Trade", trade + ";" + tradeProgress);
            request.AddParameter("Explore", exploration + ";" + explorationProgress);
            request.AddParameter("CQC", cqc + ";" + cqcProgress);
            request.AddParameter("Federation", federation + ";" + federationProgress);
            request.AddParameter("Empire", empire + ";" + empireProgress);
            request.AddParameter("fromSoftware", Constants.EDDI_NAME);
            request.AddParameter("fromSoftwareVersion", Constants.EDDI_VERSION);

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug("Sending data to EDSM: " + client.BuildUri(request).AbsoluteUri);
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    StarMapLogResponse response = clientResponse.Data;
                    Logging.Debug("Data sent to EDSM");
                    if (response.Msgnum != 100)
                    {
                        Logging.Warn("EDSM responded with " + response.Msg);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendMaterials(Dictionary<string, int> materials)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-commander-v1/set-materials", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("type", "materials");
            request.AddParameter("values", JsonConvert.SerializeObject(materials));
            request.AddParameter("fromSoftware", Constants.EDDI_NAME);
            request.AddParameter("fromSoftwareVersion", Constants.EDDI_VERSION);

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug("Sending data to EDSM: " + client.BuildUri(request).AbsoluteUri);
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    StarMapLogResponse response = clientResponse.Data;
                    Logging.Debug("Data sent to EDSM");
                    if (response.Msgnum != 100)
                    {
                        Logging.Warn("EDSM responded with " + response.Msg);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendData(Dictionary<string, int> data)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-commander-v1/set-materials", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("type", "data");
            request.AddParameter("values", JsonConvert.SerializeObject(data));
            request.AddParameter("fromSoftware", Constants.EDDI_NAME);
            request.AddParameter("fromSoftwareVersion", Constants.EDDI_VERSION);

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug("Sending data to EDSM: " + client.BuildUri(request).AbsoluteUri);
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    StarMapLogResponse response = clientResponse.Data;
                    Logging.Debug("Data sent to EDSM");
                    if (response.Msgnum != 100)
                    {
                        Logging.Warn("EDSM responded with " + response.Msg);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendShip(Ship ship)
        {
            if (ship == null)
            {
                return;
            }

            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-commander-v1/update-ship", Method.POST);
            string coriolisUri = ship.CoriolisUri();
            string edshipyardUri = ship.EDShipyardUri();

            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("shipId", ship.LocalId);
            request.AddParameter("shipName", ship.name);
            request.AddParameter("shipIdent", ship.ident);
            request.AddParameter("type", ship.EDName);
            request.AddParameter("paintJob", ship.paintjob);
            request.AddParameter("cargoQty", ship.cargocarried);
            request.AddParameter("cargoCapacity", ship.cargocapacity);
            request.AddParameter("linkToEDShipyard", edshipyardUri);
            request.AddParameter("linkToCoriolis", coriolisUri);

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug("Sending ship data to EDSM: " + client.BuildUri(request).AbsoluteUri);
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    Logging.Debug("Data sent to EDSM");
                    StarMapLogResponse response = clientResponse.Data;
                    if (response == null)
                    {
                        Logging.Warn($"EDSM rejected ship data with {clientResponse.ErrorMessage}");
                        return;
                    }
                    else
                    {
                        Logging.Debug($"EDSM response {response.Msgnum}: " + response.Msg);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendShipSwapped(int shipId)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-commander-v1/set-ship-id", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("shipId", shipId);

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug("Sending data to EDSM: " + client.BuildUri(request).AbsoluteUri);
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    StarMapLogResponse response = clientResponse.Data;
                    Logging.Debug("Data sent to EDSM");
                    if (response.Msgnum != 100)
                    {
                        Logging.Warn("EDSM responded with " + response.Msg);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendShipSold(int shipId)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-commander-v1/sell-ship", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("shipId", shipId);

            Task.Run(() =>
            {
                try
                {
                    Logging.Debug("Sending data to EDSM: " + client.BuildUri(request).AbsoluteUri);
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    StarMapLogResponse response = clientResponse.Data;
                    Logging.Debug("Data sent to EDSM");
                    if (response.Msgnum != 100)
                    {
                        Logging.Warn("EDSM responded with " + response.Msg);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send data to EDSM", ex);
                }
            });
        }

        public void SendStarMapComment(string systemName, string comment)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-logs-v1/set-comment", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("systemName", systemName);
            request.AddParameter("comment", comment);

            Task.Run(() =>
            {
                try
                {
                    var clientResponse = client.Execute<StarMapLogResponse>(request);
                    StarMapLogResponse response = clientResponse.Data;
                    if (response.Msgnum != 100)
                    {
                        Logging.Warn("EDSM responded with " + response.Msg);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
                catch (Exception ex)
                {
                    Logging.Warn("Failed to send comment to EDSM", ex);
                }
            });
        }

        public string GetStarMapComment(string systemName)
        {
            var client = new RestClient(_baseUrl);
            var commentRequest = new RestRequest("api-logs-v1/get-comment", Method.POST);
            commentRequest.AddParameter("apiKey", _apiKey);
            commentRequest.AddParameter("commanderName", _commanderName);
            commentRequest.AddParameter("systemName", systemName);
            var commentClientResponse = client.Execute<StarMapLogResponse>(commentRequest);
            StarMapLogResponse commentResponse = commentClientResponse.Data;
            return commentResponse?.Comment;
        }

        public StarMapInfo GetStarMapInfo(string systemName)
        {
            var client = new RestClient(_baseUrl);

            // First fetch the data itself
            var logRequest = new RestRequest("api-logs-v1/get-logs", Method.POST);
            logRequest.AddParameter("apiKey", _apiKey);
            logRequest.AddParameter("commanderName", _commanderName);
            logRequest.AddParameter("systemName", systemName);
            var logClientResponse = client.Execute<StarMapLogResponse>(logRequest);
            StarMapLogResponse logResponse = logClientResponse.Data;
            if (logResponse.Msgnum != 100)
            {
                Logging.Warn("EDSM responded with " + logResponse.Msg);
            }

            // Also grab any comment that might be present
            var commentRequest = new RestRequest("api-logs-v1/get-comment", Method.POST);
            commentRequest.AddParameter("apiKey", _apiKey);
            commentRequest.AddParameter("commanderName", _commanderName);
            commentRequest.AddParameter("systemName", systemName);
            var commentClientResponse = client.Execute<StarMapLogResponse>(commentRequest);
            StarMapLogResponse commentResponse = commentClientResponse.Data;
            if (commentResponse.Msgnum != 100)
            {
                Logging.Warn("EDSM responded with " + commentResponse.Msg);
            }

            int visits = logResponse.Logs?.Count ?? 1;
            DateTime lastUpdate = logResponse.LastUpdate ?? new DateTime();
            string comment = commentResponse.Comment;

            return new StarMapInfo(visits, lastUpdate, comment);
        }


        public void SendStarMapDistance(string systemName, string remoteSystemName, decimal distance)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest(Method.POST);
            request.Resource = "api-v1/submit-distances";

            StarMapData data = new StarMapData(_commanderName, systemName, remoteSystemName, distance);
            StarMapSubmission submission = new StarMapSubmission(data);

            request.JsonSerializer = NewtonsoftJsonSerializer.Default;
            request.RequestFormat = DataFormat.Json;
            request.AddBody(submission);

            var clientResponse = client.Execute<StarMapDistanceResponse>(request);
            StarMapDistanceResponse response = clientResponse.Data;
        }

        public Dictionary<string, string> GetStarMapComments()
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-logs-v1/get-comments", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            var starMapCommentResponse = client.Execute<StarMapCommentResponse>(request);
            StarMapCommentResponse response = starMapCommentResponse.Data;

            Dictionary<string, string> vals = new Dictionary<string, string>();
            if (response?.Comments != null)
            {
                foreach (StarMapResponseCommentEntry entry in response.Comments)
                {
                    if (!string.IsNullOrEmpty(entry.Comment))
                    {
                        Logging.Debug("Comment found for " + entry.System);
                        vals[entry.System] = entry.Comment;
                    }
                }
            }
            return vals;
        }

        public Dictionary<string, StarMapLogInfo> GetStarMapLog(DateTime? since = null)
        {
            var client = new RestClient(_baseUrl);
            var request = new RestRequest("api-logs-v1/get-logs", Method.POST);
            request.AddParameter("apiKey", _apiKey);
            request.AddParameter("commanderName", _commanderName);
            request.AddParameter("fullSync", 1);
            if (since.HasValue)
            {
                request.AddParameter("startdatetime", since.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            var starMapLogResponse = client.Execute<StarMapLogResponse>(request);
            StarMapLogResponse response = starMapLogResponse.Data;

            Logging.Debug("Response for star map logs is " + JsonConvert.SerializeObject(response));
            if (response.Msgnum != 100)
            {
                // An error occurred
                throw new EDSMException(response.Msg);
            }

            Dictionary<string, StarMapLogInfo> vals = new Dictionary<string, StarMapLogInfo>();
            if (response != null && response.Logs != null)
            {
                foreach (StarMapResponseLogEntry entry in response.Logs)
                {
                    if (vals.ContainsKey(entry.System))
                    {
                        vals[entry.System].Visits = vals[entry.System].Visits + 1;
                        if (entry.Date > vals[entry.System].LastVisit)
                        {
                            vals[entry.System].PreviousVisit = vals[entry.System].LastVisit;
                            vals[entry.System].LastVisit = entry.Date;
                        }
                        else if (vals[entry.System].PreviousVisit == null || entry.Date > vals[entry.System].PreviousVisit)
                        {
                            vals[entry.System].PreviousVisit = entry.Date;
                        }
                    }
                    else
                    {
                        vals[entry.System] = new StarMapLogInfo
                        {
                            System = entry.System,
                            Visits = 1,
                            LastVisit = entry.Date
                        };
                    }
                }
            }
            return vals;
        }

        public void Sync(DateTime? since = null)
        {
            Logging.Info("Syncing with EDSM");
            try
            {
                Dictionary<string, StarMapLogInfo> systems = GetStarMapLog(since);
                Dictionary<string, string> comments = GetStarMapComments();
                int total = systems.Count;
                foreach (string system in systems.Keys)
                {
                    StarSystem currentStarSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(system, false);
                    currentStarSystem.visits = systems[system].Visits;
                    currentStarSystem.lastvisit = systems[system].LastVisit;
                    if (comments.ContainsKey(system))
                    {
                        currentStarSystem.comment = comments[system];
                    }
                    StarSystemSqLiteRepository.Instance.SaveStarSystem(currentStarSystem);
                }
                StarMapConfiguration starMapConfiguration = StarMapConfiguration.FromFile();
                starMapConfiguration.LastSync = DateTime.UtcNow;
                starMapConfiguration.ToFile();
                Logging.Info("EDSM sync completed");
            }
            catch (EDSMException edsme)
            {
                Logging.Debug("EDSM error received: " + edsme.Message);
            }
        }
    }

    // response from the Star Map distance API
    public class StarMapDistanceResponse
    {

    }

    // response from the Star Map log API
    public class StarMapLogResponse
    {
        public int Msgnum { get; set; }
        public string Msg { get; set; }
        public string Comment { get; set; }
        public DateTime? LastUpdate { get; set; }
        public List<StarMapResponseLogEntry> Logs { get; set; }
    }

    public class StarMapLogInfo
    {
        public string System { get; set; }
        public int Visits { get; set; }
        public DateTime LastVisit { get; set; }
        public DateTime? PreviousVisit { get; set; }
    }

    public class StarMapResponseLogEntry
    {
        public string System { get; set; }
        public DateTime Date { get; set; }
    }

    // response from the Star Map comment API
    public class StarMapCommentResponse
    {
        public int Msgnum { get; set; }
        public string Msg { get; set; }
        public string Comment { get; set; }
        public DateTime? LastUpdate { get; set; }
        public List<StarMapResponseCommentEntry> Comments { get; set; }
    }

    public class StarMapResponseCommentEntry
    {
        public string System { get; set; }
        public string Comment { get; set; }
    }

    // public consolidated version of star map log information
    public class StarMapInfo
    {
        public int Visits { get; set; }
        public DateTime? LastVisited { get; set; }
        public string Comment { get; set; }

        public StarMapInfo(int visits, DateTime? lastVisited, string comment)
        {
            Visits = visits;
            LastVisited = lastVisited;
            Comment = comment;
        }
    }

    public class StarMapSubmission
    {
        public StarMapData Data { get; set; }

        public StarMapSubmission(StarMapData data)
        {
            Data = data;
        }
    }

    public class StarMapDistance
    {
        [JsonProperty("name")]
        public string SystemName { get; set; }
        [JsonProperty("dist")]
        public decimal? Distance { get; set; }

        public StarMapDistance(string systemName)
        {
            SystemName = systemName;
        }

        public StarMapDistance(string systemName, decimal distance)
        {
            SystemName = systemName;
            Distance = distance;
        }
    }

    public class StarMapData
    {
        public string Commander { get; set; }
        public string FromSoftware { get; set; }
        public string FromSoftwareVersion { get; set; }
        public StarMapDistance P0 { get; set; }
        public List<StarMapDistance> Refs { get; set; }

        public StarMapData(string commanderName, string systemName, string remoteSystemName, decimal distance)
        {
            Commander = commanderName;
            FromSoftware = Constants.EDDI_NAME;
            FromSoftwareVersion = Constants.EDDI_VERSION;
            P0 = new StarMapDistance(systemName);
            Refs = new List<StarMapDistance> {new StarMapDistance(remoteSystemName, distance)};
        }
    }

    // Custom serializer for REST requests
    public class NewtonsoftJsonSerializer : ISerializer
    {
        private readonly Newtonsoft.Json.JsonSerializer _serializer;

        public NewtonsoftJsonSerializer(Newtonsoft.Json.JsonSerializer serializer)
        {
            _serializer = serializer;
        }

        public string ContentType
        {
            get { return "application/json"; } // Probably used for Serialization?
            set { }
        }

        public string DateFormat { get; set; }

        public string Namespace { get; set; }

        public string RootElement { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")] // this usage is perfectly correct
        public string Serialize(object obj)
        {
            using (var stringWriter = new StringWriter())
            using (var jsonTextWriter = new JsonTextWriter(stringWriter))
            {
                _serializer.Serialize(jsonTextWriter, obj);
                return stringWriter.ToString();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")] // this usage is perfectly correct
        public T Deserialize<T>(IRestResponse response)
        {
            var content = response.Content;

            using (var stringReader = new StringReader(content))
            using (var jsonTextReader = new JsonTextReader(stringReader))
            {
                return _serializer.Deserialize<T>(jsonTextReader);
            }
        }

        public static NewtonsoftJsonSerializer Default
        {
            get
            {
                return new NewtonsoftJsonSerializer(new Newtonsoft.Json.JsonSerializer()
                {
                    NullValueHandling = NullValueHandling.Ignore,
                });
            }
        }
    }
}

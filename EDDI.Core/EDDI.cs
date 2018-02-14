using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using EddiCompanionAppService;
using EddiDataDefinitions;
using EddiDataProviderService;
using EddiEvents;
using EddiSpeechService;
using EddiStarMapService;
using Exceptionless;
using Newtonsoft.Json;
using Utilities;
using ThreadState = System.Threading.ThreadState;

namespace EDDI.Core
{
    /// <summary>
    ///     Eddi is the controller for all EDDI operations.  Its job is to retain the state of the objects such as the
    ///     commander, the current system, etc.
    ///     and keep them up-to-date with changes that occur.  It also passes on messages to responders to handle as required.
    /// </summary>
    public class Eddi
    {
        private static Eddi _instance;

        // True if we have been started by VoiceAttack
        public static bool FromVa = false;

        private static bool _started;

        private static bool _running = true;

        private static readonly object InstanceLock = new object();

        /// <summary>Work out the title for the commander in the current system</summary>
        private static readonly int _minEmpireRankForTitle = 3;

        private static readonly int _minFederationRankForTitle = 1;

        // Each monitor runs in its own thread
        private readonly List<Thread> _monitorThreads = new List<Thread>();
        private readonly List<IEDDIResponder> _activeResponders = new List<IEDDIResponder>();

        /// <summary>
        ///     Special case - trigger our first location event regardless of if it matches our current location
        /// </summary>
        private bool _firstLocation = true;

        private string _profileStationRequired;

        private bool _profileUpdateNeeded;

        public List<IEDDIMonitor> Monitors = new List<IEDDIMonitor>();
        public string Motd;
        public List<string> ProductionBuilds = new List<string> { "r131487/r0" };

        public List<IEDDIResponder> Responders = new List<IEDDIResponder>();

        // Session state
        public ObservableConcurrentDictionary<string, object> State =
            new ObservableConcurrentDictionary<string, object>();

        // Upgrade information
        public bool UpgradeAvailable;
        public string UpgradeLocation;
        public bool UpgradeRequired;
        public string UpgradeVersion;

        static Eddi()
        {
            // Set up our app directory
            Directory.CreateDirectory(Constants.DATA_DIR);

            // Use invariant culture to ensure that we use . rather than , for our separator when writing out decimals
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        }

        private Eddi()
        {
            //Mutex appMutex = new Mutex(true, $"{Constants.EDDI_NAME}_{Constants.EDDI_VERSION}");
            try
            {
                Logging.Info($"{Constants.EDDI_NAME} {Constants.EDDI_VERSION} starting");

                var configuration = EDDIConfiguration.FromFile();
                // Exception handling
                if (configuration.ErrorReporting)
                {
                    ExceptionlessClient.Default.Startup("vJW9HtWB2NHiQb7AwVQsBQM6hjWN1sKzHf5PCpW1");
                    ExceptionlessClient.Default.Configuration.SetVersion(Constants.EDDI_VERSION);
                }

                // Start by fetching information from the update server, and handling appropriately
                CheckUpgrade();
                if (UpgradeRequired)
                {
                    // We are too old to continue; don't
                    _running = false;
                    return;
                }

                // Ensure that our primary data structures have something in them.  This allows them to be updated from any source
                Cmdr = new Commander();

                // Set up the Elite configuration
                var eliteConfiguration = EliteConfiguration.FromFile();
                InBeta = eliteConfiguration.Beta;
                Logging.Info(InBeta ? "On beta" : "On live");

                // Set up the EDDI configuration
                UpdateHomeSystemStation(configuration);

                // Set up monitors and responders
                Monitors = FindMonitors();
                Responders = FindResponders();

                // Set up the app service
                if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.READY)
                    try
                    {
                        RefreshProfile();
                    }
                    catch (Exception ex)
                    {
                        Logging.Debug("Failed to obtain profile: " + ex);
                    }

                Cmdr.insurance = configuration.Insurance;
                Cmdr.gender = configuration.Gender;
                if (Cmdr.name != null)
                    Logging.Info("EDDI access to the companion app is enabled");
                else
                    Logging.Info("EDDI access to the companion app is disabled");

                // Set up the star map service
                var starMapCredentials = StarMapConfiguration.FromFile();
                if (starMapCredentials?.ApiKey != null)
                {
                    // Commander name might come from star map credentials or the companion app's profile
                    string commanderName = null;
                    if (starMapCredentials.CommanderName != null)
                        commanderName = starMapCredentials.CommanderName;
                    else if (Cmdr?.name != null) commanderName = Cmdr.name;
                    if (commanderName != null)
                    {
                        StarMapService = new StarMapService(starMapCredentials.ApiKey, commanderName);
                        Logging.Info("EDDI access to EDSM is enabled");
                    }

                    // Spin off a thread to download & sync EDSM flight logs & system comments in the background
                    var updateThread =
                        new Thread(() => StarMapService.Sync(starMapCredentials.LastSync)) { IsBackground = true };
                    updateThread.Start();
                }

                if (StarMapService == null) Logging.Info("EDDI access to EDSM is disabled");

                // We always start in normal space
                Environment = Constants.ENVIRONMENT_NORMAL_SPACE;

                Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " initialised");
            }
            catch (Exception ex)
            {
                Logging.Error("Failed to initialise", ex);
            }
        }

        public bool InCqc { get; private set; }

        public bool InCrew { get; private set; }

        public bool InBeta { get; private set; }

        public static Eddi Instance
        {
            get
            {
                if (_instance == null)
                    lock (InstanceLock)
                    {
                        if (_instance != null) return _instance;
                        Logging.Debug("No EDDI instance: creating one");
                        _instance = new Eddi();
                    }

                return _instance;
            }
        }

        // Information obtained from the companion app service
        public Commander Cmdr { get; private set; }

        public DateTime ApiTimeStamp { get; private set; }

        //public ObservableCollection<Ship> Shipyard { get; private set; } = new ObservableCollection<Ship>();
        public Station CurrentStation { get; private set; }

        // Services made available from EDDI
        public StarMapService StarMapService { get; }

        // Information obtained from the configuration
        public StarSystem HomeStarSystem { get; private set; }
        public Station HomeStation { get; private set; }

        // Information obtained from the log watcher
        public string Environment { get; private set; }
        public StarSystem CurrentStarSystem { get; private set; }
        public StarSystem LastStarSystem { get; private set; }

        // Information obtained from the player journal
        public DateTime JournalTimeStamp { get; set; } = DateTime.MinValue;

        // Current vehicle of player
        public string Vehicle { get; private set; } = Constants.VEHICLE_SHIP;
        public Ship CurrentShip { get; set; }

        /// <summary>
        ///     Check to see if an upgrade is available and populate relevant variables
        /// </summary>
        public void CheckUpgrade()
        {
            // Clear the old values
            UpgradeRequired = false;
            UpgradeAvailable = false;
            UpgradeLocation = null;
            UpgradeVersion = null;
            Motd = null;

            try
            {
                var updateServerInfo = ServerInfo.FromServer(Constants.EDDI_SERVER_URL);
                if (updateServerInfo == null)
                {
                    Logging.Warn("Failed to contact update server");
                }
                else
                {
                    var configuration = EDDIConfiguration.FromFile();
                    var info = configuration.Beta ? updateServerInfo.beta : updateServerInfo.production;
                    Motd = info.motd;
                    if (updateServerInfo.productionbuilds != null) ProductionBuilds = updateServerInfo.productionbuilds;

                    if (Versioning.Compare(info.minversion, Constants.EDDI_VERSION) == 1)
                    {
                        // There is a mandatory update available
                        if (!FromVa)
                            SpeechService.Instance.Say(null,
                                "Mandatory Eddi upgrade to " + info.version.Replace(".", " point ") + " is required.",
                                false);
                        UpgradeRequired = true;
                        UpgradeLocation = info.url;
                        UpgradeVersion = info.version;
                        return;
                    }

                    if (Versioning.Compare(info.version, Constants.EDDI_VERSION) == 1)
                    {
                        // There is an update available
                        if (!FromVa)
                            SpeechService.Instance.Say(null,
                                "Eddi version " + info.version.Replace(".", " point ") + " is now available.", false);
                        UpgradeAvailable = true;
                        UpgradeLocation = info.url;
                        UpgradeVersion = info.version;
                    }
                }
            }
            catch (Exception ex)
            {
                SpeechService.Instance.Say(null,
                    "There was a problem connecting to external data services; some features may be temporarily unavailable",
                    false);
                Logging.Warn("Failed to access api.eddp.co", ex);
            }
        }

        /// <summary>
        ///     Obtain and check the information from the update server
        /// </summary>
        private bool UpdateServer()
        {
            try
            {
                var updateServerInfo = ServerInfo.FromServer(Constants.EDDI_SERVER_URL);
                if (updateServerInfo == null)
                {
                    Logging.Warn("Failed to contact update server");
                    return false;
                }

                var info = Constants.EDDI_VERSION.Contains("b") ? updateServerInfo.beta : updateServerInfo.production;
                if (Versioning.Compare(info.minversion, Constants.EDDI_VERSION) == 1)
                {
                    Logging.Warn("This version of Eddi is too old to operate; please upgrade at " + info.url);
                    SpeechService.Instance.Say(null, "This version of Eddi is too old to operate; please upgrade.",
                        true);
                    UpgradeRequired = true;
                    UpgradeLocation = info.url;
                    UpgradeVersion = info.version;
                }

                if (Versioning.Compare(info.version, Constants.EDDI_VERSION) == 1)
                {
                    // There is an update available
                    SpeechService.Instance.Say(null,
                        "EDDI version " + info.version.Replace(".", " point ") + " is now available.", true);
                    UpgradeAvailable = true;
                    UpgradeLocation = info.url;
                    UpgradeVersion = info.version;
                }

                if (info.motd != null) SpeechService.Instance.Say(null, info.motd, false);
            }
            catch (Exception ex)
            {
                SpeechService.Instance.Say(null,
                    "There was a problem connecting to external data services; some features may be temporarily unavailable",
                    false);
                Logging.Warn("Failed to access " + Constants.EDDI_SERVER_URL, ex);
            }

            return true;
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public void Upgrade()
        {
            try
            {
                if (UpgradeLocation != null)
                {
                    Logging.Info("Downloading upgrade from " + UpgradeLocation);
                    SpeechService.Instance.Say(null, "Downloading upgrade.", true);
                    var updateFile = Net.DownloadFile(UpgradeLocation, @"EDDI-update.exe");
                    if (updateFile == null)
                    {
                        SpeechService.Instance.Say(null, "Download failed.  Please try again later.", true);
                    }
                    else
                    {
                        // Inno setup will attempt to restart this application so register it
                        NativeMethods.RegisterApplicationRestart(null, RestartFlags.None);

                        Logging.Info("Downloaded update to " + updateFile);
                        Logging.Info("Path is " + Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                        File.SetAttributes(updateFile, FileAttributes.Normal);
                        SpeechService.Instance.Say(null, "Starting upgrade.", true);
                        Logging.Info("Starting upgrade.");

                        Process.Start(updateFile,
                            @"/closeapplications /restartapplications /silent /log /nocancel /noicon /dir=""" +
                            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + @"""");
                    }
                }
            }
            catch (Exception ex)
            {
                SpeechService.Instance.Say(null, "Upgrade failed.  Please try again later.", true);
                Logging.Error("Upgrade failed", ex);
            }
        }

        public void Start()
        {
            if (!_started)
            {
                var configuration = EDDIConfiguration.FromFile();

                var activeMonitors = Monitors.Where(x =>
                    configuration.Plugins.TryGetValue(x.MonitorName, out var enabled) && enabled);

                foreach (var monitor in activeMonitors)
                {
                    if (!monitor.NeedsStart) continue;
                    var monitorThread = new Thread(() => KeepAlive(monitor.MonitorName, monitor.Start))
                    {
                        IsBackground = true,
                        Name = monitor.MonitorName
                    };
                    Logging.Info("Starting keepalive for " + monitor.MonitorName);
                    monitorThread.Start();
                    _monitorThreads.Add(monitorThread);
                }

                foreach (var responder in Responders)
                {
                    if (!configuration.Plugins.TryGetValue(responder.ResponderName, out var enabled)) enabled = true;

                    if (!enabled)
                    {
                        Logging.Debug(responder.ResponderName + " is disabled; not starting");
                    }
                    else
                    {
                        var responderStarted = responder.Start();
                        if (responderStarted)
                        {
                            _activeResponders.Add(responder);
                            Logging.Info("Started " + responder.ResponderName);
                        }
                        else
                        {
                            Logging.Warn("Failed to start " + responder.ResponderName);
                        }
                    }
                }

                EDDIConfiguration.OnConfigurationChange.Subscribe(x => Refresh());

                _started = true;
            }
        }

        private void Refresh()
        {
            Monitors.ForEach(x => x.Stop());
            Monitors.ForEach(x => x.Reload());
            Responders.ForEach(x => x.Stop());
            Responders.ForEach(x => x.Reload());
            _monitorThreads.Clear();
            _activeResponders.Clear();

            var configuration = EDDIConfiguration.FromFile();

            var activeMonitors = Monitors.Where(x =>
                configuration.Plugins.TryGetValue(x.MonitorName, out var enabled) && enabled);
            foreach (var monitor in activeMonitors)
            {
                if (!monitor.NeedsStart) continue;
                var monitorThread = new Thread(() => KeepAlive(monitor.MonitorName, monitor.Start))
                {
                    IsBackground = true,
                    Name = monitor.MonitorName
                };
                Logging.Info("Starting keepalive for " + monitor.MonitorName);
                monitorThread.Start();
                _monitorThreads.Add(monitorThread);
            }
            var activeResponders = Responders.Where(x =>
                configuration.Plugins.TryGetValue(x.ResponderName, out var enabled) && enabled).ToList();
            activeResponders.ForEach(x =>
            {
                if (x.Start())
                {
                    _activeResponders.Add(x);
                    Logging.Info("Started " + x.ResponderName);
                }
                else
                    Logging.Warn("Failed to start " + x.ResponderName);
            });
        }

        public void Stop()
        {
            _running = false; // Otherwise keepalive restarts them
            if (_started)
            {
                foreach (var responder in Responders)
                {
                    responder.Stop();
                    _activeResponders.Remove(responder);
                }

                foreach (var monitor in Monitors) monitor.Stop();
                _monitorThreads.ForEach(x =>
                {
                    if (x.ThreadState == ThreadState.Running) x.Abort();
                });
            }

            Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " stopped");

            ExceptionlessClient.Default.Shutdown();

            _started = false;
        }

        /// <summary>
        ///     Reload all monitors and responders
        /// </summary>
        public void Reload()
        {
            foreach (var responder in Responders) responder.Reload();
            foreach (var monitor in Monitors) monitor.Reload();

            Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " reloaded");
        }

        /// <summary>
        ///     Obtain a named monitor
        /// </summary>
        public IEDDIMonitor ObtainMonitor(string name)
        {
            foreach (var monitor in Monitors)
                if (monitor.MonitorName == name)
                    return monitor;
            return null;
        }

        /// <summary>
        ///     Obtain a named responder
        /// </summary>
        public IEDDIResponder ObtainResponder(string name)
        {
            foreach (var responder in Responders)
                if (responder.ResponderName == name)
                    return responder;
            return null;
        }

        /// <summary>
        ///     Disable a named responder for this session.  This does not update the on-disk status of the responder
        /// </summary>
        public void DisableResponder(string name)
        {
            var responder = ObtainResponder(name);
            if (responder == null) return;
            responder.Stop();
            _activeResponders.Remove(responder);
        }

        /// <summary>
        ///     Enable a named responder for this session.  This does not update the on-disk status of the responder
        /// </summary>
        public void EnableResponder(string name)
        {
            var responder = ObtainResponder(name);
            if (responder == null) return;
            if (_activeResponders.Contains(responder)) return;
            responder.Start();
            _activeResponders.Add(responder);
        }

        /// <summary>
        ///     Reload a specific monitor or responder
        /// </summary>
        public void Reload(string name)
        {
            foreach (var responder in Responders)
                if (responder.ResponderName == name)
                {
                    responder.Reload();
                    return;
                }

            foreach (var monitor in Monitors)
                if (monitor.MonitorName == name)
                    monitor.Reload();

            Logging.Info(Constants.EDDI_NAME + " " + Constants.EDDI_VERSION + " stopped");
        }

        /// <summary>
        ///     Keep a thread alive, restarting it as required
        /// </summary>
        private void KeepAlive(string name, Action start)
        {
            try
            {
                var failureCount = 0;
                while (_running && failureCount < 5)
                {
                    try
                    {
                        var monitorThread = new Thread(() => start())
                        {
                            Name = name,
                            IsBackground = true
                        };
                        Logging.Info("Starting " + name + " (" + failureCount + ")");
                        monitorThread.Start();
                        monitorThread.Join();
                    }
                    catch (ThreadAbortException tax)
                    {
                        Thread.ResetAbort();
                        if (_running) Logging.Error("Restarting " + name + " after thread abort", tax);
                    }
                    catch (Exception ex)
                    {
                        if (_running) Logging.Error("Restarting " + name + " after exception", ex);
                    }

                    failureCount++;
                }

                if (_running) Logging.Warn(name + " stopping after too many failures");
            }
            catch (ThreadAbortException)
            {
                Logging.Debug("Thread aborted");
            }
            catch (Exception ex)
            {
                Logging.Warn("KeepAlive failed", ex);
            }
        }

        public void EventHandler(Event journalEvent)
        {
            if (journalEvent != null)
                try
                {
                    Logging.Debug("Handling event " + JsonConvert.SerializeObject(journalEvent));
                    // We have some additional processing to do for a number of events
                    var passEvent = true;
                    switch (journalEvent)
                    {
                        case FileHeaderEvent _:
                            passEvent = EventFileHeader((FileHeaderEvent)journalEvent);
                            break;
                        case JumpedEvent _:
                            passEvent = EventJumped((JumpedEvent)journalEvent);
                            break;
                        case DockedEvent _:
                            passEvent = EventDocked((DockedEvent)journalEvent);
                            break;
                        case UndockedEvent _:
                            passEvent = EventUndocked((UndockedEvent)journalEvent);
                            break;
                        case LocationEvent _:
                            passEvent = EventLocation((LocationEvent)journalEvent);
                            break;
                        case FSDEngagedEvent _:
                            passEvent = EventFsdEngaged((FSDEngagedEvent)journalEvent);
                            break;
                        case EnteredSupercruiseEvent _:
                            passEvent = EventEnteredSupercruise((EnteredSupercruiseEvent)journalEvent);
                            break;
                        case EnteredNormalSpaceEvent _:
                            passEvent = EventEnteredNormalSpace((EnteredNormalSpaceEvent)journalEvent);
                            break;
                        case CommanderContinuedEvent _:
                            passEvent = EventCommanderContinued((CommanderContinuedEvent)journalEvent);
                            break;
                        case CommanderRatingsEvent _:
                            passEvent = EventCommanderRatings((CommanderRatingsEvent)journalEvent);
                            break;
                        case CombatPromotionEvent _:
                            passEvent = EventCombatPromotion((CombatPromotionEvent)journalEvent);
                            break;
                        case TradePromotionEvent _:
                            passEvent = EventTradePromotion((TradePromotionEvent)journalEvent);
                            break;
                        case ExplorationPromotionEvent _:
                            passEvent = EventExplorationPromotion((ExplorationPromotionEvent)journalEvent);
                            break;
                        case FederationPromotionEvent _:
                            passEvent = EventFederationPromotion((FederationPromotionEvent)journalEvent);
                            break;
                        case EmpirePromotionEvent _:
                            passEvent = EventEmpirePromotion((EmpirePromotionEvent)journalEvent);
                            break;
                        case CrewJoinedEvent _:
                            passEvent = EventCrewJoined((CrewJoinedEvent)journalEvent);
                            break;
                        case CrewLeftEvent _:
                            passEvent = EventCrewLeft((CrewLeftEvent)journalEvent);
                            break;
                        case EnteredCQCEvent _:
                            passEvent = EventEnteredCqc((EnteredCQCEvent)journalEvent);
                            break;
                        case SRVLaunchedEvent _:
                            passEvent = EventSrvLaunched((SRVLaunchedEvent)journalEvent);
                            break;
                        case SRVDockedEvent _:
                            passEvent = EventSrvDocked((SRVDockedEvent)journalEvent);
                            break;
                        case FighterLaunchedEvent _:
                            passEvent = EventFighterLaunched((FighterLaunchedEvent)journalEvent);
                            break;
                        case FighterDockedEvent _:
                            passEvent = EventFighterDocked((FighterDockedEvent)journalEvent);
                            break;
                        case BeltScannedEvent _:
                            passEvent = EventBeltScanned((BeltScannedEvent)journalEvent);
                            break;
                        case StarScannedEvent _:
                            passEvent = EventStarScanned((StarScannedEvent)journalEvent);
                            break;
                        case BodyScannedEvent _:
                            passEvent = EventBodyScanned((BodyScannedEvent)journalEvent);
                            break;
                        case VehicleDestroyedEvent _:
                            passEvent = EventVehicleDestroyed((VehicleDestroyedEvent)journalEvent);
                            break;
                    }
                    // Additional processing is over, send to the event responders if required
                    if (passEvent) OnEvent(journalEvent);
                }
                catch (Exception ex)
                {
                    Logging.Error("Failed to handle event " + JsonConvert.SerializeObject(journalEvent), ex);
                }
        }

        private void OnEvent(Event ev)
        {
            // We send the event to all monitors to ensure that their info is up-to-date
            // This is synchronous
            foreach (var monitor in Monitors)
                try
                {
                    monitor.PreHandle(ev);
                }
                catch (Exception ex)
                {
                    Logging.Error(JsonConvert.SerializeObject(ev), ex);
                }

            // Now we pass the data to the responders
            // This is asynchronous
            foreach (var responder in _activeResponders)
                try
                {
                    var responderThread = new Thread(() =>
                    {
                        try
                        {
                            responder.Handle(ev);
                        }
                        catch (Exception ex)
                        {
                            Logging.Warn("Responder failed", ex);
                        }
                    })
                    {
                        Name = responder.ResponderName,
                        IsBackground = true
                    };
                    responderThread.Start();
                }
                catch (ThreadAbortException tax)
                {
                    Thread.ResetAbort();
                    Logging.Error(JsonConvert.SerializeObject(ev), tax);
                }
                catch (Exception ex)
                {
                    Logging.Error(JsonConvert.SerializeObject(ev), ex);
                }

            // We also pass the event to all monitors in case they have follow-on work
            foreach (var monitor in Monitors)
                try
                {
                    var monitorThread = new Thread(() =>
                    {
                        try
                        {
                            monitor.PostHandle(ev);
                        }
                        catch (Exception ex)
                        {
                            Logging.Warn("Monitor failed", ex);
                        }
                    })
                    {
                        IsBackground = true
                    };
                    monitorThread.Start();
                }
                catch (ThreadAbortException tax)
                {
                    Thread.ResetAbort();
                    Logging.Error(JsonConvert.SerializeObject(ev), tax);
                }
                catch (Exception ex)
                {
                    Logging.Error(JsonConvert.SerializeObject(ev), ex);
                }
        }

        private bool EventLocation(LocationEvent theEvent)
        {
            UpdateCurrentSystem(theEvent.system);
            // Always update the current system with the current co-ordinates, just in case things have changed
            CurrentStarSystem.x = theEvent.x;
            CurrentStarSystem.y = theEvent.y;
            CurrentStarSystem.z = theEvent.z;
            SetSystemDistanceFromHome(CurrentStarSystem);

            // Update the system population from the journal
            if (theEvent.population != null) CurrentStarSystem.population = theEvent.population;

            if (theEvent.docked)
            {
                // In this case body === station

                // Force first location update even if it matches with 'firstLocation' bool
                if (!_firstLocation && CurrentStation != null && CurrentStation.name == theEvent.body)
                {
                    // We are already at this station; nothing to do
                    Logging.Debug("Already at station " + theEvent.body);
                    return false;
                }

                _firstLocation = false;

                // Update the station
                Logging.Debug("Now at station " + theEvent.body);
                var station = CurrentStarSystem.stations.Find(s => s.name == theEvent.body) ?? new Station
                {
                    name = theEvent.body,
                    systemname = theEvent.system
                };

                // Information from the event might be more current than that from EDDB so use it in preference
                station.faction = theEvent.faction;
                station.government = theEvent.government;
                station.allegiance = theEvent.allegiance;

                CurrentStation = station;

                // Kick off the profile refresh if the companion API is available
                if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.READY)
                {
                    // Refresh station data
                    _profileUpdateNeeded = true;
                    _profileStationRequired = CurrentStation.name;
                    var updateThread = new Thread(ConditionallyRefreshProfile) { IsBackground = true };
                    updateThread.Start();
                }
            }
            else
            {
                // If we are not docked then our station information is invalid
                CurrentStation = null;
            }

            return true;
        }

        private bool EventDocked(DockedEvent theEvent)
        {
            UpdateCurrentSystem(theEvent.system);

            if (CurrentStation != null && CurrentStation.name == theEvent.station)
            {
                // We are already at this station; nothing to do
                Logging.Debug("Already at station " + theEvent.station);
                return false;
            }

            // We are in the ship
            Vehicle = Constants.VEHICLE_SHIP;

            // Update the station
            Logging.Debug("Now at station " + theEvent.station);
            var station = CurrentStarSystem.stations.Find(s => s.name == theEvent.station) ?? new Station
            {
                name = theEvent.station,
                systemname = theEvent.system
            };

            // Information from the event might be more current than that from EDDB so use it in preference
            station.state = theEvent.factionstate;
            station.faction = theEvent.faction;
            station.government = theEvent.government;

            if (theEvent.stationservices != null)
                foreach (var service in theEvent.stationservices)
                    if (service == "Refuel")
                        station.hasrefuel = true;
                    else if (service == "Rearm")
                        station.hasrearm = true;
                    else if (service == "Repair")
                        station.hasrepair = true;
                    else if (service == "Outfitting")
                        station.hasoutfitting = true;
                    else if (service == "Shipyard")
                        station.hasshipyard = true;
                    else if (service == "Commodities")
                        station.hasmarket = true;
                    else if (service == "BlackMarket") station.hasblackmarket = true;

            CurrentStation = station;

            // Kick off the profile refresh if the companion API is available
            if (CompanionAppService.Instance.CurrentState == CompanionAppService.State.READY)
            {
                // Refresh station data
                _profileUpdateNeeded = true;
                _profileStationRequired = CurrentStation.name;
                var updateThread = new Thread(ConditionallyRefreshProfile) { IsBackground = true };
                updateThread.Start();
            }
            else
            {
                // Kick off a dummy that triggers a market refresh after a couple of seconds
                var updateThread = new Thread(DummyRefreshMarketData) { IsBackground = true };
                updateThread.Start();
            }

            return true;
        }

        private bool EventUndocked(UndockedEvent theEvent)
        {
            // Call refreshProfile() to ensure that our ship is up-to-date
            RefreshProfile();

            // Remove information about the station
            CurrentStation = null;

            return true;
        }

        private void UpdateCurrentSystem(string name)
        {
            if (name == null) return;
            if (CurrentStarSystem == null || CurrentStarSystem.name != name)
            {
                if (CurrentStarSystem != null && CurrentStarSystem.name != name)
                    StarSystemSqLiteRepository.Instance.LeaveStarSystem(CurrentStarSystem);
                LastStarSystem = CurrentStarSystem;
                CurrentStarSystem = StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(name);
                SetSystemDistanceFromHome(CurrentStarSystem);
            }
        }

        private bool EventFsdEngaged(FSDEngagedEvent @event)
        {
            // Keep track of our environment
            if (@event.target == "Supercruise")
                Environment = Constants.ENVIRONMENT_SUPERCRUISE;
            else
                Environment = Constants.ENVIRONMENT_WITCH_SPACE;

            // We are in the ship
            Vehicle = Constants.VEHICLE_SHIP;

            return true;
        }

        private bool EventFileHeader(FileHeaderEvent ev)
        {
            // Test whether we're in beta by checking the filename, version described by the header, 
            // and certain version / build combinations
            InBeta =
                ev.filename.Contains("Beta") ||
                ev.version.Contains("Beta") ||
                ev.version.Contains("2.2") &&
                (
                    ev.build.Contains("r121645/r0") ||
                    ev.build.Contains("r129516/r0")
                );
            Logging.Info(InBeta ? "On beta" : "On live");
            var config = EliteConfiguration.FromFile();
            config.Beta = InBeta;
            config.ToFile();

            return true;
        }

        private bool EventJumped(JumpedEvent theEvent)
        {
            bool passEvent;
            Logging.Debug("Jumped to " + theEvent.system);
            if (CurrentStarSystem == null || CurrentStarSystem.name != theEvent.system)
            {
                // New system
                passEvent = true;
                UpdateCurrentSystem(theEvent.system);
                // The information in the event is more up-to-date than the information we obtain from external sources, so update it here
                CurrentStarSystem.x = theEvent.x;
                CurrentStarSystem.y = theEvent.y;
                CurrentStarSystem.z = theEvent.z;
                SetSystemDistanceFromHome(CurrentStarSystem);
                CurrentStarSystem.allegiance = theEvent.allegiance;
                CurrentStarSystem.faction = theEvent.faction;
                CurrentStarSystem.primaryeconomy = theEvent.economy;
                CurrentStarSystem.government = theEvent.government;
                CurrentStarSystem.security = theEvent.security;
                CurrentStarSystem.updatedat = (long)theEvent.timestamp.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                if (theEvent.population != null) CurrentStarSystem.population = theEvent.population;

                CurrentStarSystem.visits++;
                // We don't update lastvisit because we do that when we leave
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                SetCommanderTitle();
            }
            else if (CurrentStarSystem.name == theEvent.system && Environment == Constants.ENVIRONMENT_SUPERCRUISE)
            {
                // Restatement of current system
                passEvent = false;
            }
            else if (CurrentStarSystem.name == theEvent.system && Environment == Constants.ENVIRONMENT_WITCH_SPACE)
            {
                passEvent = true;

                // Jumped event following a Jumping event, so most information is up-to-date but we should pass this anyway for
                // plugin triggers

                // The information in the event is more up-to-date than the information we obtain from external sources, so update it here
                CurrentStarSystem.allegiance = theEvent.allegiance;
                CurrentStarSystem.faction = theEvent.faction;
                CurrentStarSystem.primaryeconomy = theEvent.economy;
                CurrentStarSystem.government = theEvent.government;
                CurrentStarSystem.security = theEvent.security;
                CurrentStarSystem.updatedat = (long)theEvent.timestamp.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                SetCommanderTitle();
            }
            else
            {
                passEvent = true;
                UpdateCurrentSystem(theEvent.system);

                // The information in the event is more up-to-date than the information we obtain from external sources, so update it here
                CurrentStarSystem.x = theEvent.x;
                CurrentStarSystem.y = theEvent.y;
                CurrentStarSystem.z = theEvent.z;
                SetSystemDistanceFromHome(CurrentStarSystem);
                CurrentStarSystem.allegiance = theEvent.allegiance;
                CurrentStarSystem.faction = theEvent.faction;
                CurrentStarSystem.primaryeconomy = theEvent.economy;
                CurrentStarSystem.government = theEvent.government;
                CurrentStarSystem.security = theEvent.security;

                CurrentStarSystem.visits++;
                // We don't update lastvisit because we do that when we leave
                CurrentStarSystem.updatedat = (long)theEvent.timestamp.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                SetCommanderTitle();
            }

            // After jump has completed we are always in supercruise
            Environment = Constants.ENVIRONMENT_SUPERCRUISE;

            return passEvent;
        }

        private bool EventEnteredSupercruise(EnteredSupercruiseEvent theEvent)
        {
            Environment = Constants.ENVIRONMENT_SUPERCRUISE;
            UpdateCurrentSystem(theEvent.system);

            // We are in the ship
            Vehicle = Constants.VEHICLE_SHIP;

            return true;
        }

        private bool EventEnteredNormalSpace(EnteredNormalSpaceEvent theEvent)
        {
            Environment = Constants.ENVIRONMENT_NORMAL_SPACE;
            UpdateCurrentSystem(theEvent.system);
            return true;
        }

        private bool EventCrewJoined(CrewJoinedEvent theEvent)
        {
            InCrew = true;
            Logging.Info("Entering multicrew session");
            return true;
        }

        private bool EventCrewLeft(CrewLeftEvent theEvent)
        {
            InCrew = false;
            Logging.Info("Leaving multicrew session");
            return true;
        }

        private bool EventCommanderContinued(CommanderContinuedEvent theEvent)
        {
            // If we see this it means that we aren't in CQC
            InCqc = false;

            // Set our commander name
            if (Cmdr.name == null) Cmdr.name = theEvent.commander;

            return true;
        }

        private bool EventCommanderRatings(CommanderRatingsEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null)
            {
                Cmdr.combatrating = theEvent.combat;
                Cmdr.traderating = theEvent.trade;
                Cmdr.explorationrating = theEvent.exploration;
                Cmdr.cqcrating = theEvent.cqc;
                Cmdr.empirerating = theEvent.empire;
                Cmdr.federationrating = theEvent.federation;
            }

            return true;
        }

        private bool EventCombatPromotion(CombatPromotionEvent theEvent)
        {
            // There is a bug with the journal where it reports superpower increases in rank as combat increases
            // Hence we check to see if this is a real event by comparing our known combat rating to the promoted rating
            if (Cmdr == null || Cmdr.combatrating == null || theEvent.rating != Cmdr.combatrating.name)
            {
                // Real event. Capture commander ratings and add them to the commander object
                if (Cmdr != null) Cmdr.combatrating = CombatRating.FromName(theEvent.rating);
                return true;
            }

            // False event
            return false;
        }

        private bool EventTradePromotion(TradePromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null) Cmdr.traderating = TradeRating.FromName(theEvent.rating);
            return true;
        }

        private bool EventExplorationPromotion(ExplorationPromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null) Cmdr.explorationrating = ExplorationRating.FromName(theEvent.rating);
            return true;
        }

        private bool EventFederationPromotion(FederationPromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null) Cmdr.federationrating = FederationRating.FromName(theEvent.rank);
            return true;
        }

        private bool EventEmpirePromotion(EmpirePromotionEvent theEvent)
        {
            // Capture commander ratings and add them to the commander object
            if (Cmdr != null) Cmdr.empirerating = EmpireRating.FromName(theEvent.rank);
            return true;
        }

        private bool EventEnteredCqc(EnteredCQCEvent theEvent)
        {
            // In CQC we don't want to report anything, so set our CQC flag
            InCqc = true;
            return true;
        }

        private bool EventSrvLaunched(SRVLaunchedEvent theEvent)
        {
            // SRV is always player-controlled, so we are in the SRV
            Vehicle = Constants.VEHICLE_SRV;
            return true;
        }

        private bool EventSrvDocked(SRVDockedEvent theEvent)
        {
            // We are back in the ship
            Vehicle = Constants.VEHICLE_SHIP;
            return true;
        }

        private bool EventFighterLaunched(FighterLaunchedEvent theEvent)
        {
            if (theEvent.playercontrolled)
                Vehicle = Constants.VEHICLE_FIGHTER;
            else
                Vehicle = Constants.VEHICLE_SHIP;
            return true;
        }

        private bool EventFighterDocked(FighterDockedEvent theEvent)
        {
            // We are back in the ship
            Vehicle = Constants.VEHICLE_SHIP;
            return true;
        }

        private bool EventVehicleDestroyed(VehicleDestroyedEvent theEvent)
        {
            // We are back in the ship
            Vehicle = Constants.VEHICLE_SHIP;
            return true;
        }

        private bool EventBeltScanned(BeltScannedEvent theEvent)
        {
            // We just scanned a star.  We can only proceed if we know our current star system
            if (CurrentStarSystem != null)
            {
                var belt = CurrentStarSystem.bodies?.FirstOrDefault(b => b.name == theEvent.name);
                if (belt == null)
                {
                    Logging.Debug("Scanned belt " + theEvent.name + " is new - creating");
                    // A new item - set it up
                    belt = new Body();
                    belt.EDDBID = -1;
                    belt.type = "Star";
                    belt.name = theEvent.name;
                    belt.systemname = CurrentStarSystem?.name;
                    CurrentStarSystem.bodies?.Add(belt);
                }

                // Update with the information we have

                belt.distance = (long?)theEvent.distancefromarrival;

                CurrentStarSystem.bodies?.Add(belt);
                Logging.Debug("Saving data for scanned belt " + theEvent.name);
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
            }

            return CurrentStarSystem != null;
        }

        private bool EventStarScanned(StarScannedEvent theEvent)
        {
            // We just scanned a star.  We can only proceed if we know our current star system
            if (CurrentStarSystem != null)
            {
                var star = CurrentStarSystem.bodies?.FirstOrDefault(b => b.name == theEvent.name);
                if (star == null)
                {
                    Logging.Debug("Scanned star " + theEvent.name + " is new - creating");
                    // A new item - set it up
                    star = new Body();
                    star.EDDBID = -1;
                    star.type = "Star";
                    star.name = theEvent.name;
                    star.systemname = CurrentStarSystem?.name;
                    CurrentStarSystem.bodies?.Add(star);
                }

                // Update with the information we have
                star.age = theEvent.age;
                star.distance = (long?)theEvent.distancefromarrival;
                star.luminosityclass = theEvent.luminosityclass;
                star.temperature = (long?)theEvent.temperature;
                star.mainstar = theEvent.distancefromarrival == 0 ? true : false;
                star.stellarclass = theEvent.stellarclass;
                star.solarmass = theEvent.solarmass;
                star.solarradius = theEvent.solarradius;
                star.rings = theEvent.rings;

                star.setStellarExtras();

                CurrentStarSystem.bodies?.Add(star);
                Logging.Debug("Saving data for scanned star " + theEvent.name);
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
            }

            return CurrentStarSystem != null;
        }

        private bool EventBodyScanned(BodyScannedEvent theEvent)
        {
            // We just scanned a body.  We can only proceed if we know our current star system
            if (CurrentStarSystem != null)
            {
                var body = CurrentStarSystem.bodies.FirstOrDefault(b => b.name == theEvent.name);
                if (body == null)
                {
                    Logging.Debug("Scanned body " + theEvent.name + " is new - creating");
                    // A new body - set it up
                    body = new Body
                    {
                        EDDBID = -1,
                        type = "Planet",
                        name = theEvent.name,
                        systemname = CurrentStarSystem.name
                    };
                    CurrentStarSystem.bodies.Add(body);
                }

                // Update with the information we have
                body.distance = (long?)theEvent.distancefromarrival;
                body.landable = theEvent.landable;
                body.tidallylocked = theEvent.tidallylocked;
                body.temperature = (long?)theEvent.temperature;
                body.periapsis = theEvent.periapsis;
                body.atmosphere = theEvent.atmosphere;
                body.gravity = theEvent.gravity;
                body.eccentricity = theEvent.eccentricity;
                body.inclination = theEvent.orbitalinclination;
                body.orbitalperiod = Math.Round(theEvent.orbitalperiod / 86400, 2);
                body.rotationalperiod = Math.Round(theEvent.rotationperiod / 86400, 2);
                body.semimajoraxis = theEvent.semimajoraxis;
                body.pressure = theEvent.pressure;
                switch (theEvent.terraformstate)
                {
                    case "terrraformable":
                    case "terraformable":
                        body.terraformstate = "Terraformable";
                        break;
                    case "terraforming":
                        body.terraformstate = "Terraforming";
                        break;
                    case "Terraformed":
                        body.terraformstate = "Terraformed";
                        break;
                    default:
                        body.terraformstate = "Not terraformable";
                        break;
                }

                body.terraformstate = theEvent.terraformstate;
                body.planettype = theEvent.bodyclass;
                body.volcanism = theEvent.volcanism;
                body.materials = new List<MaterialPresence>();
                foreach (var presence in theEvent.materials)
                    body.materials.Add(new MaterialPresence(presence.definition, presence.percentage));
                body.reserves = theEvent.reserves;
                body.rings = theEvent.rings;

                Logging.Debug("Saving data for scanned body " + theEvent.name);
                StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
            }

            return CurrentStarSystem != null;
        }

        /// <summary>Obtain information from the companion API and use it to refresh our own data</summary>
        public bool RefreshProfile(bool refreshStation = false)
        {
            var success = true;
            if (CompanionAppService.Instance?.CurrentState != CompanionAppService.State.READY) return false;
            try
            {
                // Save a timestamp when the API refreshes, so that we can compare whether events are more or less recent
                ApiTimeStamp = DateTime.UtcNow;

                var profileTime = (long)DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                var profile = CompanionAppService.Instance.Profile();
                if (profile != null)
                {
                    // Use the profile as primary information for our commander and shipyard
                    Cmdr = profile.Cmdr;

                    // Reinstate information not obtained from the Companion API (insurance & gender settings)
                    var configuration = EDDIConfiguration.FromFile();
                    if (configuration != null)
                    {
                        Cmdr.insurance = configuration.Insurance;
                        Cmdr.gender = configuration.Gender;
                    }

                    var updatedCurrentStarSystem = false;

                    // Only set the current star system if it is not present, otherwise we leave it to events
                    if (CurrentStarSystem == null)
                    {
                        CurrentStarSystem = profile?.CurrentStarSystem;
                        SetSystemDistanceFromHome(CurrentStarSystem);

                        // We don't know if we are docked or not at this point.  Fill in the data if we can, and
                        // let later systems worry about removing it if it's decided that we aren't docked
                        if (profile.LastStation != null &&
                            profile.LastStation.systemname == CurrentStarSystem.name &&
                            CurrentStarSystem.stations != null)
                        {
                            CurrentStation =
                                CurrentStarSystem.stations.FirstOrDefault(s => s.name == profile.LastStation.name);
                            if (CurrentStation != null)
                            {
                                Logging.Debug("Set current station to " + CurrentStation.name);
                                CurrentStation.updatedat = profileTime;
                                updatedCurrentStarSystem = true;
                            }
                        }
                    }

                    if (refreshStation && CurrentStation != null)
                    {
                        // Refresh station data
                        _profileUpdateNeeded = true;
                        _profileStationRequired = CurrentStation.name;
                        var updateThread = new Thread(ConditionallyRefreshProfile) { IsBackground = true };
                        updateThread.Start();
                    }

                    SetCommanderTitle();

                    if (updatedCurrentStarSystem)
                    {
                        Logging.Debug("Star system information updated from remote server; updating local copy");
                        StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);
                    }

                    foreach (var monitor in Monitors)
                        try
                        {
                            var monitorThread = new Thread(() =>
                            {
                                try
                                {
                                    monitor.HandleProfile(profile.json);
                                }
                                catch (Exception ex)
                                {
                                    Logging.Warn("Monitor failed", ex);
                                }
                            });
                            monitorThread.Name = monitor.MonitorName;
                            monitorThread.IsBackground = true;
                            monitorThread.Start();
                        }
                        catch (ThreadAbortException tax)
                        {
                            Thread.ResetAbort();
                            Logging.Error(JsonConvert.SerializeObject(profile), tax);
                            success = false;
                        }
                        catch (Exception ex)
                        {
                            Logging.Error(JsonConvert.SerializeObject(profile), ex);
                            success = false;
                        }
                }
            }
            catch (Exception ex)
            {
                Logging.Error("Exception obtaining profile", ex);
                success = false;
            }

            return success;
        }

        private void SetSystemDistanceFromHome(StarSystem system)
        {
            if (HomeStarSystem?.x != null && system.x != null)
            {
                system.distancefromhome = (decimal)Math.Round(Math.Sqrt(
                    Math.Pow((double)(system.x - HomeStarSystem.x), 2)
                    + Math.Pow((double)(system.y - HomeStarSystem.y), 2)
                    + Math.Pow((double)(system.z - HomeStarSystem.z), 2)), 2);
                Logging.Debug("Distance from home is " + system.distancefromhome);
            }
        }

        private void SetCommanderTitle()
        {
            if (Cmdr != null)
            {
                Cmdr.title = "Commander";
                if (CurrentStarSystem != null)
                    if (CurrentStarSystem.allegiance == "Federation" && Cmdr.federationrating != null &&
                        Cmdr.federationrating.rank > _minFederationRankForTitle)
                        Cmdr.title = Cmdr.federationrating.name;
                    else if (CurrentStarSystem.allegiance == "Empire" && Cmdr.empirerating != null &&
                             Cmdr.empirerating.rank > _minEmpireRankForTitle) Cmdr.title = Cmdr.empirerating.name;
            }
        }

        /// <summary>
        ///     Find all monitors
        /// </summary>
        private List<IEDDIMonitor> FindMonitors()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new DirectoryNotFoundException());
            var monitors = new List<IEDDIMonitor>();
            var pluginType = typeof(IEDDIMonitor);
            foreach (var file in dir.GetFiles("*Monitor.dll", SearchOption.AllDirectories))
            {
                Logging.Debug("Checking potential plugin at " + file.FullName);
                try
                {
                    var assembly = Assembly.LoadFrom(file.FullName);
                    foreach (var type in assembly.GetTypes())
                        if (type.IsInterface || type.IsAbstract)
                        {
                        }
                        else
                        {
                            if (type.GetInterface(pluginType.FullName) != null)
                            {
                                Logging.Debug("Instantiating monitor plugin at " + file.FullName);
                                var monitor = type.InvokeMember(null,
                                    BindingFlags.CreateInstance,
                                    null, null, null) as IEDDIMonitor;
                                monitors.Add(monitor);
                            }
                        }
                }
                catch (BadImageFormatException)
                {
                    // Ignore this; probably due to CPU architecture mismatch
                }
                catch (ReflectionTypeLoadException ex)
                {
                    var sb = new StringBuilder();
                    foreach (var exSub in ex.LoaderExceptions)
                    {
                        sb.AppendLine(exSub.Message);
                        if (exSub is FileNotFoundException exFileNotFound)
                            if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                            {
                                sb.AppendLine("Fusion Log:");
                                sb.AppendLine(exFileNotFound.FusionLog);
                            }

                        sb.AppendLine();
                    }

                    Logging.Warn("Failed to instantiate plugin at " + file.FullName + ":\n" + sb);
                }
                catch (FileLoadException flex)
                {
                    var msg = "Failed to load monitor. Please ensure that " + dir.FullName +
                              " is not on a network share, or itself shared";
                    Logging.Error(msg, flex);
                    SpeechService.Instance.Say(null, msg, false);
                }
                catch (Exception ex)
                {
                    var msg = $"Failed to load monitor: {file.Name}.\n{ex.Message} {ex.InnerException?.Message ?? ""}";
                    Logging.Error(msg, ex);
                    SpeechService.Instance.Say(null, msg, false);
                }
            }

            return monitors;
        }

        /// <summary>
        ///     Find all responders
        /// </summary>
        private List<IEDDIResponder> FindResponders()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException());
            var responders = new List<IEDDIResponder>();
            var pluginType = typeof(IEDDIResponder);
            foreach (var file in dir.GetFiles("*Responder.dll", SearchOption.AllDirectories))
            {
                Logging.Debug("Checking potential plugin at " + file.FullName);
                try
                {
                    var assembly = Assembly.LoadFrom(file.FullName);
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsInterface || type.IsAbstract) continue;

                        if (type.GetInterface(pluginType.FullName) != null)
                        {
                            Logging.Debug("Instantiating responder plugin at " + file.FullName);
                            var responder = type.InvokeMember(null,
                                BindingFlags.CreateInstance,
                                null, null, null) as IEDDIResponder;
                            responders.Add(responder);
                        }
                    }
                }
                catch (BadImageFormatException)
                {
                    // Ignore this; probably due to CPU architecure mismatch
                }
                catch (ReflectionTypeLoadException ex)
                {
                    var sb = new StringBuilder();
                    foreach (var exSub in ex.LoaderExceptions)
                    {
                        sb.AppendLine(exSub.Message);
                        if (exSub is FileNotFoundException exFileNotFound)
                            if (!string.IsNullOrEmpty(exFileNotFound.FusionLog))
                            {
                                sb.AppendLine("Fusion Log:");
                                sb.AppendLine(exFileNotFound.FusionLog);
                            }

                        sb.AppendLine();
                    }

                    Logging.Warn($"Failed to instantiate plugin at {file.FullName}:\n{sb}");
                }
            }

            return responders;
        }

        /// <summary>
        ///     Update the profile when requested, ensuring that we meet the condition in the updated profile
        /// </summary>
        private void ConditionallyRefreshProfile()
        {
            var maxTries = 6;

            while (_running && maxTries > 0 &&
                   CompanionAppService.Instance.CurrentState == CompanionAppService.State.READY)
                try
                {
                    Logging.Debug("Starting conditional profile fetch");

                    // See if we need to fetch the profile
                    if (_profileUpdateNeeded)
                    {
                        // See if we still need this particular update
                        if (_profileStationRequired != null &&
                            (CurrentStation == null || CurrentStation.name != _profileStationRequired))
                        {
                            Logging.Debug("No longer at requested station; giving up on update");
                            _profileUpdateNeeded = false;
                            _profileStationRequired = null;
                            break;
                        }

                        // Make sure we know where we are
                        if (CurrentStarSystem.name.Length < 0) break;

                        // We do need to fetch an updated profile; do so
                        ApiTimeStamp = DateTime.UtcNow;
                        var profileTime = (long)DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                        Logging.Debug("Fetching station profile");
                        var profile = CompanionAppService.Instance.Station(CurrentStarSystem.name);

                        // See if it is up-to-date regarding our requirements
                        Logging.Debug("profileStationRequired is " + _profileStationRequired + ", profile station is " +
                                      profile.LastStation.name);

                        if (_profileStationRequired != null && _profileStationRequired == profile.LastStation.name)
                        {
                            // We have the required station information
                            Logging.Debug("Current station matches profile information; updating info");
                            CurrentStation.commodities = profile.LastStation.commodities;
                            CurrentStation.economies = profile.LastStation.economies;
                            CurrentStation.prohibited = profile.LastStation.prohibited;
                            CurrentStation.commoditiesupdatedat = profileTime;
                            CurrentStation.outfitting = profile.LastStation.outfitting;
                            CurrentStation.shipyard = profile.LastStation.shipyard;
                            CurrentStation.updatedat = profileTime;

                            // Update the current station information in our backend DB
                            Logging.Debug("Star system information updated from remote server; updating local copy");
                            StarSystemSqLiteRepository.Instance.SaveStarSystem(CurrentStarSystem);

                            // Post an update event
                            Event @event = new MarketInformationUpdatedEvent(DateTime.Now);
                            EventHandler(@event);

                            _profileUpdateNeeded = false;
                            break;
                        }

                        // No luck; sleep and try again
                        Thread.Sleep(15000);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Error("Exception obtaining profile", ex);
                }
                finally
                {
                    maxTries--;
                }

            if (maxTries == 0) Logging.Info("Maximum attempts reached; giving up on request");

            // Clear the update info
            _profileUpdateNeeded = false;
            _profileStationRequired = null;
        }

        // If we have no access to the companion API but need to trigger a market update then we can call this method
        private void DummyRefreshMarketData()
        {
            Thread.Sleep(2000);
            Event @event = new MarketInformationUpdatedEvent(DateTime.Now);
            EventHandler(@event);
        }

        public void UpdateHomeSystemStation(EDDIConfiguration configuration)
        {
            Logging.Verbose = configuration.Debug;
            if (configuration.HomeSystem != null && configuration.HomeSystem.Trim().Length > 0)
            {
                HomeStarSystem =
                    StarSystemSqLiteRepository.Instance.GetOrCreateStarSystem(configuration.HomeSystem.Trim());
                if (HomeStarSystem != null)
                {
                    Logging.Debug("Home star system is " + HomeStarSystem.name);
                    if (configuration.HomeStation != null && configuration.HomeStation.Trim().Length > 0)
                    {
                        var homeStationName = configuration.HomeStation.Trim();
                        foreach (var station in HomeStarSystem.stations)
                            if (station.name == homeStationName)
                            {
                                HomeStation = station;
                                Logging.Debug("Home station is " + HomeStation.name);
                                break;
                            }
                    }
                }
            }
        }

        internal static class NativeMethods
        {
            // Required to restart app after upgrade
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            internal static extern uint RegisterApplicationRestart(string pwzCommandLine, RestartFlags dwFlags);
        }

        // Flags for upgrade
        [Flags]
        internal enum RestartFlags
        {
            None = 0,
            RestartCyclical = 1,
            RestartNotifySolution = 2,
            RestartNotifyFault = 4,
            RestartNoCrash = 8,
            RestartNoHang = 16,
            RestartNoPatch = 32,
            RestartNoReboot = 64
        }
    }
}
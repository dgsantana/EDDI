using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EDDI;
using EddiDataDefinitions;
using EddiEvents;
using EddiShipMonitor;
using Newtonsoft.Json;
using Utilities;

namespace EddiJournalMonitor
{
    public partial class JournalMonitor
    {
        public static List<Event> ParseJournalEntry(string line)
        {
            var events = new List<Event>();
            try
            {
                var match = JsonRegex.Match(line);
                if (match.Success)
                {
                    var data = Deserialization.DeserializeData(line);

                    // Every ev has a timestamp field
                    var timestamp = DateTime.Now;
                    if (data.ContainsKey("timestamp"))
                        timestamp = data["timestamp"] is DateTime time
                            ? time.ToUniversalTime()
                            : DateTime.Parse(GetString(data, "timestamp")).ToUniversalTime();
                    else
                        Logging.Warn("Event without timestamp; using current time");

                    // Every ev has an ev field
                    if (!data.ContainsKey("event"))
                    {
                        Logging.Warn("Event without ev field!");
                        return null;
                    }

                    var handled = false;

                    string edType = GetString(data, "event");
                    EDDI.Core.Eddi.Instance.JournalTimeStamp = edType == "Fileheader" ? DateTime.MinValue : timestamp;

                    object val;
                    switch (edType)
                    {
                        case "Docked":
                            DockedEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "Undocked":
                            {
                                string stationName = GetString(data, "StationName");
                                events.Add(new UndockedEvent(timestamp, stationName) { raw = line });
                            }
                            handled = true;
                            break;
                        case "Touchdown":
                            {
                                decimal? latitude = GetOptionalDecimal(data, "Latitude");
                                decimal? longitude = GetOptionalDecimal(data, "Longitude");
                                bool playercontrolled = GetOptionalBool(data, "PlayerControlled") ?? true;
                                events.Add(
                                    new TouchdownEvent(timestamp, longitude, latitude, playercontrolled) { raw = line });
                            }
                            handled = true;
                            break;
                        case "Liftoff":
                            {
                                decimal? latitude = GetOptionalDecimal(data, "Latitude");
                                decimal? longitude = GetOptionalDecimal(data, "Longitude");
                                bool playercontrolled = GetOptionalBool(data, "PlayerControlled") ?? true;
                                events.Add(new LiftoffEvent(timestamp, longitude, latitude, playercontrolled) { raw = line });
                            }
                            handled = true;
                            break;
                        case "SupercruiseEntry":
                            {
                                string system = GetString(data, "StarySystem");
                                events.Add(new EnteredSupercruiseEvent(timestamp, system) { raw = line });
                            }
                            handled = true;
                            break;
                        case "SupercruiseExit":
                            {
                                string system = GetString(data, "StarSystem");
                                string body = GetString(data, "Body");
                                string bodyType = GetString(data, "BodyType");
                                events.Add(new EnteredNormalSpaceEvent(timestamp, system, body, bodyType) { raw = line });
                            }
                            handled = true;
                            break;
                        case "FSDJump":
                            FSDJumpEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "Location":
                            {

                                string systemName = GetString(data, "StarSystem");

                                if (systemName == "Training")
                                {
                                    // Training system; ignore
                                    break;
                                }

                                data.TryGetValue("StarPos", out val);
                                List<object> starPos = (List<object>)val;
                                decimal x = Math.Round(GetDecimal("X", starPos[0]) * 32) / (decimal)32.0;
                                decimal y = Math.Round(GetDecimal("Y", starPos[1]) * 32) / (decimal)32.0;
                                decimal z = Math.Round(GetDecimal("Z", starPos[2]) * 32) / (decimal)32.0;

                                string body = GetString(data, "Body");
                                string bodyType = GetString(data, "BodyType");
                                bool docked = GetBool(data, "Docked");
                                Superpower allegiance = GetAllegiance(data, "SystemAllegiance");
                                string faction = GetFaction(data, "SystemFaction");
                                Economy economy = Economy.FromEDName(GetString(data, "SystemEconomy"));
                                Government government = Government.FromEDName(GetString(data, "SystemGovernment"));
                                SecurityLevel security = SecurityLevel.FromEDName(GetString(data, "SystemSecurity"));
                                long? population = GetOptionalLong(data, "Population");

                                string station = GetString(data, "StationName");
                                string stationtype = GetString(data, "StationType");

                                decimal? latitude = GetOptionalDecimal(data, "Latitude");
                                decimal? longitude = GetOptionalDecimal(data, "Longitude");

                                events.Add(new LocationEvent(timestamp, systemName, x, y, z, body, bodyType, docked,
                                    station, stationtype, allegiance, faction, economy, government, security, population,
                                    longitude, latitude)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "Bounty":
                            BountyEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "CapShipBond":
                        case "DatalinkVoucher":
                        case "FactionKillBond":
                            {
                                data.TryGetValue("Reward", out val);
                                long reward = (long)val;
                                string victimFaction = GetString(data, "VictimFaction");

                                if (data.ContainsKey("AwardingFaction"))
                                {
                                    string awardingFaction = GetFaction(data, "AwardingFaction");
                                    events.Add(new BondAwardedEvent(timestamp, awardingFaction, victimFaction, reward)
                                    {
                                        raw = line
                                    });
                                }
                                else if (data.ContainsKey("PayeeFaction"))
                                {
                                    string payeeFaction = GetFaction(data, "PayeeFaction");
                                    events.Add(new DataVoucherAwardedEvent(timestamp, payeeFaction, victimFaction, reward)
                                    {
                                        raw = line
                                    });
                                }
                            }
                            handled = true;
                            break;
                        case "CommitCrime":
                            {
                                string crimetype = GetString(data, "CrimeType");
                                string faction = GetFaction(data, "Faction");
                                string victim = GetString(data, "Victim");
                                // Might be a fine or a bounty
                                if (data.ContainsKey("Fine"))
                                {
                                    data.TryGetValue("Fine", out val);
                                    long fine = (long)val;
                                    events.Add(new FineIncurredEvent(timestamp, crimetype, faction, victim, fine)
                                    {
                                        raw = line
                                    });
                                }
                                else
                                {
                                    data.TryGetValue("Bounty", out val);
                                    long bounty = (long)val;
                                    events.Add(new BountyIncurredEvent(timestamp, crimetype, faction, victim, bounty)
                                    {
                                        raw = line
                                    });
                                }
                            }
                            handled = true;
                            break;
                        case "Promotion":
                            {
                                if (data.ContainsKey("Combat"))
                                {
                                    data.TryGetValue("Combat", out val);
                                    CombatRating rating = CombatRating.FromRank(Convert.ToInt32(val));
                                    events.Add(new CombatPromotionEvent(timestamp, rating) { raw = line });
                                    handled = true;
                                }
                                else if (data.ContainsKey("Trade"))
                                {
                                    data.TryGetValue("Trade", out val);
                                    TradeRating rating = TradeRating.FromRank(Convert.ToInt32(val));
                                    events.Add(new TradePromotionEvent(timestamp, rating) { raw = line });
                                    handled = true;
                                }
                                else if (data.ContainsKey("Explore"))
                                {
                                    data.TryGetValue("Explore", out val);
                                    ExplorationRating rating = ExplorationRating.FromRank(Convert.ToInt32(val));
                                    events.Add(new ExplorationPromotionEvent(timestamp, rating) { raw = line });
                                    handled = true;
                                }
                                else if (data.ContainsKey("Federation"))
                                {
                                    Superpower superpower = Superpower.FromName("Federation");
                                    data.TryGetValue("Federation", out val);
                                    FederationRating rating = FederationRating.FromRank(Convert.ToInt32(val));
                                    events.Add(new FederationPromotionEvent(timestamp, rating) { raw = line });
                                    handled = true;
                                }
                                else if (data.ContainsKey("Empire"))
                                {
                                    data.TryGetValue("Empire", out val);
                                    EmpireRating rating = EmpireRating.FromRank(Convert.ToInt32(val));
                                    events.Add(new EmpirePromotionEvent(timestamp, rating) { raw = line });
                                    handled = true;
                                }
                            }
                            break;
                        case "CollectCargo":
                            {
                                string commodityName = GetString(data, "Type");
                                Commodity commodity = CommodityDefinitions.FromName(commodityName);
                                if (commodity == null)
                                {
                                    Logging.Error("Failed to map collectcargo type " + commodityName + " to commodity");
                                }

                                bool stolen = GetBool(data, "Stolen");
                                events.Add(new CommodityCollectedEvent(timestamp, commodity, stolen) { raw = line });
                                handled = true;
                            }
                            handled = true;
                            break;
                        case "EjectCargo":
                            {
                                string commodityName = GetString(data, "Type");
                                Commodity commodity = CommodityDefinitions.FromName(commodityName);
                                if (commodity == null)
                                {
                                    Logging.Error("Failed to map ejectcargo type " + commodityName + " to commodity");
                                }

                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                bool abandoned = GetBool(data, "Abandoned");
                                events.Add(new CommodityEjectedEvent(timestamp, commodity, amount, abandoned) { raw = line });
                            }
                            handled = true;
                            break;
                        case "Loadout":
                            LoadoutEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "CockpitBreached":
                            events.Add(new CockpitBreachedEvent(timestamp) { raw = line });
                            handled = true;
                            break;
                        case "ApproachSettlement":
                            {
                                string name = GetString(data, "Name");
                                // Replace with localised name if available
                                if (data.TryGetValue("Name_Localised", out val))
                                {
                                    name = (string)val;
                                }

                                events.Add(new SettlementApproachedEvent(timestamp, name) { raw = line });
                            }
                            handled = true;
                            break;
                        case "Scan":
                            handled = ScanEvent(line, data, events, timestamp);
                            break;
                        case "DatalinkScan":
                            {
                                string message = GetString(data, "Message");
                                events.Add(new DatalinkMessageEvent(timestamp, message) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DataScanned":
                            {
                                DataScan datalinktype = DataScan.FromEDName(GetString(data, "Type"));
                                events.Add(new DataScannedEvent(timestamp, datalinktype) { raw = line });
                            }
                            handled = true;
                            break;
                        case "ShipyardBuy":
                            {
                                // We don't have a ship ID at this point so use the ship type
                                string ship = GetString(data, "ShipType");

                                data.TryGetValue("ShipPrice", out val);
                                long price = (long)val;

                                data.TryGetValue("StoreShipID", out val);
                                int? storedShipId = (val == null ? (int?)null : (int)(long)val);
                                string storedShip = GetString(data, "StoreOldShip");

                                data.TryGetValue("SellShipID", out val);
                                int? soldShipId = (val == null ? (int?)null : (int)(long)val);
                                string soldShip = GetString(data, "SellOldShip");

                                data.TryGetValue("SellPrice", out val);
                                long? soldPrice = (long?)val;
                                events.Add(new ShipPurchasedEvent(timestamp, ship, price, soldShip, soldShipId, soldPrice,
                                    storedShip, storedShipId)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ShipyardNew":
                            {
                                data.TryGetValue("NewShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "ShipType");

                                events.Add(new ShipDeliveredEvent(timestamp, ship, shipId) { raw = line });
                            }
                            handled = true;
                            break;
                        case "ShipyardSell":
                            {
                                data.TryGetValue("SellShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "ShipType");
                                data.TryGetValue("ShipPrice", out val);
                                long price = (long)val;
                                string system = GetString(data, "System");
                                events.Add(new ShipSoldEvent(timestamp, ship, shipId, price, system) { raw = line });
                            }
                            handled = true;
                            break;
                        case "SellShipOnRebuy":
                            {
                                data.TryGetValue("SellShipId", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "ShipType");
                                data.TryGetValue("ShipPrice", out val);
                                long price = (long)val;
                                string system = GetString(data, "System");
                                events.Add(new ShipSoldOnRebuyEvent(timestamp, ship, shipId, price, system) { raw = line });
                            }
                            handled = true;
                            break;
                        case "ShipyardArrived":
                            {
                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "ShipType");
                                string system = GetString(data, "System");
                                decimal distance = GetDecimal(data, "Distance");
                                long? price = GetOptionalLong(data, "TransferPrice");
                                long? time = GetOptionalLong(data, "TransferTime");
                                string station = GetString(data, "Station");
                                events.Add(new ShipArrivedEvent(timestamp, ship, shipId, system, distance, price, time,
                                    station)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ShipyardSwap":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "ShipType");

                                data.TryGetValue("StoreShipID", out val);
                                int? storedShipId = (val == null ? (int?)null : (int)(long)val);
                                string storedShip = GetString(data, "StoreOldShip");

                                data.TryGetValue("SellShipID", out val);
                                int? soldShipId = (val == null ? (int?)null : (int)(long)val);
                                string soldShip = GetString(data, "SellOldShip");

                                events.Add(new ShipSwappedEvent(timestamp, ship, shipId, soldShip, soldShipId, storedShip,
                                    storedShipId)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ShipyardTransfer":
                            {
                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "ShipType");

                                string system = GetString(data, "System");
                                decimal distance = GetDecimal(data, "Distance");
                                long? price = GetOptionalLong(data, "TransferPrice");
                                long? time = GetOptionalLong(data, "TransferTime");

                                events.Add(new ShipTransferInitiatedEvent(timestamp, ship, shipId, system, distance, price,
                                    time)
                                { raw = line });

                                // Generate secondary ev when the ship is arriving
                                if (time.HasValue)
                                {
                                    ShipArrived();

                                    async void ShipArrived()
                                    {
                                        // Add a bit of context
                                        string arrivalStation = EDDI.Core.Eddi.Instance.CurrentStation?.name ?? string.Empty;
                                        string arrivalSystem = EDDI.Core.Eddi.Instance.CurrentStarSystem?.name ?? string.Empty;

                                        line = line.Replace("ShipyardTransfer", "ShipyardArrived");
                                        line = line.Replace(
                                            timestamp.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
                                            timestamp.AddSeconds((double)time).ToUniversalTime().ToString());
                                        line = line.Replace(",\"System\":\"" + system + "\"",
                                            ",\"System\":\"" + arrivalSystem +
                                            "\""); // Include the system at which the transfer will arrive
                                        line = line.Replace("}",
                                            ",\"Station\":\"" + arrivalStation +
                                            "\"}"); // Include the station at which the transferred ship will arrive
                                        await Task.Delay((int)time * 1000);
                                        ForwardJournalEntry(line, EDDI.Core.Eddi.Instance.EventHandler);
                                    }
                                }
                            }
                            handled = true;
                            break;
                        case "FetchRemoteModule":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                Module module = ModuleDefinitions.fromEDName(GetString(data, "StoredItem"));
                                data.TryGetValue("TransferCost", out val);
                                long transferCost = (long)val;
                                long? transferTime = GetOptionalLong(data, "TransferTime");

                                // Probably not useful. We'll get these but we won't tell the end user about them
                                data.TryGetValue("StorageSlot", out val);
                                int storageSlot = (int)(long)val;
                                data.TryGetValue("ServerId", out val);
                                long serverId = (long)val;

                                events.Add(new ModuleTransferEvent(timestamp, ship, shipId, storageSlot, serverId, module,
                                    transferCost, transferTime)
                                { raw = line });

                                // Generate a secondary ev when the module is arriving

                                if (transferTime.HasValue)
                                {
                                    ModuleArrived();

                                    async void ModuleArrived()
                                    {
                                        // Add a bit of context
                                        string arrivalStation = EDDI.Core.Eddi.Instance.CurrentStation?.name ?? string.Empty;
                                        string arrivalSystem = EDDI.Core.Eddi.Instance.CurrentStarSystem?.name ?? string.Empty;

                                        line = line.Replace("FetchRemoteModule", "ModuleArrived");
                                        line = line.Replace(
                                            timestamp.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
                                            timestamp.AddSeconds((double)transferTime).ToUniversalTime().ToString());
                                        line = line.Replace("}",
                                            ",\"System\":\"" + arrivalSystem +
                                            "\"}"); // Include the system at which the transfer will arrive
                                        line = line.Replace("}",
                                            ",\"Station\":\"" + arrivalStation +
                                            "\"}"); // Include the station at which the transferred module will arrive
                                        await Task.Delay((int)transferTime * 1000);
                                        ForwardJournalEntry(line, EDDI.Core.Eddi.Instance.EventHandler);
                                    }
                                }
                            }
                            handled = true;
                            break;
                        case "MassModuleStore":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                data.TryGetValue("Items", out val);
                                List<object> items = (List<object>)val;

                                List<string> slots = new List<string>();
                                List<Module> modules = new List<Module>();

                                Module module = new Module();
                                if (items != null)
                                {
                                    foreach (Dictionary<string, object> item in items)
                                    {
                                        string slot = GetString(item, "Slot");
                                        slots.Add(slot);

                                        module = ModuleDefinitions.fromEDName(GetString(item, "Name"));
                                        module.modified = GetString(item, "EngineerModifications") != null;
                                        modules.Add(module);
                                    }
                                }

                                events.Add(new ModulesStoredEvent(timestamp, ship, shipId, slots, modules) { raw = line });
                            }
                            handled = true;
                            break;
                        case "ModuleArrived":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                Module module = ModuleDefinitions.fromEDName(GetString(data, "StoredItem"));
                                data.TryGetValue("TransferCost", out val);
                                long transferCost = (long)val;
                                long? transferTime = GetOptionalLong(data, "TransferTime");

                                // Probably not useful. We'll get these but we won't tell the end user about them
                                data.TryGetValue("StorageSlot", out val);
                                int storageSlot = (int)(long)val;
                                data.TryGetValue("ServerId", out val);
                                long serverId = (long)val;

                                string system = GetString(data, "System");
                                string station = GetString(data, "Station");

                                events.Add(new ModuleArrivedEvent(timestamp, ship, shipId, storageSlot, serverId, module,
                                    transferCost, transferTime, system, station)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ModuleBuy":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                string slot = GetString(data, "Slot");
                                Module buyModule = ModuleDefinitions.fromEDName(GetString(data, "BuyItem"));
                                data.TryGetValue("BuyPrice", out val);
                                long buyPrice = (long)val;
                                buyModule.price = buyPrice;

                                // Set retrieved module defaults
                                buyModule.enabled = true;
                                buyModule.priority = 1;
                                buyModule.health = 100;
                                buyModule.modified = false;

                                Module sellModule = ModuleDefinitions.fromEDName(GetString(data, "SellItem"));
                                long? sellPrice = GetOptionalLong(data, "SellPrice");
                                Module storedModule = ModuleDefinitions.fromEDName(GetString(data, "StoredItem"));

                                events.Add(new ModulePurchasedEvent(timestamp, ship, shipId, slot, buyModule, buyPrice,
                                    sellModule, sellPrice, storedModule)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ModuleRetrieve":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                string slot = GetString(data, "Slot");
                                Module module = ModuleDefinitions.fromEDName(GetString(data, "RetrievedItem"));
                                data.TryGetValue("Cost", out val);
                                long? cost = GetOptionalLong(data, "Cost");
                                string engineerModifications = GetString(data, "EngineerModifications");
                                module.modified = engineerModifications != null;

                                // Set retrieved module defaults
                                module.price = module.value;
                                module.enabled = true;
                                module.priority = 1;
                                module.health = 100;

                                Module swapoutModule = ModuleDefinitions.fromEDName(GetString(data, "SwapOutItem"));

                                events.Add(new ModuleRetrievedEvent(timestamp, ship, shipId, slot, module, cost,
                                    engineerModifications, swapoutModule)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ModuleSell":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                string slot = GetString(data, "Slot");
                                Module module = ModuleDefinitions.fromEDName(GetString(data, "SellItem"));
                                data.TryGetValue("SellPrice", out val);
                                long price = (long)val;


                                events.Add(new ModuleSoldEvent(timestamp, ship, shipId, slot, module, price) { raw = line });
                            }
                            handled = true;
                            break;
                        case "ModuleSellRemote":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                Module module = ModuleDefinitions.fromEDName(GetString(data, "SellItem"));
                                data.TryGetValue("SellPrice", out val);
                                long price = (long)val;

                                // Probably not useful. We'll get these but we won't tell the end user about them
                                data.TryGetValue("StorageSlot", out val);
                                int storageSlot = (int)(long)val;
                                data.TryGetValue("ServerId", out val);
                                long serverId = (long)val;

                                events.Add(new ModuleSoldFromStorageEvent(timestamp, ship, shipId, storageSlot, serverId,
                                    module, price)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ModuleStore":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                string slot = GetString(data, "Slot");
                                Module module = ModuleDefinitions.fromEDName(GetString(data, "StoredItem"));
                                string engineerModifications = GetString(data, "EngineerModifications");
                                module.modified = engineerModifications != null;
                                data.TryGetValue("Cost", out val);
                                long? cost = GetOptionalLong(data, "Cost");


                                Module replacementModule = ModuleDefinitions.fromEDName(GetString(data, "ReplacementItem"));
                                if (replacementModule != null)
                                {
                                    replacementModule.price = replacementModule.value;
                                    replacementModule.enabled = true;
                                    replacementModule.priority = 1;
                                    replacementModule.health = 100;
                                    replacementModule.modified = false;
                                }

                                events.Add(new ModuleStoredEvent(timestamp, ship, shipId, slot, module, cost,
                                    engineerModifications, replacementModule)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "ModuleSwap":
                            {

                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");

                                string fromSlot = GetString(data, "FromSlot");
                                Module fromModule = ModuleDefinitions.fromEDName(GetString(data, "FromItem"));
                                string toSlot = GetString(data, "ToSlot");
                                Module toModule = ModuleDefinitions.fromEDName(GetString(data, "ToItem"));

                                events.Add(new ModuleSwappedEvent(timestamp, ship, shipId, fromSlot, fromModule, toSlot,
                                    toModule)
                                { raw = line });
                            }
                            handled = true;
                            break;
                        case "SetUserShipName":
                            {
                                data.TryGetValue("ShipID", out val);
                                int shipId = (int)(long)val;
                                string ship = GetString(data, "Ship");
                                string name = GetString(data, "UserShipName");
                                string ident = GetString(data, "UserShipId");

                                events.Add(new ShipRenamedEvent(timestamp, ship, shipId, name, ident) { raw = line });
                            }
                            handled = true;
                            break;
                        case "LaunchSRV":
                            {
                                string loadout = GetString(data, "Loadout");
                                bool playercontrolled = GetBool(data, "PlayerControlled");

                                events.Add(new SRVLaunchedEvent(timestamp, loadout, playercontrolled) { raw = line });
                            }
                            handled = true;
                            break;
                        case "Music":
                            {
                                string musicTrack = GetString(data, "MusicTrack");
                                events.Add(new MusicEvent(timestamp, musicTrack) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DockSRV":
                            events.Add(new SRVDockedEvent(timestamp) { raw = line });
                            handled = true;
                            break;
                        case "LaunchFighter":
                            {
                                string loadout = GetString(data, "Loadout");
                                bool playerControlled = GetBool(data, "PlayerControlled");
                                events.Add(new FighterLaunchedEvent(timestamp, loadout, playerControlled) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DockFighter":
                            events.Add(new FighterDockedEvent(timestamp) { raw = line });
                            handled = true;
                            break;
                        case "VehicleSwitch":
                            {
                                string to = GetString(data, "To");
                                if (to == "Fighter")
                                {
                                    events.Add(new ControllingFighterEvent(timestamp) { raw = line });
                                    handled = true;
                                }
                                else if (to == "Mothership")
                                {
                                    events.Add(new ControllingShipEvent(timestamp) { raw = line });
                                    handled = true;
                                }
                                else if (to == null)
                                {
                                    // The variable 'to' may be blank if this ev is written after a fighter or SRV is destroyed. In either case, we are back in our ship.
                                    if (EDDI.Core.Eddi.Instance.Vehicle == Constants.VEHICLE_FIGHTER ||
                                        EDDI.Core.Eddi.Instance.Vehicle == Constants.VEHICLE_SRV)
                                    {
                                        events.Add(new VehicleDestroyedEvent(timestamp) { raw = line });
                                        handled = true;
                                    }
                                }
                            }
                            break;
                        case "Interdicted":
                            {
                                bool submitted = GetBool(data, "Submitted");
                                string interdictor = GetString(data, "Interdictor");
                                bool iscommander = GetBool(data, "IsPlayer");
                                data.TryGetValue("CombatRank", out val);
                                CombatRating rating = (val == null ? null : CombatRating.FromRank((int)val));
                                string faction = GetFaction(data, "Faction");
                                string power = GetString(data, "Power");

                                events.Add(new ShipInterdictedEvent(timestamp, true, submitted, iscommander, interdictor,
                                    rating, faction, power)
                                { raw = line });
                                handled = true;
                            }
                            break;
                        case "EscapeInterdiction":
                            {
                                string interdictor = GetString(data, "Interdictor");
                                bool iscommander = GetBool(data, "IsPlayer");

                                events.Add(new ShipInterdictedEvent(timestamp, false, false, iscommander, interdictor, null,
                                    null, null)
                                { raw = line });
                                handled = true;
                            }
                            break;
                        case "Interdiction":
                            {
                                bool success = GetBool(data, "Success");
                                string interdictee = GetString(data, "Interdicted");
                                bool iscommander = GetBool(data, "IsPlayer");
                                data.TryGetValue("CombatRank", out val);
                                CombatRating rating = (val == null ? null : CombatRating.FromRank((int)val));
                                string faction = GetFaction(data, "Faction");
                                string power = GetString(data, "Power");

                                events.Add(new ShipInterdictionEvent(timestamp, success, iscommander, interdictee, rating,
                                    faction, power)
                                { raw = line });
                                handled = true;
                            }
                            break;
                        case "PVPKill":
                            {
                                string victim = GetString(data, "Victim");
                                data.TryGetValue("CombatRank", out val);
                                CombatRating rating = (val == null ? null : CombatRating.FromRank((int)val));

                                events.Add(new KilledEvent(timestamp, victim, rating) { raw = line });
                                handled = true;
                            }
                            break;
                        case "MaterialCollected":
                            {
                                Material material = Material.FromEDName(GetString(data, "Name"));
                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                events.Add(new MaterialCollectedEvent(timestamp, material, amount) { raw = line });
                                handled = true;
                            }
                            break;
                        case "MaterialDiscarded":
                            {
                                Material material = Material.FromEDName(GetString(data, "Name"));
                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                events.Add(new MaterialDiscardedEvent(timestamp, material, amount) { raw = line });
                                handled = true;
                            }
                            break;
                        case "MaterialDiscovered":
                            {
                                Material material = Material.FromEDName(GetString(data, "Name"));
                                events.Add(new MaterialDiscoveredEvent(timestamp, material) { raw = line });
                                handled = true;
                            }
                            break;
                        case "ScientificResearch":
                            {
                                data.TryGetValue("Name", out val);
                                Material material = Material.FromEDName(GetString(data, "Name"));
                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                events.Add(new MaterialDonatedEvent(timestamp, material, amount) { raw = line });
                                handled = true;
                            }
                            break;
                        case "StartJump":
                            {
                                string target = GetString(data, "JumpType");
                                string stellarclass = GetString(data, "StarClass");
                                string system = GetString(data, "StarSystem");
                                events.Add(new FSDEngagedEvent(timestamp, target, system, stellarclass) { raw = line });
                                handled = true;
                            }
                            break;
                        case "ReceiveText":
                            ReceivedTextEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "SendText":
                            {
                                string to = GetString(data, "To");
                                string message = GetString(data, "Message");
                                events.Add(new MessageSentEvent(timestamp, to, message) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DockingRequested":
                            {
                                string stationName = GetString(data, "StationName");
                                events.Add(new DockingRequestedEvent(timestamp, stationName) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DockingGranted":
                            {
                                string stationName = GetString(data, "StationName");
                                data.TryGetValue("LandingPad", out val);
                                int landingPad = (int)(long)val;
                                events.Add(new DockingGrantedEvent(timestamp, stationName, landingPad) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DockingDenied":
                            {
                                string stationName = GetString(data, "StationName");
                                string reason = GetString(data, "Reason");
                                events.Add(new DockingDeniedEvent(timestamp, stationName, reason) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DockingCancelled":
                            {
                                string stationName = GetString(data, "StationName");
                                events.Add(new DockingCancelledEvent(timestamp, stationName) { raw = line });
                            }
                            handled = true;
                            break;
                        case "DockingTimeout":
                            {
                                string stationName = GetString(data, "StationName");
                                events.Add(new DockingTimedOutEvent(timestamp, stationName) { raw = line });
                            }
                            handled = true;
                            break;
                        case "MiningRefined":
                            {
                                string commodityName = GetString(data, "Type");

                                Commodity commodity = CommodityDefinitions.FromName(commodityName);
                                if (commodity == null)
                                {
                                    Logging.Error("Failed to map commodityrefined type " + commodityName + " to commodity");
                                }

                                events.Add(new CommodityRefinedEvent(timestamp, commodity) { raw = line });
                            }
                            handled = true;
                            break;
                        case "HeatWarning":
                            events.Add(new HeatWarningEvent(timestamp) { raw = line });
                            handled = true;
                            break;
                        case "HeatDamage":
                            events.Add(new HeatDamageEvent(timestamp) { raw = line });
                            handled = true;
                            break;
                        case "HullDamage":
                            {
                                decimal health = SensibleHealth(GetDecimal(data, "Health") * 100);
                                bool? piloted = GetOptionalBool(data, "PlayerPilot");
                                bool? fighter = GetOptionalBool(data, "Fighter");

                                string vehicle = EDDI.Core.Eddi.Instance.Vehicle;
                                if (fighter == true && piloted == false)
                                {
                                    vehicle = Constants.VEHICLE_FIGHTER;
                                }

                                events.Add(new HullDamagedEvent(timestamp, vehicle, piloted, health) { raw = line });
                            }
                            handled = true;
                            break;
                        case "ShieldState":
                            {
                                bool shieldsUp = GetBool(data, "ShieldsUp");
                                if (shieldsUp == true)
                                {
                                    events.Add(new ShieldsUpEvent(timestamp) { raw = line });
                                }
                                else
                                {
                                    events.Add(new ShieldsDownEvent(timestamp) { raw = line });
                                }

                                handled = true;
                                break;
                            }
                        case "SelfDestruct":
                            events.Add(new SelfDestructEvent(timestamp) { raw = line });
                            handled = true;
                            break;
                        case "Died":
                            {

                                List<string> names = new List<string>();
                                List<string> ships = new List<string>();
                                List<CombatRating> ratings = new List<CombatRating>();

                                if (data.ContainsKey("KillerName"))
                                {
                                    // Single killer
                                    names.Add(GetString(data, "KillerName"));
                                    ships.Add(GetString(data, "KillerShip"));
                                    ratings.Add(CombatRating.FromEDName(GetString(data, "KillerRank")));
                                }

                                if (data.ContainsKey("killers"))
                                {
                                    // Multiple killers
                                    data.TryGetValue("Killers", out val);
                                    List<object> killers = (List<object>)val;
                                    foreach (IDictionary<string, object> killer in killers)
                                    {
                                        names.Add(GetString(killer, "Name"));
                                        ships.Add(GetString(killer, "Ship"));
                                        ratings.Add(CombatRating.FromEDName(GetString(killer, "Rank")));
                                    }
                                }

                                events.Add(new DiedEvent(timestamp, names, ships, ratings) { raw = line });
                                handled = true;
                            }
                            break;
                        case "Resurrect":
                            {
                                string option = GetString(data, "Option");
                                long price = GetLong(data, "Cost");

                                if (option == "rebuy")
                                {
                                    events.Add(new ShipRepurchasedEvent(timestamp, price) { raw = line });
                                    handled = true;
                                }
                            }
                            break;
                        case "NavBeaconScan":
                            {
                                data.TryGetValue("NumBodies", out val);
                                int numbodies = (int)(long)val;
                                events.Add(new NavBeaconScanEvent(timestamp, numbodies) { raw = line });
                            }
                            handled = true;
                            break;
                        case "BuyExplorationData":
                            {
                                string system = GetString(data, "System");
                                long price = GetLong(data, "Cost");
                                events.Add(new ExplorationDataPurchasedEvent(timestamp, system, price) { raw = line });
                                handled = true;
                                break;
                            }
                        case "SellExplorationData":
                            {
                                data.TryGetValue("Systems", out val);
                                List<string> systems = ((List<object>)val).Cast<string>().ToList();
                                data.TryGetValue("Discovered", out val);
                                List<string> firsts = ((List<object>)val).Cast<string>().ToList();
                                data.TryGetValue("BaseValue", out val);
                                decimal reward = (long)val;
                                data.TryGetValue("Bonus", out val);
                                decimal bonus = (long)val;
                                events.Add(new ExplorationDataSoldEvent(timestamp, systems, firsts, reward, bonus)
                                {
                                    raw = line
                                });
                                handled = true;
                                break;
                            }
                        case "USSDrop":
                            {
                                string source = GetString(data, "USSType");
                                data.TryGetValue("USSThreat", out val);
                                int threat = (int)(long)val;
                                events.Add(new EnteredSignalSourceEvent(timestamp, source, threat) { raw = line });
                            }
                            handled = true;
                            break;
                        case "MarketBuy":
                            {
                                string commodityName = GetString(data, "Type");
                                Commodity commodity = CommodityDefinitions.FromName(commodityName);
                                if (commodity == null)
                                {
                                    Logging.Error("Failed to map marketbuy type " + commodityName + " to commodity");
                                }

                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                data.TryGetValue("BuyPrice", out val);
                                long price = (long)val;
                                events.Add(new CommodityPurchasedEvent(timestamp, commodity, amount, price) { raw = line });
                                handled = true;
                                break;
                            }
                        case "MarketSell":
                            {
                                string commodityName = GetString(data, "Type");
                                Commodity commodity = CommodityDefinitions.FromName(commodityName);
                                if (commodity == null)
                                {
                                    Logging.Error("Failed to map marketsell type " + commodityName + " to commodity");
                                }

                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                data.TryGetValue("SellPrice", out val);
                                long price = (long)val;
                                data.TryGetValue("AvgPricePaid", out val);
                                long buyPrice = (long)val;
                                // We don't care about buy price, we care about profit per unit
                                long profit = price - buyPrice;
                                bool? tmp = GetOptionalBool(data, "IllegalGoods");
                                bool illegal = tmp.HasValue ? (bool)tmp : false;
                                tmp = GetOptionalBool(data, "StolenGoods");
                                bool stolen = tmp.HasValue ? (bool)tmp : false;
                                tmp = GetOptionalBool(data, "BlackMarket");
                                bool blackmarket = tmp.HasValue ? (bool)tmp : false;
                                events.Add(new CommoditySoldEvent(timestamp, commodity, amount, price, profit, illegal,
                                    stolen, blackmarket)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "EngineerCraft":
                            {
                                string engineer = GetString(data, "Engineer");
                                string blueprint = GetString(data, "Blueprint");
                                data.TryGetValue("Level", out val);
                                int level = (int)(long)val;

                                List<CommodityAmount> commodities = new List<CommodityAmount>();
                                List<MaterialAmount> materials = new List<MaterialAmount>();
                                if (data.TryGetValue("Ingredients", out val))
                                {
                                    if (val is Dictionary<string, object>)
                                    {
                                        // 2.2 style
                                        Dictionary<string, object> usedData = (Dictionary<string, object>)val;
                                        foreach (KeyValuePair<string, object> used in usedData)
                                        {
                                            // Used could be a material or a commodity
                                            Commodity commodity = CommodityDefinitions.FromName(used.Key);
                                            if (commodity.category != null)
                                            {
                                                // This is a real commodity
                                                commodities.Add(new CommodityAmount(commodity, (int)(long)used.Value));
                                            }
                                            else
                                            {
                                                // Probably a material then
                                                Material material = Material.FromEDName(used.Key);
                                                materials.Add(new MaterialAmount(material, (int)(long)used.Value));
                                            }
                                        }
                                    }
                                    else if (val is List<object>)
                                    {
                                        // 2.3 style
                                        List<object> materialsJson = (List<object>)val;

                                        foreach (Dictionary<string, object> materialJson in materialsJson)
                                        {
                                            Material material = Material.FromEDName(GetString(materialJson, "Name"));
                                            materials.Add(new MaterialAmount(material, (int)(long)materialJson["Count"]));
                                        }
                                    }
                                }

                                events.Add(new ModificationCraftedEvent(timestamp, engineer, blueprint, level, materials,
                                    commodities)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "EngineerApply":
                            {
                                string engineer = GetString(data, "Engineer");
                                string blueprint = GetString(data, "Blueprint");
                                data.TryGetValue("Level", out val);
                                int level = (int)(long)val;

                                events.Add(new ModificationAppliedEvent(timestamp, engineer, blueprint,
                                    level)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "EngineerProgress":
                            {
                                string engineer = GetString(data, "Engineer");
                                data.TryGetValue("Rank", out val);
                                if (val == null)
                                {
                                    // There are other non-rank events for engineers but we don't pay attention to them
                                    break;
                                }

                                int rank = (int)(long)val;

                                events.Add(new EngineerProgressedEvent(timestamp, engineer, rank) { raw = line });
                                handled = true;
                                break;
                            }
                        case "LoadGame":
                            {
                                string commander = GetString(data, "Commander");

                                data.TryGetValue("ShipID", out val);
                                int? shipId = (int?)(long?)val;

                                if (shipId == null)
                                {
                                    // This happens if we are in CQC.  Flag it back to EDDI so that it ignores everything that happens until
                                    // we're out of CQC again
                                    events.Add(new EnteredCQCEvent(timestamp, commander) { raw = line });
                                    handled = true;
                                    break;
                                }

                                string ship = GetString(data, "Ship");
                                string shipName = GetString(data, "ShipName");
                                string shipIdent = GetString(data, "ShipIdent");

                                GameMode mode = GameMode.FromEDName(GetString(data, "GameMode"));
                                string group = GetString(data, "Group");
                                data.TryGetValue("Credits", out val);
                                decimal credits = (long)val;
                                data.TryGetValue("Loan", out val);
                                decimal loan = (long)val;
                                decimal? fuel = GetOptionalDecimal(data, "FuelLevel");
                                decimal? fuelCapacity = GetOptionalDecimal(data, "FuelCapacity");

                                events.Add(new CommanderContinuedEvent(timestamp, commander, (int)shipId, ship, shipName,
                                    shipIdent, mode, @group, credits, loan, fuel, fuelCapacity)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "CrewHire":
                            {
                                string name = GetString(data, "Name");
                                string faction = GetFaction(data, "Faction");
                                long price = GetLong(data, "Cost");
                                CombatRating rating = CombatRating.FromRank(GetInt(data, "CombatRank"));
                                events.Add(new CrewHiredEvent(timestamp, name, faction, price, rating) { raw = line });
                                handled = true;
                                break;
                            }
                        case "CrewFire":
                            {
                                string name = GetString(data, "Name");
                                events.Add(new CrewFiredEvent(timestamp, name) { raw = line });
                                handled = true;
                                break;
                            }
                        case "CrewAssign":
                            {
                                string name = GetString(data, "Name");
                                string role = GetRole(data, "Role");
                                events.Add(new CrewAssignedEvent(timestamp, name, role) { raw = line });
                                handled = true;
                                break;
                            }
                        case "JoinACrew":
                            {
                                string captain = GetString(data, "Captain");
                                captain = captain.Replace("$cmdr_decorate:#name=", "Commander ").Replace(";", "")
                                    .Replace("&", "Commander ");

                                events.Add(new CrewJoinedEvent(timestamp, captain) { raw = line });
                                handled = true;
                                break;
                            }
                        case "QuitACrew":
                            {
                                string captain = GetString(data, "Captain");
                                captain = captain.Replace("$cmdr_decorate:#name=", "Commander ").Replace(";", "")
                                    .Replace("&", "Commander ");

                                events.Add(new CrewLeftEvent(timestamp, captain) { raw = line });
                                handled = true;
                                break;
                            }
                        case "ChangeCrewRole":
                            {
                                string role = GetRole(data, "Role");
                                events.Add(new CrewRoleChangedEvent(timestamp, role) { raw = line });
                                handled = true;
                                break;
                            }
                        case "CrewMemberJoins":
                            {
                                string member = GetString(data, "Crew");
                                member = member.Replace("$cmdr_decorate:#name=", "Commander ").Replace(";", "")
                                    .Replace("&", "Commander ");

                                events.Add(new CrewMemberJoinedEvent(timestamp, member) { raw = line });
                                handled = true;
                                break;
                            }
                        case "CrewMemberQuits":
                            {
                                string member = GetString(data, "Crew");
                                member = member.Replace("$cmdr_decorate:#name=", "Commander ").Replace(";", "")
                                    .Replace("&", "Commander ");

                                events.Add(new CrewMemberLeftEvent(timestamp, member) { raw = line });
                                handled = true;
                                break;
                            }
                        case "CrewLaunchFighter":
                            {
                                string name = GetString(data, "Crew");
                                events.Add(new CrewMemberLaunchedEvent(timestamp, name) { raw = line });
                                handled = true;
                                break;
                            }
                        case "CrewMemberRoleChange":
                            {
                                string name = GetString(data, "Crew");
                                string role = GetRole(data, "Role");
                                events.Add(new CrewMemberRoleChangedEvent(timestamp, name, role) { raw = line });
                                handled = true;
                                break;
                            }
                        case "KickCrewMember":
                            {
                                string member = GetString(data, "Crew");
                                member = member.Replace("$cmdr_decorate:#name=", "Commander ").Replace(";", "")
                                    .Replace("&", "Commander ");

                                events.Add(new CrewMemberRemovedEvent(timestamp, member) { raw = line });
                                handled = true;
                                break;
                            }
                        case "BuyAmmo":
                            {
                                data.TryGetValue("Cost", out val);
                                long price = (long)val;
                                events.Add(new ShipRestockedEvent(timestamp, price) { raw = line });
                                handled = true;
                                break;
                            }
                        case "BuyDrones":
                            {
                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                data.TryGetValue("BuyPrice", out val);
                                long price = (long)val;
                                events.Add(new LimpetPurchasedEvent(timestamp, amount, price) { raw = line });
                                handled = true;
                                break;
                            }
                        case "SellDrones":
                            {
                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;
                                data.TryGetValue("SellPrice", out val);
                                long price = (long)val;
                                events.Add(new LimpetSoldEvent(timestamp, amount, price) { raw = line });
                                handled = true;
                                break;
                            }
                        case "ClearSavedGame":
                            {
                                string name = GetString(data, "Name");
                                events.Add(new ClearedSaveEvent(timestamp, name) { raw = line });
                                handled = true;
                                break;
                            }
                        case "NewCommander":
                            {
                                string name = GetString(data, "Name");
                                string package = GetString(data, "Package");
                                events.Add(new CommanderStartedEvent(timestamp, name, package) { raw = line });
                                handled = true;
                                break;
                            }
                        case "Progress":
                            {
                                data.TryGetValue("Combat", out val);
                                decimal combat = (long)val;
                                data.TryGetValue("Trade", out val);
                                decimal trade = (long)val;
                                data.TryGetValue("Explore", out val);
                                decimal exploration = (long)val;
                                data.TryGetValue("CQC", out val);
                                decimal cqc = (long)val;
                                data.TryGetValue("Empire", out val);
                                decimal empire = (long)val;
                                data.TryGetValue("Federation", out val);
                                decimal federation = (long)val;

                                events.Add(new CommanderProgressEvent(timestamp, combat, trade, exploration, cqc, empire,
                                    federation)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "Rank":
                            {
                                data.TryGetValue("Combat", out val);
                                CombatRating combat = CombatRating.FromRank((int)((long)val));
                                data.TryGetValue("Trade", out val);
                                TradeRating trade = TradeRating.FromRank((int)((long)val));
                                data.TryGetValue("Explore", out val);
                                ExplorationRating exploration = ExplorationRating.FromRank((int)((long)val));
                                data.TryGetValue("CQC", out val);
                                CQCRating cqc = CQCRating.FromRank((int)((long)val));
                                data.TryGetValue("Empire", out val);
                                EmpireRating empire = EmpireRating.FromRank((int)((long)val));
                                data.TryGetValue("Federation", out val);
                                FederationRating federation = FederationRating.FromRank((int)((long)val));

                                events.Add(new CommanderRatingsEvent(timestamp, combat, trade, exploration, cqc, empire,
                                    federation)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "Screenshot":
                            {
                                string filename = GetString(data, "Filename");
                                data.TryGetValue("Width", out val);
                                int width = (int)(long)val;
                                data.TryGetValue("Height", out val);
                                int height = (int)(long)val;
                                string system = GetString(data, "System");
                                string body = GetString(data, "Body");
                                decimal? latitude = GetOptionalDecimal(data, "Latitude");
                                decimal? longitude = GetOptionalDecimal(data, "Longitude");

                                events.Add(new ScreenshotEvent(timestamp, filename, width, height, system, body, longitude,
                                    latitude)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "BuyTradeData":
                            {
                                string system = GetString(data, "System");
                                data.TryGetValue("Cost", out val);
                                long price = (long)val;

                                events.Add(new TradeDataPurchasedEvent(timestamp, system, price) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PayFines":
                            {
                                data.TryGetValue("Amount", out val);
                                long amount = (long)val;
                                decimal? brokerpercentage = GetOptionalDecimal(data, "BrokerPercentage");

                                events.Add(new FinePaidEvent(timestamp, amount, brokerpercentage, false) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PayLegacyFines":
                            {
                                data.TryGetValue("Amount", out val);
                                long amount = (long)val;
                                decimal? brokerpercentage = GetOptionalDecimal(data, "BrokerPercentage");

                                events.Add(new FinePaidEvent(timestamp, amount, brokerpercentage, true) { raw = line });
                                handled = true;
                                break;
                            }
                        case "RefuelPartial":
                            {
                                decimal amount = GetDecimal(data, "Amount");
                                data.TryGetValue("Cost", out val);
                                long price = (long)val;

                                events.Add(new ShipRefuelledEvent(timestamp, "Market", price, amount, null) { raw = line });
                                handled = true;
                                break;
                            }
                        case "RefuelAll":
                            {
                                decimal amount = GetDecimal(data, "Amount");
                                data.TryGetValue("Cost", out val);
                                long price = (long)val;

                                events.Add(new ShipRefuelledEvent(timestamp, "Market", price, amount, null) { raw = line });
                                handled = true;
                                break;
                            }
                        case "FuelScoop":
                            {
                                decimal amount = GetDecimal(data, "Scooped");
                                decimal total = GetDecimal(data, "Total");

                                events.Add(new ShipRefuelledEvent(timestamp, "Scoop", null, amount, total) { raw = line });
                                handled = true;
                                break;
                            }
                        case "Friends":
                            {
                                string status = GetString(data, "Status");
                                string name = GetString(data, "Name");
                                name = name.Replace("$cmdr_decorate:#name=", "Commander ").Replace(";", "")
                                    .Replace("&", "Commander ");

                                Friend cmdr = new Friend();
                                cmdr.name = name;
                                cmdr.status = status;

                                /// Does this friend exist in our friends list?
                                List<Friend> friends = EDDI.Core.Eddi.Instance.Cmdr.friends;
                                int index = friends.FindIndex(friend => friend.name == name);
                                if (index >= 0)
                                {
                                    if (friends[index].status != cmdr.status)
                                    {
                                        /// This is a known friend: replace in situ (this is more efficient than removing and re-adding).
                                        friends[index] = cmdr;
                                    }
                                }
                                else
                                {
                                    /// This is a new friend, add them to the list
                                    friends.Add(cmdr);
                                    events.Add(new FriendsEvent(timestamp, name, status) { raw = line });
                                }

                                handled = true;
                                break;
                            }
                        case "JetConeBoost":
                            {
                                decimal boost = GetDecimal(data, "BoostValue");

                                events.Add(new JetConeBoostEvent(timestamp, boost) { raw = line });
                                handled = true;
                                break;
                            }
                        case "RedeemVoucher":
                            {

                                string type = GetString(data, "Type");
                                List<Reward> rewards = new List<Reward>();

                                // Obtain list of factions
                                data.TryGetValue("Factions", out val);
                                List<object> factionsData = (List<object>)val;
                                if (factionsData != null)
                                {
                                    foreach (Dictionary<string, object> rewardData in factionsData)
                                    {
                                        string factionName = GetFaction(rewardData, "Faction");
                                        rewardData.TryGetValue("Amount", out val);
                                        long factionReward = (long)val;

                                        rewards.Add(new Reward(factionName, factionReward));
                                    }
                                }

                                data.TryGetValue("Amount", out val);
                                long amount = (long)val;

                                decimal? brokerpercentage = GetOptionalDecimal(data, "BrokerPercentage");

                                if (type == "bounty")
                                {
                                    events.Add(new BountyRedeemedEvent(timestamp, rewards, amount, brokerpercentage)
                                    {
                                        raw = line
                                    });
                                }
                                else if (type == "CombatBond")
                                {
                                    events.Add(new BondRedeemedEvent(timestamp, rewards, amount, brokerpercentage)
                                    {
                                        raw = line
                                    });
                                }
                                else if (type == "trade")
                                {
                                    events.Add(new TradeVoucherRedeemedEvent(timestamp, rewards, amount, brokerpercentage)
                                    {
                                        raw = line
                                    });
                                }
                                else if (type == "settlement" || type == "scannable")
                                {
                                    events.Add(new DataVoucherRedeemedEvent(timestamp, rewards, amount, brokerpercentage)
                                    {
                                        raw = line
                                    });
                                }
                                else
                                {
                                    Logging.Warn("Unhandled voucher type " + type);
                                    Logging.Report("Unhandled voucher type " + type);
                                }

                                handled = true;
                                break;
                            }
                        case "CommunityGoal":
                            {

                                // There may be multiple goals in each ev. We add them all to lists
                                data.TryGetValue("CurrentGoals", out val);
                                List<object> goalsdata = (List<object>)val;

                                // Create empty lists
                                List<long> cgid = new List<long>();
                                List<string> name = new List<string>();
                                List<string> system = new List<string>();
                                List<string> station = new List<string>();
                                List<long> expiry = new List<long>();
                                List<bool> iscomplete = new List<bool>();
                                List<int> total = new List<int>();
                                List<int> contribution = new List<int>();
                                List<int> contributors = new List<int>();
                                List<decimal> percentileband = new List<decimal>();

                                List<int?> topranksize = new List<int?>();
                                List<bool?> toprank = new List<bool?>();

                                List<string> tier = new List<string>();
                                List<long?> tierreward = new List<long?>();

                                // Fill the lists
                                foreach (IDictionary<string, object> goaldata in goalsdata)
                                {
                                    cgid.Add(GetLong(goaldata, "CGID"));
                                    name.Add(GetString(goaldata, "Title"));
                                    system.Add(GetString(goaldata, "SystemName"));
                                    station.Add(GetString(goaldata, "MarketName"));
                                    DateTime expiryDateTime = ((DateTime)goaldata["Expiry"]).ToUniversalTime();
                                    long expiryseconds = (long)(expiryDateTime - timestamp).TotalSeconds;
                                    expiry.Add(expiryseconds);
                                    iscomplete.Add(GetBool(goaldata, "IsComplete"));
                                    total.Add(GetInt(goaldata, "CurrentTotal"));
                                    contribution.Add(GetInt(goaldata, "PlayerContribution"));
                                    contributors.Add(GetInt(goaldata, "NumContributors"));
                                    percentileband.Add(GetDecimal(goaldata, "PlayerPercentileBand"));

                                    // If the community goal is constructed with a fixed-size top rank (ie max reward for top 10 players)

                                    topranksize.Add(GetOptionalInt(goaldata, "TopRankSize"));
                                    toprank.Add(GetOptionalBool(goaldata, "PlayerInTopRank"));

                                    // If the community goal has reached the first success tier

                                    goaldata.TryGetValue("TierReached", out val);
                                    tier.Add((string)val);
                                    tierreward.Add(GetOptionalLong(goaldata, "Bonus"));
                                }

                                events.Add(new CommunityGoalEvent(timestamp, cgid, name, system, station, expiry,
                                    iscomplete, total, contribution, contributors, percentileband, topranksize, toprank,
                                    tier, tierreward)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "CommunityGoalJoin":
                            {
                                string name = GetString(data, "Name");
                                string system = GetString(data, "System");

                                events.Add(new MissionAcceptedEvent(timestamp, null, name, system, null, null, null, null,
                                    null, null, null, null, null, true, null, null, null)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "CommunityGoalReward":
                            {
                                string name = GetString(data, "Name");
                                string system = GetString(data, "System");
                                data.TryGetValue("Reward", out val);
                                long reward = (val == null ? 0 : (long)val);

                                events.Add(new MissionCompletedEvent(timestamp, null, name, null, null, null, true, reward,
                                    null, 0)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "MissionAccepted":
                            {
                                data.TryGetValue("MissionID", out val);
                                long missionid = (long)val;
                                data.TryGetValue("Expiry", out val);
                                DateTime? expiry = (val == null ? (DateTime?)null : (DateTime)val);
                                string name = GetString(data, "Name");
                                string faction = GetFaction(data, "Faction");

                                // Missions with destinations
                                string destinationsystem = GetString(data, "DestinationSystem");
                                string destinationstation = GetString(data, "DestinationStation");

                                // Missions with commodities
                                Commodity commodity = CommodityDefinitions.FromName(GetString(data, "Commodity"));
                                data.TryGetValue("Count", out val);
                                int? amount = (int?)(long?)val;

                                // Missions with targets
                                string target = GetString(data, "Target");
                                string targettype = GetString(data, "TargetType");
                                string targetfaction = GetFaction(data, "TargetFaction");
                                data.TryGetValue("KillCount", out val);
                                if (val != null)
                                {
                                    amount = (int?)(long?)val;
                                }

                                // Missions with passengers
                                string passengertype = GetString(data, "PassengerType");
                                bool? passengerswanted = GetOptionalBool(data, "PassengerWanted");
                                data.TryGetValue("PassengerCount", out val);
                                if (val != null)
                                {
                                    amount = (int?)(long?)val;
                                }

                                // Impact on influence and reputation
                                string influence = GetString(data, "Influence");
                                string reputation = GetString(data, "Reputation");

                                events.Add(new MissionAcceptedEvent(timestamp, missionid, name, faction, destinationsystem,
                                    destinationstation, commodity, amount, passengertype, passengerswanted, target,
                                    targettype, targetfaction, false, expiry, influence, reputation)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "MissionCompleted":
                            {
                                data.TryGetValue("MissionID", out val);
                                long missionid = (long)val;
                                string name = GetString(data, "Name");
                                data.TryGetValue("Reward", out val);
                                long reward = (val == null ? 0 : (long)val);
                                data.TryGetValue("Donation", out val);
                                long donation = (val == null ? 0 : (long)val);
                                string faction = GetFaction(data, "Faction");

                                // Missions with commodities
                                Commodity commodity = CommodityDefinitions.FromName(GetString(data, "Commodity"));
                                data.TryGetValue("Count", out val);
                                int? amount = (int?)(long?)val;

                                List<CommodityAmount> commodityrewards = new List<CommodityAmount>();
                                data.TryGetValue("CommodityReward", out val);
                                List<object> commodityRewardsData = (List<object>)val;
                                if (commodityRewardsData != null)
                                {
                                    foreach (Dictionary<string, object> commodityRewardData in commodityRewardsData)
                                    {
                                        Commodity rewardCommodity =
                                            CommodityDefinitions.FromName(GetString(commodityRewardData, "Name"));
                                        commodityRewardData.TryGetValue("Count", out val);
                                        int count = (int)(long)val;
                                        commodityrewards.Add(new CommodityAmount(rewardCommodity, count));
                                    }
                                }

                                events.Add(new MissionCompletedEvent(timestamp, missionid, name, faction, commodity, amount,
                                    false, reward, commodityrewards, donation)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "MissionAbandoned":
                            {
                                data.TryGetValue("MissionID", out val);
                                long missionid = (long)val;
                                string name = GetString(data, "Name");
                                events.Add(new MissionAbandonedEvent(timestamp, missionid, name) { raw = line });
                                handled = true;
                                break;
                            }
                        case "MissionRedirected":
                            {
                                data.TryGetValue("MissionID", out val);
                                long missionid = (long)val;
                                string name = GetString(data, "MissionName");
                                string newdestinationstation = GetString(data, "NewDestinationStation");
                                string olddestinationstation = GetString(data, "OldDestinationStation");
                                string newdestinationsystem = GetString(data, "NewDestinationSystem");
                                string olddestinationsystem = GetString(data, "OldDestinationSystem");
                                events.Add(new MissionRedirectedEvent(timestamp, missionid, name, newdestinationstation,
                                    olddestinationstation, newdestinationsystem, olddestinationsystem)
                                { raw = line });
                                handled = true;
                                break;
                            }
                        case "MissionFailed":
                            {
                                data.TryGetValue("MissionID", out val);
                                long missionid = (long)val;
                                string name = GetString(data, "Name");
                                events.Add(new MissionFailedEvent(timestamp, missionid, name) { raw = line });
                                handled = true;
                                break;
                            }
                        case "SearchAndRescue":
                            {
                                string commodityName = GetString(data, "Name");
                                Commodity commodity = CommodityDefinitions.FromName(GetString(data, "Name"));
                                if (commodity == null)
                                {
                                    Logging.Error("Failed to map SearchAndRescue commodity type " + commodityName +
                                                  " to commodity");
                                }

                                data.TryGetValue("Count", out val);
                                int? amount = (int?)(long?)val;
                                data.TryGetValue("Reward", out val);
                                long reward = (val == null ? 0 : (long)val);
                                events.Add(new SearchAndRescueEvent(timestamp, commodity, amount, reward) { raw = line });
                                handled = true;
                                break;
                            }
                        case "AfmuRepairs":
                            AfmuRepairsEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "Repair":
                            RepairEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "RepairDrone":
                            {
                                decimal? hull = GetOptionalDecimal(data, "HullRepaired");
                                decimal? cockpit = GetOptionalDecimal(data, "CockpitRepaired");
                                decimal? corrosion = GetOptionalDecimal(data, "CorrosionRepaired");

                                events.Add(new ShipRepairDroneEvent(timestamp, hull, cockpit, corrosion) { raw = line });
                                handled = true;
                                break;
                            }
                        case "RepairAll":
                            {
                                data.TryGetValue("Cost", out val);
                                long price = (long)val;
                                events.Add(new ShipRepairedEvent(timestamp, null, price) { raw = line });
                                handled = true;
                                break;
                            }
                        case "RebootRepair":
                            RebootRepairEvent(line, data, events, timestamp);
                            handled = true;
                            break;
                        case "Synthesis":
                            SynthesisEvent(line, events, data, timestamp);
                            handled = true;
                            break;
                        case "Materials":
                            MaterialsEvent(line, data, events);
                            handled = true;
                            break;
                        case "Cargo":
                            {
                                List<Cargo> inventory = new List<Cargo>();

                                data.TryGetValue("Inventory", out val);
                                if (val != null)
                                {
                                    List<object> inventoryJson = (List<object>)val;
                                    foreach (Dictionary<string, object> cargoJson in inventoryJson)
                                    {
                                        Cargo cargo = new Cargo();
                                        cargo.commodity = CommodityDefinitions.FromName(GetString(cargoJson, "Name"));
                                        cargo.amount = GetInt(cargoJson, "Count");
                                        inventory.Add(cargo);
                                    }
                                }

                                events.Add(new CargoInventoryEvent(DateTime.Now, inventory) { raw = line });
                            }
                            handled = true;
                            break;
                        case "PowerplayJoin":
                            {
                                string power = GetString(data, "Power");

                                events.Add(new PowerJoinedEvent(timestamp, power) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PowerplayLeave":
                            {
                                string power = GetString(data, "Power");

                                events.Add(new PowerLeftEvent(timestamp, power) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PowerplayDefect":
                            {
                                string frompower = GetString(data, "FromPower");
                                string topower = GetString(data, "ToPower");

                                events.Add(new PowerDefectedEvent(timestamp, frompower, topower) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PowerplayVote":
                            {
                                string power = GetString(data, "Power");
                                string system = GetString(data, "System");
                                data.TryGetValue("Votes", out val);
                                int amount = (int)(long)val;

                                events.Add(new PowerPreparationVoteCast(timestamp, power, system, amount) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PowerplaySalary":
                            {
                                string power = GetString(data, "Power");
                                data.TryGetValue("Amount", out val);
                                int amount = (int)(long)val;

                                events.Add(new PowerSalaryClaimedEvent(timestamp, power, amount) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PowerplayCollect":
                            {
                                string power = GetString(data, "Power");
                                // Currently using localised information as we don't have commodity definitions for all powerplay commodities
                                string commodity = GetString(data, "Type_Localised");
                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;

                                events.Add(
                                    new PowerCommodityObtainedEvent(timestamp, power, commodity, amount) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PowerplayDeliver":
                            {
                                string power = GetString(data, "Power");
                                // Currently using localised information as we don't have commodity definitions for all powerplay commodities
                                string commodity = GetString(data, "Type_Localised");
                                data.TryGetValue("Count", out val);
                                int amount = (int)(long)val;

                                events.Add(new PowerCommodityDeliveredEvent(timestamp, power, commodity, amount)
                                {
                                    raw = line
                                });
                                handled = true;
                                break;
                            }
                        case "PowerplayFastTrack":
                            {
                                string power = GetString(data, "Power");
                                data.TryGetValue("Cost", out val);
                                int amount = (int)(long)val;

                                events.Add(new PowerCommodityFastTrackedEvent(timestamp, power, amount) { raw = line });
                                handled = true;
                                break;
                            }
                        case "PowerplayVoucher":
                            {
                                string power = GetString(data, "Power");
                                data.TryGetValue("Systems", out val);
                                List<string> systems = ((List<object>)val).Cast<string>().ToList();

                                events.Add(new PowerVoucherReceivedEvent(timestamp, power, systems) { raw = line });
                                handled = true;
                                break;
                            }
                        case "SystemsShutdown":
                            {
                                events.Add(new ShipShutdownEvent(timestamp) { raw = line });
                                handled = true;
                                break;
                            }
                        case "Fileheader":
                            {
                                string filename = JournalFileName;
                                string version = GetString(data, "gameversion");
                                string build = GetString(data, "build").Replace(" ", "");

                                events.Add(new FileHeaderEvent(timestamp, filename, version, build) { raw = line });
                                handled = true;
                                break;
                            }
                    }

                    if (!handled)
                    {
                        Logging.Debug("Unhandled ev: " + line);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Warn("Failed to parse line: " + ex);
                Logging.Error("Exception whilst parsing line", line);
            }

            return events;
        }

        private static void BountyEvent(string line, IDictionary<string, object> data, ICollection<Event> events, DateTime timestamp)
        {

            string target = GetString(data, "Target");
            if (target != null)
            {
                // Target might be a ship, but if not then the string we provide is repopulated in ship.model so use it regardless
                Ship ship = ShipDefinitions.FromEDModel(target);
                target = ship.model;
            }

            var victimFaction = GetFaction(data, "VictimFaction");

            data.TryGetValue("SharedWithOthers", out object val);
            bool shared = val != null && (long) val == 1;

            long reward;
            var rewards = new List<Reward>();

            if (data.ContainsKey("Reward"))
            {
                // Old-style
                data.TryGetValue("Reward", out val);
                reward = (long) val;
                if (reward == 0)
                {
                    // 0-credit reward; ignore
                    return;
                }

                string factionName = GetFaction(data, "Faction");
                rewards.Add(new Reward(factionName, reward));
            }
            else
            {
                data.TryGetValue("TotalReward", out val);
                reward = (long) val;
                if (reward == 0)
                {
                    // 0-credit reward; ignore
                    return;
                }

                // Obtain list of rewards
                data.TryGetValue("Rewards", out val);
                List<object> rewardsData = (List<object>) val;
                if (rewardsData != null)
                {
                    foreach (Dictionary<string, object> rewardData in rewardsData)
                    {
                        string factionName = GetFaction(rewardData, "Faction");
                        rewardData.TryGetValue("Reward", out val);
                        long factionReward = (long) val;

                        rewards.Add(new Reward(factionName, factionReward));
                    }
                }
            }

            events.Add(
                new BountyAwardedEvent(timestamp, target, victimFaction, reward, rewards, shared)
                {
                    raw = line
                });
        }

        private static void LoadoutEvent(string line, IDictionary<string, object> data, List<Event> events, DateTime timestamp)
        {
            object val;

            data.TryGetValue("ShipID", out val);
            int shipId = (int) (long) val;
            string ship = GetString(data, "Ship");
            string shipName = GetString(data, "ShipName");
            string shipIdent = GetString(data, "ShipIdent");

            data.TryGetValue("Modules", out val);
            List<object> modulesData = (List<object>) val;

            string paintjob = null;
            List<Hardpoint> hardpoints = new List<Hardpoint>();
            List<Compartment> compartments = new List<Compartment>();
            if (modulesData != null)
            {
                foreach (Dictionary<string, object> moduleData in modulesData)
                {
                    // Common items
                    string slot = GetString(moduleData, "Slot");
                    string item = GetString(moduleData, "Item");
                    bool enabled = GetBool(moduleData, "On");
                    int priority = GetInt(moduleData, "Priority");
                    // Health is as 0->1 but we want 0->100, and to a sensible number of decimal places
                    decimal health = GetDecimal(moduleData, "Health") * 100;
                    if (health < 5)
                    {
                        health = Math.Round(health, 1);
                    }
                    else
                    {
                        health = Math.Round(health);
                    }

                    long price = GetLong(moduleData, "Value");

                    // Ammunition
                    int? clip = GetOptionalInt(moduleData, "AmmoInClip");
                    int? hopper = GetOptionalInt(moduleData, "AmmoInHopper");

                    if (slot.Contains("Hardpoint"))
                    {
                        // This is a hardpoint
                        Hardpoint hardpoint = new Hardpoint() {name = slot};
                        if (hardpoint.name.StartsWith("Tiny"))
                        {
                            hardpoint.size = 0;
                        }
                        else if (hardpoint.name.StartsWith("Small"))
                        {
                            hardpoint.size = 1;
                        }
                        else if (hardpoint.name.StartsWith("Medium"))
                        {
                            hardpoint.size = 2;
                        }
                        else if (hardpoint.name.StartsWith("Large"))
                        {
                            hardpoint.size = 3;
                        }
                        else if (hardpoint.name.StartsWith("Huge"))
                        {
                            hardpoint.size = 4;
                        }

                        Module module = ModuleDefinitions.fromEDName(item);
                        if (module == null)
                        {
                            Logging.Info("Unknown module " + item);
                            Logging.Report("Unknown module " + item,
                                JsonConvert.SerializeObject(moduleData));
                        }
                        else
                        {
                            module.enabled = enabled;
                            module.priority = priority;
                            module.health = health;
                            module.price = price;
                            module.clipcapacity = clip;
                            module.hoppercapacity = hopper;
                            hardpoint.module = module;
                            hardpoints.Add(hardpoint);
                        }
                    }
                    else if (slot == "PaintJob")
                    {
                        // This is a paintjob
                        paintjob = item;
                    }
                    else if (slot == "PlanetaryApproachSuite")
                    {
                        // Ignore planetary approach suite for now
                    }
                    else if (slot.StartsWith("Bobble"))
                    {
                        // Ignore bobbles
                    }
                    else if (slot.StartsWith("Decal"))
                    {
                        // Ignore decals
                    }
                    else if (slot == "WeaponColour")
                    {
                        // Ignore weapon colour
                    }
                    else if (slot == "EngineColour")
                    {
                        // Ignore engine colour
                    }
                    else if (slot.StartsWith("ShipKit"))
                    {
                        // Ignore ship kits
                    }
                    else if (slot.StartsWith("ShipName") || slot.StartsWith("ShipID"))
                    {
                        // Ignore nameplates
                    }
                    else
                    {
                        // This is a compartment
                        Compartment compartment = new Compartment() {name = slot};
                        Module module = ModuleDefinitions.fromEDName(item);
                        if (module == null)
                        {
                            Logging.Info("Unknown module " + item);
                            Logging.Report("Unknown module " + item,
                                JsonConvert.SerializeObject(moduleData));
                        }
                        else
                        {
                            module.enabled = enabled;
                            module.priority = priority;
                            module.health = health;
                            module.price = price;
                            compartment.module = module;
                            compartments.Add(compartment);
                        }
                    }
                }
            }

            events.Add(new ShipLoadoutEvent(timestamp, ship, shipId, shipName, shipIdent, compartments,
                    hardpoints, paintjob)
                {raw = line});
        }

        private static void ReceivedTextEvent(string line, IDictionary<string, object> data, List<Event> events, DateTime timestamp)
        {
            string from = GetString(data, "From");
            string channel = GetString(data, "Channel");
            string message = GetString(data, "Message");
            string source = "";

            if (
                channel == "player" ||
                channel == "wing" ||
                channel == "friend" ||
                channel == "voicechat" ||
                channel == "local" ||
                channel == null
            )
            {
                // Give priority to player messages
                source = channel == "wing"
                    ? "Wing mate"
                    : (channel == null ? "Crew mate" : "Commander");
                channel = channel == null ? "multicrew" : channel;
                events.Add(new MessageReceivedEvent(timestamp, @from, source, true, channel, message)
                {
                    raw = line
                });
            }
            else
            {
                // This is NPC speech.  What's the source?
                if (@from.Contains("npc_name_decorate"))
                {
                    source = NpcSpeechBy(@from, message);
                    @from = @from.Replace("$npc_name_decorate:#name=", "").Replace(";", "");
                }
                else if (@from.Contains("ShipName_"))
                {
                    source = NpcSpeechBy(@from, message);
                    @from = GetString(data, "From_Localised");
                }
                else if ((message.StartsWith("$STATION_")) || message.Contains("$Docking"))
                {
                    source = "Station";
                }
                else
                {
                    source = "NPC";
                }

                events.Add(new MessageReceivedEvent(timestamp, @from, source, false, channel,
                    GetString(data, "Message_Localised")));

                // See if we also want to spawn a specific ev as well?
                if (message == "$STATION_NoFireZone_entered;")
                {
                    events.Add(new StationNoFireZoneEnteredEvent(timestamp, false) {raw = line});
                }
                else if (message == "$STATION_NoFireZone_entered_deployed;")
                {
                    events.Add(new StationNoFireZoneEnteredEvent(timestamp, true) {raw = line});
                }
                else if (message == "$STATION_NoFireZone_exited;")
                {
                    events.Add(new StationNoFireZoneExitedEvent(timestamp) {raw = line});
                }
                else if (message.Contains("_StartInterdiction"))
                {
                    // Find out who is doing the interdicting
                    string by = NpcSpeechBy(@from, message);

                    events.Add(new NPCInterdictionCommencedEvent(timestamp, @by) {raw = line});
                }
                else if (message.Contains("_Attack") || message.Contains("_OnAttackStart") ||
                         message.Contains("AttackRun") || message.Contains("OnDeclarePiracyAttack"))
                {
                    // Find out who is doing the attacking
                    string by = NpcSpeechBy(@from, message);
                    events.Add(new NPCAttackCommencedEvent(timestamp, @by) {raw = line});
                }
                else if (message.Contains("_OnStartScanCargo"))
                {
                    // Find out who is doing the scanning
                    string by = NpcSpeechBy(@from, message);
                    events.Add(new NPCCargoScanCommencedEvent(timestamp, @by) {raw = line});
                }
            }
        }

        private static void AfmuRepairsEvent(string line, IDictionary<string, object> data, List<Event> events, DateTime timestamp)
        {
            string item = GetString(data, "Module");
            // Item might be a module
            Module module = ModuleDefinitions.fromEDName(item);
            if (module != null)
            {
                if (module.mount != null)
                {
                    // This is a weapon so provide a bit more information
                    string mount;
                    if (module.mount == Module.ModuleMount.Fixed)
                    {
                        mount = "fixed";
                    }
                    else if (module.mount == Module.ModuleMount.Gimballed)
                    {
                        mount = "gimballed";
                    }
                    else
                    {
                        mount = "turreted";
                    }

                    item = "" + module.@class.ToString() + module.grade + " " + mount + " " +
                           module.name;
                }
                else
                {
                    item = module.name;
                }
            }

            bool repairedfully = GetBool(data, "FullyRepaired");
            decimal health = GetDecimal(data, "Health");

            events.Add(new ShipAfmuRepairedEvent(timestamp, item, repairedfully, health) { raw = line });
        }

        private static void RepairEvent(string line, IDictionary<string, object> data, List<Event> events, DateTime timestamp)
        {
            object val;
            string item = GetString(data, "Item");
            // Item might be a module
            Module module = ModuleDefinitions.fromEDName(item);
            if (module != null)
            {
                if (module.mount != null)
                {
                    // This is a weapon so provide a bit more information
                    string mount;
                    if (module.mount == Module.ModuleMount.Fixed)
                    {
                        mount = "fixed";
                    }
                    else if (module.mount == Module.ModuleMount.Gimballed)
                    {
                        mount = "gimballed";
                    }
                    else
                    {
                        mount = "turreted";
                    }

                    item = "" + module.@class.ToString() + module.grade + " " + mount + " " +
                           module.name;
                }
                else
                {
                    item = module.name;
                }
            }

            data.TryGetValue("Cost", out val);
            long price = (long)val;
            events.Add(new ShipRepairedEvent(timestamp, item, price) { raw = line });
        }

        private static void RebootRepairEvent(string line, IDictionary<string, object> data, List<Event> events, DateTime timestamp)
        {
            data.TryGetValue("Modules", out var val);
            List<object> modulesJson = (List<object>)val;

            List<string> modules = new List<string>();
            foreach (string module in modulesJson)
            {
                modules.Add(module);
            }

            events.Add(new ShipRebootedEvent(timestamp, modules) { raw = line });
        }

        private static void SynthesisEvent(string line, List<Event> events, IDictionary<string, object> data, DateTime timestamp)
        {
            string synthesis = GetString(data, "Name");

            data.TryGetValue("Materials", out var val);
            List<MaterialAmount> materials = new List<MaterialAmount>();
            if (val is Dictionary<string, object>)
            {
                // 2.2 style
                Dictionary<string, object> materialsData = (Dictionary<string, object>)val;
                if (materialsData != null)
                {
                    foreach (KeyValuePair<string, object> materialData in materialsData)
                    {
                        Material material = Material.FromEDName(materialData.Key);
                        materials.Add(new MaterialAmount(material, (int)(long)materialData.Value));
                    }
                }
            }
            else if (val is List<object>)
            {
                // 2.3 style
                List<object> materialsJson = (List<object>)val;

                foreach (Dictionary<string, object> materialJson in materialsJson)
                {
                    Material material = Material.FromEDName(GetString(materialJson, "Name"));
                    materials.Add(new MaterialAmount(material, (int)(long)materialJson["Count"]));
                }
            }

            events.Add(new SynthesisedEvent(timestamp, synthesis, materials) { raw = line });
        }

        private static void MaterialsEvent(string line, IDictionary<string, object> data, List<Event> events)
        {
            object val;
            List<MaterialAmount> materials = new List<MaterialAmount>();

            data.TryGetValue("Raw", out val);
            if (val != null)
            {
                List<object> materialsJson = (List<object>)val;
                foreach (Dictionary<string, object> materialJson in materialsJson)
                {
                    Material material = Material.FromEDName(GetString(materialJson, "Name"));
                    material.category = "Element";
                    materials.Add(new MaterialAmount(material, (int)(long)materialJson["Count"]));
                }
            }

            data.TryGetValue("Manufactured", out val);
            if (val != null)
            {
                List<object> materialsJson = (List<object>)val;
                foreach (Dictionary<string, object> materialJson in materialsJson)
                {
                    Material material = Material.FromEDName(GetString(materialJson, "Name"));
                    material.category = "Manufactured";
                    materials.Add(new MaterialAmount(material, (int)(long)materialJson["Count"]));
                }
            }

            data.TryGetValue("Encoded", out val);
            if (val != null)
            {
                List<object> materialsJson = (List<object>)val;
                foreach (Dictionary<string, object> materialJson in materialsJson)
                {
                    Material material = Material.FromEDName(GetString(materialJson, "Name"));
                    material.category = "Data";
                    materials.Add(new MaterialAmount(material, (int)(long)materialJson["Count"]));
                }
            }

            events.Add(new MaterialInventoryEvent(DateTime.Now, materials) { raw = line });
        }

        private static bool ScanEvent(string line, IDictionary<string, object> data, ICollection<Event> events, DateTime timestamp)
        {
            string name = GetString(data, "BodyName");
            decimal distancefromarrival = GetDecimal(data, "DistanceFromArrivalLS");

            // Belt
            if (name.Contains("Belt Cluster"))
            {
                events.Add(new BeltScannedEvent(timestamp, name, distancefromarrival) { raw = line });
                return true;
            }

            // Common items
            decimal radius = GetDecimal(data, "Radius");
            decimal? orbitalperiod = GetOptionalDecimal(data, "OrbitalPeriod");
            decimal rotationperiod = GetDecimal(data, "RotationPeriod");
            decimal? semimajoraxis = GetOptionalDecimal(data, "SemiMajorAxis");
            decimal? eccentricity = GetOptionalDecimal(data, "Eccentricity");
            decimal? orbitalinclination = GetOptionalDecimal(data, "OrbitalInclination");
            decimal? periapsis = GetOptionalDecimal(data, "Periapsis");

            // Check whether we have a detailed discovery scanner on board the current ship
            bool dssEquipped = false;
            Ship ship = EDDI.Core.Eddi.Instance.CurrentShip;
            if (ship != null)
            {
                foreach (Compartment compartment in ship.compartments)
                {
                    if ((compartment.module.name == "Detailed Surface Scanner") &&
                        (compartment.module.enabled))
                    {
                        dssEquipped = true;
                    }
                }
            }

            // Rings
            data.TryGetValue("Rings", out object val);
            List<object> ringsData = (List<object>)val;
            List<Ring> rings = new List<Ring>();
            if (ringsData != null)
            {
                foreach (Dictionary<string, object> ringData in ringsData)
                {
                    string ringName = GetString(ringData, "Name");
                    string ringComposition =
                        Composition.FromEDName(GetString(ringData, "RingClass")).name;
                    decimal ringMass = GetDecimal(ringData, "MassMT");
                    decimal ringInnerRadius = GetDecimal(ringData, "InnerRad");
                    decimal ringOuterRadius = GetDecimal(ringData, "OuterRad");

                    rings.Add(new Ring(ringName, ringComposition, ringMass, ringInnerRadius,
                        ringOuterRadius));
                }
            }

            if (data.ContainsKey("StarType"))
            {
                // Star
                string starType = GetString(data, "StarType");
                decimal stellarMass = GetDecimal(data, "StellarMass");
                decimal absoluteMagnitude = GetDecimal(data, "AbsoluteMagnitude");
                string luminosityClass = GetString(data, "Luminosity");
                data.TryGetValue("Age_MY", out val);
                long ageMegaYears = (long)val;
                decimal temperature = GetDecimal(data, "SurfaceTemperature");

                events.Add(new StarScannedEvent(timestamp, name, starType, stellarMass, radius,
                        absoluteMagnitude, luminosityClass, ageMegaYears, temperature, distancefromarrival,
                        orbitalperiod, rotationperiod, semimajoraxis, eccentricity, orbitalinclination,
                        periapsis, rings, dssEquipped)
                { raw = line });
            }
            else
            {
                // Body
                bool? tidallyLocked = GetOptionalBool(data, "TidalLock");

                string bodyClass = GetString(data, "PlanetClass");
                decimal? earthMass = GetOptionalDecimal(data, "MassEM");

                // MKW: Gravity in the Journal is in m/s; must convert it to G
                decimal gravity = Body.ms2g(GetDecimal(data, "SurfaceGravity"));

                decimal? temperature = GetOptionalDecimal(data, "SurfaceTemperature");

                decimal? pressure = GetOptionalDecimal(data, "SurfacePressure");

                bool? landable = GetOptionalBool(data, "Landable");

                string reserves = GetString(data, "ReserveLevel");

                decimal? axialTilt = GetOptionalDecimal(data, "AxialTilt");

                // TODO atmosphere composition

                data.TryGetValue("Materials", out val);
                List<MaterialPresence> materials = new List<MaterialPresence>();
                if (val != null)
                {
                    if (val is Dictionary<string, object>)
                    {
                        // 2.2 style
                        IDictionary<string, object> materialsData = (IDictionary<string, object>)val;
                        foreach (KeyValuePair<string, object> kv in materialsData)
                        {
                            Material material = Material.FromEDName(kv.Key);
                            if (material != null)
                            {
                                materials.Add(new MaterialPresence(material,
                                    GetDecimal("Amount", kv.Value)));
                            }
                        }
                    }
                    else if (val is List<object>)
                    {
                        // 2.3 style
                        List<object> materialsJson = (List<object>)val;

                        foreach (Dictionary<string, object> materialJson in materialsJson)
                        {
                            Material material = Material.FromEDName((string)materialJson["Name"]);
                            materials.Add(new MaterialPresence(material,
                                GetDecimal(materialJson, "Percent")));
                        }
                    }
                }

                string terraformState = GetString(data, "TerraformState");
                string atmosphere = GetString(data, "Atmosphere");
                Volcanism volcanism = Volcanism.FromName(GetString(data, "Volcanism"));

                events.Add(new BodyScannedEvent(timestamp, name, bodyClass, earthMass, radius, gravity,
                        temperature, pressure, tidallyLocked, landable, atmosphere, volcanism,
                        distancefromarrival, (decimal)orbitalperiod, rotationperiod, semimajoraxis,
                        eccentricity, orbitalinclination, periapsis, rings, reserves, materials,
                        terraformState, axialTilt, dssEquipped)
                { raw = line });
            }

            return true;
        }

        private static void FSDJumpEvent(string line, IDictionary<string, object> data, ICollection<Event> events, DateTime timestamp)
        {
            string systemName = GetString(data, "StarSystem");
            data.TryGetValue("StarPos", out var val);
            List<object> starPos = (List<object>)val;
            decimal x = Math.Round(GetDecimal("X", starPos[0]) * 32) / (decimal)32.0;
            decimal y = Math.Round(GetDecimal("Y", starPos[1]) * 32) / (decimal)32.0;
            decimal z = Math.Round(GetDecimal("Z", starPos[2]) * 32) / (decimal)32.0;

            decimal fuelUsed = GetDecimal(data, "FuelUsed");
            decimal fuelRemaining = GetDecimal(data, "FuelLevel");
            decimal distance = GetDecimal(data, "JumpDist");
            Superpower allegiance = GetAllegiance(data, "SystemAllegiance");
            string faction = GetFaction(data, "SystemFaction");
            State factionState = State.FromEDName(GetString(data, "FactionState"));
            Economy economy = Economy.FromEDName(GetString(data, "SystemEconomy"));
            Government government = Government.FromEDName(GetString(data, "SystemGovernment"));
            SecurityLevel security = SecurityLevel.FromEDName(GetString(data, "SystemSecurity"));
            long? population = GetOptionalLong(data, "Population");

            events.Add(new JumpedEvent(timestamp, systemName, x, y, z, distance, fuelUsed,
                fuelRemaining, allegiance, faction, factionState, economy, government, security,
                population)
            { raw = line });
        }

        private static void DockedEvent(string line, IDictionary<string, object> data, ICollection<Event> events, DateTime timestamp)
        {
            string systemName = GetString(data, "StarSystem");
            string stationName = GetString(data, "StationName");
            string stationState = GetString(data, "StationState") ?? string.Empty;
            string stationModel = GetString(data, "StationType");
            Superpower allegiance = GetAllegiance(data, "StationAllegiance");
            string faction = GetFaction(data, "StationFaction");
            State factionState = State.FromEDName(GetString(data, "FactionState"));
            Economy economy = Economy.FromEDName(GetString(data, "StationEconomy"));
            Government government = Government.FromEDName(GetString(data, "StationGovernment"));
            decimal? distancefromstar = GetOptionalDecimal(data, "DistFromStarLS");

            // Get station services data
            data.TryGetValue("StationServices", out var val);
            List<string> stationservices = (val as List<object>)?.Cast<string>().ToList();

            events.Add(new DockedEvent(timestamp, systemName, stationName, stationState, stationModel,
                allegiance, faction, factionState, economy, government, distancefromstar,
                stationservices)
            { raw = line });
        }
    }
}
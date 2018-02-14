using System.Collections.Generic;
using System.Windows.Controls;
using EddiDataDefinitions;
using EddiEvents;
using EddiShipMonitor;
using EddiStarMapService;
using EDDI.Core;
using Utilities;

namespace EddiEdsmResponder
{
    public class EDSMResponder : IEDDIResponder
    {
        private StarMapService _starMapService;
        private string _system;

        public EDSMResponder()
        {
            Logging.Info("Initialised " + ResponderName + " " + ResponderVersion);
        }

        public string ResponderName => "EDSM responder";

        public string ResponderVersion => "1.0.0";

        public string ResponderDescription =>
            "Send details of your travels to EDSM.  EDSM is a third-party tool that provides information on the locations of star systems and keeps a log of the star systems you have visited.  It uses the data provided to crowd-source a map of the galaxy";

        public bool Start()
        {
            Reload();
            return _starMapService != null;
        }

        public void Stop()
        {
            _starMapService = null;
        }

        public void Reload()
        {
            // Set up the star map service
            var starMapCredentials = StarMapConfiguration.FromFile();
            if (starMapCredentials?.ApiKey != null)
            {
                // Commander name might come from star map credentials or the companion app's profile
                string commanderName = null;
                if (starMapCredentials.CommanderName != null)
                    commanderName = starMapCredentials.CommanderName;
                else if (Eddi.Instance.Cmdr != null) commanderName = Eddi.Instance.Cmdr.name;
                if (commanderName != null)
                    _starMapService = new StarMapService(starMapCredentials.ApiKey, commanderName);
            }
        }

        public void Handle(Event theEvent)
        {
            if (Eddi.Instance.InCqc) return;

            if (Eddi.Instance.InCrew) return;

            if (Eddi.Instance.InBeta) return;

            if (_starMapService != null)
                switch (theEvent)
                {
                    case JumpedEvent _:
                        var jumpedEvent = (JumpedEvent) theEvent;

                        if (jumpedEvent.system != _system)
                        {
                            Logging.Debug("Sending jump data to EDSM (jumped)");
                            _starMapService.SendStarMapLog(jumpedEvent.timestamp, jumpedEvent.system, jumpedEvent.x,
                                jumpedEvent.y, jumpedEvent.z);
                            _system = jumpedEvent.system;
                        }

                        break;
                    case CommanderContinuedEvent _:
                        var continuedEvent = (CommanderContinuedEvent) theEvent;
                        _starMapService.SendCredits(continuedEvent.credits, continuedEvent.loan);
                        break;
                    case MaterialInventoryEvent _:
                        var materialInventoryEvent = (MaterialInventoryEvent) theEvent;
                        var materials = new Dictionary<string, int>();
                        var data = new Dictionary<string, int>();
                        foreach (var ma in materialInventoryEvent.inventory)
                        {
                            var material = Material.FromEDName(ma.edname);
                            if (material.category == "Element" || material.category == "Manufactured")
                                materials.Add(material.EDName, ma.amount);
                            else
                                data.Add(material.EDName, ma.amount);
                        }

                        _starMapService.SendMaterials(materials);
                        _starMapService.SendData(data);
                        break;
                    case ShipLoadoutEvent _:
                        var shipLoadoutEvent = (ShipLoadoutEvent) theEvent;
                        var ship =
                            ((ShipMonitor) Eddi.Instance.ObtainMonitor("Ship monitor"))
                            .GetShip(shipLoadoutEvent.shipid);
                        _starMapService.SendShip(ship);
                        break;
                    case ShipSwappedEvent _:
                        var shipSwappedEvent = (ShipSwappedEvent) theEvent;
                        if (shipSwappedEvent.shipid.HasValue)
                            _starMapService.SendShipSwapped((int) shipSwappedEvent.shipid);

                        break;
                    case ShipSoldEvent _:
                        var shipSoldEvent = (ShipSoldEvent) theEvent;
                        if (shipSoldEvent.shipid.HasValue) _starMapService.SendShipSold((int) shipSoldEvent.shipid);

                        break;
                    case ShipDeliveredEvent _:
                        var shipDeliveredEvent = (ShipDeliveredEvent) theEvent;
                        if (shipDeliveredEvent.shipid.HasValue)
                            _starMapService.SendShipSwapped((int) shipDeliveredEvent.shipid);

                        break;
                    case CommanderProgressEvent _:
                        var progressEvent = (CommanderProgressEvent) theEvent;
                        if (Eddi.Instance.Cmdr != null && Eddi.Instance.Cmdr.federationrating != null)
                            _starMapService.SendRanks(Eddi.Instance.Cmdr.combatrating.rank, (int) progressEvent.combat,
                                Eddi.Instance.Cmdr.traderating.rank, (int) progressEvent.trade,
                                Eddi.Instance.Cmdr.explorationrating.rank, (int) progressEvent.exploration,
                                Eddi.Instance.Cmdr.cqcrating.rank, (int) progressEvent.cqc,
                                Eddi.Instance.Cmdr.federationrating.rank, (int) progressEvent.federation,
                                Eddi.Instance.Cmdr.empirerating.rank, (int) progressEvent.empire);

                        break;
                }
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }
    }
}
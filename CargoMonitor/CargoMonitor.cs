using EDDI;
using EddiDataDefinitions;
using EddiEvents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using EDDI.Core;
using Utilities;

namespace EddiCargoMonitor
{
    /**
     * Monitor cargo for the current ship
     * Missing: there is no ev for when a drone is fired, so we cannot keep track of this individually.  Instead we have to rely
     * on the inventory events to give us information on the number of drones in-ship.
     */
    public class CargoMonitor : IEDDIMonitor
    {
        // Observable collection for us to handle changes
        public ObservableCollection<Cargo> Inventory = new ObservableCollection<Cargo>();

        public string MonitorName => "Cargo monitor";

        public string MonitorVersion => "1.0.0";

        public string MonitorDescription => "Track information on your cargo.";

        public bool IsRequired => true;

        public CargoMonitor() => Logging.Info($"Initialised {MonitorName} {MonitorVersion}");

        public bool NeedsStart => false;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Reload()
        {
        }

        public UserControl ConfigurationTabItem()
        {
            return null;
        }

        public void HandleProfile(JObject profile)
        {
        }

        public void PostHandle(Event ev)
        {
        }

        public void PreHandle(Event ev)
        {
            Logging.Debug("Received ev " + JsonConvert.SerializeObject(ev));

            // Handle the events that we care about
            switch (ev)
            {
                case CargoInventoryEvent _:
                    HandleCargoInventoryEvent((CargoInventoryEvent)ev);
                    break;
                case CommodityCollectedEvent _:
                    break;
                case CommodityEjectedEvent _:
                    break;
                case CommodityPurchasedEvent _:
                    break;
                case CommodityRefinedEvent _:
                    break;
                case CommoditySoldEvent _:
                    break;
                case PowerCommodityObtainedEvent _:
                    break;
                case PowerCommodityDeliveredEvent _:
                    break;
                case LimpetPurchasedEvent _:
                    break;
                case LimpetSoldEvent _:
                    break;
                case MissionAbandonedEvent _:
                    // If we abandon a mission with cargo it becomes stolen
                    break;
                case MissionAcceptedEvent _:
                    // Check to see if this is a cargo mission and update our inventory accordingly
                    break;
                case MissionCompletedEvent _:
                    // Check to see if this is a cargo mission and update our inventory accordingly
                    break;
                case MissionFailedEvent _:
                    // If we fail a mission with cargo it becomes stolen
                    break;
            }

            // TODO Powerplay events
        }

        private void HandleCargoInventoryEvent(CargoInventoryEvent ev)
        {
            // CargoInventoryEvent does not contain stolen, missionid, or cost information so merge it here
            foreach (var cargo in ev.inventory)
            {
                var added = Inventory.Any(x => x.commodity == cargo.commodity);
                if (!added)
                    AddCargo(cargo);
            }

            Inventory.Clear();
            foreach (var cargo in ev.inventory)
            {
                Inventory.Add(cargo);
            }
        }

        public IDictionary<string, object> GetVariables()
        {
            var variables = new Dictionary<string, object>
            {
                ["cargo"] = new List<Cargo>(Inventory)
            };
            return variables;
        }

        private static void CheckApplication()
        {
            if (Application.Current == null)
            {
                new Application();
            }
        }

        public void AddCargo(Cargo cargo)
        {
            // If we were started from VoiceAttack then we might not have an application; check here and create if it doesn't exist
            CheckApplication();

            // Run this on the dispatcher to ensure that we can update it whilst reflecting changes in the UI
            if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
            {
                AddCargoInternal(cargo);
            }
            else
            {
                if (Application.Current != null)
                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal,
                        new Action(() => { AddCargoInternal(cargo); }));
            }
        }

        public void RemoveCargo(Cargo cargo)
        {
            CheckApplication();

            // Run this on the dispatcher to ensure that we can update it whilst reflecting changes in the UI
            if (Application.Current.Dispatcher.CheckAccess())
            {
                RemoveCargoInternal(cargo);
            }
            else
            {
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    RemoveCargoInternal(cargo);
                }));
            }
        }

        private void AddCargoInternal(Cargo cargo)
        {
            foreach (Cargo inventoryCargo in Inventory)
            {
                if (inventoryCargo.commodity == cargo.commodity)
                {
                    // Matching commodity; see if the details match
                    if (inventoryCargo.missionid != null)
                    {
                        // Mission-specific cargo
                        if (inventoryCargo.missionid == cargo.missionid)
                        {
                            // Both for the same mission - add to this
                            inventoryCargo.amount += cargo.amount;
                            return;
                        }
                        // Different mission; skip
                        continue;
                    }

                    if (inventoryCargo.stolen == cargo.stolen)
                    {
                        // Both of the same legality
                        if (inventoryCargo.price == cargo.price)
                        {
                            // Same cost basis - add to this
                            inventoryCargo.amount += cargo.amount;
                            return;
                        }
                    }
                }
            }
            // No matching cargo - add entry
            Inventory.Add(cargo);
        }

        private void RemoveCargoInternal(Cargo cargo)
        {
            for (int i = 0; i < Inventory.Count; i++)
            {
                Cargo inventoryCargo = Inventory[i];
                if (inventoryCargo.commodity == cargo.commodity)
                {
                    // Matching commodity; see if the details match
                    if (inventoryCargo.missionid != null)
                    {
                        // Mission-specific cargo
                        if (inventoryCargo.missionid == cargo.missionid)
                        {
                            // Both for the same mission - remove from this
                            if (inventoryCargo.amount == cargo.amount)
                            {
                                Inventory.RemoveAt(i);
                                return;
                            }
                            else
                            {
                                inventoryCargo.amount -= cargo.amount;
                            }
                            return;
                        }
                        // Different mission; skip
                        continue;
                    }

                    if (inventoryCargo.stolen == cargo.stolen)
                    {
                        // Both of the same legality
                        if (inventoryCargo.price == cargo.price)
                        {
                            // Same cost basis - remove from this
                            if (inventoryCargo.amount == cargo.amount)
                            {
                                Inventory.RemoveAt(i);
                                return;
                            }
                            else
                            {
                                inventoryCargo.amount -= cargo.amount;
                            }
                            return;
                        }
                    }
                }
            }
            // No matching cargo - ignore
            Logging.Debug($"Did not find match for cargo {JsonConvert.SerializeObject(cargo)}");
        }
    }
}

using System.Collections.Generic;
using EddiDataDefinitions;
using System.Windows.Controls;
using System;
using EddiEvents;
using Utilities;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using System.Threading;
using EDDI.Core;
using Utilities.Collections;

namespace EddiMaterialMonitor
{
    /// <summary>
    /// A monitor that keeps track of the number of materials held and sends events on user-defined changes
    /// </summary>
    public class MaterialMonitor : IEDDIMonitor
    {
        // Observable collection for us to handle
        public SynchronizedObservableCollection<MaterialAmount> Inventory = new SynchronizedObservableCollection<MaterialAmount>();
        private static readonly object InventoryLock = new object();

        // The material monitor both consumes and emits events, but only one for a given ev.  We hold any pending events here so
        // they are fired at the correct time
        private readonly ConcurrentQueue<Event> _pendingEvents = new ConcurrentQueue<Event>();

        public string MonitorName => "Material monitor";

        public string MonitorVersion => "1.0.0";

        public string MonitorDescription => "Track the amount of materials and generate events when limits are reached.";

        public bool IsRequired => true;

        public MaterialMonitor()
        {
            ReadMaterials();
            PopulateMaterialBlueprints();
            PopulateMaterialLocations();
            Logging.Info("Initialised " + MonitorName + " " + MonitorVersion);
        }

        public bool NeedsStart => false;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Reload()
        {
            ReadMaterials();
            Logging.Info("Reloaded " + MonitorName + " " + MonitorVersion);
        }

        public UserControl ConfigurationTabItem()
        {
            return new ConfigurationWindow();
        }

        public void PreHandle(Event ev)
        {
            Logging.Debug("Received ev " + JsonConvert.SerializeObject(ev));

            // Handle the events that we care about
            switch (ev)
            {
                case MaterialInventoryEvent _:
                    HandleMaterialInventoryEvent((MaterialInventoryEvent)ev);
                    break;
                case MaterialCollectedEvent _:
                    HandleMaterialCollectedEvent((MaterialCollectedEvent)ev);
                    break;
                case MaterialDiscardedEvent _:
                    HandleMaterialDiscardedEvent((MaterialDiscardedEvent)ev);
                    break;
                case MaterialDonatedEvent _:
                    HandleMaterialDonatedEvent((MaterialDonatedEvent)ev);
                    break;
                case SynthesisedEvent _:
                    HandleSynthesisedEvent((SynthesisedEvent)ev);
                    break;
                case ModificationCraftedEvent _:
                    HandleModificationCraftedEvent((ModificationCraftedEvent)ev);
                    break;
            }
        }

        // Flush any pending events
        public void PostHandle(Event ev)
        {
            // Spin out ev in to a different thread to stop blocking
            var thread = new Thread(() =>
            {
                try
                {
                    while (_pendingEvents.TryDequeue(out var pendingEvent))
                    {
                        Eddi.Instance.EventHandler(pendingEvent);
                    }
                }
                catch (ThreadAbortException)
                {
                    Logging.Debug("Thread aborted");
                }
            })
            {
                IsBackground = true
            };
            thread.Start();
        }

        private void HandleMaterialInventoryEvent(MaterialInventoryEvent @event)
        {
            // BUG
            var knownNames = new List<string>();
            foreach (MaterialAmount materialAmount in @event.inventory)
            {
                SetMaterial(materialAmount.edname, materialAmount.amount);
                knownNames.Add(materialAmount.edname);
            }

            // Update configuration information
            WriteMaterials();
        }

        private void HandleMaterialCollectedEvent(MaterialCollectedEvent @event)
        {
            IncMaterial(@event.edname, @event.amount);
        }

        private void HandleMaterialDiscardedEvent(MaterialDiscardedEvent @event)
        {
            DecMaterial(@event.edname, @event.amount);
        }

        private void HandleMaterialDonatedEvent(MaterialDonatedEvent @event)
        {
            DecMaterial(@event.edname, @event.amount);
        }

        private void HandleSynthesisedEvent(SynthesisedEvent @event)
        {
            foreach (var component in @event.materials)
            {
                DecMaterial(component.edname, component.amount);
            }
        }

        private void HandleModificationCraftedEvent(ModificationCraftedEvent @event)
        {
            foreach (var component in @event.materials)
            {
                DecMaterial(component.edname, component.amount);
            }
        }

        public void HandleProfile(JObject profile)
        {
        }

        /// <summary>
        /// Increment the current amount of a material, potentially triggering events as a result
        /// </summary>
        private void IncMaterial(string edname, int amount)
        {
            lock (InventoryLock)
            {
                var material = Material.FromEDName(edname);
                var ma = Inventory.FirstOrDefault(inv => inv.edname == material.EDName);
                if (ma == null)
                {
                    // No information for the current material - create one and set it to 0
                    ma = new MaterialAmount(material, 0);
                    Inventory.Add(ma);
                }

                int previous = ma.amount;
                ma.amount += amount;
                Logging.Debug(ma.edname + ": " + previous + "->" + ma.amount);

                if (previous <= ma.maximum && ma.amount > ma.maximum)
                {
                    // We have crossed the high water threshold for this material
                    _pendingEvents.Enqueue(new MaterialThresholdEvent(DateTime.Now, Material.FromEDName(edname), "Maximum", (int)ma.maximum, ma.amount, "Increase"));
                }

                if (previous < ma.desired && ma.amount >= ma.desired)
                {
                    // We have crossed the desired threshold for this material
                    _pendingEvents.Enqueue(new MaterialThresholdEvent(DateTime.Now, Material.FromEDName(edname), "Desired", (int)ma.desired, ma.amount, "Increase"));
                }

                WriteMaterials();
            }
        }

        /// <summary>
        /// Decrement the current amount of a material, potentially triggering events as a result
        /// </summary>
        private void DecMaterial(string edname, int amount)
        {
            lock (InventoryLock)
            {
                Material material = Material.FromEDName(edname);
                MaterialAmount ma = Inventory.FirstOrDefault(inv => inv.edname == material.EDName);
                if (ma == null)
                {
                    // No information for the current material - create one and set it to amount
                    ma = new MaterialAmount(material, amount);
                    Inventory.Add(ma);
                }

                int previous = ma.amount;
                ma.amount -= amount;
                Logging.Debug(ma.edname + ": " + previous + "->" + ma.amount);

                // We have limits for this material; carry out relevant checks
                if (previous >= ma.minimum && ma.amount < ma.minimum)
                {
                    // We have crossed the low water threshold for this material
                    _pendingEvents.Enqueue(new MaterialThresholdEvent(DateTime.Now, Material.FromEDName(edname), "Minimum", (int)ma.minimum, ma.amount, "Decrease"));
                }

                if (previous >= ma.desired && ma.amount < ma.desired)
                {
                    // We have crossed the desired threshold for this material
                    _pendingEvents.Enqueue(new MaterialThresholdEvent(DateTime.Now, Material.FromEDName(edname), "Desired", (int)ma.desired, ma.amount, "Decrease"));
                }

                WriteMaterials();
            }
        }

        /// <summary>
        /// Set the current amount of a material
        /// </summary>
        private void SetMaterial(string edname, int amount)
        {
            lock (InventoryLock)
            {
                Material material = Material.FromEDName(edname);
                MaterialAmount ma = Inventory.FirstOrDefault(inv => inv.edname == material.EDName);
                if (ma == null)
                {
                    // No information for the current material - create one and set it to amount
                    ma = new MaterialAmount(material, amount);
                    Logging.Debug(ma.edname + ": " + ma.amount);
                    Inventory.Add(ma);
                }
                ma.amount = amount;
            }
        }

        public IDictionary<string, object> GetVariables()
        {
            lock (InventoryLock)
            {
                IDictionary<string, object> variables = new Dictionary<string, object>
                {
                    ["materials"] = new List<MaterialAmount>(Inventory)
                };
                return variables;
            }
        }

        public void WriteMaterials()
        {
            lock (InventoryLock)
            {
                // Write material configuration with current inventory
                var configuration = new MaterialMonitorConfiguration { Materials = Inventory };
                configuration.ToFile();
            }
        }

        private void ReadMaterials()
        {
            lock (InventoryLock)
            {
                // Obtain current inventory from  configuration
                MaterialMonitorConfiguration configuration = MaterialMonitorConfiguration.FromFile();

                // Build a new inventory
                List<MaterialAmount> newInventory = new List<MaterialAmount>();

                // Start with the materials we have in the log
                foreach (MaterialAmount ma in configuration.Materials)
                {
                    // Fix up & add any materials that are not deprecated material names 
                    if (Material.DeprecatedMaterials(ma.material) == false)
                    {
                        bool addToInv = false;
                        // if the edname is not set, or
                        if (ma.edname == null)
                        {
                            addToInv = true;
                        }
                        // if the edname is UNIQUE to the collection, or
                        else if (configuration.Materials.Any(item => item.edname == ma.edname) == false)
                        {
                            addToInv = true;
                        }
                        // if the EDNAME IS NOT UNIQUE to the collection, the MATERIAL NAME IS UNIQUE, & THE EDNAME DOESN'T MATCH THE MATERIAL NAME 
                        // (once an EDName is established, this will identify & "heal" any duplicate entries having the same EDName in the materialmonitor)
                        else if (configuration.Materials.Any(item => item.edname == ma.edname) &&
                            configuration.Materials.Any(item => item.material == ma.material) &&
                            (ma.edname != ma.material))
                        {
                            addToInv = true;
                        }
                        // then add the material to the new inventory list, preserving user preferences for that material
                        if (addToInv)
                        {
                            MaterialAmount ma2 = new MaterialAmount(ma.material, ma.amount, ma.minimum, ma.desired, ma.maximum);
                            newInventory.Add(ma2);
                        }
                    }
                }

                // Add in any new materials
                foreach (Material material in Material.MATERIALS)
                {
                    MaterialAmount ma = newInventory.FirstOrDefault(inv => inv.edname == material.EDName);
                    if (ma == null)
                    {
                        // We don't have this one - add it and set it to zero
                        if ((Material.DeprecatedMaterials(material.name) == false))
                        {
                            Logging.Debug("Adding new material " + material.name + " to the materials list");
                            ma = new MaterialAmount(material, 0);
                            newInventory.Add(ma);
                        }
                    }
                }

                // Now order the list by name
                newInventory = newInventory.OrderBy(m => m.material).ToList();

                // Update the inventory 
                Inventory.Clear();
                foreach (MaterialAmount ma in newInventory)
                {
                    Inventory.Add(ma);
                }
            }
        }

        private void PopulateMaterialBlueprints()
        {
            string data = Net.DownloadString(Constants.EDDI_SERVER_URL + "materialuses.json");
            if (data != null)
            {
                var blueprints = JsonConvert.DeserializeObject<Dictionary<string, List<Blueprint>>>(data);
                foreach (KeyValuePair<string, List<Blueprint>> kv in blueprints)
                {
                    Material material = Material.MATERIALS.FirstOrDefault(m => m.name == kv.Key);
                    if (material != null)
                    {
                        material.blueprints = kv.Value;
                    }
                }
            }
        }

        private void PopulateMaterialLocations()
        {
            string data = Net.DownloadString(Constants.EDDI_SERVER_URL + "materiallocations.json");
            if (data != null)
            {
                Dictionary<string, Dictionary<string, object>> locations = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(data);
                foreach (KeyValuePair<string, Dictionary<string, object>> kv in locations)
                {
                    Material material = Material.MATERIALS.FirstOrDefault(m => m.name == kv.Key);
                    if (material != null)
                    {
                        material.location = (string)kv.Value["location"];
                    }
                }
            }
        }
    }
}

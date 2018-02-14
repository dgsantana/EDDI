using EddiDataDefinitions;
using EddiEvents;
using System;
using System.Collections.Generic;

namespace EddiMaterialMonitor
{
    class MaterialThresholdEvent : Event
    {
        public const string NAME = "Material threshold";
        public const string DESCRIPTION = "Triggered when a material reaches a threshold";
        public static Dictionary<string, string> VARIABLES = new Dictionary<string, string>();
        public static MaterialThresholdEvent SAMPLE = new MaterialThresholdEvent(DateTime.Now, Material.AnomalousBulkScanData, "Minimum", 6, 5, "Reduction");

        static MaterialThresholdEvent()
        {
            VARIABLES.Add("material", "The material");
            VARIABLES.Add("level", "The level that has been triggered (Minimum/Desired/Maximum)");
            VARIABLES.Add("limit", "The amount of the limit that has been passed");
            VARIABLES.Add("amount", "The current amount of the material");
            VARIABLES.Add("change", "The change to the inventory (Increase/Reduction)");
        }

        public Material Material { get; }
        public string Level { get; }
        public int Limit { get; }
        public int Amount { get; }
        public string Change { get; }

        public MaterialThresholdEvent(DateTime timestamp, Material material, string level, int limit, int amount, string change) : base(timestamp, NAME)
        {
            Material = material;
            Level = level;
            Limit = limit;
            Amount = amount;
            Change = change;
        }
    }
}

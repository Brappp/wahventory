using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace WahVentory;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    
    // Inventory Settings
    public InventorySettings InventorySettings { get; set; } = new();

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

[Serializable]
public class InventorySettings
{
    // Safety Filters
    public SafetyFilters SafetyFilters { get; set; } = new();
    
    // Market Price Settings
    public bool ShowMarketPrices { get; set; } = false;
    public int PriceCacheDurationMinutes { get; set; } = 30;
    public bool AutoRefreshPrices { get; set; } = true;
    
    // UI State
    public Dictionary<uint, bool> ExpandedCategories { get; set; } = new();
    
    // Blacklisted items (user-defined)
    public HashSet<uint> BlacklistedItems { get; set; } = new();
}

[Serializable]
public class SafetyFilters
{
    public bool FilterUltimateTokens { get; set; } = true;
    public bool FilterCurrencyItems { get; set; } = true;
    public bool FilterCrystalsAndShards { get; set; } = true;
    public bool FilterGearsetItems { get; set; } = true;
    public bool FilterIndisposableItems { get; set; } = true;
    public bool FilterHighLevelGear { get; set; } = true;
    public bool FilterUniqueUntradeable { get; set; } = true;
    public bool FilterHQItems { get; set; } = false;
    public bool FilterCollectables { get; set; } = false;
    public bool FilterSpiritbondedItems { get; set; } = false;
    
    // Configurable thresholds
    public uint MaxGearItemLevel { get; set; } = 600;
    public float MinSpiritbondToFilter { get; set; } = 100.0f;
}

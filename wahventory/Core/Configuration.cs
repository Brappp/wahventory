using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace wahventory.Core;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    
    public InventorySettings InventorySettings { get; set; } = new();

    [NonSerialized]
    public Action? Save;
}

[Serializable]
public class InventorySettings
{
    public SafetyFilters SafetyFilters { get; set; } = new();
    
    public bool ShowMarketPrices { get; set; } = false;
    public int PriceCacheDurationMinutes { get; set; } = 30;
    public bool AutoRefreshPrices { get; set; } = true;
    
    public Dictionary<uint, bool> ExpandedCategories { get; set; } = new();
    
    public PassiveDiscardSettings PassiveDiscard { get; set; } = new();
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
    
    public uint MaxGearItemLevel { get; set; } = 600;
    public float MinSpiritbondToFilter { get; set; } = 100.0f;
}

[Serializable]
public class PassiveDiscardSettings
{
    public bool Enabled { get; set; } = false;
    public int IdleTimeSeconds { get; set; } = 30;
    public int DiscardIntervalSeconds { get; set; } = 5;
}

using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace wahventory.Core;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;

    public InventorySettings InventorySettings { get; set; } = new();

    public SearchBarSettings SearchBarSettings { get; set; } = new();

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

    public uint MaxGearItemLevel { get; set; } = 600;
}

[Serializable]
public class PassiveDiscardSettings
{
    public bool Enabled { get; set; } = false;
    public int IdleTimeSeconds { get; set; } = 30;
    public int DiscardIntervalSeconds { get; set; } = 5;
}

[Serializable]
public class SearchBarSettings
{
    public bool AutoFocus { get; set; } = false;
    public bool AutoClear { get; set; } = true;
    public bool HighlightTabs { get; set; } = true;

    public KeyBindSettings Keybind { get; set; } = new();
    public bool KeybindOnly { get; set; } = true;
    public bool KeybindPassthrough { get; set; } = false;

    public int SearchBarWidth { get; set; } = 100;
    public Vector4 SearchBarBackgroundColor { get; set; } = new(0.1f, 0.1f, 0.1f, 1f);
    public Vector4 SearchBarTextColor { get; set; } = Vector4.One;

    public int NormalInventoryOffset { get; set; } = 20;
    public int LargeInventoryOffset { get; set; } = 0;
    public int LargestInventoryOffset { get; set; } = 0;
    public int ChocoboInventoryOffset { get; set; } = 0;
    public int RetainerInventoryOffset { get; set; } = 18;
    public int LargeRetainerInventoryOffset { get; set; } = 0;
    public int ArmouryInventoryOffset { get; set; } = 30;

    public string TagSeparatorCharacter { get; set; } = ":";
    public string SearchTermsSeparatorCharacter { get; set; } = " ";

    public FilterSettings NameFilter { get; set; } = new() { Enabled = true, RequireTag = false, Tag = "name", AbbreviatedTag = "n" };
    public FilterSettings TypeFilter { get; set; } = new() { Enabled = true, RequireTag = true, Tag = "type", AbbreviatedTag = "t" };
    public FilterSettings JobFilter { get; set; } = new() { Enabled = true, RequireTag = true, Tag = "job", AbbreviatedTag = "j" };
    public FilterSettings LevelFilter { get; set; } = new() { Enabled = true, RequireTag = true, Tag = "level", AbbreviatedTag = "l" };
}

[Serializable]
public class KeyBindSettings
{
    public int Key { get; set; } = 70; // F key
    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; } = false;
    public bool Shift { get; set; } = false;
}

[Serializable]
public class FilterSettings
{
    public bool Enabled { get; set; } = true;
    public bool RequireTag { get; set; } = false;
    public string Tag { get; set; } = "";
    public string AbbreviatedTag { get; set; } = "";
}

using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace wahventory.Models;

public class InventoryItemInfo
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public InventoryType Container { get; set; }
    public short Slot { get; set; }
    public bool IsHQ { get; set; }
    public ushort IconId { get; set; }
    public bool CanBeDiscarded { get; set; }
    public bool CanBeTraded { get; set; }
    public bool IsCollectable { get; set; }
    public int SpiritBond { get; set; }
    public int Durability { get; set; }
    public int MaxDurability { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public uint ItemUICategory { get; set; }
    
    // Safety assessment properties
    public uint ItemLevel { get; set; }
    public uint EquipLevel { get; set; }
    public byte Rarity { get; set; }
    public bool IsUnique { get; set; }
    public bool IsUntradable { get; set; }
    public bool IsIndisposable { get; set; }
    public uint EquipSlotCategory { get; set; }
    public SafetyAssessment? SafetyAssessment { get; set; }
    
    // UI state
    public bool IsSelected { get; set; }
    
    // Market price data
    public long? MarketPrice { get; set; }
    public DateTime? MarketPriceFetchTime { get; set; }
    
    public bool IsGear => ItemUICategory >= 35 && ItemUICategory <= 44;
    
    public string GetUniqueKey() => $"{Container}_{Slot}";
    
    public string GetFormattedPrice()
    {
        if (!MarketPrice.HasValue) return "---";
        if (MarketPrice.Value == -1) return "N/A";
        return $"{MarketPrice.Value:N0} gil";
    }
}

public class SafetyAssessment
{
    public uint ItemId { get; set; }
    public bool IsSafeToDiscard { get; set; }
    public List<string> SafetyFlags { get; set; } = new();
    public SafetyFlagColor FlagColor { get; set; } = SafetyFlagColor.None;
}

public enum SafetyFlagColor
{
    None,
    Info,       // Blue - informational
    Caution,    // Yellow - proceed with caution  
    Warning,    // Orange - potentially valuable
    Critical    // Red - definitely do not discard
}

public class CategoryGroup
{
    public uint CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<InventoryItemInfo> Items { get; set; } = new();
    public bool IsExpanded { get; set; } = true;
    
    public int TotalQuantity => Items.Sum(i => i.Quantity);
    public long? TotalValue => Items.All(i => i.MarketPrice.HasValue && i.MarketPrice.Value > 0) 
        ? Items.Sum(i => i.MarketPrice!.Value * i.Quantity) 
        : null;
}

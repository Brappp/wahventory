using System;
using System.Collections.Generic;
using System.Linq;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public class ItemFilterService
{
    public IEnumerable<InventoryItemInfo> ApplyFilters(
        IEnumerable<InventoryItemInfo> items,
        SafetyFilters filters,
        HashSet<uint> blacklistedItems,
        string? searchFilter = null)
    {
        var filtered = items.AsEnumerable();
        
        // Apply search filter first
        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            filtered = filtered.Where(i => i.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase));
        }
        
        // Apply safety filters
        if (filters.FilterUltimateTokens)
            filtered = filtered.Where(i => !InventoryHelpers.HardcodedBlacklist.Contains(i.ItemId));
        
        if (filters.FilterCurrencyItems)
            filtered = filtered.Where(i => !InventoryHelpers.CurrencyRange.Contains(i.ItemId));
        
        if (filters.FilterCrystalsAndShards)
            filtered = filtered.Where(i => !(i.ItemUICategory == 63 || i.ItemUICategory == 64));
        
        if (filters.FilterGearsetItems)
            filtered = filtered.Where(i => !InventoryHelpers.IsInGearset(i.ItemId));
        
        if (filters.FilterIndisposableItems)
            filtered = filtered.Where(i => !i.IsIndisposable);
        
        if (filters.FilterHighLevelGear)
            filtered = filtered.Where(i => !(i.EquipSlotCategory > 0 && i.ItemLevel >= filters.MaxGearItemLevel));
        
        if (filters.FilterUniqueUntradeable)
            filtered = filtered.Where(i => !(i.IsUnique && i.IsUntradable));
        
        if (filters.FilterHQItems)
            filtered = filtered.Where(i => !i.IsHQ);
        
        if (filters.FilterCollectables)
            filtered = filtered.Where(i => !i.IsCollectable);
        
        if (filters.FilterSpiritbondedItems)
            filtered = filtered.Where(i => i.SpiritBond < filters.MinSpiritbondToFilter);
        
        return filtered;
    }
    
    public bool IsItemFiltered(
        InventoryItemInfo item,
        SafetyFilters filters,
        HashSet<uint> blacklistedItems)
    {
        if (filters.FilterUltimateTokens && InventoryHelpers.HardcodedBlacklist.Contains(item.ItemId))
            return true;
        
        if (filters.FilterCurrencyItems && InventoryHelpers.CurrencyRange.Contains(item.ItemId))
            return true;
        
        if (filters.FilterCrystalsAndShards && (item.ItemUICategory == 63 || item.ItemUICategory == 64))
            return true;
        
        if (filters.FilterGearsetItems && InventoryHelpers.IsInGearset(item.ItemId))
            return true;
        
        if (filters.FilterIndisposableItems && item.IsIndisposable)
            return true;
        
        if (filters.FilterHighLevelGear && item.EquipSlotCategory > 0 && item.ItemLevel >= filters.MaxGearItemLevel)
            return true;
        
        if (filters.FilterUniqueUntradeable && item.IsUnique && item.IsUntradable)
            return true;
        
        if (filters.FilterHQItems && item.IsHQ)
            return true;
        
        if (filters.FilterCollectables && item.IsCollectable)
            return true;
        
        if (filters.FilterSpiritbondedItems && item.SpiritBond >= filters.MinSpiritbondToFilter)
            return true;
        
        return false;
    }
    
    public string GetFilterReason(
        InventoryItemInfo item,
        SafetyFilters filters)
    {
        if (filters.FilterUltimateTokens && InventoryHelpers.HardcodedBlacklist.Contains(item.ItemId))
            return "Ultimate/Special";
        
        if (filters.FilterCurrencyItems && InventoryHelpers.CurrencyRange.Contains(item.ItemId))
            return "Currency";
        
        if (filters.FilterCrystalsAndShards && (item.ItemUICategory == 63 || item.ItemUICategory == 64))
            return "Crystal/Shard";
        
        if (filters.FilterGearsetItems && InventoryHelpers.IsInGearset(item.ItemId))
            return "In Gearset";
        
        if (filters.FilterIndisposableItems && item.IsIndisposable)
            return "Indisposable";
        
        if (filters.FilterHighLevelGear && item.EquipSlotCategory > 0 && item.ItemLevel >= filters.MaxGearItemLevel)
            return $"High Level (i{item.ItemLevel})";
        
        if (filters.FilterUniqueUntradeable && item.IsUnique && item.IsUntradable)
            return "Unique & Untradeable";
        
        if (filters.FilterHQItems && item.IsHQ)
            return "High Quality";
        
        if (filters.FilterCollectables && item.IsCollectable)
            return "Collectable";
        
        if (filters.FilterSpiritbondedItems && item.SpiritBond >= filters.MinSpiritbondToFilter)
            return $"Spiritbond {item.SpiritBond}%";
        
        return "Protected";
    }
    
    public List<InventoryItemInfo> GetProtectedItems(
        IEnumerable<InventoryItemInfo> allItems,
        SafetyFilters filters,
        HashSet<uint> blacklistedItems)
    {
        var protectedItems = new List<InventoryItemInfo>();
        
        foreach (var item in allItems)
        {
            if (IsItemFiltered(item, filters, blacklistedItems))
            {
                protectedItems.Add(item);
            }
        }
        
        return protectedItems;
    }
    
    public List<CategoryGroup> GroupIntoCategories(
        IEnumerable<InventoryItemInfo> items)
    {
        return items
            .GroupBy(i => new { i.ItemUICategory, i.CategoryName })
            .Select(categoryGroup => 
            {
                var groupedItems = categoryGroup
                    .GroupBy(i => i.ItemId)
                    .Select(itemGroup => 
                    {
                        var first = itemGroup.First();
                        return new InventoryItemInfo
                        {
                            ItemId = first.ItemId,
                            Name = first.Name,
                            Quantity = itemGroup.Sum(i => i.Quantity),
                            Container = first.Container,
                            Slot = first.Slot,
                            IsHQ = first.IsHQ,
                            IconId = first.IconId,
                            CanBeDiscarded = first.CanBeDiscarded,
                            CanBeTraded = first.CanBeTraded,
                            IsCollectable = first.IsCollectable,
                            SpiritBond = first.SpiritBond,
                            Durability = first.Durability,
                            MaxDurability = first.MaxDurability,
                            CategoryName = first.CategoryName,
                            ItemUICategory = first.ItemUICategory,
                            MarketPrice = first.MarketPrice,
                            MarketPriceFetchTime = first.MarketPriceFetchTime,
                            IsSelected = first.IsSelected,
                            ItemLevel = first.ItemLevel,
                            EquipLevel = first.EquipLevel,
                            Rarity = first.Rarity,
                            IsUnique = first.IsUnique,
                            IsUntradable = first.IsUntradable,
                            IsIndisposable = first.IsIndisposable,
                            EquipSlotCategory = first.EquipSlotCategory,
                            SafetyAssessment = first.SafetyAssessment
                        };
                    })
                    .OrderBy(i => i.Name)
                    .ToList();
                
                return new CategoryGroup
                {
                    CategoryId = categoryGroup.Key.ItemUICategory,
                    Name = categoryGroup.Key.CategoryName,
                    Items = groupedItems
                };
            })
            .OrderBy(c => c.Name)
            .ToList();
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public class FilterHiddenCounts
{
    public int UltimateSpecial;
    public int InGearset;
    public int Indisposable;
    public int HQ;
    public int Collectables;
    public int UniqueUntradeable;
    public int HighLevelGear;
}

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
        
        // Currency items, crystals, and shards are always hidden — they can't
        // be discarded anyway, and surfacing them is just noise.
        filtered = filtered.Where(i => !ItemSafetyData.CurrencyRange.Contains(i.ItemId));
        filtered = filtered.Where(i => !ItemSafetyData.CrystalAndShardCategoryIds.Contains(i.ItemUICategory));

        if (filters.FilterUltimateTokens)
            filtered = filtered.Where(i => !ItemSafetyData.HardcodedBlacklist.Contains(i.ItemId));

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

        return filtered;
    }

    public bool IsItemFiltered(
        InventoryItemInfo item,
        SafetyFilters filters,
        HashSet<uint> blacklistedItems)
    {
        if (ItemSafetyData.CurrencyRange.Contains(item.ItemId))
            return true;

        if (ItemSafetyData.CrystalAndShardCategoryIds.Contains(item.ItemUICategory))
            return true;

        if (filters.FilterUltimateTokens && ItemSafetyData.HardcodedBlacklist.Contains(item.ItemId))
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

        return false;
    }

    public string GetFilterReason(
        InventoryItemInfo item,
        SafetyFilters filters)
    {
        if (ItemSafetyData.CurrencyRange.Contains(item.ItemId))
            return "Currency";

        if (ItemSafetyData.CrystalAndShardCategoryIds.Contains(item.ItemUICategory))
            return "Crystal/Shard";

        if (filters.FilterUltimateTokens && ItemSafetyData.HardcodedBlacklist.Contains(item.ItemId))
            return "Ultimate/Special";

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
    
    /// <summary>
    /// Counts how many items in the source set each filter would individually match —
    /// independent of whether the filter is currently enabled, and independent of other
    /// filters. Used for the per-filter "(N)" indicators in the sidebar.
    /// </summary>
    public FilterHiddenCounts CountHiddenPerFilter(
        IEnumerable<InventoryItemInfo> items,
        SafetyFilters filters)
    {
        var counts = new FilterHiddenCounts();
        foreach (var item in items)
        {
            if (ItemSafetyData.HardcodedBlacklist.Contains(item.ItemId)) counts.UltimateSpecial++;
            if (InventoryHelpers.IsInGearset(item.ItemId)) counts.InGearset++;
            if (item.IsIndisposable) counts.Indisposable++;
            if (item.IsHQ) counts.HQ++;
            if (item.IsCollectable) counts.Collectables++;
            if (item.IsUnique && item.IsUntradable) counts.UniqueUntradeable++;
            if (item.EquipSlotCategory > 0 && item.ItemLevel >= filters.MaxGearItemLevel) counts.HighLevelGear++;
        }
        return counts;
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


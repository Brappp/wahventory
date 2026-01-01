using System;
using System.Collections.Generic;
using System.Linq;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public class ItemFilterService
{
    // Filter check result - null means not filtered, string is the reason
    private string? GetFilterReasonInternal(InventoryItemInfo item, SafetyFilters filters)
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

        return null; // Not filtered
    }

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

        // Apply safety filters - keep items that are NOT filtered
        filtered = filtered.Where(i => GetFilterReasonInternal(i, filters) == null);

        return filtered;
    }

    public bool IsItemFiltered(
        InventoryItemInfo item,
        SafetyFilters filters,
        HashSet<uint> blacklistedItems)
    {
        return GetFilterReasonInternal(item, filters) != null;
    }

    public string GetFilterReason(InventoryItemInfo item, SafetyFilters filters)
    {
        return GetFilterReasonInternal(item, filters) ?? "Protected";
    }

    public List<InventoryItemInfo> GetProtectedItems(
        IEnumerable<InventoryItemInfo> allItems,
        SafetyFilters filters,
        HashSet<uint> blacklistedItems)
    {
        return allItems.Where(item => IsItemFiltered(item, filters, blacklistedItems)).ToList();
    }

    public List<CategoryGroup> GroupIntoCategories(IEnumerable<InventoryItemInfo> items)
    {
        return items
            .GroupBy(i => new { i.ItemUICategory, i.CategoryName })
            .Select(categoryGroup =>
            {
                var groupedItems = categoryGroup
                    .GroupBy(i => i.ItemId)
                    .Select(itemGroup => CloneWithSummedQuantity(itemGroup))
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

    private static InventoryItemInfo CloneWithSummedQuantity(IGrouping<uint, InventoryItemInfo> itemGroup)
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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.Automation.NeoTaskManager;
using Dalamud.Plugin.Services;
using wahventory.Services.Helpers;
using wahventory.Models;
using wahventory.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace wahventory.Services.Tasks;

public class InventoryTaskService : TaskServiceBase
{
    private readonly InventoryHelpers _inventoryHelpers;
    private readonly Configuration _configuration;
    private readonly object _itemsLock;
    private readonly object _categoriesLock;
    
    // Events for inventory updates
    public event Action<List<InventoryItemInfo>>? InventoryRefreshed;
    public event Action<List<CategoryGroup>>? CategoriesUpdated;
    
    private List<InventoryItemInfo> _allItems = new();
    private List<InventoryItemInfo> _originalItems = new();
    private List<CategoryGroup> _categories = new();
    private bool _showArmory = false;

    public InventoryTaskService(
        TaskManager taskManager, 
        IPluginLog log,
        InventoryHelpers inventoryHelpers,
        Configuration configuration,
        object itemsLock,
        object categoriesLock) : base(taskManager, log)
    {
        _inventoryHelpers = inventoryHelpers;
        _configuration = configuration;
        _itemsLock = itemsLock;
        _categoriesLock = categoriesLock;
    }

    public void EnqueueRefresh(bool includeArmory = false)
    {
        TaskManager.Enqueue(() => RefreshInventory(includeArmory));
    }

    public void EnqueueCategoryUpdate(string searchFilter = "")
    {
        TaskManager.Enqueue(() => UpdateCategories(searchFilter));
    }

    public void EnqueueFullUpdate(bool includeArmory = false, string searchFilter = "")
    {
        TaskManager.Enqueue(() => RefreshInventory(includeArmory));
        TaskManager.Enqueue(() => UpdateCategories(searchFilter));
    }
    
    public void RefreshImmediately(bool includeArmory = false, string searchFilter = "")
    {
        // Synchronous refresh for UI-triggered changes that need immediate response
        RefreshInventory(includeArmory);
        UpdateCategories(searchFilter);
    }
    
    public void RefreshInventoryAndCategories(bool includeArmory = false)
    {
        // Match original behavior - refresh inventory then immediately update categories with empty filter
        // This matches the original UpdateCategories() that reads _searchFilter from the main module
        RefreshInventory(includeArmory);
        UpdateCategories(""); // Empty string to match original flow
    }

    private void RefreshInventory(bool includeArmory = false)
    {
        try
        {
            _showArmory = includeArmory;
            var newItems = _inventoryHelpers.GetAllItems(_showArmory, false);
            
            lock (_itemsLock)
            {
                _originalItems = newItems;
                _allItems = new List<InventoryItemInfo>(_originalItems);
                
                // Update safety assessments
                foreach (var item in _allItems)
                {
                    item.SafetyAssessment = InventoryHelpers.AssessItemSafety(
                        item, 
                        _configuration.InventorySettings, 
                        new HashSet<uint>() // Will be provided by blacklist service
                    );
                }
            }
            
            Log.Debug($"Inventory refreshed: {newItems.Count} items");
            InventoryRefreshed?.Invoke(_allItems);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh inventory");
        }
    }

    private void UpdateCategories(string searchFilter = "")
    {
        try
        {
            List<InventoryItemInfo> itemsCopy;
            lock (_itemsLock)
            {
                itemsCopy = new List<InventoryItemInfo>(_allItems);
            }
            
            var filteredItems = itemsCopy.AsEnumerable();
            
            // Apply search filter
            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                filteredItems = filteredItems.Where(i => 
                    i.Name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            // Apply safety filters from configuration
            filteredItems = ApplySafetyFilters(filteredItems);
            
            lock (_categoriesLock)
            {
                _categories = filteredItems
                    .GroupBy(i => new { i.ItemUICategory, i.CategoryName })
                    .Select(categoryGroup => 
                    {
                        var items = categoryGroup
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
                            Items = items
                        };
                    })
                    .OrderBy(c => c.Name)
                    .ToList();
            }
            
            Log.Debug($"Categories updated: {_categories.Count} categories");
            CategoriesUpdated?.Invoke(_categories);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update categories");
        }
    }

    private IEnumerable<InventoryItemInfo> ApplySafetyFilters(IEnumerable<InventoryItemInfo> items)
    {
        var filters = _configuration.InventorySettings.SafetyFilters;
        var filteredItems = items;

        if (filters.FilterUltimateTokens)
            filteredItems = filteredItems.Where(i => !InventoryHelpers.HardcodedBlacklist.Contains(i.ItemId));
        if (filters.FilterCurrencyItems)
            filteredItems = filteredItems.Where(i => !InventoryHelpers.CurrencyRange.Contains(i.ItemId));
        if (filters.FilterCrystalsAndShards)
            filteredItems = filteredItems.Where(i => !(i.ItemUICategory == 63 || i.ItemUICategory == 64));
        if (filters.FilterGearsetItems)
            filteredItems = filteredItems.Where(i => !InventoryHelpers.IsInGearset(i.ItemId));
        if (filters.FilterIndisposableItems)
            filteredItems = filteredItems.Where(i => !i.IsIndisposable);
        if (filters.FilterHighLevelGear)
            filteredItems = filteredItems.Where(i => !(i.EquipSlotCategory > 0 && i.ItemLevel >= filters.MaxGearItemLevel));
        if (filters.FilterUniqueUntradeable)
            filteredItems = filteredItems.Where(i => !(i.IsUnique && i.IsUntradable));
        if (filters.FilterHQItems)
            filteredItems = filteredItems.Where(i => !i.IsHQ);
        if (filters.FilterCollectables)
            filteredItems = filteredItems.Where(i => !i.IsCollectable);
        if (filters.FilterSpiritbondedItems)
            filteredItems = filteredItems.Where(i => i.SpiritBond < filters.MinSpiritbondToFilter);

        return filteredItems;
    }

    public List<InventoryItemInfo> GetCurrentItems()
    {
        lock (_itemsLock)
        {
            return new List<InventoryItemInfo>(_allItems);
        }
    }

    public List<InventoryItemInfo> GetOriginalItems()
    {
        lock (_itemsLock)
        {
            return new List<InventoryItemInfo>(_originalItems);
        }
    }

    public List<CategoryGroup> GetCurrentCategories()
    {
        lock (_categoriesLock)
        {
            return new List<CategoryGroup>(_categories);
        }
    }
}


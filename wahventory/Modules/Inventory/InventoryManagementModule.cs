using System;
using System.Collections.Generic;
using System.Linq;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services;
using wahventory.Services.Helpers;

namespace wahventory.Modules.Inventory;

public class InventoryManagementModule : IDisposable
{
    internal readonly Plugin _plugin;
    private readonly IGameServices _services;
    private readonly InventoryHelpers _inventoryHelpers;
    internal readonly IconCache _iconCache;

    // Services
    private readonly ItemFilterService _filterService;
    internal readonly ItemSearchService _searchService;
    internal readonly PriceService _priceService;
    public readonly DiscardService DiscardService;
    internal readonly PassiveDiscardService _passiveDiscardService;

    // Expose filter service for UI
    internal ItemFilterService FilterService => _filterService;

    // UI
    private readonly InventoryUIRenderer _ui;

    // State
    private bool _initialized = false;
    internal readonly object _stateLock = new object();

    public HashSet<uint> BlacklistedItems { get; private set; }
    public HashSet<uint> AutoDiscardItems { get; private set; }

    internal List<CategoryGroup> _categories = new();
    internal List<InventoryItemInfo> _allItems = new();
    internal List<InventoryItemInfo> _originalItems = new();
    internal readonly HashSet<uint> _selectedItems = new();

    internal string _searchFilter = string.Empty;
    internal bool _showArmory = false;
    internal string _selectedWorld = "";
    internal List<string> _availableWorlds = new();

    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    internal bool _expandedCategoriesChanged = false;
    private DateTime _lastConfigSave = DateTime.MinValue;
    private readonly TimeSpan _configSaveInterval = TimeSpan.FromSeconds(2);
    private bool _windowIsOpen = false;

    internal InventorySettings Settings => _plugin.Configuration.InventorySettings;
    internal Dictionary<uint, bool> ExpandedCategories => Settings.ExpandedCategories;

    public InventoryManagementModule(Plugin plugin, IGameServices services)
    {
        _plugin = plugin;
        _services = services;
        _inventoryHelpers = new InventoryHelpers(_services.DataManager, _services.Log);
        _iconCache = new IconCache(_services.TextureProvider);

        // Initialize services
        _filterService = new ItemFilterService();
        _searchService = new ItemSearchService(_services.DataManager, _services.Log);
        _priceService = new PriceService(_services.Log, Settings, "Excalibur");
        DiscardService = new DiscardService(
            _inventoryHelpers,
            _services.Log,
            _services.ChatGui,
            _services.GameGui);
        _passiveDiscardService = new PassiveDiscardService(
            _services.ClientState,
            _services.Condition,
            _services.GameGui,
            _services.Log,
            Settings);

        BlacklistedItems = _plugin.ConfigManager.LoadBlacklist();
        AutoDiscardItems = _plugin.ConfigManager.LoadAutoDiscard();
        _selectedWorld = "Excalibur";

        PopulateAvailableWorlds();
        InitializeWorld();

        _ui = new InventoryUIRenderer(this);
    }
    
    private void InitializeWorld()
    {
        try
        {
            var currentWorld = _services.PlayerState.IsLoaded ? _services.PlayerState.CurrentWorld.Value.Name.ToString() : null;
            if (!string.IsNullOrEmpty(currentWorld))
            {
                _selectedWorld = currentWorld;
                _priceService.UpdateWorld(_selectedWorld);
            }
        }
        catch
        {
            // Ignore errors during initialization
        }
    }
    
    private void PopulateAvailableWorlds()
    {
        _availableWorlds.Clear();
        
        var worldSheet = _services.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
        if (worldSheet != null)
        {
            try
            {
                var hasPlayer = _services.PlayerState.IsLoaded;
                var currentWorld = hasPlayer ? (Lumina.Excel.Sheets.World?)_services.PlayerState.CurrentWorld.Value : null;
                var worldName = currentWorld?.Name.ExtractText() ?? "Aether";

                if (currentWorld != null)
                {
                    try
                    {
                        var worldRow = worldSheet.FirstOrDefault(w => w.Name.ExtractText() == worldName);
                        if (worldRow.RowId > 0)
                        {
                            try
                            {
                                var currentDatacenterId = worldRow.DataCenter.RowId;
                                
                                var datacenterWorlds = worldSheet
                                    .Where(w => w.DataCenter.RowId == currentDatacenterId && w.IsPublic)
                                    .Select(w => w.Name.ExtractText())
                                    .Where(name => !string.IsNullOrEmpty(name))
                                    .OrderBy(w => w)
                                    .ToList();
                                
                                _availableWorlds = datacenterWorlds;
                            }
                            catch
                            {
                                _availableWorlds = new List<string> { worldName };
                            }
                        }
                        else
                        {
                            _availableWorlds = new List<string> { worldName };
                        }
                    }
                    catch
                    {
                        _availableWorlds = new List<string> { worldName };
                    }
                }
                else
                {
                    _availableWorlds = new List<string> { worldName };
                }
            }
            catch (Exception ex)
            {
                _services.Log.Warning($"Failed to get datacenter worlds: {ex.Message}");
                _availableWorlds = new List<string> { "Aether" };
            }
        }
    }
    
    public void Initialize()
    {
        if (_initialized)
            return;
        
        InitializeWorld();
        RefreshInventory();
        _initialized = true;
    }
    
    public void Update()
    {
        if (!_initialized)
        {
            Initialize();
        }
        
        _priceService.CleanupStuckFetches();
        
        // Update price service world if changed
        try
        {
            var currentWorld = _services.PlayerState.IsLoaded ? _services.PlayerState.CurrentWorld.Value.Name.ToString() : null;
            if (!string.IsNullOrEmpty(currentWorld) && currentWorld != _selectedWorld)
            {
                _selectedWorld = currentWorld;
                _priceService.UpdateWorld(_selectedWorld);
            }
        }
        catch
        {
            // Ignore
        }
        
        // Auto-refresh prices
        if (_windowIsOpen && Settings.AutoRefreshPrices && !DiscardService.IsDiscarding && 
            DateTime.Now - _lastRefresh > _refreshInterval)
        {
            _lastRefresh = DateTime.Now;
            
            List<InventoryItemInfo> visibleItems;
            lock (_stateLock)
            {
                visibleItems = GetVisibleItems();
            }
            
            var itemsNeedingPrice = _priceService.GetItemsNeedingPriceFetch(visibleItems, 2);
            foreach (var item in itemsNeedingPrice)
            {
                _ = _priceService.FetchPrice(item).ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully && task.Result.HasValue)
                    {
                        lock (_stateLock)
                        {
                            item.MarketPrice = task.Result.Value;
                            item.MarketPriceFetchTime = DateTime.Now;
                        }
                    }
                });
            }
        }
        
        _windowIsOpen = false;
        
        // Save config if categories changed
        if (_expandedCategoriesChanged && DateTime.Now - _lastConfigSave > _configSaveInterval)
        {
            _plugin.ConfigManager.SaveConfiguration();
            _expandedCategoriesChanged = false;
            _lastConfigSave = DateTime.Now;
        }
        
        // Update passive discard
        _passiveDiscardService.Update(
            AutoDiscardItems,
            _originalItems,
            BlacklistedItems,
            ExecuteAutoDiscard);
    }
    
    public void Draw()
    {
        _windowIsOpen = true;
        if (!_initialized)
        {
            Initialize();
        }
        _ui.Draw();
    }

    internal void RefreshInventory()
    {
        var newItems = _inventoryHelpers.GetAllItems(_showArmory, false);
        
        lock (_stateLock)
        {
            _originalItems = newItems;
            
            // Apply safety assessment
            foreach (var item in _originalItems)
            {
                item.SafetyAssessment = InventoryHelpers.AssessItemSafety(item, Settings, BlacklistedItems);
                
                // Update price from cache
                _priceService.UpdateItemPrice(item);
                
                // Update selection state
                item.IsSelected = _selectedItems.Contains(item.ItemId);
            }
            
            UpdateCategories();
        }
    }
    
    internal void UpdateCategories()
    {
        List<InventoryItemInfo> itemsCopy;
        lock (_stateLock)
        {
            itemsCopy = new List<InventoryItemInfo>(_originalItems);
        }
        
        var filteredItems = _filterService.ApplyFilters(
            itemsCopy,
            Settings.SafetyFilters,
            BlacklistedItems,
            _searchFilter);
        
        lock (_stateLock)
        {
            _allItems = filteredItems.ToList();
            _categories = _filterService.GroupIntoCategories(_allItems);
        }
    }
    
    private List<InventoryItemInfo> GetVisibleItems()
    {
        return _filterService.ApplyFilters(
            _originalItems,
            Settings.SafetyFilters,
            BlacklistedItems,
            _searchFilter).ToList();
    }
    
    internal List<InventoryItemInfo> GetProtectedItems()
    {
        return _filterService.GetProtectedItems(
            _originalItems,
            Settings.SafetyFilters,
            BlacklistedItems);
    }
    
    public void SaveBlacklist()
    {
        _plugin.ConfigManager.SaveBlacklist(BlacklistedItems);
    }
    
    public void SaveAutoDiscard()
    {
        _plugin.ConfigManager.SaveAutoDiscard(AutoDiscardItems);
    }
    
    public void ExecuteAutoDiscard()
    {
        if (AutoDiscardItems.Count == 0)
        {
            _services.ChatGui.PrintError("No items configured for auto-discard. Add items in the Auto Discard tab.");
            return;
        }
        
        List<InventoryItemInfo> itemsToDiscard;
        lock (_stateLock)
        {
            itemsToDiscard = _allItems
                .Where(item => AutoDiscardItems.Contains(item.ItemId) && 
                              item.CanBeDiscarded &&
                              !BlacklistedItems.Contains(item.ItemId))
                .ToList();
        }
        
        if (!itemsToDiscard.Any())
        {
            _services.ChatGui.PrintError("No auto-discard items found in inventory.");
            return;
        }
        
        List<uint> selectedItemIds;
        lock (_stateLock)
        {
            selectedItemIds = itemsToDiscard.Select(i => i.ItemId).Distinct().ToList();
        }
        
        DiscardService.PrepareDiscard(selectedItemIds, _originalItems, BlacklistedItems);
    }
    
    internal void AddSelectedToBlacklist()
    {
        lock (_stateLock)
        {
            foreach (var itemId in _selectedItems)
            {
                if (!BlacklistedItems.Contains(itemId))
                {
                    BlacklistedItems.Add(itemId);
                }
            }

            _selectedItems.Clear();
            foreach (var item in _allItems)
            {
                item.IsSelected = false;
            }
        }

        SaveBlacklist();
        RefreshInventory();
    }

    internal void AddSelectedToAutoDiscard()
    {
        lock (_stateLock)
        {
            foreach (var itemId in _selectedItems)
            {
                if (!AutoDiscardItems.Contains(itemId))
                {
                    AutoDiscardItems.Add(itemId);
                }
            }

            _selectedItems.Clear();
            foreach (var item in _allItems)
            {
                item.IsSelected = false;
            }
        }

        SaveAutoDiscard();
        RefreshInventory();
    }

    public void Dispose()
    {
        if (_expandedCategoriesChanged)
        {
            _plugin.ConfigManager.SaveConfiguration();
        }

        _priceService?.Dispose();
        DiscardService?.Dispose();
        _iconCache?.Dispose();
    }
}

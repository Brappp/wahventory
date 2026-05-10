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
    private readonly Plugin _plugin;
    private readonly IGameServices _services;
    private readonly InventoryHelpers _inventoryHelpers;
    private readonly IconCache _iconCache;

    // Services
    private readonly ItemFilterService _filterService;
    private readonly ItemSearchService _searchService;
    private readonly PriceService _priceService;
    public readonly DiscardService DiscardService;
    private readonly PassiveDiscardService _passiveDiscardService;

    // UI
    private readonly InventoryUIRenderer _ui;

    // State
    private bool _initialized = false;
    internal readonly InventoryState _state = new();

    public HashSet<uint> BlacklistedItems { get; private set; }
    public HashSet<uint> AutoDiscardItems { get; private set; }

    internal string _searchFilter = string.Empty;
    internal bool _showArmory = false;
    internal string _selectedWorld = "";
    internal List<string> _availableWorlds = new();

    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    private bool _expandedCategoriesChanged = false;
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

        _ui = new InventoryUIRenderer(this, _filterService, _searchService, _priceService, _passiveDiscardService, _iconCache);
    }

    internal void SaveConfig() => _plugin.ConfigManager.SaveConfiguration();
    internal void MarkExpansionChanged() => _expandedCategoriesChanged = true;
    
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

            var visibleItems = GetVisibleItems();
            var itemsNeedingPrice = _priceService.GetItemsNeedingPriceFetch(visibleItems, 2);
            foreach (var item in itemsNeedingPrice)
            {
                _ = _priceService.FetchPrice(item).ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully && task.Result.HasValue)
                    {
                        _state.SetItemPrice(item, task.Result.Value, DateTime.Now);
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
            _state.SnapshotOriginalItems(),
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

        _state.ApplyRefreshAndRecategorize(
            newItems,
            initEachItem: item =>
            {
                item.SafetyAssessment = InventoryHelpers.AssessItemSafety(item, Settings, BlacklistedItems);
                _priceService.UpdateItemPrice(item);
            },
            applyFilters: items => _filterService.ApplyFilters(items, Settings.SafetyFilters, BlacklistedItems, _searchFilter),
            categorize: items => _filterService.GroupIntoCategories(items));
    }

    internal void UpdateCategories()
    {
        _state.Recategorize(
            applyFilters: items => _filterService.ApplyFilters(items, Settings.SafetyFilters, BlacklistedItems, _searchFilter),
            categorize: items => _filterService.GroupIntoCategories(items));
    }

    private List<InventoryItemInfo> GetVisibleItems()
    {
        return _state.ApplyFiltersToOriginal(
            items => _filterService.ApplyFilters(items, Settings.SafetyFilters, BlacklistedItems, _searchFilter));
    }

    internal List<InventoryItemInfo> GetProtectedItems()
    {
        return _state.GetProtectedItems(
            items => _filterService.GetProtectedItems(items, Settings.SafetyFilters, BlacklistedItems));
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

        var itemsToDiscard = _state.SnapshotAutoDiscardCandidates(AutoDiscardItems, BlacklistedItems);

        if (!itemsToDiscard.Any())
        {
            _services.ChatGui.PrintError("No auto-discard items found in inventory.");
            return;
        }

        var selectedItemIds = itemsToDiscard.Select(i => i.ItemId).Distinct().ToList();
        DiscardService.PrepareDiscard(selectedItemIds, _state.SnapshotOriginalItems(), BlacklistedItems);
    }

    internal void AddSelectedToBlacklist()
    {
        _state.TransferSelectionTo(BlacklistedItems);
        SaveBlacklist();
        RefreshInventory();
    }

    internal void AddSelectedToAutoDiscard()
    {
        _state.TransferSelectionTo(AutoDiscardItems);
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

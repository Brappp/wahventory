using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Core;
using wahventory.Models;
using wahventory.Modules.Search;
using wahventory.Services;
using wahventory.Services.Helpers;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule : IDisposable
{
    private readonly Plugin _plugin;
    private readonly InventoryHelpers _inventoryHelpers;
    private readonly IconCache _iconCache;
    
    // Services
    private readonly ItemFilterService _filterService;
    private readonly ItemSearchService _searchService;
    private readonly PriceService _priceService;
    public readonly DiscardService DiscardService;
    private readonly PassiveDiscardService _passiveDiscardService;
    
    // Expose filter service for UI
    internal ItemFilterService FilterService => _filterService;
    
    // State
    private bool _initialized = false;
    private readonly object _stateLock = new object();
    
    public HashSet<uint> BlacklistedItems { get; private set; }
    public HashSet<uint> AutoDiscardItems { get; private set; }
    
    private List<CategoryGroup> _categories = new();
    private List<InventoryItemInfo> _allItems = new();
    private List<InventoryItemInfo> _originalItems = new();
    private readonly HashSet<uint> _selectedItems = new();
    
    private string _searchFilter = string.Empty;
    private bool _showArmory = false;
    private string _selectedWorld = "";
    private List<string> _availableWorlds = new();
    
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    private bool _expandedCategoriesChanged = false;
    private DateTime _lastConfigSave = DateTime.MinValue;
    private readonly TimeSpan _configSaveInterval = TimeSpan.FromSeconds(2);
    private bool _windowIsOpen = false;
    private SearchModule? _currentSearchModule;

    private InventorySettings Settings => _plugin.Configuration.InventorySettings;
    private Dictionary<uint, bool> ExpandedCategories => Settings.ExpandedCategories;
    
    public InventoryManagementModule(Plugin plugin)
    {
        _plugin = plugin;
        _inventoryHelpers = new InventoryHelpers(Plugin.DataManager, Plugin.Log);
        _iconCache = new IconCache(Plugin.TextureProvider);
        
        // Initialize services
        _filterService = new ItemFilterService();
        _searchService = new ItemSearchService(Plugin.DataManager, Plugin.Log);
        _priceService = new PriceService(Plugin.Log, Settings, "Excalibur");
        DiscardService = new DiscardService(
            _inventoryHelpers,
            Plugin.Log,
            Plugin.ChatGui,
            Plugin.GameGui);
        DiscardService.OnInventoryRefreshNeeded += RefreshInventory;
        _passiveDiscardService = new PassiveDiscardService(
            Plugin.ClientState,
            Plugin.Condition,
            Plugin.GameGui,
            Plugin.Log,
            Settings);
        
        BlacklistedItems = _plugin.ConfigManager.LoadBlacklist();
        AutoDiscardItems = _plugin.ConfigManager.LoadAutoDiscard();
        _selectedWorld = "Excalibur";
        
        InitializeUIComponents();
    }
    
    private void InitializeWorld()
    {
        try
        {
            var currentWorld = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.Value.Name.ToString();
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
        
        var worldSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
        if (worldSheet != null)
        {
            try
            {
                var currentWorld = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.Value;
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
                Plugin.Log.Warning($"Failed to get datacenter worlds: {ex.Message}");
                _availableWorlds = new List<string> { "Aether" };
            }
        }
    }
    
    public void Initialize()
    {
        if (_initialized)
            return;
        
        PopulateAvailableWorlds();
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
            var currentWorld = Plugin.ObjectTable.LocalPlayer?.CurrentWorld.Value.Name.ToString();
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
    
    public void Draw(SearchModule? searchModule = null)
    {
        _windowIsOpen = true;
        _currentSearchModule = searchModule;
        if (!_initialized)
        {
            Initialize();
        }
        DrawMainContent();
    }
    
    private void DrawMainContent()
    {
        DrawTopControls();
        DrawFiltersAndSettings();
        
        ImGui.Separator();
        var windowHeight = ImGui.GetWindowHeight();
        var currentY = ImGui.GetCursorPosY();
        var bottomBarHeight = 42f;
        var separatorHeight = ImGui.GetStyle().ItemSpacing.Y * 2 + 2;
        var tabBarHeight = ImGui.GetFrameHeight();
        var contentHeight = windowHeight - currentY - bottomBarHeight - separatorHeight - tabBarHeight - 10f;
        contentHeight = Math.Max(100f, contentHeight);
        
        using (var tabBar = ImRaii.TabBar("InventoryTabs"))
        {
            if (tabBar)
            {
                List<InventoryItemInfo> filteredItems;
                lock (_stateLock)
                {
                    filteredItems = GetProtectedItems();
                }
                
                int availableCount;
                lock (_stateLock)
                {
                    availableCount = _categories.Sum(c => c.Items.Count);
                }
                
                string availableTabText = $"Available Items ({availableCount})###AvailableTab";
                using (var tabItem = ImRaii.TabItem(availableTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("AvailableContent", new Vector2(0, contentHeight), false))
                        {
                            DrawAvailableItemsTab();
                        }
                    }
                }
                
                string protectedTabText = $"Protected Items ({filteredItems.Count})###ProtectedTab";
                using (var tabItem = ImRaii.TabItem(protectedTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("ProtectedContent", new Vector2(0, contentHeight), false))
                        {
                            DrawProtectedItemsTab(filteredItems);
                        }
                    }
                }
                
                string blacklistTabText = "Blacklist Management###BlacklistTab";
                using (var tabItem = ImRaii.TabItem(blacklistTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("BlacklistContent", new Vector2(0, contentHeight), false))
                        {
                            DrawBlacklistTab();
                        }
                    }
                }
                
                string autoDiscardTabText = "Auto Discard###AutoDiscardTab";
                using (var tabItem = ImRaii.TabItem(autoDiscardTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("AutoDiscardContent", new Vector2(0, contentHeight), false))
                        {
                            DrawAutoDiscardTab();
                        }
                    }
                }
            }
        }
        
        ImGui.Separator();
        DrawBottomActionBar();
    }
    
    private void RefreshInventory()
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
    
    private void UpdateCategories()
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
    
    private List<InventoryItemInfo> GetProtectedItems()
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
            Plugin.ChatGui.PrintError("No items configured for auto-discard. Add items in the Auto Discard tab.");
            return;
        }

        // Refresh inventory to get fresh data
        RefreshInventory();

        List<InventoryItemInfo> itemsToDiscard;
        lock (_stateLock)
        {
            itemsToDiscard = _originalItems
                .Where(item => AutoDiscardItems.Contains(item.ItemId) &&
                              item.CanBeDiscarded &&
                              !BlacklistedItems.Contains(item.ItemId))
                .ToList();

            // Update prices from cache
            foreach (var item in itemsToDiscard)
            {
                _priceService.UpdateItemPrice(item);
            }
        }

        if (!itemsToDiscard.Any())
        {
            Plugin.ChatGui.PrintError("No auto-discard items found in inventory.");
            return;
        }

        List<uint> selectedItemIds;
        lock (_stateLock)
        {
            selectedItemIds = itemsToDiscard.Select(i => i.ItemId).Distinct().ToList();
        }

        DiscardService.PrepareDiscard(selectedItemIds, _originalItems, BlacklistedItems, skipConfirmation: true);
    }
    
    public void Dispose()
    {
        if (_expandedCategoriesChanged)
        {
            _plugin.ConfigManager.SaveConfiguration();
        }

        if (DiscardService != null)
        {
            DiscardService.OnInventoryRefreshNeeded -= RefreshInventory;
        }

        _priceService?.Dispose();
        DiscardService?.Dispose();
        _iconCache?.Dispose();
    }
}

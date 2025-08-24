using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Services.External;
using wahventory.Services.Helpers;
using wahventory.Services.Tasks;
using wahventory.Models;
using wahventory.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace wahventory.Modules.Inventory;

/// <summary>
/// Refactored InventoryManagementModule using task-based architecture
/// This replaces the existing monolithic InventoryManagementModule.cs
/// </summary>
public partial class InventoryManagementModule : IDisposable
{
    // UI Colors
    private static readonly Vector4 ColorHQItem = new(0.6f, 0.8f, 1f, 1f);
    private static readonly Vector4 ColorHQName = new(0.8f, 0.8f, 1f, 1f);
    private static readonly Vector4 ColorPrice = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorNotTradeable = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 ColorLoading = new(0.8f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorError = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 ColorSuccess = new(0.2f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorSubdued = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorInfo = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ColorWarning = new(0.9f, 0.5f, 0.1f, 1f);
    private static readonly Vector4 ColorCaution = new(0.9f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 ColorBlue = new(0.3f, 0.7f, 1.0f, 1f);
    
    // Core dependencies
    private readonly Plugin _plugin;
    private readonly TaskCoordinator _taskCoordinator;
    private readonly IconCache _iconCache;
    
    // Shared state locks (passed to task coordinator)
    private readonly object _itemsLock = new object();
    private readonly object _categoriesLock = new object();
    private readonly object _selectedItemsLock = new object();
    private readonly object _priceCacheLock = new object();
    private readonly object _fetchingPricesLock = new object();
    
    // UI state
    private bool _windowIsOpen = false;
    private bool _showArmory = false;
    private string _searchFilter = string.Empty;
    private readonly HashSet<uint> _selectedItems = new();
    private bool _expandedCategoriesChanged = false;
    private DateTime _lastConfigSave = DateTime.MinValue;
    private readonly TimeSpan _configSaveInterval = TimeSpan.FromSeconds(2);
    
    // World selection
    private string _selectedWorld = "";
    private List<string> _availableWorlds = new();
    
    // Cached data for UI
    private List<InventoryItemInfo> _cachedItems = new();
    private List<CategoryGroup> _cachedCategories = new();
    
    // Timing and throttling
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    
    private InventorySettings Settings => _plugin.Configuration.InventorySettings;
    private Dictionary<uint, bool> ExpandedCategories => Settings.ExpandedCategories;

    public InventoryManagementModule(Plugin plugin)
    {
        _plugin = plugin;
        _iconCache = new IconCache(Plugin.TextureProvider);
        
        // Initialize world selection
        _selectedWorld = "Excalibur";
        PopulateAvailableWorlds();
        
        // Create task coordinator with all dependencies
        _taskCoordinator = new TaskCoordinator(
            Plugin.Log,
            _plugin.Configuration,
            new InventoryHelpers(Plugin.DataManager, Plugin.Log),
            new UniversalisClient(Plugin.Log, _selectedWorld),
            Plugin.ChatGui,
            Plugin.GameGui,
            Plugin.Condition,
            Plugin.ClientState,
            _itemsLock,
            _categoriesLock,
            _priceCacheLock,
            _fetchingPricesLock);
        
        // Load persistent data
        _taskCoordinator.BlacklistedItems = _plugin.ConfigManager.LoadBlacklist();
        _taskCoordinator.AutoDiscardItems = _plugin.ConfigManager.LoadAutoDiscard();
        
        // Setup event handlers for UI updates
        SetupEventHandlers();
    }
    
    private void SetupEventHandlers()
    {
        // Update cached data when task services complete operations
        _taskCoordinator.InventoryTasks.InventoryRefreshed += OnInventoryRefreshed;
        _taskCoordinator.InventoryTasks.CategoriesUpdated += OnCategoriesUpdated;
        
        // Handle price updates
        _taskCoordinator.PriceTasks.PriceUpdated += OnPriceUpdated;
        _taskCoordinator.PriceTasks.PriceFetchFailed += OnPriceFetchFailed;
        
        // Handle discard progress updates
        _taskCoordinator.DiscardTasks.DiscardStarted += OnDiscardStarted;
        _taskCoordinator.DiscardTasks.DiscardProgress += OnDiscardProgress;
        _taskCoordinator.DiscardTasks.DiscardError += OnDiscardError;
        _taskCoordinator.DiscardTasks.DiscardCompleted += OnDiscardCompleted;
        _taskCoordinator.DiscardTasks.DiscardCancelled += OnDiscardCancelled;
    }
    
    private void OnInventoryRefreshed(List<InventoryItemInfo> items)
    {
        lock (_itemsLock)
        {
            _cachedItems = items;
            
            // Update selected state
            foreach (var item in _cachedItems)
            {
                lock (_selectedItemsLock)
                {
                    item.IsSelected = _selectedItems.Contains(item.ItemId);
                }
                
                // Update prices from cache
                _taskCoordinator.PriceTasks.UpdateItemPrice(item);
            }
        }
    }
    
    private void OnCategoriesUpdated(List<CategoryGroup> categories)
    {
        lock (_categoriesLock)
        {
            _cachedCategories = categories;
        }
    }
    
    private void OnPriceUpdated(uint itemId, long price, DateTime fetchTime)
    {
        Plugin.Log.Debug($"Price updated for item {itemId}: {price} gil");
        
        // Update price in cached items
        lock (_itemsLock)
        {
            foreach (var item in _cachedItems.Where(i => i.ItemId == itemId))
            {
                item.MarketPrice = price;
                item.MarketPriceFetchTime = fetchTime;
            }
        }
        
        // Update price in cached categories
        lock (_categoriesLock)
        {
            foreach (var category in _cachedCategories)
            {
                foreach (var item in category.Items.Where(i => i.ItemId == itemId))
                {
                    item.MarketPrice = price;
                    item.MarketPriceFetchTime = fetchTime;
                }
            }
        }
    }
    
    private void OnPriceFetchFailed(uint itemId)
    {
        Plugin.Log.Debug($"Price fetch failed for item {itemId}");
        
        // Update price to indicate failure
        lock (_itemsLock)
        {
            foreach (var item in _cachedItems.Where(i => i.ItemId == itemId))
            {
                item.MarketPrice = -1; // Indicates no data
                item.MarketPriceFetchTime = DateTime.Now;
            }
        }
        
        // Update price in cached categories
        lock (_categoriesLock)
        {
            foreach (var category in _cachedCategories)
            {
                foreach (var item in category.Items.Where(i => i.ItemId == itemId))
                {
                    item.MarketPrice = -1; // Indicates no data
                    item.MarketPriceFetchTime = DateTime.Now;
                }
            }
        }
    }
    
    private void OnDiscardStarted(List<InventoryItemInfo> items)
    {
        Plugin.Log.Information($"Discard started for {items.Count} items");
    }
    
    private void OnDiscardProgress(int current, int total)
    {
        Plugin.Log.Debug($"Discard progress: {current}/{total}");
    }
    
    private void OnDiscardError(string error)
    {
        Plugin.Log.Error($"Discard error: {error}");
    }
    
    private void OnDiscardCompleted()
    {
        Plugin.Log.Information("Discard completed successfully");
        // Clear selection after successful discard
        lock (_selectedItemsLock)
        {
            _selectedItems.Clear();
        }
    }
    
    private void OnDiscardCancelled()
    {
        Plugin.Log.Information("Discard was cancelled");
    }
    
    private void PopulateAvailableWorlds()
    {
        _availableWorlds.Clear();
        
        var worldSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
        if (worldSheet != null)
        {
            foreach (var world in worldSheet)
            {
                if (world.IsPublic && !string.IsNullOrEmpty(world.Name.ToString()))
                {
                    _availableWorlds.Add(world.Name.ToString());
                }
            }
        }
        
        _availableWorlds.Sort();
    }
    
    public void Initialize()
    {
        UpdateCurrentWorld();
        _taskCoordinator.Initialize();
    }
    
    private void UpdateCurrentWorld()
    {
        try
        {
            var currentWorld = Plugin.ClientState.LocalPlayer?.CurrentWorld.Value.Name.ToString();
            if (!string.IsNullOrEmpty(currentWorld) && currentWorld != _selectedWorld)
            {
                _selectedWorld = currentWorld;
                // Update the UniversalisClient in the TaskCoordinator
                var newClient = new UniversalisClient(Plugin.Log, _selectedWorld);
                _taskCoordinator.UpdateUniversalisClient(newClient);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to update current world");
        }
    }
    
    public void Update()
    {
        // Handle config saves
        if (_expandedCategoriesChanged && DateTime.Now - _lastConfigSave > _configSaveInterval)
        {
            _plugin.ConfigManager.SaveConfiguration();
            _expandedCategoriesChanged = false;
            _lastConfigSave = DateTime.Now;
        }
        
        // Throttled price refresh when window is open
        if (_windowIsOpen && Settings.ShowMarketPrices && Settings.AutoRefreshPrices && !_taskCoordinator.IsDiscarding)
        {
            var timeSinceLastRefresh = DateTime.Now - _lastRefresh;
            if (timeSinceLastRefresh > _refreshInterval)
            {
                _lastRefresh = DateTime.Now;
                
                var visibleItems = GetVisibleItems();
                var stalePrices = visibleItems.Where(item => 
                    item.CanBeTraded && 
                    !item.MarketPrice.HasValue)
                    .Take(2) // Limit to 2 items for throttling
                    .ToList();
                    
                if (stalePrices.Any())
                {
                    _taskCoordinator.FetchPricesForVisible(stalePrices);
                }
            }
        }
        
        // Schedule regular maintenance tasks
        _taskCoordinator.ScheduleRegularTasks();
        
        // Reset window state
        _windowIsOpen = false;
    }
    
    public void Draw()
    {
        _windowIsOpen = true;
        DrawMainContent();
        
        if (_taskCoordinator.IsDiscarding)
        {
            DrawDiscardConfirmation();
        }
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
        var contentHeight = Math.Max(100f, windowHeight - currentY - bottomBarHeight - separatorHeight - tabBarHeight - 10f);
        
        using (var tabBar = ImRaii.TabBar("InventoryTabs"))
        {
            if (tabBar)
            {
                var availableCount = _cachedCategories.Sum(c => c.Items.Count);
                var protectedItems = GetProtectedItems();
                
                DrawAvailableItemsTab($"Available Items ({availableCount})###AvailableTab", contentHeight);
                DrawProtectedItemsTab($"Protected Items ({protectedItems.Count})###ProtectedTab", contentHeight, protectedItems);
                DrawBlacklistTab("Blacklist Management###BlacklistTab", contentHeight);
                DrawAutoDiscardTab("Auto Discard###AutoDiscardTab", contentHeight);
            }
        }
        
        ImGui.Separator();
        DrawBottomActionBar();
    }
    
    private List<InventoryItemInfo> GetVisibleItems()
    {
        // Get items from filtered categories instead of unfiltered cached items
        lock (_categoriesLock)
        {
            var filteredItems = _cachedCategories.SelectMany(c => c.Items);
            
            // Search filter is already applied in task service, but apply here for consistency
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                filteredItems = filteredItems.Where(i => 
                    i.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            return filteredItems.ToList();
        }
    }
    
    private List<InventoryItemInfo> GetProtectedItems()
    {
        lock (_itemsLock)
        {
            return _cachedItems.Where(item => 
                !item.CanBeDiscarded || 
                _taskCoordinator.BlacklistedItems.Contains(item.ItemId) ||
                !InventoryHelpers.IsSafeToDiscard(item, _taskCoordinator.BlacklistedItems))
                .ToList();
        }
    }
    
    public void ExecuteAutoDiscard()
    {
        _taskCoordinator.ExecuteAutoDiscard();
    }
    
    public void SaveBlacklist()
    {
        _plugin.ConfigManager.SaveBlacklist(_taskCoordinator.BlacklistedItems);
    }
    
    public void SaveAutoDiscard()
    {
        _plugin.ConfigManager.SaveAutoDiscard(_taskCoordinator.AutoDiscardItems);
    }
    
    public void Dispose()
    {
        if (_expandedCategoriesChanged)
        {
            _plugin.ConfigManager.SaveConfiguration();
        }
        
        _iconCache?.Dispose();
        _taskCoordinator?.Dispose();
    }
}
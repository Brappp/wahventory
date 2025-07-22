using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using wahventory.External;
using wahventory.Helpers;
using wahventory.Models;
using ECommons.Automation.NeoTaskManager;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule : IDisposable
{
    // Color constants
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
    
    private readonly Plugin _plugin;
    private readonly InventoryHelpers _inventoryHelpers;
    private UniversalisClient _universalisClient;
    private readonly TaskManager _taskManager;
    private readonly IconCache _iconCache;
    private bool _initialized = false;
    
    // Thread safety
    private readonly object _itemsLock = new object();
    private readonly object _categoriesLock = new object();
    private readonly object _selectedItemsLock = new object();
    private readonly object _priceCacheLock = new object();
    private readonly object _fetchingPricesLock = new object();
    
    // Performance optimization
    private DateTime _lastRefresh = DateTime.MinValue;
    private DateTime _lastCategoryUpdate = DateTime.MinValue;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _categoryUpdateInterval = TimeSpan.FromMilliseconds(500);
    private bool _expandedCategoriesChanged = false;
    private DateTime _lastConfigSave = DateTime.MinValue;
    private readonly TimeSpan _configSaveInterval = TimeSpan.FromSeconds(2);
    private bool _windowIsOpen = false;
    
    // UI State
    private List<CategoryGroup> _categories = new();
    private List<InventoryItemInfo> _allItems = new();
    private List<InventoryItemInfo> _originalItems = new();
    private string _searchFilter = string.Empty;
    private Dictionary<uint, bool> ExpandedCategories => Settings.ExpandedCategories;
    private readonly HashSet<uint> _selectedItems = new();
    private readonly Dictionary<uint, (long price, DateTime fetchTime)> _priceCache = new();
    private readonly HashSet<uint> _fetchingPrices = new();
    private readonly Dictionary<uint, DateTime> _fetchStartTimes = new();
    private readonly TimeSpan _fetchTimeout = TimeSpan.FromSeconds(30);
    
    // Filter options
    private bool _showArmory = false;
    private bool _showOnlyHQ = false;
    
    // Safety filter options
    private bool _showSafetyFilters = true;
    private bool _showOnlyFlagged = false;
    
    private string _selectedWorld = "";
    private List<string> _availableWorlds = new();
    
    // Settings references
    private InventorySettings Settings => _plugin.Configuration.InventorySettings;
    
    // Discard state
    private bool _isDiscarding = false;
    private List<InventoryItemInfo> _itemsToDiscard = new();
    private int _discardProgress = 0;
    private string? _discardError = null;
    private DateTime _discardStartTime = DateTime.MinValue;
    private int _confirmRetryCount = 0;
    
    public InventoryManagementModule(Plugin plugin)
    {
        _plugin = plugin;
        _inventoryHelpers = new InventoryHelpers(Plugin.DataManager, Plugin.Log);
        _iconCache = new IconCache(Plugin.TextureProvider);
        _universalisClient = new UniversalisClient(Plugin.Log, "Aether");
        _taskManager = new TaskManager();
    }
    
    public void Initialize()
    {
    }
    
    public void Update()
    {
        // Lazy initialization when we're sure to be on main thread
        if (!_initialized)
        {
            InitializeOnMainThread();
        }
        
        // Clean up stuck price fetches
        CleanupStuckFetches();
        
        // Only update prices when window is open and every few seconds to reduce CPU load
        if (_windowIsOpen && Settings.AutoRefreshPrices && !_isDiscarding && DateTime.Now - _lastRefresh > _refreshInterval)
        {
            _lastRefresh = DateTime.Now;
            
            // Only fetch prices for currently visible (not filtered out) items
            List<InventoryItemInfo> visibleItems;
            lock (_itemsLock)
            {
                visibleItems = GetVisibleItems();
            }
            var stalePrices = visibleItems.Where(item => 
                item.CanBeTraded && // Only check prices for tradable items
                !IsFetchingPrice(item.ItemId) &&
                !HasValidCachedPrice(item.ItemId))
                .Take(2) // Reduced to 2 for slower, more stable price fetching
                .ToList();
                
            // Only fetch if there are visible items that need pricing
            if (stalePrices.Count > 0)
            {
                foreach (var item in stalePrices)
                {
                    _ = FetchMarketPrice(item);
                }
            }
        }
        
        // Mark window as closed when not actively drawing
        _windowIsOpen = false;
        
        // Save config periodically if needed
        if (_expandedCategoriesChanged && DateTime.Now - _lastConfigSave > _configSaveInterval)
        {
            _plugin.Configuration.Save();
            _expandedCategoriesChanged = false;
            _lastConfigSave = DateTime.Now;
        }
    }
    
    public void Draw()
    {
        // Mark window as open for price fetching optimization
        _windowIsOpen = true;
        
        // Ensure initialization before drawing
        if (!_initialized)
        {
            InitializeOnMainThread();
        }
        
        // Draw the main inventory window content
        DrawMainContent();
        
        // Draw discard confirmation as a separate window if active
        if (_isDiscarding)
        {
            DrawDiscardConfirmation();
        }
    }
    
    private void DrawMainContent()
    {
        // Top row: Search and controls
        DrawTopControls();
        
        // Filter and settings sections  
        DrawFiltersAndSettings();
        
        ImGui.Separator();
        
        // Main content area
        var contentHeight = ImGui.GetContentRegionAvail().Y - 42; // Leave space for bottom action bar
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        ImGui.BeginChild("MainContent", new Vector2(0, contentHeight), true, ImGuiWindowFlags.NoScrollbar);
        
        // Content area - removed DrawTabBar() call since we're removing those buttons
        ImGui.BeginChild("ContentArea", new Vector2(0, 0), false, ImGuiWindowFlags.NoScrollbar);
        
        if (ImGui.BeginTabBar("InventoryTabs", ImGuiTabBarFlags.None))
        {
            // Calculate filtered items count for tab display
            List<InventoryItemInfo> filteredItems;
            lock (_itemsLock)
            {
                filteredItems = GetFilteredOutItems();
            }
            
            // Available Items tab
            int availableCount;
            lock (_categoriesLock)
            {
                availableCount = _categories.Sum(c => c.Items.Count);
            }
            if (ImGui.BeginTabItem($"Available Items ({availableCount})"))
            {
                DrawAvailableItemsTab();
                ImGui.EndTabItem();
            }
            
            // Protected Items tab
            if (ImGui.BeginTabItem($"Protected Items ({filteredItems.Count})"))
            {
                DrawFilteredItemsTab(filteredItems);
                ImGui.EndTabItem();
            }
            
            // Blacklist Management tab
            if (ImGui.BeginTabItem("Blacklist Management"))
            {
                DrawBlacklistTab();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
        
        ImGui.EndChild(); // ContentArea
        ImGui.EndChild(); // MainContent
        
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        
        // Bottom action bar
        DrawBottomActionBar();
    }
    
    private void InitializeOnMainThread()
    {
        if (_initialized) return;
        
        try
        {
            // Now we can safely access ClientState
            var currentWorld = Plugin.ClientState.LocalPlayer?.CurrentWorld.Value;
            var worldName = currentWorld?.Name.ExtractText() ?? "Aether";
            _selectedWorld = worldName;
            
            // Get available worlds for the current datacenter
            try
            {
                var allWorlds = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>();
                if (allWorlds != null && !string.IsNullOrEmpty(worldName))
                {
                    var currentWorldData = allWorlds.FirstOrDefault(w => w.Name.ExtractText() == worldName);
                    if (currentWorldData.RowId != 0)
                    {
                        var currentDatacenterId = currentWorldData.DataCenter.RowId;
                        
                        var datacenterWorlds = allWorlds
                            .Where(w => w.DataCenter.RowId == currentDatacenterId && w.IsPublic)
                            .Select(w => w.Name.ExtractText())
                            .Where(name => !string.IsNullOrEmpty(name))
                            .OrderBy(w => w)
                            .ToList();
                            
                        _availableWorlds = datacenterWorlds;
                    }
                    else
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
                _availableWorlds = new List<string> { worldName };
            }
            
            _universalisClient.Dispose();
            _universalisClient = new UniversalisClient(Plugin.Log, worldName);
            
            RefreshInventory();
            _initialized = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to initialize InventoryManagementModule on main thread");
        }
    }
    
    private void RefreshInventory()
    {
        var newItems = _inventoryHelpers.GetAllItems(_showArmory, false);
        
        lock (_itemsLock)
        {
            _originalItems = newItems;
            _allItems = new List<InventoryItemInfo>(_originalItems);
            
            // Assess safety for all items and update market prices from cache
            foreach (var item in _allItems)
            {
                item.SafetyAssessment = InventoryHelpers.AssessItemSafety(item, Settings);
                
                lock (_priceCacheLock)
                {
                    if (_priceCache.TryGetValue(item.ItemId, out var cached))
                    {
                        item.MarketPrice = cached.price;
                        item.MarketPriceFetchTime = cached.fetchTime;
                    }
                }
                
                // Restore selection state
                lock (_selectedItemsLock)
                {
                    item.IsSelected = _selectedItems.Contains(item.ItemId);
                }
            }
        }
        
        UpdateCategories();
    }
    
    private void UpdateCategories()
    {
        List<InventoryItemInfo> itemsCopy;
        lock (_itemsLock)
        {
            itemsCopy = new List<InventoryItemInfo>(_allItems);
        }
        
        var filteredItems = itemsCopy.AsEnumerable();
        
        // Apply text search filter
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            filteredItems = filteredItems.Where(i => i.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
        }
        
        // Apply item filters
        if (_showOnlyHQ)
        {
            filteredItems = filteredItems.Where(i => i.IsHQ);
        }
        
        // Apply safety filters directly
        var filters = Settings.SafetyFilters;
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
            filteredItems = filteredItems.Where(i => !(i.IsUnique && i.IsUntradable && !InventoryHelpers.SafeUniqueItems.Contains(i.ItemId)));
        if (filters.FilterHQItems)
            filteredItems = filteredItems.Where(i => !i.IsHQ);
        if (filters.FilterCollectables)
            filteredItems = filteredItems.Where(i => !i.IsCollectable);
        if (filters.FilterSpiritbondedItems)
            filteredItems = filteredItems.Where(i => i.SpiritBond < filters.MinSpiritbondToFilter);
        
        if (_showOnlyFlagged)
        {
            filteredItems = filteredItems.Where(i => i.SafetyAssessment?.SafetyFlags.Any() == true);
        }
            

            
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
    }
    
    private List<InventoryItemInfo> GetVisibleItems()
    {
        // NOTE: This method should be called inside a lock(_itemsLock)
        var filteredItems = _allItems.AsEnumerable();
        
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            filteredItems = filteredItems.Where(i => i.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
        }
        
        if (_showOnlyHQ)
        {
            filteredItems = filteredItems.Where(i => i.IsHQ);
        }
        
        var filters = Settings.SafetyFilters;
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
            filteredItems = filteredItems.Where(i => !(i.IsUnique && i.IsUntradable && !InventoryHelpers.SafeUniqueItems.Contains(i.ItemId)));
        if (filters.FilterHQItems)
            filteredItems = filteredItems.Where(i => !i.IsHQ);
        if (filters.FilterCollectables)
            filteredItems = filteredItems.Where(i => !i.IsCollectable);
        if (filters.FilterSpiritbondedItems)
            filteredItems = filteredItems.Where(i => i.SpiritBond < filters.MinSpiritbondToFilter);
        
        if (_showOnlyFlagged)
        {
            filteredItems = filteredItems.Where(i => i.SafetyAssessment?.SafetyFlags.Any() == true);
        }
        
        return filteredItems.ToList();
    }
    
    private void CleanupStuckFetches()
    {
        List<uint> stuckItems;
        lock (_fetchingPricesLock)
        {
            stuckItems = _fetchStartTimes.Where(kvp => 
                DateTime.Now - kvp.Value > _fetchTimeout).Select(kvp => kvp.Key).ToList();
        }
        
        foreach (var stuckItem in stuckItems)
        {
            lock (_fetchingPricesLock)
            {
                _fetchingPrices.Remove(stuckItem);
                _fetchStartTimes.Remove(stuckItem);
            }
            
            lock (_itemsLock)
            {
                var stuckItemInfo = _allItems.FirstOrDefault(i => i.ItemId == stuckItem);
                if (stuckItemInfo != null)
                {
                    stuckItemInfo.MarketPrice = -1;
                    lock (_priceCacheLock)
                    {
                        _priceCache[stuckItem] = (-1, DateTime.Now);
                    }
                }
            }
            
            Plugin.Log.Warning($"Cleaned up stuck price fetch for item {stuckItem}");
        }
    }

    private bool IsFetchingPrice(uint itemId)
    {
        lock (_fetchingPricesLock)
        {
            return _fetchingPrices.Contains(itemId);
        }
    }
    
    private bool HasValidCachedPrice(uint itemId)
    {
        lock (_priceCacheLock)
        {
            if (_priceCache.TryGetValue(itemId, out var cached))
            {
                return DateTime.Now - cached.fetchTime <= TimeSpan.FromMinutes(Settings.PriceCacheDurationMinutes);
            }
            return false;
        }
    }
    
    private async Task FetchMarketPrice(InventoryItemInfo item)
    {
        if (IsFetchingPrice(item.ItemId)) return;
        if (!item.CanBeTraded) return;
        
        lock (_fetchingPricesLock)
        {
            _fetchingPrices.Add(item.ItemId);
            _fetchStartTimes[item.ItemId] = DateTime.Now;
        }
        
        try
        {
            var result = await _universalisClient.GetMarketPrice(item.ItemId, item.IsHQ);
            
            if (result != null)
            {
                item.MarketPrice = result.Price;
                item.MarketPriceFetchTime = DateTime.Now;
                lock (_priceCacheLock)
                {
                    _priceCache[item.ItemId] = (result.Price, DateTime.Now);
                }
            }
            else
            {
                item.MarketPrice = -1;
                lock (_priceCacheLock)
                {
                    _priceCache[item.ItemId] = (-1, DateTime.Now);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to fetch price for {item.Name}");
            item.MarketPrice = -1;
            lock (_priceCacheLock)
            {
                _priceCache[item.ItemId] = (-1, DateTime.Now);
            }
        }
        finally
        {
            lock (_fetchingPricesLock)
            {
                _fetchingPrices.Remove(item.ItemId);
                _fetchStartTimes.Remove(item.ItemId);
            }
        }
    }
    
    public void Dispose()
    {
        if (_expandedCategoriesChanged)
        {
            _plugin.Configuration.Save();
        }
        
        lock (_fetchingPricesLock)
        {
            _fetchingPrices.Clear();
            _fetchStartTimes.Clear();
        }
        
        _windowIsOpen = false;
        
        _iconCache?.Dispose();
        _taskManager?.Dispose();
        _universalisClient?.Dispose();
    }
}

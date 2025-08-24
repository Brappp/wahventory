using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ECommons.Automation.NeoTaskManager;
using Dalamud.Plugin.Services;
using wahventory.Services.External;
using wahventory.Services.Helpers;
using wahventory.Models;
using wahventory.Core;

namespace wahventory.Services.Tasks;

/// <summary>
/// Coordinates all task services and provides high-level operations
/// </summary>
public class TaskCoordinator : IDisposable
{
    private readonly TaskManager _taskManager;
    private readonly IPluginLog _log;
    
    // Task services
    public InventoryTaskService InventoryTasks { get; }
    public PriceTaskService PriceTasks { get; }
    public DiscardTaskService DiscardTasks { get; }
    public PassiveTaskService PassiveTasks { get; }
    
    // Shared lock objects (will be passed from InventoryManagementModule)
    private readonly object _itemsLock;
    private readonly object _categoriesLock;
    private readonly object _priceCacheLock;
    private readonly object _fetchingPricesLock;
    
    // Dependencies
    private readonly Configuration _configuration;
    private readonly InventoryHelpers _inventoryHelpers;
    private UniversalisClient _universalisClient;
    private readonly IChatGui _chatGui;
    private readonly IGameGui _gameGui;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    
    // Shared state
    public HashSet<uint> BlacklistedItems { get; set; } = new();
    public HashSet<uint> AutoDiscardItems { get; set; } = new();

    public TaskCoordinator(
        IPluginLog log,
        Configuration configuration,
        InventoryHelpers inventoryHelpers,
        UniversalisClient universalisClient,
        IChatGui chatGui,
        IGameGui gameGui,
        ICondition condition,
        IClientState clientState,
        object itemsLock,
        object categoriesLock,
        object priceCacheLock,
        object fetchingPricesLock)
    {
        _log = log;
        _configuration = configuration;
        _inventoryHelpers = inventoryHelpers;
        _universalisClient = universalisClient;
        _chatGui = chatGui;
        _gameGui = gameGui;
        _condition = condition;
        _clientState = clientState;
        _itemsLock = itemsLock;
        _categoriesLock = categoriesLock;
        _priceCacheLock = priceCacheLock;
        _fetchingPricesLock = fetchingPricesLock;
        
        // Create single task manager
        _taskManager = new TaskManager();
        
        // Initialize task services
        InventoryTasks = new InventoryTaskService(
            _taskManager, _log, _inventoryHelpers, _configuration, _itemsLock, _categoriesLock);
            
        PriceTasks = new PriceTaskService(
            _taskManager, _log, () => _universalisClient, _configuration, _priceCacheLock, _fetchingPricesLock);
            
        DiscardTasks = new DiscardTaskService(
            _taskManager, _log, _inventoryHelpers, _chatGui, _gameGui);
            
        PassiveTasks = new PassiveTaskService(
            _taskManager, _log, _condition, _clientState, _gameGui, _configuration);
        
        // Wire up event handlers
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        // When inventory is refreshed, update prices for visible items
        InventoryTasks.InventoryRefreshed += OnInventoryRefreshed;
        
        // When prices are updated, notify UI to refresh
        PriceTasks.PriceUpdated += OnPriceUpdated;
        PriceTasks.PriceFetchFailed += OnPriceFetchFailed;
        
        // When discard operations complete, refresh inventory
        DiscardTasks.DiscardCompleted += OnDiscardCompleted;
        DiscardTasks.DiscardCancelled += OnDiscardCompleted;
        
        // When passive discard is triggered, execute auto-discard
        PassiveTasks.PassiveDiscardTriggered += OnPassiveDiscardTriggered;
    }

    private void OnInventoryRefreshed(List<InventoryItemInfo> items)
    {
        _log.Debug($"Inventory refreshed with {items.Count} items");
        
        // Auto-fetch prices for tradeable items if enabled
        if (_configuration.InventorySettings.AutoRefreshPrices)
        {
            var tradeableItems = items.Where(i => i.CanBeTraded).Take(5); // Limit to prevent API spam
            PriceTasks.EnqueueBatchPriceFetch(tradeableItems, 3);
        }
    }

    private void OnPriceUpdated(uint itemId, long price, DateTime fetchTime)
    {
        _log.Debug($"Price updated for item {itemId}: {price} gil");
        
        // Update item prices in current inventory
        var items = InventoryTasks.GetCurrentItems();
        foreach (var item in items.Where(i => i.ItemId == itemId))
        {
            item.MarketPrice = price;
            item.MarketPriceFetchTime = fetchTime;
        }
    }

    private void OnPriceFetchFailed(uint itemId)
    {
        _log.Warning($"Price fetch failed for item {itemId}");
    }

    private void OnDiscardCompleted()
    {
        _log.Information("Discard operation completed, refreshing inventory");
        InventoryTasks.EnqueueRefresh();
    }

    private void OnPassiveDiscardTriggered()
    {
        _log.Information("Passive discard triggered");
        ExecuteAutoDiscard();
    }

    // High-level operations
    public void Initialize()
    {
        _log.Information("Initializing task coordinator");
        
        InventoryTasks.Initialize();
        PriceTasks.Initialize();
        DiscardTasks.Initialize();
        PassiveTasks.Initialize();
        
        // Start with a full inventory refresh (includes categories)
        InventoryTasks.EnqueueFullUpdate();
        
        // Start passive monitoring if enabled
        if (_configuration.InventorySettings.PassiveDiscard.Enabled)
        {
            PassiveTasks.StartMonitoring();
        }
    }

    public void RefreshAll(bool includeArmory = false, string searchFilter = "")
    {
        InventoryTasks.EnqueueFullUpdate(includeArmory, searchFilter);
    }
    
    public void RefreshAllImmediate(bool includeArmory = false, string searchFilter = "")
    {
        // For UI-triggered changes that need immediate response, call synchronously
        InventoryTasks.RefreshImmediately(includeArmory, searchFilter);
    }

    public void ExecuteBulkDiscard(IEnumerable<uint> selectedItemIds)
    {
        var allItems = InventoryTasks.GetOriginalItems();
        var itemsToDiscard = allItems.Where(item => selectedItemIds.Contains(item.ItemId)).ToList();
        
        if (!itemsToDiscard.Any())
        {
            _chatGui.PrintError("No items selected for discard.");
            return;
        }
        
        DiscardTasks.EnqueueBulkDiscard(itemsToDiscard, BlacklistedItems);
    }

    public void ExecuteAutoDiscard()
    {
        if (!AutoDiscardItems.Any())
        {
            _chatGui.PrintError("No items configured for auto-discard.");
            return;
        }
        
        var allItems = InventoryTasks.GetOriginalItems();
        DiscardTasks.EnqueueAutoDiscard(allItems, AutoDiscardItems, BlacklistedItems);
    }

    public void FetchPricesForVisible(IEnumerable<InventoryItemInfo> visibleItems)
    {
        PriceTasks.EnqueueBatchPriceFetch(visibleItems);
    }

    public void UpdatePassiveDiscardSettings(bool enabled)
    {
        _configuration.InventorySettings.PassiveDiscard.Enabled = enabled;
        
        if (enabled)
        {
            PassiveTasks.StartMonitoring();
        }
        else
        {
            PassiveTasks.StopMonitoring();
        }
    }
    
    public void UpdateUniversalisClient(UniversalisClient newClient)
    {
        // Store old client for delayed disposal
        var oldClient = _universalisClient;
        
        // Update the client reference
        _universalisClient = newClient;
        
        // Update PriceTasks with new client
        // Note: PriceTasks was created with the old client reference, need to fix this
        
        // Clear the price cache when world changes
        PriceTasks.ClearPriceCache();
        
        // Dispose old client after a delay to allow in-flight requests to complete
        if (oldClient != null)
        {
            Task.Delay(5000).ContinueWith(_ => 
            {
                try
                {
                    oldClient.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Error disposing old UniversalisClient");
                }
            });
        }
    }

    public void ScheduleRegularTasks()
    {
        // Schedule periodic cleanup tasks
        _taskManager.Enqueue(() => PriceTasks.EnqueuePriceCleanup());
    }

    // Getters for current state
    public bool IsDiscarding => DiscardTasks.IsDiscarding;
    public int DiscardProgress => DiscardTasks.CurrentProgress;
    public string? DiscardError => DiscardTasks.CurrentError;
    public List<InventoryItemInfo> CurrentDiscardItems => DiscardTasks.CurrentDiscardItems;

    public void Dispose()
    {
        _log.Information("Disposing task coordinator");
        
        InventoryTasks?.Dispose();
        PriceTasks?.Dispose();
        DiscardTasks?.Dispose();
        PassiveTasks?.Dispose();
        _taskManager?.Dispose();
    }
}
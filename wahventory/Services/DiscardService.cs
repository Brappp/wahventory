using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Plugin.Services;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public class DiscardService : IDisposable
{
    private readonly InventoryHelpers _inventoryHelpers;
    private readonly IPluginLog _log;
    private readonly IChatGui _chatGui;
    private readonly IGameGui _gameGui;
    private readonly TaskManager _taskManager;

    private List<InventoryItemInfo> _itemsToDiscard = new();
    private int _discardProgress = 0;
    private string? _discardError = null;
    private DateTime _discardStartTime = DateTime.MinValue;
    private int _confirmRetryCount = 0;
    private bool _isDiscarding = false;

    // Cached addon index for faster lookups
    private int _lastKnownAddonIndex = 1;

    // Adaptive timing
    private int _consecutiveSuccesses = 0;
    private int _baseDelayMs = 150;
    private int _currentDelayMs = 150;

    public event Action? OnDiscardStarted;
    public event Action? OnDiscardCompleted;
    public event Action? OnDiscardCancelled;
    public event Action<int, int>? OnDiscardProgress; // current, total
    public event Action<string>? OnDiscardError;
    public event Action? OnInventoryRefreshNeeded;

    public bool IsDiscarding => _isDiscarding;
    public int DiscardProgress => _discardProgress;
    public int TotalItems => _itemsToDiscard.Count;
    public string? DiscardError => _discardError;
    public IReadOnlyList<InventoryItemInfo> ItemsToDiscard => _itemsToDiscard;
    
    public DiscardService(
        InventoryHelpers inventoryHelpers,
        IPluginLog log,
        IChatGui chatGui,
        IGameGui gameGui)
    {
        _inventoryHelpers = inventoryHelpers;
        _log = log;
        _chatGui = chatGui;
        _gameGui = gameGui;
        _taskManager = new TaskManager();
    }
    
    public void PrepareDiscard(
        IEnumerable<uint> selectedItemIds,
        IEnumerable<InventoryItemInfo> allItems,
        HashSet<uint> blacklistedItems,
        bool skipConfirmation = false)
    {
        var actualItemsToDiscard = new List<InventoryItemInfo>();

        foreach (var selectedItemId in selectedItemIds)
        {
            var actualItems = allItems.Where(i =>
                i.ItemId == selectedItemId &&
                InventoryHelpers.IsSafeToDiscard(i, blacklistedItems)).ToList();

            _log.Information($"Found {actualItems.Count} instances of item {selectedItemId} to discard");
            actualItemsToDiscard.AddRange(actualItems);
        }

        // Pre-validate: verify items still exist in inventory
        var validatedItems = PreValidateItems(actualItemsToDiscard);
        _itemsToDiscard = validatedItems;
        _log.Information($"Items to discard after validation: {_itemsToDiscard.Count}");

        if (_itemsToDiscard.Count > 0)
        {
            _isDiscarding = true;
            _discardProgress = 0;
            _discardError = null;
            _consecutiveSuccesses = 0;
            _currentDelayMs = _baseDelayMs;
            _log.Information("Discard preparation successful");

            if (skipConfirmation)
            {
                _chatGui.Print($"Auto-discarding {_itemsToDiscard.Count} item(s)...");
                StartDiscarding();
            }
            else
            {
                OnDiscardStarted?.Invoke();
            }
        }
        else
        {
            _log.Warning("No items to discard after filtering");
            _chatGui.PrintError("No items selected for discard or all selected items cannot be safely discarded.");
        }
    }

    private List<InventoryItemInfo> PreValidateItems(List<InventoryItemInfo> items)
    {
        var validated = new List<InventoryItemInfo>();

        foreach (var item in items)
        {
            var freshItem = _inventoryHelpers.FindItemInInventory(item.ItemId, item.Container);
            if (freshItem != null)
            {
                validated.Add(freshItem);
            }
            else
            {
                _log.Warning($"Pre-validation: Item {item.Name} (ID: {item.ItemId}) not found, skipping");
            }
        }

        return validated;
    }
    
    public void StartDiscarding()
    {
        if (_itemsToDiscard.Count == 0)
        {
            _chatGui.PrintError("No items to discard.");
            return;
        }
        
        _log.Information($"StartDiscarding called. Items to discard: {_itemsToDiscard.Count}");
        _taskManager.Abort();
        _taskManager.Enqueue(() => DiscardNextItem());
        _log.Information("Discard task enqueued");
    }
    
    public void CancelDiscard()
    {
        _taskManager.Abort();
        _isDiscarding = false;
        _itemsToDiscard.Clear();
        _discardProgress = 0;
        _discardError = null;
        _confirmRetryCount = 0;
        _discardStartTime = DateTime.MinValue;

        OnDiscardCancelled?.Invoke();
        OnInventoryRefreshNeeded?.Invoke();
    }
    
    private unsafe void DiscardNextItem()
    {
        _log.Information($"DiscardNextItem called. Progress: {_discardProgress}/{_itemsToDiscard.Count}");

        if (_discardProgress >= _itemsToDiscard.Count)
        {
            _chatGui.Print($"Finished discarding {_itemsToDiscard.Count} item(s).");
            _isDiscarding = false;
            OnDiscardCompleted?.Invoke();
            OnInventoryRefreshNeeded?.Invoke();
            _itemsToDiscard.Clear();
            _discardProgress = 0;
            _discardError = null;
            _confirmRetryCount = 0;
            _discardStartTime = DateTime.MinValue;
            return;
        }

        var item = _itemsToDiscard[_discardProgress];
        _log.Information($"Attempting to discard item: {item.Name} (ID: {item.ItemId})");

        try
        {
            // Re-fetch fresh item location from inventory to handle slot shifts
            var freshItem = _inventoryHelpers.FindItemInInventory(item.ItemId, item.Container);
            if (freshItem == null)
            {
                _log.Warning($"Item {item.Name} no longer found in inventory, skipping");
                _discardProgress++;
                OnDiscardProgress?.Invoke(_discardProgress, _itemsToDiscard.Count);
                // No delay needed for skipped items
                _taskManager.Enqueue(() => DiscardNextItem());
                return;
            }

            _inventoryHelpers.DiscardItem(freshItem);
            _discardProgress++;
            OnDiscardProgress?.Invoke(_discardProgress, _itemsToDiscard.Count);
            _log.Information($"Discard call completed for {item.Name}");

            _discardStartTime = DateTime.Now;
            _confirmRetryCount = 0;

            // Reduced initial delay - dialog usually appears quickly
            _taskManager.EnqueueDelay(_currentDelayMs);
            _taskManager.Enqueue(() => ConfirmDiscard());
        }
        catch (Exception ex)
        {
            _discardError = $"Failed to discard {item.Name}: {ex.Message}";
            OnDiscardError?.Invoke(_discardError);
            _log.Error(ex, $"Failed to discard {item.Name}");

            // Slow down on errors
            _consecutiveSuccesses = 0;
            _currentDelayMs = Math.Min(_currentDelayMs + 50, 300);
        }
    }
    
    private unsafe void ConfirmDiscard()
    {
        // Only log every 5th retry to reduce spam
        if (_confirmRetryCount % 5 == 0)
            _log.Information($"ConfirmDiscard called, looking for dialog (retry {_confirmRetryCount})");

        if (DateTime.Now - _discardStartTime > TimeSpan.FromSeconds(10))
        {
            _log.Warning("Discard confirmation timed out");
            _discardError = "Discard confirmation timed out";
            OnDiscardError?.Invoke(_discardError);
            CancelDiscard();
            return;
        }

        var addon = GetDiscardAddon();
        if (addon != null)
        {
            _log.Information("Found discard dialog, clicking Yes");

            var selectYesno = (AddonSelectYesno*)addon;
            if (selectYesno->YesButton != null)
            {
                selectYesno->YesButton->AtkComponentBase.SetEnabledState(true);
                addon->FireCallbackInt(0);

                _log.Information("Yes button clicked");
                _confirmRetryCount = 0;

                // Reduced delay after clicking - dialog dismisses quickly
                _taskManager.EnqueueDelay(_currentDelayMs);
                _taskManager.Enqueue(() => WaitForDiscardComplete());
            }
            else
            {
                _log.Warning("Yes button not found in dialog");
                _confirmRetryCount++;
                if (_confirmRetryCount > 10)
                {
                    _log.Error("Too many retries trying to find Yes button");
                    _discardError = "Could not find Yes button in dialog";
                    OnDiscardError?.Invoke(_discardError);
                    CancelDiscard();
                    return;
                }
                _taskManager.EnqueueDelay(50);
                _taskManager.Enqueue(() => ConfirmDiscard());
            }
        }
        else
        {
            _confirmRetryCount++;

            // Adaptive retry limit - start fast, give up sooner
            int maxRetries = 30; // 30 * 50ms = 1.5s max wait
            if (_confirmRetryCount > maxRetries)
            {
                _log.Warning("No discard dialog found, assuming no confirmation needed");
                _confirmRetryCount = 0;
                _consecutiveSuccesses++;
                AdjustTiming();
                _taskManager.EnqueueDelay(50);
                _taskManager.Enqueue(() => DiscardNextItem());
            }
            else
            {
                // Fast polling - 50ms intervals
                _taskManager.EnqueueDelay(50);
                _taskManager.Enqueue(() => ConfirmDiscard());
            }
        }
    }
    
    private unsafe void WaitForDiscardComplete()
    {
        _confirmRetryCount++;

        var addon = GetDiscardAddon();
        if (addon != null)
        {
            // Dialog still visible - keep waiting but with timeout
            if (_confirmRetryCount > 20) // 20 * 50ms = 1s max
            {
                _log.Warning("Dialog still visible after timeout, forcing continue");
                _confirmRetryCount = 0;
                _consecutiveSuccesses = 0;
                _currentDelayMs = Math.Min(_currentDelayMs + 25, 300);
                _taskManager.EnqueueDelay(100);
                _taskManager.Enqueue(() => DiscardNextItem());
            }
            else
            {
                _taskManager.EnqueueDelay(50);
                _taskManager.Enqueue(() => WaitForDiscardComplete());
            }
        }
        else
        {
            _log.Information("Dialog dismissed, continuing to next item");
            _confirmRetryCount = 0;
            _consecutiveSuccesses++;
            AdjustTiming();

            // Minimal delay before next item
            _taskManager.EnqueueDelay(75);
            _taskManager.Enqueue(() => DiscardNextItem());
        }
    }

    private void AdjustTiming()
    {
        // Speed up after consecutive successes
        if (_consecutiveSuccesses >= 3)
        {
            _currentDelayMs = Math.Max(_currentDelayMs - 10, 100);
        }
    }
    
    private unsafe AtkUnitBase* GetDiscardAddon()
    {
        // Try cached index first for faster lookup
        var result = TryGetDiscardAddonAtIndex(_lastKnownAddonIndex);
        if (result != null)
            return result;

        // Search other indices
        for (int i = 1; i < 10; i++) // Reduced from 100 - rarely need more
        {
            if (i == _lastKnownAddonIndex)
                continue;

            result = TryGetDiscardAddonAtIndex(i);
            if (result != null)
            {
                _lastKnownAddonIndex = i; // Cache for next time
                return result;
            }
        }

        return null;
    }

    private unsafe AtkUnitBase* TryGetDiscardAddonAtIndex(int index)
    {
        var addonPtr = _gameGui.GetAddonByName("SelectYesno", index);
        if (addonPtr.IsNull)
            return null;

        var addon = (AtkUnitBase*)addonPtr.Address;
        if (addon == null || !addon->IsVisible || addon->UldManager.LoadedState != AtkLoadState.Loaded)
            return null;

        if (addon->UldManager.NodeListCount <= 15)
            return null;

        var node = addon->UldManager.NodeList[15];
        if (node == null)
            return null;

        var textNode = node->GetAsAtkTextNode();
        if (textNode == null)
            return null;

        var text = Dalamud.Memory.MemoryHelper.ReadSeString(&textNode->NodeText).TextValue;

        if (text.Contains("discard", StringComparison.OrdinalIgnoreCase))
        {
            return addon;
        }

        return null;
    }
    
    public void Dispose()
    {
        CancelDiscard();
        _taskManager?.Dispose();
    }
}


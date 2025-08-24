using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.Automation.NeoTaskManager;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using wahventory.Services.Helpers;
using wahventory.Models;
using wahventory.Core;

namespace wahventory.Services.Tasks;

public class DiscardTaskService : TaskServiceBase
{
    private readonly InventoryHelpers _inventoryHelpers;
    private readonly IChatGui _chatGui;
    private readonly IGameGui _gameGui;
    
    // Events for discard progress
    public event Action<List<InventoryItemInfo>>? DiscardStarted;
    public event Action<int, int>? DiscardProgress; // current, total
    public event Action<string>? DiscardError;
    public event Action? DiscardCompleted;
    public event Action? DiscardCancelled;
    
    private List<InventoryItemInfo> _itemsToDiscard = new();
    private int _discardProgress = 0;
    private string? _discardError = null;
    private DateTime _discardStartTime = DateTime.MinValue;
    private int _confirmRetryCount = 0;
    private bool _isDiscarding = false;

    public bool IsDiscarding => _isDiscarding;
    public List<InventoryItemInfo> CurrentDiscardItems => new(_itemsToDiscard);
    public int CurrentProgress => _discardProgress;
    public string? CurrentError => _discardError;

    public DiscardTaskService(
        TaskManager taskManager,
        IPluginLog log,
        InventoryHelpers inventoryHelpers,
        IChatGui chatGui,
        IGameGui gameGui) : base(taskManager, log)
    {
        _inventoryHelpers = inventoryHelpers;
        _chatGui = chatGui;
        _gameGui = gameGui;
    }

    public void EnqueueBulkDiscard(IEnumerable<InventoryItemInfo> items, HashSet<uint> blacklistedItems)
    {
        if (_isDiscarding)
        {
            Log.Warning("Cannot start discard - already discarding");
            return;
        }

        var safeItems = items.Where(item => 
            item.CanBeDiscarded && 
            !blacklistedItems.Contains(item.ItemId) &&
            InventoryHelpers.IsSafeToDiscard(item, blacklistedItems)).ToList();

        if (!safeItems.Any())
        {
            Log.Warning("No safe items to discard");
            _chatGui.PrintError("No items can be safely discarded.");
            return;
        }

        TaskManager.Enqueue(() => StartBulkDiscard(safeItems));
    }

    public void EnqueueAutoDiscard(IEnumerable<InventoryItemInfo> allItems, HashSet<uint> autoDiscardItems, HashSet<uint> blacklistedItems)
    {
        var itemsToDiscard = allItems.Where(item => 
            autoDiscardItems.Contains(item.ItemId) && 
            item.CanBeDiscarded &&
            !blacklistedItems.Contains(item.ItemId)).ToList();

        if (!itemsToDiscard.Any())
        {
            _chatGui.PrintError("No auto-discard items found in inventory.");
            return;
        }

        _chatGui.Print($"Auto-discarding {itemsToDiscard.Count} item(s)...");
        TaskManager.Enqueue(() => StartBulkDiscard(itemsToDiscard));
    }

    public void EnqueueCancelDiscard()
    {
        TaskManager.Enqueue(() => CancelDiscard());
    }

    private void StartBulkDiscard(List<InventoryItemInfo> items)
    {
        _itemsToDiscard = items;
        _discardProgress = 0;
        _discardError = null;
        _isDiscarding = true;
        _confirmRetryCount = 0;

        Log.Information($"Starting bulk discard of {items.Count} items");
        DiscardStarted?.Invoke(_itemsToDiscard);

        TaskManager.Enqueue(() => DiscardNextItem());
    }

    private unsafe void DiscardNextItem()
    {
        if (_discardProgress >= _itemsToDiscard.Count)
        {
            Log.Information("Bulk discard completed");
            _chatGui.Print("Finished discarding items.");
            CompleteDiscard();
            return;
        }

        var item = _itemsToDiscard[_discardProgress];
        Log.Information($"Discarding item: {item.Name} (ID: {item.ItemId})");

        try
        {
            _inventoryHelpers.DiscardItem(item);
            _discardStartTime = DateTime.Now;
            
            // Wait for confirmation dialog
            TaskManager.EnqueueDelay(500);
            TaskManager.Enqueue(() => ConfirmDiscard());
        }
        catch (Exception ex)
        {
            _discardError = $"Failed to discard {item.Name}: {ex.Message}";
            Log.Error(ex, $"Failed to discard {item.Name}");
            DiscardError?.Invoke(_discardError);
        }
    }

    private unsafe void ConfirmDiscard()
    {
        Log.Debug($"Looking for discard confirmation dialog (retry {_confirmRetryCount})");

        // Timeout check
        if (DateTime.Now - _discardStartTime > TimeSpan.FromSeconds(15))
        {
            _discardError = "Discard confirmation timed out";
            Log.Warning(_discardError);
            DiscardError?.Invoke(_discardError);
            CancelDiscard();
            return;
        }

        var addon = GetDiscardAddon();
        if (addon != null)
        {
            Log.Information("Found discard dialog, clicking Yes");

            var selectYesno = (FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)addon;
            if (selectYesno->YesButton != null)
            {
                selectYesno->YesButton->AtkComponentBase.SetEnabledState(true);
                addon->FireCallbackInt(0);

                _confirmRetryCount = 0;
                _discardProgress++;
                DiscardProgress?.Invoke(_discardProgress, _itemsToDiscard.Count);

                TaskManager.EnqueueDelay(500);
                TaskManager.Enqueue(() => WaitForDiscardComplete());
            }
            else
            {
                RetryConfirmation("Yes button not found in dialog");
            }
        }
        else
        {
            _confirmRetryCount++;
            if (_confirmRetryCount > 50)
            {
                Log.Information("No discard dialog found after many retries, assuming no confirmation needed");
                _confirmRetryCount = 0;
                _discardProgress++;
                DiscardProgress?.Invoke(_discardProgress, _itemsToDiscard.Count);
                
                TaskManager.EnqueueDelay(200);
                TaskManager.Enqueue(() => DiscardNextItem());
            }
            else
            {
                TaskManager.EnqueueDelay(100);
                TaskManager.Enqueue(() => ConfirmDiscard());
            }
        }
    }

    private void RetryConfirmation(string reason)
    {
        _confirmRetryCount++;
        if (_confirmRetryCount > 10)
        {
            _discardError = $"Too many retries: {reason}";
            Log.Error(_discardError);
            DiscardError?.Invoke(_discardError);
            CancelDiscard();
            return;
        }

        TaskManager.EnqueueDelay(100);
        TaskManager.Enqueue(() => ConfirmDiscard());
    }

    private unsafe void WaitForDiscardComplete()
    {
        var addon = GetDiscardAddon();
        if (addon != null)
        {
            // Dialog still visible, wait longer
            TaskManager.EnqueueDelay(100);
            TaskManager.Enqueue(() => WaitForDiscardComplete());
        }
        else
        {
            // Dialog dismissed, continue to next item
            TaskManager.EnqueueDelay(200);
            TaskManager.Enqueue(() => DiscardNextItem());
        }
    }

    private unsafe AtkUnitBase* GetDiscardAddon()
    {
        for (int i = 1; i < 100; i++)
        {
            try
            {
                var addonPtr = _gameGui.GetAddonByName("SelectYesno", i);
                if (addonPtr.IsNull) continue;
                
                var addon = (AtkUnitBase*)addonPtr.Address;
                if (addon == null) continue;

                if (addon->IsVisible && addon->UldManager.LoadedState == FFXIVClientStructs.FFXIV.Component.GUI.AtkLoadState.Loaded)
                {
                    var textNode = addon->UldManager.NodeList[15]->GetAsAtkTextNode();
                    if (textNode != null)
                    {
                        var text = Dalamud.Memory.MemoryHelper.ReadSeString(&textNode->NodeText).TextValue;
                        
                        if (text.Contains("Discard", StringComparison.OrdinalIgnoreCase))
                        {
                            return addon;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error checking addon {i}");
                return null;
            }
        }

        return null;
    }

    private void CompleteDiscard()
    {
        _isDiscarding = false;
        _itemsToDiscard.Clear();
        _discardProgress = 0;
        _discardError = null;
        _confirmRetryCount = 0;
        _discardStartTime = DateTime.MinValue;

        DiscardCompleted?.Invoke();
    }

    private void CancelDiscard()
    {
        Log.Information("Cancelling discard operation");
        
        TaskManager.Abort();
        _isDiscarding = false;
        _itemsToDiscard.Clear();
        _discardProgress = 0;
        _discardError = null;
        _confirmRetryCount = 0;
        _discardStartTime = DateTime.MinValue;

        DiscardCancelled?.Invoke();
    }

    public override void Dispose()
    {
        if (_isDiscarding)
        {
            CancelDiscard();
        }
        base.Dispose();
    }
}
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
    
    public event Action? OnDiscardStarted;
    public event Action? OnDiscardCompleted;
    public event Action? OnDiscardCancelled;
    public event Action<int, int>? OnDiscardProgress; // current, total
    public event Action<string>? OnDiscardError;
    
    public bool IsDiscarding => _isDiscarding;
    public int DiscardProgress => _discardProgress;
    public int TotalItems => _itemsToDiscard.Count;
    public string? DiscardError => _discardError;
    public List<InventoryItemInfo> ItemsToDiscard => new(_itemsToDiscard);
    
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
        HashSet<uint> blacklistedItems)
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
        
        _itemsToDiscard = actualItemsToDiscard;
        _log.Information($"Items to discard after filtering: {_itemsToDiscard.Count}");
        
        if (_itemsToDiscard.Count > 0)
        {
            _isDiscarding = true;
            _discardProgress = 0;
            _discardError = null;
            _log.Information("Discard preparation successful");
            OnDiscardStarted?.Invoke();
        }
        else
        {
            _log.Warning("No items to discard after filtering");
            _chatGui.PrintError("No items selected for discard or all selected items cannot be safely discarded.");
        }
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
    }
    
    private unsafe void DiscardNextItem()
    {
        _log.Information($"DiscardNextItem called. Progress: {_discardProgress}/{_itemsToDiscard.Count}");
        
        if (_discardProgress >= _itemsToDiscard.Count)
        {
            _chatGui.Print("Finished discarding items.");
            _isDiscarding = false;
            OnDiscardCompleted?.Invoke();
            CancelDiscard();
            return;
        }
        
        var item = _itemsToDiscard[_discardProgress];
        _log.Information($"Attempting to discard item: {item.Name} (ID: {item.ItemId})");
        
        try
        {
            _inventoryHelpers.DiscardItem(item);
            _discardProgress++;
            OnDiscardProgress?.Invoke(_discardProgress, _itemsToDiscard.Count);
            _log.Information($"Discard call completed for {item.Name}");
            
            _discardStartTime = DateTime.Now;
            
            _taskManager.EnqueueDelay(500);
            _taskManager.Enqueue(() => ConfirmDiscard());
        }
        catch (Exception ex)
        {
            _discardError = $"Failed to discard {item.Name}: {ex.Message}";
            OnDiscardError?.Invoke(_discardError);
            _log.Error(ex, $"Failed to discard {item.Name}");
        }
    }
    
    private unsafe void ConfirmDiscard()
    {
        _log.Information($"ConfirmDiscard called, looking for dialog (retry {_confirmRetryCount})");
        
        if (DateTime.Now - _discardStartTime > TimeSpan.FromSeconds(15))
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
                _log.Information("Yes button found, enabling and clicking");
                selectYesno->YesButton->AtkComponentBase.SetEnabledState(true);
                addon->FireCallbackInt(0);
                
                _log.Information("Yes button clicked, waiting for response");
                _confirmRetryCount = 0;
                _taskManager.EnqueueDelay(500);
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
                _taskManager.EnqueueDelay(100);
                _taskManager.Enqueue(() => ConfirmDiscard());
            }
        }
        else
        {
            _confirmRetryCount++;
            if (_confirmRetryCount > 50)
            {
                _log.Warning("No discard dialog found after many retries, assuming no confirmation needed");
                _confirmRetryCount = 0;
                _taskManager.EnqueueDelay(200);
                _taskManager.Enqueue(() => DiscardNextItem());
            }
            else
            {
                _log.Information("No discard dialog found yet, retrying");
                _taskManager.EnqueueDelay(100);
                _taskManager.Enqueue(() => ConfirmDiscard());
            }
        }
    }
    
    private unsafe void WaitForDiscardComplete()
    {
        _log.Information("WaitForDiscardComplete called");
        
        var addon = GetDiscardAddon();
        if (addon != null)
        {
            _log.Information("Dialog still visible, waiting longer");
            _taskManager.EnqueueDelay(100);
            _taskManager.Enqueue(() => WaitForDiscardComplete());
        }
        else
        {
            _log.Information("Dialog dismissed, continuing to next item");
            _taskManager.EnqueueDelay(200);
            _taskManager.Enqueue(() => DiscardNextItem());
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
                if (addon == null) return null;
                
                if (addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded)
                {
                    var textNode = addon->UldManager.NodeList[15]->GetAsAtkTextNode();
                    if (textNode != null)
                    {
                        var text = Dalamud.Memory.MemoryHelper.ReadSeString(&textNode->NodeText).TextValue;
                        _log.Information($"YesNo dialog text: {text}");
                        
                        if (text.Contains("Discard", StringComparison.OrdinalIgnoreCase) || 
                            text.Contains("discard", StringComparison.OrdinalIgnoreCase))
                        {
                            _log.Information("Found discard confirmation dialog");
                            return addon;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, $"Error checking addon {i}");
                return null;
            }
        }
        
        _log.Information("No discard dialog found");
        return null;
    }
    
    public void Dispose()
    {
        CancelDiscard();
        _taskManager?.Dispose();
    }
}


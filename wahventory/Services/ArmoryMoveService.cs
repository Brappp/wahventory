using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.Automation.NeoTaskManager;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Plugin.Services;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public class ArmoryMoveService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IChatGui _chatGui;
    private readonly TaskManager _taskManager;

    private List<InventoryItemInfo> _queue = new();
    private int _progress;
    private int _movedCount;
    private int _failedCount;
    private bool _isMoving;

    public event Action? OnMoveStarted;
    public event Action<int, int>? OnMoveProgress;
    public event Action<int, int>? OnMoveCompleted;

    public bool IsMoving => _isMoving;
    public int Progress => _progress;
    public int TotalItems => _queue.Count;

    public ArmoryMoveService(IPluginLog log, IChatGui chatGui)
    {
        _log = log;
        _chatGui = chatGui;
        _taskManager = new TaskManager();
    }

    public unsafe int CountAvailableInventorySlots()
    {
        var mgr = InventoryManager.Instance();
        if (mgr == null) return 0;

        var empty = 0;
        foreach (var type in InventoryHelpers.MainInventories)
        {
            var container = mgr->GetInventoryContainer(type);
            if (container == null) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot != null && slot->ItemId == 0) empty++;
            }
        }
        return empty;
    }

    public void StartMove(IEnumerable<InventoryItemInfo> armoryItems)
    {
        if (_isMoving)
        {
            _chatGui.PrintError("[wahventory] A move is already in progress.");
            return;
        }

        _queue = armoryItems
            .Where(i => InventoryHelpers.IsArmoryContainer(i.Container))
            .ToList();

        if (_queue.Count == 0)
        {
            _chatGui.PrintError("[wahventory] No selected armory items to move.");
            return;
        }

        var available = CountAvailableInventorySlots();
        if (available == 0)
        {
            _chatGui.PrintError("[wahventory] Inventory is full — cannot move any items.");
            return;
        }

        if (_queue.Count > available)
        {
            _chatGui.Print($"[wahventory] Only {available} inventory slots free for {_queue.Count} items — will move what fits.");
            _queue = _queue.Take(available).ToList();
        }

        _isMoving = true;
        _progress = 0;
        _movedCount = 0;
        _failedCount = 0;
        OnMoveStarted?.Invoke();

        _taskManager.Abort();
        _taskManager.Enqueue(() => MoveNext());
    }

    private unsafe void MoveNext()
    {
        if (_progress >= _queue.Count)
        {
            FinishMove();
            return;
        }

        var item = _queue[_progress];
        var mgr = InventoryManager.Instance();
        if (mgr == null)
        {
            _log.Error("[ArmoryMoveService] InventoryManager is null mid-move");
            _failedCount++;
            _progress++;
            _taskManager.Enqueue(() => MoveNext());
            return;
        }

        var moved = false;
        foreach (var destType in InventoryHelpers.MainInventories)
        {
            var destContainer = mgr->GetInventoryContainer(destType);
            if (destContainer == null) continue;

            for (var destSlot = 0; destSlot < destContainer->Size; destSlot++)
            {
                var destItem = destContainer->GetInventorySlot(destSlot);
                if (destItem == null || destItem->ItemId != 0) continue;

                var result = mgr->MoveItemSlot(
                    item.Container,
                    (ushort)item.Slot,
                    destType,
                    (ushort)destSlot,
                    true);

                if (result == 0)
                {
                    moved = true;
                    _movedCount++;
                    _log.Information($"Moved {item.Name} → {destType} slot {destSlot}");
                    break;
                }

                _log.Warning($"MoveItemSlot rejected {item.Name} into {destType} slot {destSlot} (LogMessage {result}); trying next slot");
            }

            if (moved) break;
        }

        if (!moved)
        {
            _failedCount++;
            _log.Warning($"Failed to move {item.Name} (no destination slot accepted it)");
        }

        _progress++;
        OnMoveProgress?.Invoke(_progress, _queue.Count);

        _taskManager.EnqueueDelay(100);
        _taskManager.Enqueue(() => MoveNext());
    }

    private void FinishMove()
    {
        var moved = _movedCount;
        var failed = _failedCount;
        _isMoving = false;

        if (failed == 0)
            _chatGui.Print($"[wahventory] Moved {moved} item{(moved == 1 ? "" : "s")} from armory to inventory.");
        else
            _chatGui.Print($"[wahventory] Moved {moved}, {failed} failed.");

        OnMoveCompleted?.Invoke(moved, failed);

        _queue.Clear();
        _progress = 0;
        _movedCount = 0;
        _failedCount = 0;
    }

    public void Dispose()
    {
        _taskManager.Abort();
        _taskManager.Dispose();
    }
}

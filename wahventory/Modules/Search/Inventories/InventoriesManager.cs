using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using wahventory.Core;

namespace wahventory.Modules.Search.Inventories;

internal class InventoriesManager : IDisposable
{
    private readonly List<GameInventory> _inventories;
    public GameInventory? ActiveInventory { get; private set; }

    public InventoriesManager(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
    {
        _inventories = new List<GameInventory>
        {
            new NormalInventory(gameGui, dataManager, settings),
            new LargeInventory(gameGui, dataManager, settings),
            new LargestInventory(gameGui, dataManager, settings),
            new ChocoboInventory(gameGui, dataManager, settings),
            new ChocoboInventory2(gameGui, dataManager, settings),
            new RetainerInventory(gameGui, dataManager, settings),
            new LargeRetainerInventory(gameGui, dataManager, settings),
            new ArmouryInventory(gameGui, dataManager, settings)
        };
    }

    public void Update()
    {
        foreach (var inventory in _inventories)
        {
            inventory.UpdateAddonReference();
        }

        ActiveInventory = _inventories.FirstOrDefault(o => o.IsVisible && o.IsFocused());
    }

    public void ClearHighlights()
    {
        foreach (var inventory in _inventories)
        {
            inventory.ClearHighlights();
        }
    }

    public void HighlightItemInAll(uint itemId)
    {
        foreach (var inventory in _inventories)
        {
            if (inventory.IsVisible)
            {
                inventory.HighlightItem(itemId);
                inventory.UpdateHighlights();
            }
        }
    }

    public void Dispose()
    {
        ClearHighlights();
        _inventories.Clear();
    }
}

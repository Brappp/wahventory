using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using wahventory.Core;
using wahventory.Modules.Search.Filters;

namespace wahventory.Modules.Search.Inventories;

public abstract unsafe class GameInventory
{
    protected readonly IGameGui GameGui;
    protected readonly IDataManager DataManager;
    protected readonly SearchBarSettings Settings;

    public abstract string AddonName { get; }
    public abstract int OffsetX { get; }

    protected nint _addon = 0;
    public nint Addon => _addon;

    protected AtkUnitBase* Node => (AtkUnitBase*)_addon;

    protected List<List<bool>>? _filter = null;
    protected abstract List<List<bool>> GetEmptyFilter();

    protected abstract InventoryType[] InventoryTypes { get; }
    protected abstract int FirstBagOffset { get; }
    protected abstract int GridItemCount { get; }

    public bool IsVisible => Node != null && Node->IsVisible;

    protected GameInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
    {
        GameGui = gameGui;
        DataManager = dataManager;
        Settings = settings;
    }

    public bool IsFocused()
    {
        if (Node == null || !Node->IsVisible) return false;
        if (Node->UldManager.NodeListCount < 2) return false;

        var window = Node->UldManager.NodeList[1]->GetAsAtkComponentNode();
        if (window == null || window->Component->UldManager.NodeListCount < 4) return false;

        return window->Component->UldManager.NodeList[3]->IsVisible();
    }

    public void UpdateAddonReference()
    {
        var ptr = GameGui.GetAddonByName(AddonName, 1);
        _addon = ptr.IsNull ? 0 : ptr.Address;
    }

    public virtual void ApplyFilters(List<Filter> filters, string text)
    {
        if (text.Length < 2)
        {
            _filter = null;
            return;
        }

        _filter = GetEmptyFilter();
        var searchTerms = text.ToUpper().Split(Settings.SearchTermsSeparatorCharacter);

        var items = GetSortedItems();

        foreach (var item in items)
        {
            try
            {
                var highlight = false;

                if (item.ItemData.RowId != 0)
                {
                    var successCount = 0;
                    foreach (var term in searchTerms)
                    {
                        foreach (var filter in filters)
                        {
                            if (filter.FilterItem(item.ItemData, term))
                            {
                                successCount++;
                                break;
                            }
                        }
                    }

                    highlight = successCount == searchTerms.Length;
                }

                var bagIndex = GetBagIndex(item) - FirstBagOffset;
                if (bagIndex >= 0 && bagIndex < _filter.Count)
                {
                    var bag = _filter[bagIndex];
                    var slot = GridItemCount - 1 - item.SortedSlotIndex;
                    if (slot >= 0 && slot < bag.Count)
                    {
                        bag[slot] = highlight;
                    }
                }
            }
            catch { }
        }
    }

    protected virtual List<InventoryItemData> GetSortedItems()
    {
        var items = new List<InventoryItemData>();
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null) return items;

        var itemSheet = DataManager.GetExcelSheet<Item>();
        if (itemSheet == null) return items;

        var sortedSlotIndex = 0;
        foreach (var inventoryType in InventoryTypes)
        {
            var container = inventoryManager->GetInventoryContainer(inventoryType);
            if (container == null || container->Size == 0) continue;

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->ItemId == 0) continue;

                var itemData = itemSheet.GetRow(slot->ItemId);
                items.Add(new InventoryItemData
                {
                    ItemData = itemData,
                    Container = inventoryType,
                    SlotIndex = i,
                    SortedSlotIndex = sortedSlotIndex++
                });
            }
        }

        return items;
    }

    protected virtual int GetBagIndex(InventoryItemData item)
    {
        return (int)item.Container;
    }

    public void UpdateHighlights()
    {
        InternalUpdateHighlights(false);
    }

    protected abstract void InternalUpdateHighlights(bool forced = false);

    public void ClearHighlights()
    {
        _filter = null;
        InternalUpdateHighlights(true);
    }

    protected void UpdateGridHighlights(AtkUnitBase* grid, int startIndex, int bagIndex)
    {
        if (grid == null) return;

        for (var j = startIndex; j < startIndex + GridItemCount; j++)
        {
            var highlight = true;
            if (_filter != null && bagIndex >= 0 && bagIndex < _filter.Count && j - startIndex < _filter[bagIndex].Count)
            {
                highlight = _filter[bagIndex][j - startIndex];
            }

            SetNodeHighlight(grid->UldManager.NodeList[j], highlight);
        }
    }

    protected static void SetNodeHighlight(AtkResNode* node, bool highlight)
    {
        node->MultiplyRed = highlight || !node->IsVisible() ? (byte)100 : (byte)20;
        node->MultiplyGreen = highlight || !node->IsVisible() ? (byte)100 : (byte)20;
        node->MultiplyBlue = highlight || !node->IsVisible() ? (byte)100 : (byte)20;
    }

    public static void SetTabHighlight(AtkResNode* tab, bool highlight)
    {
        tab->MultiplyRed = highlight ? (byte)250 : (byte)100;
        tab->MultiplyGreen = highlight ? (byte)250 : (byte)100;
        tab->MultiplyBlue = highlight ? (byte)250 : (byte)100;
    }

    public static bool GetTabEnabled(AtkComponentBase* tab)
    {
        if (tab->UldManager.NodeListCount < 2) return false;
        return tab->UldManager.NodeList[2]->IsVisible();
    }

    public static bool GetSmallTabEnabled(AtkComponentBase* tab)
    {
        if (tab->UldManager.NodeListCount < 1) return false;
        return tab->UldManager.NodeList[1]->IsVisible();
    }

    public void HighlightItem(uint itemId)
    {
        _filter = GetEmptyFilter();
        var items = GetSortedItems();

        foreach (var item in items)
        {
            var highlight = item.ItemData.RowId == itemId;
            var bagIndex = GetBagIndex(item) - FirstBagOffset;
            if (bagIndex >= 0 && bagIndex < _filter.Count)
            {
                var bag = _filter[bagIndex];
                var slot = GridItemCount - 1 - item.SortedSlotIndex;
                if (slot >= 0 && slot < bag.Count)
                {
                    bag[slot] = highlight;
                }
            }
        }
    }
}

public struct InventoryItemData
{
    public Item ItemData;
    public InventoryType Container;
    public int SlotIndex;
    public int SortedSlotIndex;
}

using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using wahventory.Core;
using wahventory.Modules.Search.Filters;

namespace wahventory.Modules.Search.Inventories;

internal unsafe class ArmouryInventory : GameInventory
{
    public override string AddonName => "ArmouryBoard";
    public override int OffsetX => Settings.ArmouryInventoryOffset;

    protected override InventoryType[] InventoryTypes => new[] { _currentBag };

    protected override int FirstBagOffset => (int)_currentBag;
    protected override int GridItemCount => 50;

    private InventoryType _currentBag = InventoryType.ArmoryMainHand;

    private static readonly Dictionary<int, InventoryType> TabsMap = new()
    {
        [109] = InventoryType.ArmorySoulCrystal,
        [110] = InventoryType.ArmoryRings,
        [111] = InventoryType.ArmoryWrist,
        [112] = InventoryType.ArmoryNeck,
        [113] = InventoryType.ArmoryEar,
        [114] = InventoryType.ArmoryOffHand,
        [115] = InventoryType.ArmoryFeets,
        [116] = InventoryType.ArmoryLegs,
        [117] = InventoryType.ArmoryHands,
        [118] = InventoryType.ArmoryBody,
        [119] = InventoryType.ArmoryHead,
        [120] = InventoryType.ArmoryMainHand,
    };

    public ArmouryInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
        : base(gameGui, dataManager, settings) { }

    protected override List<List<bool>> GetEmptyFilter()
    {
        var emptyFilter = new List<List<bool>>();
        var list = new List<bool>(GridItemCount);
        for (var j = 0; j < GridItemCount; j++)
        {
            list.Add(false);
        }
        emptyFilter.Add(list);
        return emptyFilter;
    }

    public override void ApplyFilters(List<Filter> filters, string text)
    {
        _currentBag = GetCurrentBag();
        base.ApplyFilters(filters, text);
    }

    private InventoryType GetCurrentBag()
    {
        if (Node->UldManager.NodeListCount < 120) return _currentBag;

        foreach (var tab in TabsMap)
        {
            if (GetSmallTabEnabled(Node->UldManager.NodeList[tab.Key]->GetComponent()))
            {
                return tab.Value;
            }
        }

        return _currentBag;
    }

    protected override List<InventoryItemData> GetSortedItems()
    {
        var items = new List<InventoryItemData>();
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null) return items;

        var itemSheet = DataManager.GetExcelSheet<Item>();
        if (itemSheet == null) return items;

        var container = inventoryManager->GetInventoryContainer(_currentBag);
        if (container == null || container->Size == 0) return items;

        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;

            var itemData = itemSheet.GetRow(slot->ItemId);
            items.Add(new InventoryItemData
            {
                ItemData = itemData,
                Container = _currentBag,
                SlotIndex = i,
                SortedSlotIndex = i
            });
        }

        return items;
    }

    protected override void InternalUpdateHighlights(bool forced = false)
    {
        if (Node == null || Node->UldManager.NodeListCount < 56) return;

        UpdateGridHighlights(Node, 7, 0);
    }
}

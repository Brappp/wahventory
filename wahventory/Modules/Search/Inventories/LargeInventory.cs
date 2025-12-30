using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using wahventory.Core;

namespace wahventory.Modules.Search.Inventories;

internal unsafe class LargeInventory : GameInventory
{
    public override string AddonName => "InventoryLarge";
    public override int OffsetX => Settings.LargeInventoryOffset;

    protected override InventoryType[] InventoryTypes => new[]
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    };

    protected override int FirstBagOffset => (int)InventoryType.Inventory1;
    protected override int GridItemCount => 35;

    public LargeInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
        : base(gameGui, dataManager, settings) { }

    protected override List<List<bool>> GetEmptyFilter()
    {
        var emptyFilter = new List<List<bool>>();
        for (var i = 0; i < 4; i++)
        {
            var list = new List<bool>(GridItemCount);
            for (var j = 0; j < GridItemCount; j++)
            {
                list.Add(false);
            }
            emptyFilter.Add(list);
        }
        return emptyFilter;
    }

    protected override void InternalUpdateHighlights(bool forced = false)
    {
        if (_addon == 0) return;

        var offset = GetGridOffset();
        if (offset == -1) return;

        for (var i = 0; i < 2; i++)
        {
            var gridPtr = GameGui.GetAddonByName("InventoryGrid" + i, 1);
            if (gridPtr.IsNull) continue;
            var grid = (AtkUnitBase*)gridPtr.Address;
            UpdateGridHighlights(grid, 3, offset + i);
        }

        HighlightTabs(forced);
    }

    private void HighlightTabs(bool forced = false)
    {
        if (Node->UldManager.NodeListCount < 70) return;
        if (!Settings.HighlightTabs && !forced) return;

        var firstBagTab = Node->UldManager.NodeList[70];
        var resultsInFirstTab = _filter != null && (_filter[0].Any(b => b) || _filter[1].Any(b => b));
        SetTabHighlight(firstBagTab, resultsInFirstTab);

        var secondBagTab = Node->UldManager.NodeList[69];
        var resultsInSecondTab = _filter != null && (_filter[2].Any(b => b) || _filter[3].Any(b => b));
        SetTabHighlight(secondBagTab, resultsInSecondTab);
    }

    private int GetGridOffset()
    {
        if (Node->UldManager.NodeListCount < 70) return -1;

        var firstBagTab = Node->UldManager.NodeList[70];
        if (GetTabEnabled(firstBagTab->GetComponent()))
        {
            return 0;
        }

        var secondBagTab = Node->UldManager.NodeList[69];
        if (GetTabEnabled(secondBagTab->GetComponent()))
        {
            return 2;
        }

        return -1;
    }
}

internal class LargestInventory : LargeInventory
{
    public override string AddonName => "InventoryExpansion";
    public override int OffsetX => Settings.LargestInventoryOffset;

    public LargestInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
        : base(gameGui, dataManager, settings) { }
}

using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using wahventory.Core;

namespace wahventory.Modules.Search.Inventories;

internal unsafe class NormalInventory : GameInventory
{
    public override string AddonName => "Inventory";
    public override int OffsetX => Settings.NormalInventoryOffset;

    protected override InventoryType[] InventoryTypes => new[]
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4
    };

    protected override int FirstBagOffset => (int)InventoryType.Inventory1;
    protected override int GridItemCount => 35;

    public NormalInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
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

        var gridPtr = GameGui.GetAddonByName("InventoryGrid", 1);
        if (gridPtr.IsNull) return;
        var grid = (AtkUnitBase*)gridPtr.Address;
        UpdateGridHighlights(grid, 3, offset);

        HighlightTabs(forced);
    }

    private void HighlightTabs(bool forced = false)
    {
        if (!Settings.HighlightTabs && !forced) return;

        for (var i = 0; i < 4; i++)
        {
            UpdateTabHighlight(i);
        }
    }

    private void UpdateTabHighlight(int index)
    {
        if (Node == null || Node->UldManager.NodeListCount < 15) return;

        var bagTab = Node->UldManager.NodeList[15 - index];
        var resultsInTab = _filter != null && _filter[index].Any(b => b);
        SetTabHighlight(bagTab, resultsInTab);
    }

    private int GetGridOffset()
    {
        if (Node == null || Node->UldManager.NodeListCount < 15) return -1;

        for (var i = 0; i < 4; i++)
        {
            var bagNode = Node->UldManager.NodeList[15 - i];
            if (GetTabEnabled(bagNode->GetComponent()))
            {
                return i;
            }
        }

        return -1;
    }
}

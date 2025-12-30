using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using wahventory.Core;

namespace wahventory.Modules.Search.Inventories;

internal unsafe class RetainerInventory : GameInventory
{
    public override string AddonName => "InventoryRetainer";
    public override int OffsetX => Settings.RetainerInventoryOffset;

    protected override InventoryType[] InventoryTypes => new[]
    {
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5
    };

    protected override int FirstBagOffset => (int)InventoryType.RetainerPage1;
    protected override int GridItemCount => 35;

    protected int TabCount = 5;
    protected int TabIndexStart = 13;

    public RetainerInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
        : base(gameGui, dataManager, settings) { }

    protected override List<List<bool>> GetEmptyFilter()
    {
        var emptyFilter = new List<List<bool>>();
        for (var i = 0; i < 5; i++)
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

        var gridPtr = GameGui.GetAddonByName("RetainerGrid", 1);
        if (gridPtr.IsNull) return;
        var grid = (AtkUnitBase*)gridPtr.Address;
        UpdateGridHighlights(grid, 3, offset);

        HighlightTabs(forced);
    }

    private void HighlightTabs(bool forced = false)
    {
        if (!Settings.HighlightTabs && !forced) return;

        for (var i = 0; i < TabCount; i++)
        {
            UpdateTabHighlight(i);
        }
    }

    protected virtual void UpdateTabHighlight(int index)
    {
        if (Node == null || Node->UldManager.NodeListCount < TabIndexStart) return;

        var tab = Node->UldManager.NodeList[TabIndexStart - index];
        var resultsInTab = _filter != null && _filter[index].Any(b => b);
        SetTabHighlight(tab, resultsInTab);
    }

    protected virtual int GetGridOffset()
    {
        if (Node == null || Node->UldManager.NodeListCount < TabIndexStart) return -1;

        for (var i = 0; i < TabCount; i++)
        {
            var bagNode = Node->UldManager.NodeList[TabIndexStart - i];
            if (GetSmallTabEnabled(bagNode->GetComponent()))
            {
                return i;
            }
        }

        return -1;
    }
}

internal class LargeRetainerInventory : RetainerInventory
{
    public override string AddonName => "InventoryRetainerLarge";
    public override int OffsetX => Settings.LargeRetainerInventoryOffset;

    public LargeRetainerInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
        : base(gameGui, dataManager, settings) { }
}

using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using wahventory.Core;

namespace wahventory.Modules.Search.Inventories;

internal unsafe class ChocoboInventory : GameInventory
{
    public override string AddonName => "InventoryBuddy";
    public override int OffsetX => Settings.ChocoboInventoryOffset;

    protected override InventoryType[] InventoryTypes => new[]
    {
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2
    };

    protected override int FirstBagOffset => (int)InventoryType.SaddleBag1;
    protected override int GridItemCount => 35;

    public ChocoboInventory(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
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

    protected override int GetBagIndex(InventoryItemData item)
    {
        if ((int)item.Container >= (int)InventoryType.PremiumSaddleBag1)
        {
            return (int)item.Container - (int)InventoryType.PremiumSaddleBag1 + 2 + (int)InventoryType.SaddleBag1;
        }
        return (int)item.Container;
    }

    protected override void InternalUpdateHighlights(bool forced = false)
    {
        if (_addon == 0) return;

        var offset = GetGridOffset();

        UpdateGridHighlights(Node, 44, offset);
        UpdateGridHighlights(Node, 8, offset + 1);

        HighlightTabs(forced);
    }

    private void HighlightTabs(bool forced = false)
    {
        if (Node->UldManager.NodeListCount < 81) return;
        if (!Settings.HighlightTabs && !forced) return;

        var firstBagTab = Node->UldManager.NodeList[81];
        var resultsInFirstTab = _filter != null && (_filter[0].Any(b => b) || _filter[1].Any(b => b));
        SetTabHighlight(firstBagTab, resultsInFirstTab);

        var secondBagTab = Node->UldManager.NodeList[80];
        var resultsInSecondTab = _filter != null && (_filter[2].Any(b => b) || _filter[3].Any(b => b));
        SetTabHighlight(secondBagTab, resultsInSecondTab);
    }

    private int GetGridOffset()
    {
        if (Node->UldManager.NodeListCount < 80) return 0;

        var firstBagTab = Node->UldManager.NodeList[80];
        if (GetTabEnabled(firstBagTab->GetComponent()))
        {
            return 2;
        }

        return 0;
    }
}

internal class ChocoboInventory2 : ChocoboInventory
{
    public override string AddonName => "InventoryBuddy2";

    public ChocoboInventory2(IGameGui gameGui, IDataManager dataManager, SearchBarSettings settings)
        : base(gameGui, dataManager, settings) { }
}

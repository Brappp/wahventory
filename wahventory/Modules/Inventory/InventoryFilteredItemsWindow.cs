using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using wahventory.Models;
using wahventory.Services;
using wahventory.Services.Helpers;
using wahventory.UI;

namespace wahventory.Modules.Inventory;

internal class InventoryFilteredItemsWindow : Window, IDisposable
{
    private readonly InventoryManagementModule _module;
    private readonly InventoryState _state;
    private readonly ItemSearchService _searchService;
    private readonly IconCache _iconCache;

    private List<uint>? _crystalShardIds;

    public InventoryFilteredItemsWindow(
        InventoryManagementModule module,
        InventoryState state,
        ItemSearchService searchService,
        IconCache iconCache)
        : base("Protected items##InventoryProtected", ImGuiWindowFlags.NoCollapse)
    {
        _module = module;
        _state = state;
        _searchService = searchService;
        _iconCache = iconCache;

        Size = new Vector2(640, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 320),
            MaximumSize = new Vector2(1200, 900),
        };
    }

    public override void Draw()
    {
        var rows = BuildRows();

        DrawHeader(rows.Count);

        if (rows.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
            {
                ImGui.TextWrapped("No protected items to show.");
            }
            return;
        }

        DrawTable(rows);
    }

    private void DrawHeader(int total)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(Theme.ColorBlue, FontAwesomeIcon.EyeSlash.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorBlue))
        {
            ImGui.Text("PROTECTED ITEMS");
        }
        ImGui.SameLine();
        ImGui.TextColored(Theme.ColorInfo, $"· {total} total");

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.TextWrapped("Items wahventory will never discard: currency, crystals/shards, hardcoded Ultimate/Special items, and your blacklist. Listed whether or not they're in your inventory.");
        }
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawTable(List<Row> rows)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(6, 3));
        using var table = ImRaii.Table("ProtectedItemsTable", 5,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp);

        if (!table) return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("9999").X + 8);
        ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("9999").X + 8);
        ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Seasonal Miscellany").X + 8);
        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("Ultimate/Special").X + 8);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (row.IconId > 0)
            {
                var icon = _iconCache.GetIcon(row.IconId);
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(18, 18));
                    ImGui.SameLine(0, 6);
                }
                else
                {
                    ImGui.Dummy(new Vector2(18, 18));
                    ImGui.SameLine(0, 6);
                }
            }
            else
            {
                ImGui.Dummy(new Vector2(18, 18));
                ImGui.SameLine(0, 6);
            }
            ImGui.Text(row.Name);

            ImGui.TableNextColumn();
            if (row.Quantity > 0) ImGui.Text(row.Quantity.ToString("N0"));
            else ImGui.TextColored(Theme.ColorSubdued, "—");

            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.ColorSubdued, row.ItemLevel > 0 ? row.ItemLevel.ToString() : "—");

            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.ColorSubdued, row.CategoryName);

            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.ColorWarning, row.Reason);
        }
    }

    private List<Row> BuildRows()
    {
        var blacklist = _module.BlacklistedItems;

        var inventoryQty = _state.SnapshotOriginalItems()
            .GroupBy(i => i.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        _crystalShardIds ??= _searchService.GetItemIdsByCategory(ItemSafetyData.CrystalAndShardCategoryIds);

        var ids = new HashSet<uint>();
        ids.UnionWith(ItemSafetyData.CurrencyRange);
        ids.UnionWith(_crystalShardIds);
        ids.UnionWith(ItemSafetyData.HardcodedBlacklist);
        ids.UnionWith(blacklist);

        var rows = new List<Row>(ids.Count);
        foreach (var id in ids)
        {
            var info = _searchService.GetItemInfo(id);
            if (!info.HasValue) continue;

            string reason;
            if (blacklist.Contains(id)) reason = "Blacklisted";
            else if (ItemSafetyData.CurrencyRange.Contains(id)) reason = "Currency";
            else if (_crystalShardIds.Contains(id)) reason = "Crystal/Shard";
            else reason = "Ultimate/Special";

            rows.Add(new Row
            {
                ItemId = id,
                Name = info.Value.Name,
                IconId = info.Value.IconId,
                ItemLevel = (uint)info.Value.ItemLevel,
                CategoryName = info.Value.CategoryName,
                Quantity = inventoryQty.TryGetValue(id, out var qty) ? qty : 0,
                Reason = reason,
            });
        }

        return rows
            .OrderBy(r => r.Reason)
            .ThenBy(r => r.Name)
            .ToList();
    }

    public void Dispose() { }

    private sealed class Row
    {
        public uint ItemId;
        public string Name = string.Empty;
        public int Quantity;
        public ushort IconId;
        public uint ItemLevel;
        public string CategoryName = string.Empty;
        public string Reason = string.Empty;
    }
}

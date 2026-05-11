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

internal class InventoryListsWindow : Window, IDisposable
{
    private readonly InventoryManagementModule _module;
    private readonly InventoryState _state;
    private readonly ItemSearchService _searchService;
    private readonly IconCache _iconCache;

    private readonly ColumnState _blacklistCol = new();
    private readonly ColumnState _autoDiscardCol = new();
    private readonly TimeSpan _searchDelay = TimeSpan.FromMilliseconds(250);

    public InventoryListsWindow(
        InventoryManagementModule module,
        InventoryState state,
        ItemSearchService searchService,
        IconCache iconCache)
        : base("Lists##InventoryLists", ImGuiWindowFlags.NoCollapse)
    {
        _module = module;
        _state = state;
        _searchService = searchService;
        _iconCache = iconCache;

        Size = new Vector2(820, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(1400, 900),
        };
    }

    public override void Draw()
    {
        RunDeferredSearch(_blacklistCol);
        RunDeferredSearch(_autoDiscardCol);

        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetContentRegionAvail().Y;
        var colW = (width - 8) / 2;

        using (ImRaii.Child("BlacklistCol", new Vector2(colW, height), true))
        {
            DrawBlacklistColumn();
        }
        ImGui.SameLine();
        using (ImRaii.Child("AutoDiscardCol", new Vector2(0, height), true))
        {
            DrawAutoDiscardColumn();
        }
    }

    private void DrawBlacklistColumn()
    {
        DrawColumnHeader("BLACKLIST", _module.BlacklistedItems.Count,
            "Never selected for discard, even if they pass filters.");
        DrawSearchAdd(_blacklistCol, "blacklist", _module.BlacklistedItems, AddToBlacklist);

        ImGui.Separator();
        ImGui.Spacing();

        // Reserve space for footer (button + hint = ~52px)
        var listHeight = ImGui.GetContentRegionAvail().Y - 52;
        if (listHeight < 60) listHeight = 60;

        using (ImRaii.Child("BlacklistItems", new Vector2(0, listHeight)))
        {
            DrawItemList(_module.BlacklistedItems, isAutoDiscard: false);
        }

        ImGui.Separator();
        if (ImGui.Button("Clear all##bl"))
        {
            ImGui.OpenPopup("ClearBlacklistConfirm");
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.TextWrapped("Hardcoded items (e.g. Ultimate tokens) are always protected.");
        }

        DrawClearConfirmPopup("ClearBlacklistConfirm",
            $"Remove all {_module.BlacklistedItems.Count} items from the blacklist?",
            () => { _module.BlacklistedItems.Clear(); _module.SaveBlacklist(); _module.RefreshInventory(); });
    }

    private void DrawAutoDiscardColumn()
    {
        DrawColumnHeader("AUTO-DISCARD", _module.AutoDiscardItems.Count,
            "Discarded by /wahventory auto and passive auto-discard.");
        DrawSearchAdd(_autoDiscardCol, "autodiscard", _module.AutoDiscardItems, AddToAutoDiscard);

        ImGui.Separator();
        ImGui.Spacing();

        var listHeight = ImGui.GetContentRegionAvail().Y - 52;
        if (listHeight < 60) listHeight = 60;

        using (ImRaii.Child("AutoDiscardItems", new Vector2(0, listHeight)))
        {
            DrawItemList(_module.AutoDiscardItems, isAutoDiscard: true);
        }

        ImGui.Separator();
        if (ImGui.Button("Clear all##ad"))
        {
            ImGui.OpenPopup("ClearAutoDiscardConfirm");
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.18f, 0.34f, 0.53f, 1f))
                              .Push(ImGuiCol.ButtonHovered, new Vector4(0.21f, 0.40f, 0.62f, 1f)))
        {
            if (ImGui.Button("Run /wahventory auto"))
            {
                _module.ExecuteAutoDiscard();
            }
        }

        DrawClearConfirmPopup("ClearAutoDiscardConfirm",
            $"Remove all {_module.AutoDiscardItems.Count} items from the auto-discard list?",
            () => { _module.AutoDiscardItems.Clear(); _module.SaveAutoDiscard(); _module.RefreshInventory(); });
    }

    private static void DrawColumnHeader(string title, int count, string description)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(Theme.ColorBlue, FontAwesomeIcon.List.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorBlue))
        {
            ImGui.Text(title);
        }
        ImGui.SameLine();
        ImGui.TextColored(Theme.ColorInfo, $"· {count} items");

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.TextWrapped(description);
        }
        ImGui.Spacing();
    }

    private void DrawSearchAdd(ColumnState col, string idSuffix, HashSet<uint> targetList, Action<uint> onAdd)
    {
        var availW = ImGui.GetContentRegionAvail().X;
        // Reserve space for the + button (28px) and gap
        ImGui.SetNextItemWidth(availW - 36);

        var search = col.SearchText;
        if (ImGui.InputTextWithHint($"##search_{idSuffix}", "Search or item ID…", ref search, 100))
        {
            col.SearchText = search;
            col.NeedsSearch = true;
            col.LastTyped = DateTime.Now;
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(string.IsNullOrWhiteSpace(col.SearchText)))
        {
            if (ImGui.Button($"+##add_{idSuffix}", new Vector2(28, 0)))
            {
                TryAddFromInput(col, targetList, onAdd);
            }
        }

        if (col.SearchResults.Count > 0 && !string.IsNullOrWhiteSpace(col.SearchText))
        {
            using (ImRaii.Child($"##results_{idSuffix}", new Vector2(0, Math.Min(120, col.SearchResults.Count * 22 + 8)), true))
            {
                foreach (var (id, name, iconId) in col.SearchResults)
                {
                    using var pushId = ImRaii.PushId($"result_{id}");
                    var already = targetList.Contains(id);

                    if (iconId > 0)
                    {
                        var icon = _iconCache.GetIcon(iconId);
                        if (icon != null)
                        {
                            ImGui.Image(icon.Handle, new Vector2(18, 18));
                            ImGui.SameLine();
                        }
                    }
                    if (already)
                    {
                        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
                        {
                            ImGui.Text($"{name} (ID: {id}) — already added");
                        }
                    }
                    else
                    {
                        if (ImGui.Selectable($"{name} (ID: {id})", false, ImGuiSelectableFlags.None))
                        {
                            onAdd(id);
                            col.SearchText = string.Empty;
                            col.SearchResults.Clear();
                        }
                    }
                }
            }
        }
    }

    private void TryAddFromInput(ColumnState col, HashSet<uint> targetList, Action<uint> onAdd)
    {
        // If the input is digits, treat as item ID. Otherwise, add the first search result.
        var text = col.SearchText.Trim();
        if (uint.TryParse(text, out var id) && id > 0)
        {
            if (!targetList.Contains(id)) onAdd(id);
        }
        else if (col.SearchResults.Count > 0)
        {
            var first = col.SearchResults[0];
            if (!targetList.Contains(first.Id)) onAdd(first.Id);
        }
        col.SearchText = string.Empty;
        col.SearchResults.Clear();
    }

    private void DrawItemList(HashSet<uint> list, bool isAutoDiscard)
    {
        if (list.Count == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
            {
                ImGui.Text("List is empty.");
            }
            return;
        }

        // Snapshot to avoid mutation-during-iteration
        var ids = list.ToArray();
        uint? toRemove = null;

        foreach (var id in ids)
        {
            using var pushId = ImRaii.PushId($"item_{id}");

            // Look up item info: prefer current inventory state, fall back to game data
            var inv = _state.FindAllItem(id);
            ushort iconId = 0;
            string name;
            string ilvlText = "—";
            bool inInventory = false;

            if (inv != null)
            {
                name = inv.Name;
                iconId = inv.IconId;
                ilvlText = inv.ItemLevel > 0 ? $"i{inv.ItemLevel}" : "—";
                inInventory = true;
            }
            else
            {
                var info = _searchService.GetItemInfo(id);
                if (info.HasValue)
                {
                    name = info.Value.Name;
                    iconId = info.Value.IconId;
                    ilvlText = info.Value.ItemLevel > 0 ? $"i{info.Value.ItemLevel}" : "—";
                }
                else
                {
                    name = $"Unknown item ({id})";
                }
            }

            if (iconId > 0)
            {
                var icon = _iconCache.GetIcon(iconId);
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(18, 18));
                    ImGui.SameLine();
                }
                else
                {
                    ImGui.Dummy(new Vector2(18, 18));
                    ImGui.SameLine();
                }
            }
            else
            {
                ImGui.Dummy(new Vector2(18, 18));
                ImGui.SameLine();
            }

            var nameColor = (isAutoDiscard && inInventory) ? Theme.ColorWarning : Theme.ColorInfo;
            using (ImRaii.PushColor(ImGuiCol.Text, nameColor))
            {
                ImGui.Text(name);
            }
            if (isAutoDiscard && inInventory)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorWarning))
                {
                    ImGui.Text("[in inventory]");
                }
            }

            // Right-align ilvl and × button
            var rowEndX = ImGui.GetContentRegionMax().X;
            ImGui.SameLine(rowEndX - 64);
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
            {
                ImGui.Text(ilvlText);
            }
            ImGui.SameLine(rowEndX - 24);
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.45f, 0.20f, 0.20f, 0.6f))
                                  .Push(ImGuiCol.Text, Theme.ColorSubdued))
            {
                if (ImGui.SmallButton("×"))
                {
                    toRemove = id;
                }
            }
        }

        if (toRemove.HasValue)
        {
            if (isAutoDiscard) RemoveFromAutoDiscard(toRemove.Value);
            else RemoveFromBlacklist(toRemove.Value);
        }
    }

    private void DrawClearConfirmPopup(string id, string message, Action onConfirm)
    {
        using var popup = ImRaii.PopupModal(id, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup) return;

        ImGui.Text(message);
        ImGui.Spacing();

        if (ImGui.Button("Yes, clear", new Vector2(110, 0)))
        {
            onConfirm();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110, 0)))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void RunDeferredSearch(ColumnState col)
    {
        if (col.NeedsSearch && DateTime.Now - col.LastTyped > _searchDelay)
        {
            col.SearchResults = _searchService.SearchItems(col.SearchText, 8);
            col.NeedsSearch = false;
        }
    }

    private void AddToBlacklist(uint id)
    {
        if (_module.BlacklistedItems.Add(id))
        {
            _module.SaveBlacklist();
            _module.RefreshInventory();
        }
    }

    private void AddToAutoDiscard(uint id)
    {
        if (_module.AutoDiscardItems.Add(id))
        {
            _module.SaveAutoDiscard();
            _module.RefreshInventory();
        }
    }

    private void RemoveFromBlacklist(uint id)
    {
        if (_module.BlacklistedItems.Remove(id))
        {
            _module.SaveBlacklist();
            _module.RefreshInventory();
        }
    }

    private void RemoveFromAutoDiscard(uint id)
    {
        if (_module.AutoDiscardItems.Remove(id))
        {
            _module.SaveAutoDiscard();
            _module.RefreshInventory();
        }
    }

    public void Dispose() { }

    private sealed class ColumnState
    {
        public string SearchText = string.Empty;
        public List<(uint Id, string Name, ushort Icon)> SearchResults = new();
        public bool NeedsSearch;
        public DateTime LastTyped = DateTime.MinValue;
    }
}

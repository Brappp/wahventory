using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Services;
using wahventory.Services.Helpers;

namespace wahventory.UI.Components;

public class SearchComponent
{
    private readonly ItemSearchService _searchService;
    private readonly IconCache _iconCache;
    
    private string _searchText = string.Empty;
    private uint _itemIdInput = 0;
    private List<(uint Id, string Name, ushort Icon)> _searchResults = new();
    private bool _isSearching = false;
    private DateTime _lastSearchTime = DateTime.MinValue;
    private readonly TimeSpan _searchDelay = TimeSpan.FromMilliseconds(300);
    
    private static readonly Vector4 ColorSubdued = new(0.6f, 0.6f, 0.6f, 1f);
    
    public event Action<uint>? OnItemSelected;
    
    public SearchComponent(ItemSearchService searchService, IconCache iconCache)
    {
        _searchService = searchService;
        _iconCache = iconCache;
    }
    
    public void Draw(string label, HashSet<uint>? existingItems = null)
    {
        ImGui.Text($"{label}:");
        ImGui.Spacing();
        
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputTextWithHint("##SearchItemName", "Search item name...", ref _searchText, 100))
        {
            _lastSearchTime = DateTime.Now;
            _isSearching = true;
        }
        
        if (_isSearching && DateTime.Now - _lastSearchTime > _searchDelay)
        {
            PerformSearch();
            _isSearching = false;
        }
        
        ImGui.SameLine();
        ImGui.Text("or ID:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        int itemId = (int)_itemIdInput;
        if (ImGui.InputInt("##ItemId", ref itemId, 0, 0))
        {
            _itemIdInput = (uint)Math.Max(0, itemId);
        }
        
        ImGui.SameLine();
        
        var canAdd = _itemIdInput > 0;
        using (var disabled = ImRaii.Disabled(!canAdd))
        {
            if (ImGui.Button($"Add##{label}"))
            {
                if (_itemIdInput > 0)
                {
                    OnItemSelected?.Invoke(_itemIdInput);
                    _itemIdInput = 0;
                    _searchText = string.Empty;
                    _searchResults.Clear();
                }
            }
        }
        
        DrawSearchResults(existingItems);
    }
    
    public void Clear()
    {
        _searchText = string.Empty;
        _itemIdInput = 0;
        _searchResults.Clear();
        _isSearching = false;
    }
    
    private void PerformSearch()
    {
        _searchResults = _searchService.SearchItems(_searchText, 20);
    }
    
    private void DrawSearchResults(HashSet<uint>? existingItems)
    {
        if (!_searchResults.Any())
            return;
        
        ImGui.Spacing();
        ImGui.Text($"Search Results ({_searchResults.Count} items):");
        using (var child = ImRaii.Child("SearchResults", new Vector2(0, 200), true))
        {
            foreach (var (id, name, iconId) in _searchResults)
            {
                using var pushId = ImRaii.PushId($"SearchResult_{id}");
                
                // Draw icon
                if (iconId > 0)
                {
                    var icon = _iconCache.GetIcon(iconId);
                    if (icon != null)
                    {
                        ImGui.Image(icon.Handle, new Vector2(20, 20));
                        ImGui.SameLine();
                    }
                    else
                    {
                        ImGui.Dummy(new Vector2(20, 20));
                        ImGui.SameLine();
                    }
                }
                else
                {
                    ImGui.Dummy(new Vector2(20, 20));
                    ImGui.SameLine();
                }
                
                // Check if already in list
                var isExisting = existingItems != null && existingItems.Contains(id);
                if (isExisting)
                {
                    using (var color = ImRaii.PushColor(ImGuiCol.Text, ColorSubdued))
                    {
                        ImGui.Text($"{name} (ID: {id}) [Already Added]");
                    }
                }
                else
                {
                    if (ImGui.Selectable($"{name} (ID: {id})"))
                    {
                        _itemIdInput = id;
                        _searchText = name;
                        OnItemSelected?.Invoke(id);
                    }
                }
            }
        }
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace wahventory.Services;

public class ItemSearchService
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    
    public ItemSearchService(IDataManager dataManager, IPluginLog log)
    {
        _dataManager = dataManager;
        _log = log;
    }
    
    public List<(uint Id, string Name, ushort Icon)> SearchItems(string searchTerm, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
            return new List<(uint, string, ushort)>();
        
        try
        {
            var itemSheet = _dataManager.GetExcelSheet<Item>();
            if (itemSheet == null) 
                return new List<(uint, string, ushort)>();
            
            var lowerSearch = searchTerm.ToLower();
            
            return itemSheet
                .Where(item => item.RowId > 0 && 
                       !string.IsNullOrEmpty(item.Name.ExtractText()) &&
                       item.Name.ExtractText().ToLower().Contains(lowerSearch))
                .Select(item => ((uint)item.RowId, item.Name.ExtractText(), item.Icon))
                .OrderBy(item => item.Item2)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to search items");
            return new List<(uint, string, ushort)>();
        }
    }
    
    public (string Name, ushort IconId, string CategoryName, int ItemLevel)? GetItemInfo(uint itemId)
    {
        try
        {
            var itemSheet = _dataManager.GetExcelSheet<Item>();
            if (itemSheet == null) 
                return null;
            
            var gameItem = itemSheet.GetRowOrDefault(itemId);
            if (gameItem == null || gameItem.Value.RowId == 0)
                return null;
            
            var item = gameItem.Value;
            var categoryName = "Unknown";
            var categorySheet = _dataManager.GetExcelSheet<ItemUICategory>();
            if (categorySheet != null && item.ItemUICategory.RowId > 0)
            {
                var category = categorySheet.GetRowOrDefault(item.ItemUICategory.RowId);
                if (category != null && category.Value.RowId != 0)
                {
                    categoryName = category.Value.Name.ExtractText();
                }
            }
            
            return (
                item.Name.ExtractText(),
                item.Icon,
                categoryName,
                (int)item.LevelItem.RowId
            );
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Failed to get item info for {itemId}");
            return null;
        }
    }
}


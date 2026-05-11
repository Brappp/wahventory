using System;
using System.Collections.Generic;
using System.Linq;
using wahventory.Models;

namespace wahventory.Modules.Inventory;

internal sealed class InventoryState
{
    private readonly object _lock = new();
    private List<CategoryGroup> _categories = new();
    private List<InventoryItemInfo> _allItems = new();
    private List<InventoryItemInfo> _originalItems = new();
    private readonly HashSet<uint> _selectedItems = new();

    public List<CategoryGroup> SnapshotCategories()
    {
        lock (_lock) return new List<CategoryGroup>(_categories);
    }

    public List<InventoryItemInfo> SnapshotOriginalItems()
    {
        lock (_lock) return _originalItems.ToList();
    }

    public int CategoryItemTotal
    {
        get { lock (_lock) return _categories.Sum(c => c.Items.Count); }
    }

    public long TotalCategoryValue
    {
        get { lock (_lock) return _categories.Sum(c => c.TotalValue ?? 0); }
    }

    public int SelectedCount
    {
        get { lock (_lock) return _selectedItems.Count; }
    }

    public bool IsSelected(uint itemId)
    {
        lock (_lock) return _selectedItems.Contains(itemId);
    }

    public List<uint> SnapshotSelectedIds()
    {
        lock (_lock) return _selectedItems.ToList();
    }

    public InventoryItemInfo? FindAllItem(uint itemId)
    {
        lock (_lock) return _allItems.FirstOrDefault(i => i.ItemId == itemId);
    }

    public bool ContainsAllItem(uint itemId)
    {
        lock (_lock) return _allItems.Any(i => i.ItemId == itemId);
    }

    public void Select(InventoryItemInfo item)
    {
        lock (_lock)
        {
            _selectedItems.Add(item.ItemId);
            item.IsSelected = true;
        }
    }

    public void Deselect(InventoryItemInfo item)
    {
        lock (_lock)
        {
            _selectedItems.Remove(item.ItemId);
            item.IsSelected = false;
        }
    }

    public void ClearSelectionAndReset()
    {
        lock (_lock)
        {
            _selectedItems.Clear();
            foreach (var item in _allItems)
            {
                item.IsSelected = false;
            }
        }
    }

    public bool AreAllSelectableSelected(CategoryGroup category, HashSet<uint> blacklist)
    {
        lock (_lock)
        {
            var selectable = category.Items.Where(i => !blacklist.Contains(i.ItemId)).ToList();
            return selectable.Count > 0 && selectable.All(i => _selectedItems.Contains(i.ItemId));
        }
    }

    public void SelectAllSelectableInCategory(CategoryGroup category, HashSet<uint> blacklist)
    {
        lock (_lock)
        {
            foreach (var item in category.Items)
            {
                if (blacklist.Contains(item.ItemId)) continue;
                _selectedItems.Add(item.ItemId);
                item.IsSelected = true;
            }
        }
    }

    public void DeselectAllInCategory(CategoryGroup category)
    {
        lock (_lock)
        {
            foreach (var item in category.Items)
            {
                _selectedItems.Remove(item.ItemId);
                item.IsSelected = false;
            }
        }
    }

    public void TransferSelectionTo(HashSet<uint> destination)
    {
        lock (_lock)
        {
            foreach (var itemId in _selectedItems)
            {
                destination.Add(itemId);
            }
            _selectedItems.Clear();
            foreach (var item in _allItems)
            {
                item.IsSelected = false;
            }
        }
    }

    public void SetItemPrice(InventoryItemInfo item, long price, DateTime fetchTime)
    {
        lock (_lock)
        {
            item.MarketPrice = price;
            item.MarketPriceFetchTime = fetchTime;
        }
    }

    public void ClearAllPrices()
    {
        lock (_lock)
        {
            foreach (var item in _allItems)
            {
                item.MarketPrice = null;
                item.MarketPriceFetchTime = null;
            }
        }
    }

    public void ApplyRefreshAndRecategorize(
        List<InventoryItemInfo> newItems,
        Action<InventoryItemInfo> initEachItem,
        Func<List<InventoryItemInfo>, IEnumerable<InventoryItemInfo>> applyFilters,
        Func<List<InventoryItemInfo>, List<CategoryGroup>> categorize)
    {
        lock (_lock)
        {
            _originalItems = newItems;
            foreach (var item in _originalItems)
            {
                initEachItem(item);
                item.IsSelected = _selectedItems.Contains(item.ItemId);
            }
            var filtered = applyFilters(_originalItems).ToList();
            _allItems = filtered;
            _categories = categorize(filtered);
        }
    }

    public void Recategorize(
        Func<List<InventoryItemInfo>, IEnumerable<InventoryItemInfo>> applyFilters,
        Func<List<InventoryItemInfo>, List<CategoryGroup>> categorize)
    {
        lock (_lock)
        {
            var filtered = applyFilters(_originalItems).ToList();
            _allItems = filtered;
            _categories = categorize(filtered);
        }
    }

    public List<InventoryItemInfo> ApplyFiltersToOriginal(
        Func<List<InventoryItemInfo>, IEnumerable<InventoryItemInfo>> applyFilters)
    {
        lock (_lock)
        {
            return applyFilters(_originalItems).ToList();
        }
    }

    public List<InventoryItemInfo> GetProtectedItems(
        Func<List<InventoryItemInfo>, List<InventoryItemInfo>> getProtected)
    {
        lock (_lock)
        {
            return getProtected(_originalItems);
        }
    }

    /// <summary>Sums MarketPrice × Quantity over every item in _allItems whose ItemId is currently selected.</summary>
    public long SumValueForSelected()
    {
        lock (_lock)
        {
            long total = 0;
            foreach (var item in _allItems)
            {
                if (!_selectedItems.Contains(item.ItemId)) continue;
                if (!item.MarketPrice.HasValue) continue;
                if (item.MarketPrice.Value <= 0) continue;
                total += item.MarketPrice.Value * item.Quantity;
            }
            return total;
        }
    }

    public List<InventoryItemInfo> SnapshotAutoDiscardCandidates(HashSet<uint> autoDiscardIds, HashSet<uint> blacklist)
    {
        lock (_lock)
        {
            return _allItems
                .Where(item => autoDiscardIds.Contains(item.ItemId) &&
                               item.CanBeDiscarded &&
                               !blacklist.Contains(item.ItemId))
                .ToList();
        }
    }
}

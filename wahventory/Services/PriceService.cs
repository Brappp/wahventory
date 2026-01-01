using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services.External;

namespace wahventory.Services;

public class PriceService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly InventorySettings _settings;
    private UniversalisClient _universalisClient;
    private string _currentWorld;
    
    private readonly object _priceCacheLock = new object();
    private readonly object _fetchingPricesLock = new object();
    
    private readonly Dictionary<uint, (long price, DateTime fetchTime)> _priceCache = new();
    private readonly HashSet<uint> _fetchingPrices = new();
    private readonly Dictionary<uint, DateTime> _fetchStartTimes = new();
    
    private readonly TimeSpan _fetchTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _fetchDelay = TimeSpan.FromSeconds(0.5);
    private DateTime _lastPriceFetch = DateTime.MinValue;
    
    public PriceService(IPluginLog log, InventorySettings settings, string initialWorld)
    {
        _log = log;
        _settings = settings;
        _currentWorld = initialWorld;
        _universalisClient = new UniversalisClient(log, initialWorld);
    }
    
    public void UpdateWorld(string worldName)
    {
        if (worldName == _currentWorld)
            return;

        _currentWorld = worldName;
        _universalisClient.UpdateWorld(worldName);

        lock (_priceCacheLock)
        {
            _priceCache.Clear();
        }
    }
    
    public bool HasValidCachedPrice(uint itemId)
    {
        lock (_priceCacheLock)
        {
            if (_priceCache.TryGetValue(itemId, out var cached))
            {
                return DateTime.Now - cached.fetchTime <= TimeSpan.FromMinutes(_settings.PriceCacheDurationMinutes);
            }
            return false;
        }
    }
    
    public long? GetCachedPrice(uint itemId)
    {
        lock (_priceCacheLock)
        {
            if (_priceCache.TryGetValue(itemId, out var cached))
            {
                if (DateTime.Now - cached.fetchTime <= TimeSpan.FromMinutes(_settings.PriceCacheDurationMinutes))
                {
                    return cached.price;
                }
            }
            return null;
        }
    }
    
    public bool IsFetchingPrice(uint itemId)
    {
        lock (_fetchingPricesLock)
        {
            return _fetchingPrices.Contains(itemId);
        }
    }
    
    public async Task<long?> FetchPrice(InventoryItemInfo item)
    {
        if (IsFetchingPrice(item.ItemId))
            return null;
        
        if (!item.CanBeTraded)
            return null;
        
        // Rate limiting
        if (DateTime.Now - _lastPriceFetch < _fetchDelay)
            return null;
        
        lock (_fetchingPricesLock)
        {
            _fetchingPrices.Add(item.ItemId);
            _fetchStartTimes[item.ItemId] = DateTime.Now;
        }
        
        _lastPriceFetch = DateTime.Now;
        
        try
        {
            var result = await _universalisClient.GetMarketPrice(item.ItemId, item.IsHQ);
            
            if (result != null)
            {
                lock (_priceCacheLock)
                {
                    _priceCache[item.ItemId] = (result.Price, DateTime.Now);
                }
                return result.Price;
            }
            else
            {
                lock (_priceCacheLock)
                {
                    _priceCache[item.ItemId] = (-1, DateTime.Now);
                }
                return -1;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Failed to fetch price for {item.Name}");
            lock (_priceCacheLock)
            {
                _priceCache[item.ItemId] = (-1, DateTime.Now);
            }
            return -1;
        }
        finally
        {
            lock (_fetchingPricesLock)
            {
                _fetchingPrices.Remove(item.ItemId);
                _fetchStartTimes.Remove(item.ItemId);
            }
        }
    }
    
    public void UpdateItemPrice(InventoryItemInfo item)
    {
        lock (_priceCacheLock)
        {
            if (_priceCache.TryGetValue(item.ItemId, out var cached))
            {
                item.MarketPrice = cached.price;
                item.MarketPriceFetchTime = cached.fetchTime;
            }
        }
    }
    
    public void CleanupStuckFetches()
    {
        List<uint> stuckItems;
        lock (_fetchingPricesLock)
        {
            stuckItems = _fetchStartTimes
                .Where(kvp => DateTime.Now - kvp.Value > _fetchTimeout)
                .Select(kvp => kvp.Key)
                .ToList();
        }
        
        foreach (var stuckItem in stuckItems)
        {
            lock (_fetchingPricesLock)
            {
                _fetchingPrices.Remove(stuckItem);
                _fetchStartTimes.Remove(stuckItem);
            }
            
            lock (_priceCacheLock)
            {
                _priceCache[stuckItem] = (-1, DateTime.Now);
            }
            
            _log.Warning($"Cleaned up stuck price fetch for item {stuckItem}");
        }
    }
    
    public void ClearCache()
    {
        lock (_priceCacheLock)
        {
            _priceCache.Clear();
        }
    }
    
    public List<InventoryItemInfo> GetItemsNeedingPriceFetch(
        IEnumerable<InventoryItemInfo> visibleItems,
        int maxCount = 2)
    {
        return visibleItems
            .Where(item => 
                item.CanBeTraded &&
                !IsFetchingPrice(item.ItemId) &&
                !HasValidCachedPrice(item.ItemId))
            .Take(maxCount)
            .ToList();
    }
    
    public void Dispose()
    {
        lock (_fetchingPricesLock)
        {
            _fetchingPrices.Clear();
            _fetchStartTimes.Clear();
        }
        
        _universalisClient?.Dispose();
    }
}


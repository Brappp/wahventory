using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ECommons.Automation.NeoTaskManager;
using Dalamud.Plugin.Services;
using wahventory.Services.External;
using wahventory.Models;
using wahventory.Core;

namespace wahventory.Services.Tasks;

public class PriceTaskService : TaskServiceBase
{
    private readonly Func<UniversalisClient> _universalisClientProvider;
    private readonly Configuration _configuration;
    private readonly object _priceCacheLock;
    private readonly object _fetchingPricesLock;
    
    // Events for price updates
    public event Action<uint, long, DateTime>? PriceUpdated;
    public event Action<uint>? PriceFetchFailed;
    
    private readonly Dictionary<uint, (long price, DateTime fetchTime)> _priceCache = new();
    private readonly HashSet<uint> _fetchingPrices = new();
    private readonly Dictionary<uint, DateTime> _fetchStartTimes = new();
    private readonly TimeSpan _fetchTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _fetchDelay = TimeSpan.FromMilliseconds(500);
    private DateTime _lastBatchFetch = DateTime.MinValue;

    public PriceTaskService(
        TaskManager taskManager, 
        IPluginLog log,
        Func<UniversalisClient> universalisClientProvider,
        Configuration configuration,
        object priceCacheLock,
        object fetchingPricesLock) : base(taskManager, log)
    {
        _universalisClientProvider = universalisClientProvider;
        _configuration = configuration;
        _priceCacheLock = priceCacheLock;
        _fetchingPricesLock = fetchingPricesLock;
    }

    public void EnqueuePriceFetch(uint itemId, bool isHQ = false)
    {
        if (!ShouldFetchPrice(itemId)) return;
        
        TaskManager.Enqueue(() => FetchSinglePrice(itemId, isHQ));
    }

    public void EnqueueBatchPriceFetch(IEnumerable<InventoryItemInfo> items, int maxBatchSize = 3)
    {
        var tradableItems = items.Where(item => 
            item.CanBeTraded && 
            ShouldFetchPrice(item.ItemId)).ToList();
            
        if (!tradableItems.Any()) return;
        
        // Rate limiting - don't fetch too frequently
        var timeSinceLastBatch = DateTime.Now - _lastBatchFetch;
        if (timeSinceLastBatch < TimeSpan.FromSeconds(2))
        {
            TaskManager.EnqueueDelay((int)(TimeSpan.FromSeconds(2) - timeSinceLastBatch).TotalMilliseconds);
        }
        
        // Process in batches to avoid overwhelming the API
        var batches = tradableItems.Chunk(maxBatchSize);
        foreach (var batch in batches)
        {
            foreach (var item in batch)
            {
                TaskManager.Enqueue(() => FetchSinglePrice(item.ItemId, item.IsHQ));
                TaskManager.EnqueueDelay((int)_fetchDelay.TotalMilliseconds);
            }
        }
        
        TaskManager.Enqueue(() => _lastBatchFetch = DateTime.Now);
    }

    public void EnqueuePriceCleanup()
    {
        TaskManager.Enqueue(() => CleanupStuckFetches());
    }

    private bool ShouldFetchPrice(uint itemId)
    {
        if (IsFetchingPrice(itemId)) return false;
        if (HasValidCachedPrice(itemId)) return false;
        return true;
    }

    private bool IsFetchingPrice(uint itemId)
    {
        lock (_fetchingPricesLock)
        {
            return _fetchingPrices.Contains(itemId);
        }
    }

    private bool HasValidCachedPrice(uint itemId)
    {
        lock (_priceCacheLock)
        {
            if (_priceCache.TryGetValue(itemId, out var cached))
            {
                var cacheAge = DateTime.Now - cached.fetchTime;
                var maxAge = TimeSpan.FromMinutes(_configuration.InventorySettings.PriceCacheDurationMinutes);
                return cacheAge <= maxAge;
            }
            return false;
        }
    }

    private async void FetchSinglePrice(uint itemId, bool isHQ)
    {
        if (!ShouldFetchPrice(itemId)) return;
        
        lock (_fetchingPricesLock)
        {
            _fetchingPrices.Add(itemId);
            _fetchStartTimes[itemId] = DateTime.Now;
        }
        
        try
        {
            Log.Debug($"Fetching price for item {itemId} (HQ: {isHQ})");
            var client = _universalisClientProvider();
            if (client == null)
            {
                Log.Warning("UniversalisClient is null, skipping price fetch");
                return;
            }
            var result = await client.GetMarketPrice(itemId, isHQ);
            
            if (result != null)
            {
                var price = result.Price;
                var fetchTime = DateTime.Now;
                
                lock (_priceCacheLock)
                {
                    _priceCache[itemId] = (price, fetchTime);
                }
                
                Log.Debug($"Price fetched for item {itemId}: {price} gil");
                PriceUpdated?.Invoke(itemId, price, fetchTime);
            }
            else
            {
                // Mark as failed fetch with -1 price
                lock (_priceCacheLock)
                {
                    _priceCache[itemId] = (-1, DateTime.Now);
                }
                
                Log.Debug($"Price fetch failed for item {itemId}");
                PriceFetchFailed?.Invoke(itemId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error fetching price for item {itemId}");
            
            lock (_priceCacheLock)
            {
                _priceCache[itemId] = (-1, DateTime.Now);
            }
            
            PriceFetchFailed?.Invoke(itemId);
        }
        finally
        {
            lock (_fetchingPricesLock)
            {
                _fetchingPrices.Remove(itemId);
                _fetchStartTimes.Remove(itemId);
            }
        }
    }

    private void CleanupStuckFetches()
    {
        List<uint> stuckItems;
        lock (_fetchingPricesLock)
        {
            stuckItems = _fetchStartTimes.Where(kvp => 
                DateTime.Now - kvp.Value > _fetchTimeout).Select(kvp => kvp.Key).ToList();
        }
        
        if (!stuckItems.Any()) return;
        
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
            
            Log.Warning($"Cleaned up stuck price fetch for item {stuckItem}");
            PriceFetchFailed?.Invoke(stuckItem);
        }
    }

    public (long price, DateTime fetchTime)? GetCachedPrice(uint itemId)
    {
        lock (_priceCacheLock)
        {
            return _priceCache.TryGetValue(itemId, out var cached) ? cached : null;
        }
    }

    public void UpdateItemPrice(InventoryItemInfo item)
    {
        var cached = GetCachedPrice(item.ItemId);
        if (cached.HasValue)
        {
            item.MarketPrice = cached.Value.price;
            item.MarketPriceFetchTime = cached.Value.fetchTime;
        }
    }

    public void ClearPriceCache()
    {
        lock (_priceCacheLock)
        {
            _priceCache.Clear();
        }
        Log.Information("Price cache cleared");
    }

    public override void Dispose()
    {
        lock (_fetchingPricesLock)
        {
            _fetchingPrices.Clear();
            _fetchStartTimes.Clear();
        }
        
        lock (_priceCacheLock)
        {
            _priceCache.Clear();
        }
        
        base.Dispose();
    }
}

public class MarketPriceResult
{
    public long Price { get; set; }
    public DateTime FetchTime { get; set; } = DateTime.Now;
}
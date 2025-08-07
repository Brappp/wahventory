using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using wahventory.Core;

namespace wahventory.Services.External;

public class UniversalisClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IPluginLog _log;
    private readonly string _worldName;
    private readonly string _baseUrl = "https://universalis.app/api/v2";
    
    public UniversalisClient(IPluginLog log, string worldName)
    {
        _log = log;
        _worldName = worldName;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "WahventoryFFXIVPlugin/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }
    
    public async Task<MarketPriceResult?> GetMarketPrice(uint itemId, bool hq = false)
    {
        try
        {
            var url = $"{_baseUrl}/{_worldName}/{itemId}";
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning($"Failed to fetch price for item {itemId}: {response.StatusCode}");
                return null;
            }
            
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonConvert.DeserializeObject<UniversalisResponse>(json);
            
            if (data == null)
            {
                return null;
            }
            long price = 0;
            if (data.Listings != null && data.Listings.Count > 0)
            {
                if (hq)
                {
                    var hqListings = data.Listings.Where(l => l.Hq).ToList();
                    if (hqListings.Any())
                    {
                        price = hqListings.Min(l => l.PricePerUnit);
                    }
                    else
                    {
                        price = data.Listings.Min(l => l.PricePerUnit);
                    }
                }
                else
                {
                    price = data.Listings.Min(l => l.PricePerUnit);
                }
            }
            else if (data.MinPrice > 0)
            {
                price = data.MinPrice;
            }
            
            return new MarketPriceResult
            {
                ItemId = itemId,
                Price = price,
                WorldName = _worldName,
                LastUpdated = DateTime.Now
            };
        }
        catch (TaskCanceledException)
        {
            _log.Warning($"Timeout fetching price for item {itemId}");
            return null;
        }
        catch (Exception ex)
        {
            _log.Error(ex, $"Error fetching price for item {itemId}");
            return null;
        }
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public class MarketPriceResult
{
    public uint ItemId { get; set; }
    public long Price { get; set; }
    public string WorldName { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
}
public class UniversalisResponse
{
    [JsonProperty("itemID")]
    public uint ItemId { get; set; }
    
    [JsonProperty("listings")]
    public List<UniversalisListing> Listings { get; set; } = new();
    
    [JsonProperty("minPrice")]
    public long MinPrice { get; set; }
    
    [JsonProperty("minPriceHQ")]
    public long MinPriceHq { get; set; }
}

public class UniversalisListing
{
    [JsonProperty("pricePerUnit")]
    public long PricePerUnit { get; set; }
    
    [JsonProperty("quantity")]
    public int Quantity { get; set; }
    
    [JsonProperty("hq")]
    public bool Hq { get; set; }
    
    [JsonProperty("worldName")]
    public string WorldName { get; set; } = string.Empty;
}

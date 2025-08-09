using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace wahventory.Models;

[Serializable]
public class TrackedInventory
{
    public string OwnerName { get; set; } = "";
    public ulong OwnerId { get; set; }
    public OwnerType Type { get; set; }
    public DateTime LastUpdated { get; set; }
    public List<TrackedItem> Items { get; set; } = new();
    
    [JsonIgnore]
    public string DisplayName => Type switch
    {
        OwnerType.Player => OwnerName,
        OwnerType.Retainer => OwnerName,
        OwnerType.FreeCompany => $"FC: {OwnerName}",
        OwnerType.Housing => $"House: {OwnerName}",
        _ => OwnerName
    };
    
    [JsonIgnore]
    public string LastUpdatedText
    {
        get
        {
            var timeAgo = DateTime.Now - LastUpdated;
            if (timeAgo.TotalMinutes < 1)
                return "just now";
            if (timeAgo.TotalHours < 1)
                return $"{(int)timeAgo.TotalMinutes} min ago";
            if (timeAgo.TotalDays < 1)
                return $"{(int)timeAgo.TotalHours} hours ago";
            if (timeAgo.TotalDays < 7)
                return $"{(int)timeAgo.TotalDays} days ago";
            return LastUpdated.ToString("MM/dd");
        }
    }
}

[Serializable]
public class TrackedItem
{
    public uint ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public uint Quantity { get; set; }
    public bool IsHQ { get; set; }
    public string Container { get; set; } = ""; // "Inventory", "Armory", "Saddlebag", "Market", etc.
    public int Slot { get; set; }
    
    // Market listing info
    public uint? MarketPrice { get; set; } // Price per unit if listed on market
    public DateTime? MarketListedDate { get; set; } // When it was listed
    
    // Optional enhanced data
    public ushort Spiritbond { get; set; }
    public ushort Condition { get; set; }
    public uint GlamourId { get; set; }
    public byte Stain1 { get; set; }
    public byte Stain2 { get; set; }
    public byte[] Materia { get; set; } = new byte[5];
}

public enum OwnerType
{
    Player,
    Retainer,
    FreeCompany,
    Housing
}

[Serializable]
public class TrackerData
{
    public int Version { get; set; } = 1;
    public Dictionary<ulong, TrackedInventory> Inventories { get; set; } = new();
    public DateTime LastFullScan { get; set; }
}

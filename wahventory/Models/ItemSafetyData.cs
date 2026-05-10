using System.Collections.Generic;
using System.Linq;

namespace wahventory.Models;

public static class ItemSafetyData
{
    public static readonly HashSet<uint> HardcodedBlacklist = new()
    {
        16039, // Ala Mhigan earrings
        24589, // Aetheryte earrings
        33648, // Menphina's earrings
        41081, // Azeyma's earrings

        21197, // UCOB token
        23175, // UWU token
        28633, // TEA token
        36810, // DSR token
        38951, // TOP token

        10155, // Ceruleum Tank
        10373, // Magitek Repair Materials
    };

    public static readonly HashSet<uint> CurrencyRange =
        Enumerable.Range(1, 99).Select(x => (uint)x).ToHashSet();

    public static readonly HashSet<uint> SafeUniqueItems = new()
    {
        2962, // Onion Doublet
        3279, // Onion Gaskins
        3743, // Onion Patterns

        9387, // Antique Helm
        9388, // Antique Mail
        9389, // Antique Gauntlets
        9390, // Antique Breeches
        9391, // Antique Sollerets

        6223, // Mended Imperial Pot Helm
        6224, // Mended Imperial Short Robe

        7060, // Durability Draught
        14945, // Squadron Enlistment Manual
        15772, // Contemporary Warfare: Defense
        15773, // Contemporary Warfare: Offense
        15774, // Contemporary Warfare: Magicks
        4572, // Company-issue Tonic
        20790, // High Grade Company-issue Tonic
    };

    public static readonly HashSet<uint> CrystalAndShardCategoryIds = new() { 63, 64 };
}

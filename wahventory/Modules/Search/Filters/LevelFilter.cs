using System.Collections.Generic;
using Lumina.Excel.Sheets;
using wahventory.Core;

namespace wahventory.Modules.Search.Filters;

internal class LevelFilter : Filter
{
    public LevelFilter(SearchBarSettings settings) : base(settings) { }

    public override string Name => "Level";
    public override string HelpText => $"Filter items by their level requirement.\nExamples: '{FilterConfig.Tag}:60', '{FilterConfig.AbbreviatedTag}:>=70', '{FilterConfig.Tag}:=90'.";

    protected override FilterSettings FilterConfig => Settings.LevelFilter;

    private enum ComparisonType
    {
        Less = 0,
        LessOrEqual = 1,
        Equal = 2,
        GreaterOrEqual = 3,
        Greater = 4
    }

    private static readonly Dictionary<string, ComparisonType> OperatorsMap = new()
    {
        ["<="] = ComparisonType.LessOrEqual,
        [">="] = ComparisonType.GreaterOrEqual,
        ["<"] = ComparisonType.Less,
        ["="] = ComparisonType.Equal,
        [">"] = ComparisonType.Greater
    };

    protected override bool Execute(Item item, string term)
    {
        var comparison = ComparisonType.LessOrEqual;
        byte value = 0;
        var found = false;

        foreach (var op in OperatorsMap)
        {
            if (term.StartsWith(op.Key))
            {
                comparison = op.Value;
                if (!byte.TryParse(term[op.Key.Length..], out value))
                    return false;
                found = true;
                break;
            }
        }

        if (!found && !byte.TryParse(term, out value))
            return false;

        return comparison switch
        {
            ComparisonType.Less => item.LevelEquip < value,
            ComparisonType.LessOrEqual => item.LevelEquip <= value,
            ComparisonType.Equal => item.LevelEquip == value,
            ComparisonType.GreaterOrEqual => item.LevelEquip >= value,
            ComparisonType.Greater => item.LevelEquip > value,
            _ => false
        };
    }
}

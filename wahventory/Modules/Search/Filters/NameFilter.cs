using Lumina.Excel.Sheets;
using wahventory.Core;

namespace wahventory.Modules.Search.Filters;

internal class NameFilter : Filter
{
    public NameFilter(SearchBarSettings settings) : base(settings) { }

    public override string Name => "Name";
    public override string HelpText => $"Filter items by checking if their name contains the search term.\nExamples: '{FilterConfig.Tag}:Materia', '{FilterConfig.AbbreviatedTag}:token'.";

    protected override FilterSettings FilterConfig => Settings.NameFilter;

    protected override bool Execute(Item item, string term)
    {
        return item.Name.ToString().ToUpper().Contains(term);
    }
}

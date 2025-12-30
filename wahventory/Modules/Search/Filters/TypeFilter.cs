using Lumina.Excel.Sheets;
using wahventory.Core;

namespace wahventory.Modules.Search.Filters;

internal class TypeFilter : Filter
{
    public TypeFilter(SearchBarSettings settings) : base(settings) { }

    public override string Name => "Type";
    public override string HelpText => $"Filter items by checking if their type/category contains the search term.\nExamples: '{FilterConfig.Tag}:Medicine', '{FilterConfig.AbbreviatedTag}:ingredient'.";

    protected override FilterSettings FilterConfig => Settings.TypeFilter;

    protected override bool Execute(Item item, string term)
    {
        return item.ItemUICategory.Value.Name.ToString().ToUpper().Contains(term);
    }
}

using Lumina.Excel.Sheets;
using wahventory.Core;

namespace wahventory.Modules.Search.Filters;

internal class JobFilter : Filter
{
    public JobFilter(SearchBarSettings settings) : base(settings) { }

    public override string Name => "Job";
    public override string HelpText => $"Filter items by checking if they are usable by a specific job.\nExamples: '{FilterConfig.Tag}:BLM', '{FilterConfig.AbbreviatedTag}:WAR'.";

    protected override FilterSettings FilterConfig => Settings.JobFilter;

    protected override bool Execute(Item item, string term)
    {
        return item.ClassJobCategory.Value.Name.ToString().ToUpper().Contains(term);
    }
}

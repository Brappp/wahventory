using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using wahventory.Core;

namespace wahventory.Modules.Search.Filters;

public abstract class Filter
{
    protected readonly SearchBarSettings Settings;

    protected Filter(SearchBarSettings settings)
    {
        Settings = settings;
    }

    public abstract string Name { get; }
    public abstract string HelpText { get; }

    protected abstract FilterSettings FilterConfig { get; }

    private string Tag => FilterConfig.Tag.ToUpper();
    private string AbbreviatedTag => FilterConfig.AbbreviatedTag.ToUpper();
    private string TagCharacter => Settings.TagSeparatorCharacter;

    public bool FilterItem(Item item, string term)
    {
        if (!FilterConfig.Enabled) return true;

        var hasAnyTag = term.Contains(TagCharacter);
        var hasTag = HasTag(term);

        if (FilterConfig.RequireTag && !hasTag) return false;
        if (!hasTag && hasAnyTag) return false;

        var t = term;
        if (hasTag)
        {
            t = RemoveTag(t);
        }

        if (t.Length == 0) return true;

        return Execute(item, t);
    }

    private bool HasTag(string text)
    {
        return text.StartsWith(Tag + TagCharacter) || text.StartsWith(AbbreviatedTag + TagCharacter);
    }

    private string RemoveTag(string text)
    {
        var t = text.Replace(Tag + TagCharacter, "");
        t = t.Replace(AbbreviatedTag + TagCharacter, "");
        return t;
    }

    protected abstract bool Execute(Item item, string term);

    public void Draw()
    {
        var enabled = FilterConfig.Enabled;
        if (ImGui.Checkbox("Enabled##" + Name, ref enabled))
        {
            FilterConfig.Enabled = enabled;
        }

        var needsTag = FilterConfig.RequireTag;
        if (ImGui.Checkbox("Requires Tag##" + Name, ref needsTag))
        {
            FilterConfig.RequireTag = needsTag;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"If enabled, the filter will only be applied if the search term begins with '{FilterConfig.Tag}{TagCharacter}' or '{FilterConfig.AbbreviatedTag}{TagCharacter}'.");
        }

        ImGui.PushItemWidth(100);
        var tag = FilterConfig.Tag;
        if (ImGui.InputText("Tag##" + Name, ref tag, 10))
        {
            FilterConfig.Tag = tag;
        }

        var abbreviatedTag = FilterConfig.AbbreviatedTag;
        if (ImGui.InputText("Abbreviated tag##" + Name, ref abbreviatedTag, 1))
        {
            if (abbreviatedTag.Length > 0)
            {
                FilterConfig.AbbreviatedTag = abbreviatedTag;
            }
        }
        ImGui.PopItemWidth();
    }
}

using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using wahventory.Core;
using wahventory.Services;
using wahventory.UI;

namespace wahventory.Modules.Inventory;

internal class InventorySettingsWindow : Window, IDisposable
{
    private readonly InventoryManagementModule _module;
    private readonly PassiveDiscardService _passiveDiscardService;
    private readonly InventoryState _state;

    public InventorySettingsWindow(
        InventoryManagementModule module,
        PassiveDiscardService passiveDiscardService,
        InventoryState state)
        : base("Settings##InventorySettings", ImGuiWindowFlags.NoCollapse)
    {
        _module = module;
        _passiveDiscardService = passiveDiscardService;
        _state = state;

        Size = new Vector2(480, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 360),
            MaximumSize = new Vector2(720, 800),
        };
    }

    public override void Draw()
    {
        var settings = _module.Settings;
        bool changed = false;

        DrawSectionHeader(FontAwesomeIcon.Robot, "PASSIVE AUTO-DISCARD");

        var enabled = settings.PassiveDiscard.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            settings.PassiveDiscard.Enabled = enabled;
            changed = true;
        }
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.TextWrapped("Auto-discards items on the auto-discard list while you're idle. Only fires in safe zones (cities, housing, inns, barracks, Gold Saucer).");
        }

        using (ImRaii.Disabled(!settings.PassiveDiscard.Enabled))
        {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(80);
            int idleTime = settings.PassiveDiscard.IdleTimeSeconds;
            if (ImGui.InputInt("Idle time required (seconds)", ref idleTime, 5, 10))
            {
                settings.PassiveDiscard.IdleTimeSeconds = Math.Max(10, Math.Min(300, idleTime));
                changed = true;
            }

            ImGui.Spacing();
            DrawStatusLine();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawSectionHeader(FontAwesomeIcon.Coins, "MARKET PRICES");

        var showPrices = settings.ShowMarketPrices;
        if (ImGui.Checkbox("Show market prices", ref showPrices))
        {
            settings.ShowMarketPrices = showPrices;
            changed = true;
        }

        using (ImRaii.Disabled(!settings.ShowMarketPrices))
        {
            ImGui.SetNextItemWidth(80);
            int cacheMin = settings.PriceCacheDurationMinutes;
            if (ImGui.InputInt("Cache duration (minutes)", ref cacheMin, 1, 10))
            {
                settings.PriceCacheDurationMinutes = Math.Max(1, cacheMin);
                changed = true;
            }

            var autoRefresh = settings.AutoRefreshPrices;
            if (ImGui.Checkbox("Auto-refresh visible items", ref autoRefresh))
            {
                settings.AutoRefreshPrices = autoRefresh;
                changed = true;
            }
        }

        if (changed)
        {
            _module.SaveConfig();
        }
    }

    private static void DrawSectionHeader(FontAwesomeIcon icon, string title)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(Theme.ColorBlue, icon.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorBlue))
        {
            ImGui.Text(title);
        }
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawStatusLine()
    {
        var status = _passiveDiscardService.GetStatus(
            _module.AutoDiscardItems,
            _state.SnapshotOriginalItems(),
            _module.BlacklistedItems);

        switch (status.State)
        {
            case PassiveDiscardState.Disabled:
                ImGui.TextColored(Theme.ColorSubdued, "● Disabled");
                break;
            case PassiveDiscardState.NoItems:
                ImGui.TextColored(Theme.ColorSubdued, "● No items to discard");
                break;
            case PassiveDiscardState.PlayerBusy:
                ImGui.TextColored(Theme.ColorWarning, "● Player busy");
                break;
            case PassiveDiscardState.NotInAllowedZone:
                ImGui.TextColored(Theme.ColorWarning, "● Not in allowed zone");
                break;
            case PassiveDiscardState.WaitingForIdle:
                ImGui.TextColored(Theme.ColorInfo, $"● Waiting for idle ({status.IdleSeconds}/{status.RequiredIdleSeconds}s)");
                break;
            case PassiveDiscardState.Cooldown:
                ImGui.TextColored(Theme.ColorSubdued, $"● Cooldown ({status.CooldownSecondsRemaining}s remaining)");
                break;
            case PassiveDiscardState.Ready:
                ImGui.TextColored(Theme.ColorSuccess, "● Ready to execute");
                break;
        }
    }

    public void Dispose() { }
}

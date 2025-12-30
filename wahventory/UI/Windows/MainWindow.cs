using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using wahventory.Modules.Inventory;
using wahventory.Modules.Search;
using wahventory.Core;

namespace wahventory.UI.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin;
    private InventoryManagementModule InventoryModule;
    private SearchModule SearchModule;

    public MainWindow(Plugin plugin, InventoryManagementModule inventoryModule, SearchModule searchModule)
        : base("wahventory - Inventory Manager", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(1400, float.MaxValue)
        };

        Plugin = plugin;
        InventoryModule = inventoryModule;
        SearchModule = searchModule;

        Size = new Vector2(900, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void OnClose()
    {
        // Stop highlighting when the window is closed
        SearchModule.StopHighlighting();
    }

    public override void Draw()
    {
        InventoryModule.Draw(SearchModule);
    }
}

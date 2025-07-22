using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using wahventory.Windows;
using wahventory.Modules.Inventory;

namespace wahventory;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/wahventory";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("WahVentory");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private InventoryManagementModule InventoryModule { get; init; }

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        InventoryModule = new InventoryManagementModule(this);
        InventoryModule.Initialize();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, InventoryModule);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the WahVentory inventory management window. Use '/wahventory auto' to auto-discard configured items."
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        
        Framework.Update += OnFrameworkUpdate;

        Log.Information($"WahVentory initialized successfully!");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        InventoryModule.Dispose();

        CommandManager.RemoveHandler(CommandName);
        
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        // Check if 'auto' argument is provided
        if (!string.IsNullOrEmpty(args) && args.Trim().ToLower() == "auto")
        {
            // Execute auto-discard
            InventoryModule.ExecuteAutoDiscard();
        }
        else
        {
            // Default behavior - toggle main UI
            ToggleMainUI();
        }
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
    
    private void OnFrameworkUpdate(IFramework framework)
    {
        InventoryModule.Update();
    }
}

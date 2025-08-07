using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using wahventory.UI.Windows;
using wahventory.Modules.Inventory;
using Dalamud.Game;
using ECommons;

namespace wahventory.Core;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/wahventory";

    public ConfigurationManager ConfigManager { get; }
    public Configuration Configuration => ConfigManager.Configuration;
    public readonly WindowSystem WindowSystem = new("wahventory");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    
    private InventoryManagementModule InventoryModule { get; init; }

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        
        ConfigManager = new ConfigurationManager(PluginInterface);

        ConfigWindow = new ConfigWindow(this);
        InventoryModule = new InventoryManagementModule(this);
        MainWindow = new MainWindow(this, InventoryModule);
        
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the wahventory window\n/wahventory auto - Execute auto-discard for configured items"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        Framework.Update += OnFrameworkUpdate;
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
    
    private bool _moduleInitialized = false;
    
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!_moduleInitialized)
        {
            InventoryModule.Initialize();
            _moduleInitialized = true;
        }
        InventoryModule.Update();
    }

    private void OnCommand(string command, string args)
    {
        if (!string.IsNullOrEmpty(args) && args.Trim().ToLower() == "auto")
        {
            InventoryModule.ExecuteAutoDiscard();
        }
        else
        {
            ToggleMainUI();
        }
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}

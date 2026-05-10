using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace wahventory.Core;

public interface IGameServices
{
    IDalamudPluginInterface PluginInterface { get; }
    ICommandManager CommandManager { get; }
    IDataManager DataManager { get; }
    IFramework Framework { get; }
    IClientState ClientState { get; }
    ITextureProvider TextureProvider { get; }
    IPluginLog Log { get; }
    IGameInteropProvider GameInteropProvider { get; }
    IChatGui ChatGui { get; }
    IGameGui GameGui { get; }
    ICondition Condition { get; }
    IObjectTable ObjectTable { get; }
    IKeyState KeyState { get; }
    IPlayerState PlayerState { get; }
}

internal sealed class GameServices : IGameServices
{
    public IDalamudPluginInterface PluginInterface { get; }
    public ICommandManager CommandManager { get; }
    public IDataManager DataManager { get; }
    public IFramework Framework { get; }
    public IClientState ClientState { get; }
    public ITextureProvider TextureProvider { get; }
    public IPluginLog Log { get; }
    public IGameInteropProvider GameInteropProvider { get; }
    public IChatGui ChatGui { get; }
    public IGameGui GameGui { get; }
    public ICondition Condition { get; }
    public IObjectTable ObjectTable { get; }
    public IKeyState KeyState { get; }
    public IPlayerState PlayerState { get; }

    public GameServices(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IDataManager dataManager,
        IFramework framework,
        IClientState clientState,
        ITextureProvider textureProvider,
        IPluginLog log,
        IGameInteropProvider gameInteropProvider,
        IChatGui chatGui,
        IGameGui gameGui,
        ICondition condition,
        IObjectTable objectTable,
        IKeyState keyState,
        IPlayerState playerState)
    {
        PluginInterface = pluginInterface;
        CommandManager = commandManager;
        DataManager = dataManager;
        Framework = framework;
        ClientState = clientState;
        TextureProvider = textureProvider;
        Log = log;
        GameInteropProvider = gameInteropProvider;
        ChatGui = chatGui;
        GameGui = gameGui;
        Condition = condition;
        ObjectTable = objectTable;
        KeyState = keyState;
        PlayerState = playerState;
    }
}

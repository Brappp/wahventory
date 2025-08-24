using ECommons.Automation.NeoTaskManager;
using Dalamud.Plugin.Services;

namespace wahventory.Services.Tasks;

public abstract class TaskServiceBase : ITaskService
{
    protected readonly IPluginLog Log;
    
    public TaskManager TaskManager { get; }

    protected TaskServiceBase(TaskManager taskManager, IPluginLog log)
    {
        TaskManager = taskManager;
        Log = log;
    }

    public virtual void Initialize()
    {
        // Override in derived classes if needed
    }

    public virtual void Dispose()
    {
        // Override in derived classes if needed
    }
}
using ECommons.Automation.NeoTaskManager;

namespace wahventory.Services.Tasks;

public interface ITaskService
{
    TaskManager TaskManager { get; }
    void Initialize();
    void Dispose();
}
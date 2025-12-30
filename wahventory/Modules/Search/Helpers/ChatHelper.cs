using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace wahventory.Modules.Search.Helpers;

public static class ChatHelper
{
    public static unsafe bool IsInputTextActive()
    {
        var framework = Framework.Instance();
        if (framework == null) return false;

        var module = framework->GetUIModule();
        if (module == null) return false;

        var atkModule = module->GetRaptureAtkModule();
        if (atkModule == null) return false;

        return atkModule->AtkModule.IsTextInputActive();
    }
}

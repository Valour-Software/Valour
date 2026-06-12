using Valour.Client.Components.Sidebar;
using Valour.Client.ContextMenu;
using Valour.Client.Modals;

namespace Valour.Client.Device;

/// <summary>
/// Handles hardware/system back requests from native hosts (e.g. the Android
/// back button or gesture). Dismisses the topmost UI layer if one is open.
/// </summary>
public static class BackNavigationService
{
    /// <summary>
    /// Attempts to dismiss the topmost dismissible UI layer.
    /// Returns true if something was closed, false if there was nothing to close
    /// (in which case the native host should perform its default behavior,
    /// such as moving the app to the background).
    /// </summary>
    public static async Task<bool> HandleBackAsync()
    {
        // Context menus sit above everything else
        var contextRoot = ContextMenuService.Root;
        if (contextRoot is not null && await contextRoot.TryCloseMenuExternalAsync())
            return true;

        // Then modals (settings menus, confirmations, etc.)
        var modalRoot = ModalRoot.Instance;
        if (modalRoot is not null && await modalRoot.TryCloseTopModalExternalAsync())
            return true;

        // Finally the mobile sidebar overlay
        if (await Sidebar.TryCloseMobileSidebarExternalAsync())
            return true;

        return false;
    }
}

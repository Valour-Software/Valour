using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Valour.Api.Planets;
using Valour.Client.Shared.Modals.ContextMenus;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

namespace Valour.Client.Modals.ContextMenus
{
    /// <summary>
    /// Assists in controlling the user context menu
    /// </summary>
    public class AddChannelContextMenu
    {
        public readonly IJSRuntime JS;
        public Category SelectedCategory;
        public AddChannelContextMenuComponent Component;
        public Func<Task> OpenEvent;

        public AddChannelContextMenu(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open(MouseEventArgs e, Category category)
        {
            Component.SetPosition(e.ClientX, e.ClientY);
            Component.SetVisibility(true);
            SelectedCategory = category;

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }
    }
}

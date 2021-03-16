using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Channels;
using Valour.Client.Planets;
using Valour.Client.Shared;
using Valour.Client.Shared.Modals.ContextMenus;
using Valour.Shared.Planets;

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
    public class ChannelListContextMenu
    {
        public readonly IJSRuntime JS;
        public ChannelListItem SelectedItem;
        public ChannelListContextMenuComponent Component;
        public Func<Task> OpenEvent;
        
        public ChannelListContextMenu(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open(MouseEventArgs e, ChannelListItem item)
        {
            Component.SetPosition(e.ClientX, e.ClientY);
            Component.SetVisibility(true);
            SelectedItem = item;

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }
    }
}

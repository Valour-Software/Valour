using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Planets;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

namespace Valour.Client.ContextMenus
{
    /// <summary>
    /// Assists in controlling the user context menu
    /// </summary>
    public class MemberContextMenu
    {
        public readonly IJSRuntime JS;
        public ClientPlanetMember SelectedMember;
        public Func<Task> OpenEvent;

        public MemberContextMenu(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open(MouseEventArgs e, ClientPlanetMember member){
            await JS.InvokeVoidAsync("OpenMemberContextMenu", e.ClientX, e.ClientY);
            SelectedMember = member;

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }
    }
}

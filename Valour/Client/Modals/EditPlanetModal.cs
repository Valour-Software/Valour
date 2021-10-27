using Microsoft.JSInterop;
using Valour.Client.Shared.Modals;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

namespace Valour.Client.Modals
{
    /// <summary>
    /// Assists in controlling the edit planet modal
    /// </summary>
    public class EditPlanetModal
    {
        public readonly IJSRuntime JS;
        public EditPlanetModalComponent Component;
        public Func<Task> OpenEvent;

        public EditPlanetModal(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open()
        {
            Component.SetVisibility(true);

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }
    }
}

using Microsoft.JSInterop;
using Valour.Api.Planets;
using Valour.Client.Shared.Modals;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

namespace Valour.Client.Modals
{
    /// <summary>
    /// Assists in controlling the category creation modal
    /// </summary>
    public class CreateCategoryModal
    {
        public readonly IJSRuntime JS;
        public CreateCategoryModalComponent Component;
        public Func<Task> OpenEvent;
        public Planet Planet;

        public CreateCategoryModal(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open(Planet planet)
        {
            this.Planet = planet;

            Component.SetVisibility(true);

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }
    }
}

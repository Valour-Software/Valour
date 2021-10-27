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
    /// Assists in controlling the user confirmation modal
    /// </summary>
    public class InfoModal
    {
        public readonly IJSRuntime JS;
        public InfoModalComponent Component;
        public Func<Task> OpenEvent;

        /// <summary>
        /// Run if the user hits the button
        /// </summary>
        public Func<Task> ButtonEvent;


        // Cosmetics
        public string title_text;
        public string desc_text;
        public string button_text;

        public InfoModal(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open(string title, string desc, string button, Func<Task> OnClick)
        {
            title_text = title;
            desc_text = desc;
            button_text = button;

            ButtonEvent = OnClick;

            Component.SetVisibility(true);

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }

        public async Task OnClick()
        {
            Component.SetVisibility(false);
            await ButtonEvent();
        }
    }
}

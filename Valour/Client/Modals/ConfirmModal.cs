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
    public class ConfirmModal
    {
        public readonly IJSRuntime JS;
        public ConfirmModalComponent Component;
        public Func<Task> OpenEvent;

        /// <summary>
        /// Run if the user hits "confirm"
        /// </summary>
        public Func<Task> ConfirmEvent;

        /// <summary>
        /// Run if the user hits "cancel"
        /// </summary>
        public Func<Task> CancelEvent;


        // Cosmetics
        public string title_text;
        public string desc_text;
        public string confirm_text;
        public string cancel_text;

        public ConfirmModal(IJSRuntime js)
        {
            JS = js;
        }

        public async Task Open(string title, string desc, string confirm, string cancel, Func<Task> OnConfirm, Func<Task> OnCancel)
        {
            title_text = title;
            desc_text = desc;
            confirm_text = confirm;
            cancel_text = cancel;

            ConfirmEvent = OnConfirm;
            CancelEvent = OnCancel;

            Component.SetVisibility(true);

            if (OpenEvent != null)
            {
                await OpenEvent.Invoke();
            }
        }

        public async Task OnConfirm()
        {
            Component.SetVisibility(false);
            await ConfirmEvent();
        }

        public async Task OnCancel()
        {
            Component.SetVisibility(false);
            await CancelEvent();
        }
    }
}

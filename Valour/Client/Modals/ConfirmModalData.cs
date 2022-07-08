namespace Valour.Client.Modals
{
    /*  Valour - A free and secure chat client
    *  Copyright (C) 2021 Vooper Media LLC
    *  This program is subject to the GNU Affero General Public license
    *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
    */

    public class ConfirmModalData
    {
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

        public ConfirmModalData(string title, string desc, string confirm, string cancel, Func<Task> OnConfirm, Func<Task> OnCancel)
        {
            title_text = title;
            desc_text = desc;
            confirm_text = confirm;
            cancel_text = cancel;

            ConfirmEvent = OnConfirm;
            CancelEvent = OnCancel;
        }
    }
}

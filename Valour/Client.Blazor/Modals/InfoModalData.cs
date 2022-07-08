namespace Valour.Client.Blazor.Modals
{
    /*  Valour - A free and secure chat client
    *  Copyright (C) 2021 Vooper Media LLC
    *  This program is subject to the GNU Affero General Public license
    *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
    */

    public class InfoModalData
    {

        /// <summary>
        /// Run if the user hits the button
        /// </summary>
        public Func<Task> ButtonEvent;


        // Cosmetics
        public string title_text;
        public string desc_text;
        public string button_text;

        public InfoModalData(string title, string desc, string button, Func<Task> OnClick)
        {
            this.title_text = title;
            this.desc_text = desc;
            this.button_text = button;

            this.ButtonEvent = OnClick;
        }
    }
}

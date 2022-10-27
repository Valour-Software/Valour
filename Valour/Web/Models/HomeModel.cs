namespace Valour.Web.Models
{
    public class HomeModel
    {
        public string UserText { get; set; }
        public string PlanetText { get; set; }

        public HomeModel(string userText, string planetText)
        {
            this.UserText = userText;
            this.PlanetText = planetText;
        }
    }
}

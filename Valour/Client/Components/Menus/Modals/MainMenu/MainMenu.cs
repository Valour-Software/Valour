using Microsoft.AspNetCore.Components;

namespace Valour.Client.Components.Menus.Modals.MainMenu;

public class MainMenu
{
    public class MenuCategory
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public List<MenuItem> Items { get; set; } = new ();
    }

    public class MenuItem
    {
        public string Name { get; set; }
        public string Icon { get; set; }
        public string Color { get; set; }
        public string Description { get; set; }
        public RenderFragment Content { get; set; }
    }
}
namespace Rroms.Web.Models.ViewModels;

/// <summary>
/// Data for one tile on a role dashboard (Admin, Staff or Customer home page).
/// A view model carries only what a view needs to display.
///  It keeps the Razor markup free of logic and lets the same partial view render every tile.
/// </summary>
public class ModuleTileViewModel
{
    //Creates a tile.
    public ModuleTileViewModel(string title, string description, string iconName, string sprintLabel, string? url = null)
    {
        Title = title;
        Description = description;
        IconName = iconName;
        SprintLabel = sprintLabel;
        Url = url;
    }

    //Heading shown on the tile.
    public string Title { get; }

    //One sentence explaining what the module does.
    public string Description { get; }

    //Icon name understood by the _Icon partial view.
    public string IconName { get; }

    //Sprint and owner, e.g. "Sprint 2 · WL".
    public string SprintLabel { get; }

    //Link to the finished module. Null means "not built yet".
    public string? Url { get; }

    //True once the module exists and the tile can link to it.
    public bool IsReady
    {
        get { return Url != null; }
    }
}

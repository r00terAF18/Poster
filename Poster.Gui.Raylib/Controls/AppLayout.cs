namespace Poster.Gui.Controls;

/// <summary>Shared responsive sizing rules for the application shell.</summary>
public static class AppLayout
{
    public static int GetSidebarWidth(int screenWidth, bool compactNavigation) =>
        screenWidth < 1080 || compactNavigation ? 72 : 238;
}

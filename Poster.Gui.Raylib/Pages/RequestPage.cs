namespace Poster.Gui.Pages;

/// <summary>Calculates the request editor's two-column or stacked layout.</summary>
public readonly record struct RequestPageLayout(
    float ContentX,
    float EditorWidth,
    float ResponseX,
    float ResponseWidth,
    bool IsStacked)
{
    public static RequestPageLayout Create(int screenWidth, int sidebarWidth)
    {
        float contentX = sidebarWidth + 32f;
        bool isStacked = screenWidth < 1250;
        float responseX = isStacked ? contentX : Math.Max(contentX + 570, screenWidth - 438);
        float editorWidth = isStacked ? screenWidth - contentX - 20 : responseX - contentX - 20;
        float responseWidth = isStacked ? editorWidth : screenWidth - responseX - 20;
        return new RequestPageLayout(contentX, editorWidth, responseX, responseWidth, isStacked);
    }
}

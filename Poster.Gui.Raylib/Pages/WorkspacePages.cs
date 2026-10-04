using Poster.Core;
using Raylib_cs;

namespace Poster.Gui;

internal sealed partial class PosterApp
{
    private void DrawHistoryPage()
    {
        float x = SidebarWidth + 34f;
        float y = 100f;
        float width = Raylib.GetScreenWidth() - x - 32;
        DrawText("Saved requests stay with this workspace. Select one to edit and run it again.", (int)x, (int)y, 15, Muted);
        y += 42;
        if (_workspace.Requests.Count == 0)
        {
            DrawText("No saved requests yet.", (int)x, (int)y, 16, Muted);
            return;
        }

        for (int index = _workspace.Requests.Count - 1; index >= 0 && y < Raylib.GetScreenHeight() - 70; index--)
        {
            PosterReqRes request = _workspace.Requests[index];
            Panel(new Rectangle(x, y, width, 58));
            DrawText(request.HttpMethod.Method, (int)x + 14, (int)y + 20, 14, Accent);
            DrawText(Shorten(request.Name, 26), (int)x + 102, (int)y + 20, 15, Text);
            DrawText(Shorten(request.Route, 44), (int)x + 300, (int)y + 21, 13, Muted);
            if (Button("Open", new Rectangle(x + width - 164, y + 10, 70, 38))) LoadRequest(request);
            if (Button("Delete", new Rectangle(x + width - 84, y + 10, 70, 38)))
            {
                _workspace.Requests.RemoveAt(index);
                SetNotice("Request removed from workspace");
                return;
            }
            y += 68;
        }
    }

    private void DrawVariablesPage()
    {
        float x = SidebarWidth + 34f;
        float width = Raylib.GetScreenWidth() - x - 32;
        DrawTextField("VARIABLE NAME", _variableName, new Rectangle(x, 104, width * 0.32f, 60));
        DrawTextField("VALUE", _variableValue, new Rectangle(x + width * 0.34f, 104, width * 0.48f, 60));
        if (Button("Save variable", new Rectangle(x + width - 142, 124, 142, 40), true)) SaveVariable();
        DrawText("Use variables in URLs, headers, query values, and bodies as {{name}}.", (int)x, 188, 14, Muted);
        float y = 230f;
        foreach ((string key, string value) in _workspace.Variables.ToArray())
        {
            Panel(new Rectangle(x, y, width, 52));
            DrawText($"{{{{{key}}}}}", (int)x + 14, (int)y + 17, 15, Accent);
            DrawText(Shorten(value, 58), (int)x + 210, (int)y + 18, 14, Text);
            if (Button("Edit", new Rectangle(x + width - 166, y + 7, 68, 38))) { _variableName.Set(key); _variableValue.Set(value); }
            if (Button("Remove", new Rectangle(x + width - 88, y + 7, 76, 38)))
            {
                _workspace.Variables.Remove(key);
                SetNotice("Variable removed");
                return;
            }
            y += 62;
        }
    }

    private void DrawWorkspacesPage()
    {
        float x = SidebarWidth + 34f;
        float y = 106f;
        float width = Raylib.GetScreenWidth() - x - 32;
        string[] files = _workspace.ListLocal();
        DrawText("Choose a saved workspace file.", (int)x, (int)y, 14, Muted);
        y += 42;
        if (files.Length == 0)
        {
            DrawText("No workspace files found in the application or project folders.", (int)x, (int)y, 15, Muted);
            return;
        }

        foreach (string file in files)
        {
            Panel(new Rectangle(x, y, width, 56));
            DrawText(Shorten(Path.GetFileName(file), 60), (int)x + 14, (int)y + 19, 14, Text);
            if (Button("Load", new Rectangle(x + width - 92, y + 9, 78, 38)))
            {
                try
                {
                    _workspace.Load(file);
                    _workspaceName.Set(_workspace.Name);
                    CreateRequest();
                    _page = AppPage.Request;
                    _pageHistory.Clear();
                    SetNotice($"Loaded {_workspace.Name}");
                }
                catch (Exception exception) { SetNotice($"Load failed: {exception.Message}"); }
                return;
            }
            y += 66;
        }
    }

    private void DrawOptionsPage()
    {
        float x = SidebarWidth + 34f;
        float width = Raylib.GetScreenWidth() - x - 32;
        DrawText("Appearance", (int)x, 106, 18, Text);
        DrawText("Navigation automatically collapses on narrow windows.", (int)x, 136, 14, Muted);
        if (Button(_compactNavigation ? "Use full navigation" : "Use compact navigation", new Rectangle(x, 164, 220, 40), _compactNavigation)) _compactNavigation = !_compactNavigation;
        if (Button(_largeText ? "Use standard text" : "Use larger text", new Rectangle(x + 232, 164, 190, 40), _largeText)) _largeText = !_largeText;
        Raylib.DrawLine((int)x, 228, (int)(x + width), 228, Border);
        DrawText("Workspace", (int)x, 252, 18, Text);
        DrawTextField("WORKSPACE NAME", _workspaceName, new Rectangle(x, 280, Math.Min(420, width), 60));
        if (Button("Apply workspace name", new Rectangle(x, 354, 190, 40), true))
        {
            string name = _workspaceName.Value.Trim();
            if (name.Length == 0) SetNotice("Workspace name is required");
            else { _workspace.Name = name; SetNotice("Workspace name updated"); }
        }
        DrawText("Saved workspaces", (int)x, 426, 18, Text);
        DrawText("Open, save, and manage workspace files from the Workspaces page.", (int)x, 456, 14, Muted);
        if (Button("Open workspaces", new Rectangle(x, 484, 178, 40))) Navigate(AppPage.Workspaces);
    }
}

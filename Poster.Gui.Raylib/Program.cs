using System.Numerics;
using Poster.Core;
using Raylib_cs;

namespace Poster.Gui;

internal static class Program
{
    private static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1440, 900, "Poster | API workspace");
        Raylib.SetWindowMinSize(1120, 740);
        Raylib.SetTargetFPS(60);

        using PosterApp app = new PosterApp();
        while (!Raylib.WindowShouldClose())
        {
            app.Update();
            Raylib.BeginDrawing();
            app.Draw();
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }
}

internal sealed class PosterApp : IDisposable
{
    private static readonly HttpMethod[] Methods =
    [
        HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch,
        HttpMethod.Delete, HttpMethod.Head, HttpMethod.Options,
    ];

    private static readonly AuthType[] AuthTypes =
        [AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey];

    private static readonly Color Background = new(13, 17, 27, 255);
    private static readonly Color Surface = new(22, 28, 42, 255);
    private static readonly Color SurfaceRaised = new(30, 38, 55, 255);
    private static readonly Color Border = new(47, 57, 77, 255);
    private static readonly Color Text = new(230, 235, 245, 255);
    private static readonly Color Muted = new(139, 151, 174, 255);
    private static readonly Color Accent = new(91, 156, 255, 255);
    private static readonly Color Green = new(78, 201, 154, 255);
    private static readonly Color Red = new(243, 105, 113, 255);

    private readonly Workspace _workspace = new();
    private readonly TextBuffer _name = new("Untitled request");
    private readonly TextBuffer _route = new("https://httpbin.org/get");
    private readonly TextBuffer _query = new();
    private readonly TextBuffer _headers = new();
    private readonly TextBuffer _body = new();
    private readonly TextBuffer _authToken = new();
    private readonly TextBuffer _username = new();
    private readonly TextBuffer _password = new();
    private readonly TextBuffer _apiKeyHeader = new("X-API-Key");
    private readonly TextBuffer _variableName = new();
    private readonly TextBuffer _variableValue = new();
    private readonly Stack<AppPage> _pageHistory = new();

    private PosterReqRes? _request;
    private TextBuffer? _focused;
    private CancellationTokenSource? _requestCancellation;
    private Task? _requestTask;
    private AppPage _page = AppPage.Request;
    private AuthType _authType;
    private string _notice = "Ready";
    private double _noticeUntil;
    private int _methodIndex;
    private bool _disposed;

    public PosterApp()
    {
        _workspace.Name = "My Workspace";
        CreateRequest();
    }

    public void Update()
    {
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            _focused = null;

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_focused != null) _focused = null;
            else GoBack();
        }

        if (Raylib.IsKeyDown(KeyboardKey.LeftControl) && Raylib.IsKeyPressed(KeyboardKey.S))
            SaveWorkspace();

        if (Raylib.IsKeyDown(KeyboardKey.LeftControl) && Raylib.IsKeyPressed(KeyboardKey.Enter))
            BeginRequest();

        UpdateTextInput();
    }

    public void Draw()
    {
        Raylib.ClearBackground(Background);
        DrawSidebar();
        DrawTopBar();

        switch (_page)
        {
            case AppPage.Request:
                DrawRequestPage();
                break;
            case AppPage.History:
                DrawHistoryPage();
                break;
            case AppPage.Variables:
                DrawVariablesPage();
                break;
            case AppPage.Workspaces:
                DrawWorkspacesPage();
                break;
        }

        if (Environment.TickCount64 / 1000.0 < _noticeUntil)
            DrawNotice();
    }

    private void DrawSidebar()
    {
        int width = 238;
        Raylib.DrawRectangle(0, 0, width, Raylib.GetScreenHeight(), Surface);
        Raylib.DrawRectangle(width - 1, 0, 1, Raylib.GetScreenHeight(), Border);
        Raylib.DrawCircle(30, 34, 13, Accent);
        Raylib.DrawText("P", 24, 24, 20, Background);
        Raylib.DrawText("POSTER", 54, 24, 20, Text);
        Raylib.DrawText("API WORKSPACE", 54, 46, 10, Muted);

        if (Button("+  New request", new Rectangle(16, 82, 206, 42), true))
        {
            CreateRequest();
            _page = AppPage.Request;
            _pageHistory.Clear();
        }

        DrawSidebarNav("Request", AppPage.Request, 150);
        DrawSidebarNav("History", AppPage.History, 194);
        DrawSidebarNav("Variables", AppPage.Variables, 238);

        Raylib.DrawText("WORKSPACE REQUESTS", 18, 300, 11, Muted);
        float y = 327f;
        for (int index = _workspace.Requests.Count - 1; index >= 0 && y < Raylib.GetScreenHeight() - 54; index--)
        {
            PosterReqRes request = _workspace.Requests[index];
            if (Button($"{request.HttpMethod.Method}  {Shorten(request.Name, 17)}",
                    new Rectangle(12, y, 214, 38), _request == request))
                LoadRequest(request);
            y += 43;
        }

        Raylib.DrawRectangle(0, Raylib.GetScreenHeight() - 48, width, 48, SurfaceRaised);
        Raylib.DrawText(Shorten(_workspace.Name, 24), 17, Raylib.GetScreenHeight() - 32, 14, Muted);
    }

    private void DrawSidebarNav(string label, AppPage page, float y)
    {
        if (Button(label, new Rectangle(12, y, 214, 36), _page == page))
            Navigate(page);
    }

    private void DrawTopBar()
    {
        int left = 260;
        Raylib.DrawRectangle(left, 0, Raylib.GetScreenWidth() - left, 72, Background);
        Raylib.DrawLine(left, 71, Raylib.GetScreenWidth(), 71, Border);

        string title = _page switch
        {
            AppPage.History => "Request history",
            AppPage.Variables => "Workspace variables",
            AppPage.Workspaces => "Open workspace",
            _ => "Request editor",
        };
        Raylib.DrawText(title, 278, 24, 21, Text);

        if (_page == AppPage.Request && _pageHistory.Count == 0 && Button("Open", new Rectangle(Raylib.GetScreenWidth() - 310, 18, 94, 38)))
            Navigate(AppPage.Workspaces);

        if (_page == AppPage.Request && _pageHistory.Count > 0 && Button("< Back", new Rectangle(Raylib.GetScreenWidth() - 310, 18, 94, 38)))
            GoBack();

        if (_sending && Button("Cancel", new Rectangle(Raylib.GetScreenWidth() - 420, 18, 92, 38)))
        {
            _requestCancellation?.Cancel();
            SetNotice("Cancelling request...");
        }

        if (_page != AppPage.Request && Button("← Back", new Rectangle(Raylib.GetScreenWidth() - 310, 18, 94, 38)))
            GoBack();

        if (Button("Save workspace", new Rectangle(Raylib.GetScreenWidth() - 204, 18, 184, 38)))
            SaveWorkspace();
    }

    private void DrawRequestPage()
    {
        float contentX = 270f;
        int width = Raylib.GetScreenWidth();
        int height = Raylib.GetScreenHeight();
        float responseX = Math.Max(contentX + 570, width - 438);
        float editorWidth = responseX - contentX - 20;
        float responseWidth = width - responseX - 20;
        float top = 90f;

        DrawTextField("REQUEST NAME", _name, new Rectangle(contentX, top, editorWidth, 58));
        top += 76;

        if (Button(Methods[_methodIndex].Method, new Rectangle(contentX, top + 20, 108, 44), true))
            _methodIndex = (_methodIndex + 1) % Methods.Length;
        DrawTextField("URL", _route, new Rectangle(contentX + 120, top, editorWidth - 120, 64));
        top += 84;

        DrawTextArea("QUERY PARAMETERS  ·  key=value per line", _query,
            new Rectangle(contentX, top, editorWidth, 106));
        top += 126;

        DrawTextArea("HEADERS  ·  name=value per line", _headers,
            new Rectangle(contentX, top, editorWidth, 100));
        top += 120;

        DrawAuthEditor(contentX, top, editorWidth);
        top += _authType == AuthType.None ? 56 : 120;

        float bodyHeight = Math.Max(100, height - top - 28);
        DrawTextArea("BODY", _body, new Rectangle(contentX, top, editorWidth, bodyHeight));

        DrawResponsePanel(responseX, 90, responseWidth, height - 110);
    }

    private void DrawAuthEditor(float x, float y, float width)
    {
        Raylib.DrawText("AUTHENTICATION", (int)x, (int)y, 11, Muted);
        string label = _authType switch
        {
            AuthType.None => "None",
            AuthType.Bearer => "Bearer",
            AuthType.Basic => "Basic",
            AuthType.ApiKey => "API key",
            _ => "None",
        };
        if (Button($"{label}  ▾", new Rectangle(x, y + 18, 150, 40)))
            _authType = AuthTypes[(Array.IndexOf(AuthTypes, _authType) + 1) % AuthTypes.Length];

        if (_authType == AuthType.Bearer)
            DrawTextField("TOKEN", _authToken, new Rectangle(x + 164, y, width - 164, 58));
        else if (_authType == AuthType.Basic)
        {
            DrawTextField("USERNAME", _username, new Rectangle(x + 164, y, (width - 176) / 2, 58));
            DrawTextField("PASSWORD", _password, new Rectangle(x + 176 + (width - 176) / 2, y, (width - 176) / 2, 58), true);
        }
        else if (_authType == AuthType.ApiKey)
        {
            DrawTextField("HEADER", _apiKeyHeader, new Rectangle(x + 164, y, (width - 176) / 2, 58));
            DrawTextField("KEY", _authToken, new Rectangle(x + 176 + (width - 176) / 2, y, (width - 176) / 2, 58), true);
        }
    }

    private void DrawResponsePanel(float x, float y, float width, float height)
    {
        Panel(new Rectangle(x, y, width, height));
        Raylib.DrawText("RESPONSE", (int)x + 18, (int)y + 16, 12, Muted);

        if (_sending)
        {
            if (Button("Cancel request", new Rectangle(x + width - 154, y + 10, 136, 36)))
            {
                _requestCancellation?.Cancel();
                SetNotice("Cancelling request…");
            }
        }
        else if (Button("Send request", new Rectangle(x + width - 150, y + 10, 132, 36), true))
            BeginRequest();

        HttpResponseMessage? response = _request?.ResponseMessage;
        string status = response == null ? "Waiting for a response" : $"{(int)response.StatusCode}  {response.ReasonPhrase}";
        Color statusColor = response == null ? Muted : response.IsSuccessStatusCode ? Green : Red;
        Raylib.DrawText(status, (int)x + 18, (int)y + 66, 18, statusColor);

        if (_request != null && _request.LastExec != default)
        {
            string metrics = $"{_request.Metric.ElapsedTime} ms   ↓ {FormatBytes(_request.Metric.ResponseSize)}";
            Raylib.DrawText(metrics, (int)x + 18, (int)y + 94, 13, Muted);
        }

        Raylib.DrawLine((int)x + 16, (int)y + 122, (int)(x + width - 16), (int)y + 122, Border);
        int     bodyY      = (int)y + 138;
        int     bodyHeight = (int)height - 154;
        string? error      = _request?.Error;
        string     body       = error ?? _request?.ResponseBody ?? "Response body will appear here.";
        DrawClippedText(body, (int)x + 18, bodyY, (int)width - 36, bodyHeight, error != null ? Red : Text);
    }

    private void DrawHistoryPage()
    {
        float x = 272f;
        float y = 100f;
        float width = Raylib.GetScreenWidth() - x - 32;
        Raylib.DrawText("Saved requests stay with this workspace. Select one to edit and run it again.",
            (int)x, (int)y, 15, Muted);
        y += 42;

        if (_workspace.Requests.Count == 0)
        {
            Raylib.DrawText("No saved requests yet.", (int)x, (int)y, 16, Muted);
            return;
        }

        for (int index = _workspace.Requests.Count - 1; index >= 0 && y < Raylib.GetScreenHeight() - 70; index--)
        {
            PosterReqRes request = _workspace.Requests[index];
            Panel(new Rectangle(x, y, width, 58));
            Raylib.DrawText(request.HttpMethod.Method, (int)x + 14, (int)y + 20, 14, Accent);
            Raylib.DrawText(Shorten(request.Name, 26), (int)x + 102, (int)y + 20, 15, Text);
            Raylib.DrawText(Shorten(request.Route, 44), (int)x + 300, (int)y + 21, 13, Muted);

            if (Button("Open", new Rectangle(x + width - 164, y + 10, 70, 38)))
                LoadRequest(request);
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
        float x = 272f;
        float width = Raylib.GetScreenWidth() - x - 32;
        DrawTextField("VARIABLE NAME", _variableName, new Rectangle(x, 104, width * 0.32f, 60));
        DrawTextField("VALUE", _variableValue, new Rectangle(x + width * 0.34f, 104, width * 0.48f, 60));
        if (Button("Save variable", new Rectangle(x + width - 142, 124, 142, 40), true))
            SaveVariable();

        Raylib.DrawText("Use variables in URLs, headers, query values, and bodies as {{name}}.",
            (int)x, 188, 14, Muted);
        float y = 230f;
        foreach ((string key, string value) in _workspace.Variables.ToArray())
        {
            Panel(new Rectangle(x, y, width, 52));
            Raylib.DrawText($"{{{{{key}}}}}", (int)x + 14, (int)y + 17, 15, Accent);
            Raylib.DrawText(Shorten(value, 58), (int)x + 210, (int)y + 18, 14, Text);
            if (Button("Edit", new Rectangle(x + width - 166, y + 7, 68, 38)))
            {
                _variableName.Set(key);
                _variableValue.Set(value);
            }
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
        float    x     = 272f;
        float    y     = 106f;
        float    width = Raylib.GetScreenWidth() - x - 32;
        string[] files = _workspace.ListLocal();
        Raylib.DrawText("Choose a saved workspace file from the current directory.", (int)x, (int)y, 14, Muted);
        y += 42;

        if (files.Length == 0)
        {
            Raylib.DrawText("No workspace files found.", (int)x, (int)y, 15, Muted);
            return;
        }

        foreach (string file in files)
        {
            Panel(new Rectangle(x, y, width, 56));
            Raylib.DrawText(Shorten(Path.GetFileName(file), 60), (int)x + 14, (int)y + 19, 14, Text);
            if (Button("Load", new Rectangle(x + width - 92, y + 9, 78, 38)))
            {
                try
                {
                    _workspace.Load(file);
                    CreateRequest();
                    _page = AppPage.Request;
                    _pageHistory.Clear();
                    SetNotice($"Loaded {_workspace.Name}");
                }
                catch (Exception exception)
                {
                    SetNotice($"Load failed: {exception.Message}");
                }
                return;
            }
            y += 66;
        }
    }

    private void DrawSidebarNav(string label, AppPage page, int y)
    {
        if (Button(label, new Rectangle(12, y, 214, 36), _page == page))
            Navigate(page);
    }

    private void DrawTextField(string label, TextBuffer buffer, Rectangle bounds, bool secret = false)
    {
        Raylib.DrawText(label, (int)bounds.X, (int)bounds.Y, 10, Muted);
        Rectangle box = new Rectangle(bounds.X, bounds.Y + 19, bounds.Width, bounds.Height - 19);
        DrawInputBox(buffer, box, false, secret);
    }

    private void DrawTextArea(string label, TextBuffer buffer, Rectangle bounds)
    {
        Raylib.DrawText(label, (int)bounds.X, (int)bounds.Y, 10, Muted);
        Rectangle box = new Rectangle(bounds.X, bounds.Y + 19, bounds.Width, bounds.Height - 19);
        DrawInputBox(buffer, box, true, false);
    }

    private void DrawInputBox(TextBuffer buffer, Rectangle bounds, bool multiline, bool secret)
    {
        bool active = _focused == buffer;
        Raylib.DrawRectangleRec(bounds, active ? SurfaceRaised : Surface);
        Raylib.DrawRectangleLinesEx(bounds, 1, active ? Accent : Border);
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), bounds))
            _focused = buffer;

        string value = secret ? new string('•', buffer.Value.Length) : buffer.Value;
        int lineHeight = 18;
        int visibleLines = Math.Max(1, ((int)bounds.Height - 12) / lineHeight);
        Raylib.BeginScissorMode((int)bounds.X + 4, (int)bounds.Y + 4, (int)bounds.Width - 8, (int)bounds.Height - 8);
        int firstCharacter = 0;
        if (multiline)
        {
            int searchPosition = value.Length;
            for (int line = 0; line < visibleLines && searchPosition > 0; line++)
            {
                int newline = value.LastIndexOf('\n', searchPosition - 1);
                if (newline < 0)
                {
                    firstCharacter = 0;
                    break;
                }
                firstCharacter = newline + 1;
                searchPosition = newline;
            }
        }

        int position = firstCharacter;
        int drawnLines = 0;
        while (drawnLines < visibleLines && position <= value.Length)
        {
            int newline = multiline ? value.IndexOf('\n', position) : -1;
            int end = newline < 0 ? value.Length : newline;
            int length = end - position;
            if (length > 0 && value[position + length - 1] == '\r') length--;
            string lineText = value.Substring(position, length);
            Raylib.DrawText(lineText, (int)bounds.X + 10, (int)bounds.Y + 8 + drawnLines * lineHeight, 14, Text);
            drawnLines++;
            if (newline < 0) break;
            position = newline + 1;
        }
        if (active && buffer.Cursor >= firstCharacter && Raylib.GetTime() % 1 < 0.5)
        {
            int cursorSearchIndex = buffer.Cursor == 0 ? -1 : buffer.Cursor - 1;
            int cursorLineStart = cursorSearchIndex < 0 ? 0 : value.LastIndexOf('\n', cursorSearchIndex) + 1;
            string cursorPrefix = value.Substring(cursorLineStart, buffer.Cursor - cursorLineStart);
            int cursorLine = 0;
            for (int index = firstCharacter; multiline && index < buffer.Cursor; index++)
                if (value[index] == '\n') cursorLine++;
            int cursorX = (int)bounds.X + 10 + Raylib.MeasureText(cursorPrefix, 14);
            int cursorY = (int)bounds.Y + 8 + cursorLine * lineHeight;
            Raylib.DrawRectangle(cursorX, cursorY, 1, 16, Accent);
        }
        Raylib.EndScissorMode();
    }

    private void DrawClippedText(string value, int x, int y, int width, int height, Color color)
    {
        Raylib.BeginScissorMode(x, y, width, height);
        int lineHeight = 18;
        int maxLines = Math.Max(1, height / lineHeight);
        int maxCharacters = Math.Max(12, width / 8);
        int position = 0;
        for (int index = 0; index < maxLines && position <= value.Length; index++)
        {
            int end = value.IndexOf('\n', position);
            if (end < 0) end = value.Length;
            int length = end - position;
            if (length > 0 && value[position + length - 1] == '\r') length--;
            int visibleLength = Math.Min(length, maxCharacters);
            Raylib.DrawText(value.Substring(position, visibleLength), x, y + index * lineHeight, 13, color);
            if (end == value.Length) break;
            position = end + 1;
        }
        Raylib.EndScissorMode();
    }

    private bool Button(string label, Rectangle bounds, bool primary = false)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        CBool hovered = Raylib.CheckCollisionPointRec(mouse, bounds);
        Color fill = primary ? Accent : hovered ? SurfaceRaised : Surface;
        Raylib.DrawRectangleRec(bounds, fill);
        Raylib.DrawRectangleLinesEx(bounds, 1, primary ? Accent : Border);
        Raylib.DrawText(label, (int)bounds.X + 12, (int)bounds.Y + ((int)bounds.Height - 16) / 2, 14,
            primary ? Background : Text);
        return hovered && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    private static void Panel(Rectangle bounds)
    {
        Raylib.DrawRectangleRec(bounds, Surface);
        Raylib.DrawRectangleLinesEx(bounds, 1, Border);
    }

    private void UpdateTextInput()
    {
        if (_focused == null) return;
        int codepoint = Raylib.GetCharPressed();
        while (codepoint > 0)
        {
            if (!char.IsControl((char)codepoint) && _focused.Value.Length < 20000)
                _focused.Insert((char)codepoint);
            codepoint = Raylib.GetCharPressed();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace)) _focused.Backspace();
        if (Raylib.IsKeyPressed(KeyboardKey.Delete)) _focused.Delete();
        if (Raylib.IsKeyPressed(KeyboardKey.Left)) _focused.MoveLeft();
        if (Raylib.IsKeyPressed(KeyboardKey.Right)) _focused.MoveRight();
        if (Raylib.IsKeyPressed(KeyboardKey.Home)) _focused.MoveHome();
        if (Raylib.IsKeyPressed(KeyboardKey.End)) _focused.MoveEnd();
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            if (_focused == _query || _focused == _headers || _focused == _body)
                _focused.Insert('\n');
            else
                _focused = null;
        }
    }

    private void CreateRequest()
    {
        _request = new PosterReqRes(_workspace)
        {
            Name = "Untitled request",
            Route = "https://httpbin.org/get",
            HttpMethod = Methods[0],
        };
        _name.Set(_request.Name);
        _route.Set(_request.Route);
        _query.Set("");
        _headers.Set("");
        _body.Set("");
        _authToken.Set("");
        _username.Set("");
        _password.Set("");
        _apiKeyHeader.Set("X-API-Key");
        _authType = AuthType.None;
        _methodIndex = 0;
        _focused = null;
    }

    private void LoadRequest(PosterReqRes request)
    {
        if (_page != AppPage.Request)
            _pageHistory.Push(_page);
        _request = request;
        _request.SetParent(_workspace);
        _name.Set(request.Name);
        _route.Set(request.Route);
        _query.Set(FormatPairs(request.QueryParams));
        _headers.Set(FormatPairs(request.Headers));
        _body.Set(request.Body);
        _methodIndex = Array.FindIndex(Methods, method => method == request.HttpMethod);
        if (_methodIndex < 0) _methodIndex = 0;
        _authType = request.Auth.Type;
        _authToken.Set(request.Auth.Token);
        _username.Set(request.Auth.Username);
        _password.Set(request.Auth.Password);
        _apiKeyHeader.Set(request.Auth.ApiKeyHeader);
        _page = AppPage.Request;
        _focused = null;
    }

    private void ApplyEditor()
    {
        if (_request == null)
            CreateRequest();

        _request!.Name = string.IsNullOrWhiteSpace(_name.Value) ? "Untitled request" : _name.Value.Trim();
        _request.Route = _route.Value.Trim();
        _request.HttpMethod = Methods[_methodIndex];
        _request.QueryParams = ParsePairs(_query.Value);
        _request.Headers = ParsePairs(_headers.Value);
        _request.Body = _body.Value;
        _request.Auth.Type = _authType;
        _request.Auth.Token = _authToken.Value;
        _request.Auth.Username = _username.Value;
        _request.Auth.Password = _password.Value;
        _request.Auth.ApiKeyHeader = string.IsNullOrWhiteSpace(_apiKeyHeader.Value) ? "X-API-Key" : _apiKeyHeader.Value.Trim();
        _request.SetParent(_workspace);
    }

    private void BeginRequest()
    {
        if (_sending) return;
        ApplyEditor();
        if (string.IsNullOrWhiteSpace(_request!.Route))
        {
            SetNotice("Enter a request URL first");
            return;
        }

        if (!_workspace.Requests.Contains(_request))
            _workspace.Requests.Add(_request);

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        _sending = true;
        SetNotice("Sending request…");
        _requestTask = SendRequestAsync(_request, _requestCancellation.Token);
    }

    private volatile bool _sending;

    private async Task SendRequestAsync(PosterReqRes request, CancellationToken cancellationToken)
    {
        try
        {
            await request.SendAsync(cancellationToken);
            SetNotice(request.Error ?? "Response received");
        }
        catch (Exception exception)
        {
            request.Error = exception.Message;
            SetNotice("Request failed");
        }
        finally
        {
            _sending = false;
        }
    }

    private void SaveWorkspace()
    {
        if (_page == AppPage.Request)
        {
            ApplyEditor();
            if (_request != null && !_workspace.Requests.Contains(_request))
                _workspace.Requests.Add(_request);
        }
        try
        {
            _workspace.Save();
            SetNotice($"Saved {_workspace.Name}");
        }
        catch (Exception exception)
        {
            SetNotice($"Save failed: {exception.Message}");
        }
    }

    private void SaveVariable()
    {
        string key = _variableName.Value.Trim();
        if (key.Length == 0)
        {
            SetNotice("Variable name is required");
            return;
        }

        _workspace.Variables[key] = _variableValue.Value;
        _variableName.Set("");
        _variableValue.Set("");
        SetNotice($"Saved variable {key}");
    }

    private void Navigate(AppPage page)
    {
        if (_page == page) return;
        _pageHistory.Push(_page);
        _page = page;
        _focused = null;
    }

    private void GoBack()
    {
        if (_pageHistory.Count > 0)
            _page = _pageHistory.Pop();
        else
            _page = AppPage.Request;
        _focused = null;
    }

    private void DrawNotice()
    {
        int textWidth = Math.Min(Raylib.MeasureText(_notice, 14) + 36, Raylib.GetScreenWidth() - 32);
        Rectangle bounds = new Rectangle(Raylib.GetScreenWidth() - textWidth - 18, Raylib.GetScreenHeight() - 54, textWidth, 38);
        Raylib.DrawRectangleRec(bounds, SurfaceRaised);
        Raylib.DrawRectangleLinesEx(bounds, 1, Border);
        Raylib.DrawText(Shorten(_notice, Math.Max(16, (int)textWidth / 8)),
            (int)bounds.X + 14, (int)bounds.Y + 11, 14, Text);
    }

    private void SetNotice(string message)
    {
        _notice = message;
        _noticeUntil = Environment.TickCount64 / 1000.0 + 3.2;
    }

    private static Dictionary<string, string> ParsePairs(string text)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf('=');
            if (separator <= 0) continue;
            string key = line[..separator].Trim();
            if (key.Length > 0)
                result[key] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static string FormatPairs(Dictionary<string, string> pairs) =>
        string.Join('\n', pairs.Select(pair => $"{pair.Key}={pair.Value}"));

    private static string Shorten(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024):F1} MB",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requestCancellation?.Cancel();
        try { _requestTask?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _requestCancellation?.Dispose();
        _workspace.Client.Dispose();
        foreach (PosterReqRes request in _workspace.Requests)
        {
            request.ResponseMessage?.Dispose();
            request.RequestMessage?.Dispose();
        }
    }

    private enum AppPage
    {
        Request,
        History,
        Variables,
        Workspaces,
    }

    private sealed class TextBuffer(string value = "")
    {
        public string Value { get; private set; } = value;
        public int Cursor { get; private set; } = value.Length;

        public void Set(string value)
        {
            Value = value;
            Cursor = value.Length;
        }

        public void Insert(char value)
        {
            Value = Value.Insert(Cursor, value.ToString());
            Cursor++;
        }

        public void Backspace()
        {
            if (Cursor == 0) return;
            Value = Value.Remove(Cursor - 1, 1);
            Cursor--;
        }

        public void Delete()
        {
            if (Cursor < Value.Length)
                Value = Value.Remove(Cursor, 1);
        }

        public void MoveLeft() => Cursor = Math.Max(0, Cursor - 1);

        public void MoveRight() => Cursor = Math.Min(Value.Length, Cursor + 1);

        public void MoveHome()
        {
            int searchIndex = Cursor == 0 ? -1 : Cursor - 1;
            Cursor = searchIndex < 0 ? 0 : Value.LastIndexOf('\n', searchIndex) + 1;
        }

        public void MoveEnd()
        {
            int newline = Value.IndexOf('\n', Cursor);
            Cursor = newline < 0 ? Value.Length : newline;
        }
    }
}

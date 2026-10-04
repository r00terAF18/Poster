using Poster.Core;

namespace Poster.Desktop.ViewModels;

/// <summary>
/// Shared state for the running app: the single <see cref="Workspace"/> plus a few events so
/// the pages can stay in sync without knowing about each other.
/// </summary>
public sealed class WorkspaceSession
{
    public Workspace Workspace { get; } = new();

    /// <summary>Requests, variables or the name changed.</summary>
    public event Action? Changed;

    /// <summary>A different workspace file was loaded into <see cref="Workspace"/>.</summary>
    public event Action? Loaded;

    public event Action<string>? NoticeRequested;
    public event Action<PosterReqRes>? OpenRequested;

    public void RaiseChanged() => Changed?.Invoke();

    public void RaiseLoaded()
    {
        Loaded?.Invoke();
        Changed?.Invoke();
    }

    public void Notify(string message) => NoticeRequested?.Invoke(message);
    public void Open(PosterReqRes request) => OpenRequested?.Invoke(request);
}

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poster.Core;

namespace Poster.Desktop.ViewModels;

public sealed class WorkspaceFileItem
{
    public WorkspaceFileItem(string path, string? name, Action<WorkspaceFileItem> load)
    {
        Path = path;
        Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(path) : name;
        FileName = System.IO.Path.GetFileName(path);
        LoadCommand = new RelayCommand(() => load(this));
    }

    public string Path { get; }
    public string Name { get; }
    public string FileName { get; }
    public IRelayCommand LoadCommand { get; }
}

public partial class WorkspacesViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;

    public ObservableCollection<WorkspaceFileItem> Files { get; } = [];

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _importUrl = "";
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private bool _hasNoFiles = true;
    [ObservableProperty] private bool _isDarkMode;

    public WorkspacesViewModel(WorkspaceSession session)
    {
        _session = session;
        _name = session.Workspace.Name;
        _isDarkMode = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        session.Loaded += () => Name = session.Workspace.Name;
        RefreshFiles();
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = value ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    [RelayCommand]
    private void RefreshFiles()
    {
        Files.Clear();
        foreach (string path in _session.Workspace.ListLocal())
            Files.Add(new WorkspaceFileItem(path, Workspace.ReadName(path), LoadFile));
        HasNoFiles = Files.Count == 0;
    }

    [RelayCommand]
    private void ApplyName()
    {
        string name = Name.Trim();
        if (name.Length == 0)
        {
            _session.Notify("Workspace name is required");
            return;
        }

        _session.Workspace.Name = name;
        _session.Notify("Workspace renamed");
        _session.RaiseChanged();
    }

    [RelayCommand]
    private void SaveWorkspace()
    {
        ApplyName();
        try
        {
            _session.Workspace.Save();
            _session.Notify($"Saved {_session.Workspace.Name}");
            RefreshFiles();
        }
        catch (Exception exception)
        {
            _session.Notify($"Save failed: {exception.Message}");
        }
    }

    private void LoadFile(WorkspaceFileItem file)
    {
        try
        {
            _session.Workspace.Load(file.Path);
            _session.Notify($"Loaded {_session.Workspace.Name}");
            _session.RaiseLoaded();
        }
        catch (Exception exception)
        {
            _session.Notify($"Load failed: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        string url = ImportUrl.Trim();
        if (url.Length == 0)
        {
            _session.Notify("Enter the URL of an OpenAPI spec");
            return;
        }

        IsImporting = true;
        try
        {
            int before = _session.Workspace.Requests.Count;
            await _session.Workspace.ImportFromOpenApiUrlAsync(url);
            _session.Notify($"Imported {_session.Workspace.Requests.Count - before} request(s)");
            _session.RaiseChanged();
        }
        catch (Exception exception)
        {
            _session.Notify($"Import failed: {exception.Message}");
        }
        finally
        {
            IsImporting = false;
        }
    }
}

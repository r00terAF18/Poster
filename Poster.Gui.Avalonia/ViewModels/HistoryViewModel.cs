using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Poster.Core;

namespace Poster.Desktop.ViewModels;

public sealed class HistoryItem
{
    public HistoryItem(PosterReqRes request, Action<HistoryItem> open, Action<HistoryItem> delete)
    {
        Request = request;
        OpenCommand = new RelayCommand(() => open(this));
        DeleteCommand = new RelayCommand(() => delete(this));
    }

    public PosterReqRes Request { get; }
    public string Method => Request.HttpMethod.Method;
    public string Name => Request.Name;
    public string Route => Request.Route;
    public IRelayCommand OpenCommand { get; }
    public IRelayCommand DeleteCommand { get; }
}

public partial class HistoryViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;

    public ObservableCollection<HistoryItem> Items { get; } = [];

    [ObservableProperty] private bool _isEmpty = true;

    public HistoryViewModel(WorkspaceSession session)
    {
        _session = session;
        session.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Items.Clear();
        // Newest first
        foreach (PosterReqRes request in _session.Workspace.Requests.AsEnumerable().Reverse())
            Items.Add(new HistoryItem(request, Open, Delete));
        IsEmpty = Items.Count == 0;
    }

    private void Open(HistoryItem item) => _session.Open(item.Request);

    private void Delete(HistoryItem item)
    {
        _session.Workspace.Requests.Remove(item.Request);
        _session.Notify("Request removed");
        _session.RaiseChanged();
    }
}

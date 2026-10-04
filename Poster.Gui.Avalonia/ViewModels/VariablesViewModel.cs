using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Poster.Desktop.ViewModels;

public sealed class VariableItem
{
    public VariableItem(string key, string value, Action<VariableItem> edit, Action<VariableItem> remove)
    {
        Key = key;
        Value = value;
        EditCommand = new RelayCommand(() => edit(this));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Key { get; }
    public string Value { get; }
    public string Placeholder => "{{" + Key + "}}";
    public IRelayCommand EditCommand { get; }
    public IRelayCommand RemoveCommand { get; }
}

public partial class VariablesViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;

    public ObservableCollection<VariableItem> Items { get; } = [];

    [ObservableProperty] private string _newKey = "";
    [ObservableProperty] private string _newValue = "";
    [ObservableProperty] private bool _isEmpty = true;

    public VariablesViewModel(WorkspaceSession session)
    {
        _session = session;
        session.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Items.Clear();
        foreach ((string key, string value) in _session.Workspace.Variables)
            Items.Add(new VariableItem(key, value, Edit, Remove));
        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    private void SaveVariable()
    {
        string key = NewKey.Trim();
        if (key.Length == 0)
        {
            _session.Notify("Variable name is required");
            return;
        }

        _session.Workspace.Variables[key] = NewValue;
        NewKey = "";
        NewValue = "";
        _session.Notify($"Saved variable {key}");
        _session.RaiseChanged();
    }

    private void Edit(VariableItem item)
    {
        NewKey = item.Key;
        NewValue = item.Value;
    }

    private void Remove(VariableItem item)
    {
        _session.Workspace.Variables.Remove(item.Key);
        _session.Notify("Variable removed");
        _session.RaiseChanged();
    }
}

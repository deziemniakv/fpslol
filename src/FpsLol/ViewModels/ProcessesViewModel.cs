using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.SystemIntegration;
using FpsLol.Utilities;

namespace FpsLol.ViewModels;

public sealed partial class ProcessRow : ObservableObject
{
    public ProcessRow(ProcessInfo info) => Update(info);

    public int Pid { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ProcessProtection Protection { get; private set; }
    public ProcessInfo Info { get; private set; } = null!;

    [ObservableProperty] private double _cpu;
    [ObservableProperty] private string _cpuText = string.Empty;
    [ObservableProperty] private long _memory;
    [ObservableProperty] private string _memoryText = string.Empty;
    [ObservableProperty] private string? _path;

    public string Tag => Info.ProtectionLabel;
    public bool HasTag => Protection != ProcessProtection.None;
    public string TagTone => Protection == ProcessProtection.Protected ? "danger" : "info";
    public bool CanEnd => Info.CanEnd;

    public void Update(ProcessInfo info)
    {
        Info = info;
        Pid = info.Pid;
        Name = info.Name;
        Protection = info.Protection;
        Cpu = info.CpuPercent ?? -1;
        CpuText = info.CpuPercent is { } c
            ? (c < 0.05 ? "0%" : c.ToString(c < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + "%")
            : Format.NotAvailable;
        Memory = info.MemoryBytes;
        MemoryText = Format.Bytes(info.MemoryBytes);
    }
}

public sealed partial class ProcessesViewModel : PageViewModel
{
    private readonly IProcessService _processes;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, ProcessRow> _rows = [];
    private bool _refreshing;

    public ProcessesViewModel(IProcessService processes, IDialogService dialogs, IToastService toasts)
    {
        _processes = processes;
        _dialogs = dialogs;
        _toasts = toasts;

        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(Rows);
        view.Filter = o => o is ProcessRow r && (string.IsNullOrWhiteSpace(Search)
                                                 || r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
                                                 || r.Pid.ToString().StartsWith(Search, StringComparison.Ordinal));
        view.IsLiveSorting = true;
        view.IsLiveFiltering = false;
        RowsView = view;
        ApplySort();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public override string Title => "Processes";
    public override string Subtitle => "Running processes — Windows-critical processes are protected";

    public ObservableCollection<ProcessRow> Rows { get; } = [];
    public ListCollectionView RowsView { get; }

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _sortColumn = "Cpu";
    [ObservableProperty] private bool _sortDescending = true;
    [ObservableProperty] private bool _autoRefresh = true;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private ProcessRow? _selected;

    partial void OnSearchChanged(string value) => RowsView.Refresh();

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value) _timer.Start();
        else _timer.Stop();
    }

    public override async Task OnNavigatedToAsync()
    {
        await RefreshAsync();
        if (AutoRefresh) _timer.Start();
    }

    public override void OnNavigatedFrom() => _timer.Stop();

    [RelayCommand]
    private void Sort(string column)
    {
        if (SortColumn == column) SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = column is "Cpu" or "Memory";
        }
        ApplySort();
    }

    private void ApplySort()
    {
        using (RowsView.DeferRefresh())
        {
            RowsView.SortDescriptions.Clear();
            RowsView.LiveSortingProperties.Clear();
            var direction = SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            RowsView.SortDescriptions.Add(new SortDescription(SortColumn, direction));
            RowsView.LiveSortingProperties.Add(SortColumn);
            if (SortColumn != "Name") RowsView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await Task.Run(_processes.Snapshot);
            var seen = new HashSet<int>();
            foreach (var info in snapshot)
            {
                seen.Add(info.Pid);
                if (_rows.TryGetValue(info.Pid, out var row) && row.Name == info.Name)
                {
                    row.Update(info);
                }
                else
                {
                    if (row is not null) Rows.Remove(row);
                    row = new ProcessRow(info);
                    _rows[info.Pid] = row;
                    Rows.Add(row);
                }
            }
            foreach (var pid in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                Rows.Remove(_rows[pid]);
                _rows.Remove(pid);
            }

            var totalCpu = snapshot.Where(p => p.Pid != 0).Sum(p => p.CpuPercent ?? 0);
            var totalMem = snapshot.Sum(p => p.MemoryBytes);
            Summary = $"{snapshot.Count} processes · {Math.Min(totalCpu, 100):F0}% CPU · {Format.Bytes(totalMem)} memory";
            IsLoaded = true;
        }
        finally
        {
            _refreshing = false;
        }
    }

    partial void OnSelectedChanged(ProcessRow? value)
    {
        if (value is not null && value.Path is null) value.Path = _processes.GetPath(value.Pid) ?? "Path not accessible";
    }

    [RelayCommand]
    private async Task EndProcessAsync(ProcessRow? row)
    {
        if (row is null) return;
        if (!row.CanEnd)
        {
            await _dialogs.ShowInfoAsync($"{row.Name} is protected",
                row.Protection == ProcessProtection.Protected
                    ? "This is a critical Windows process. Ending it would crash or destabilize Windows, so FPS.LOL does not allow it."
                    : "This process runs as part of Windows services (session 0). FPS.LOL does not end system processes to keep Windows stable.");
            return;
        }

        if (!await _dialogs.ConfirmAsync($"End {row.Name}?",
                "Unsaved data in this application will be lost. Ending a process does not uninstall it, and it may start again later.",
                "End process", danger: true, items: [$"Process: {row.Name} (PID {row.Pid})", $"Memory: {row.MemoryText}"]))
            return;

        var result = await _processes.EndAsync(row.Info);
        if (result.Status == OperationStatus.Failed) await _dialogs.ShowResultAsync(result);
        else _toasts.Show(result);
        await RefreshAsync();
    }
}

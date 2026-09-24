using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;

namespace FpsLol.Services;

/// <summary>Base class for in-window (overlay) dialogs.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public Task<bool> Result => _tcs.Task;

    public event EventHandler? Closed;

    [RelayCommand]
    protected void Confirm() => Complete(true);

    [RelayCommand]
    protected void Cancel() => Complete(false);

    protected void Complete(bool result)
    {
        if (_tcs.TrySetResult(result)) Closed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed partial class ConfirmDialogViewModel : DialogViewModel
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmText { get; init; } = "Continue";
    public string? CancelText { get; init; } = "Cancel";
    public bool IsDanger { get; init; }
    public string Icon { get; init; } = "Icon.Info";
    public string Tone { get; init; } = "info";
    public IReadOnlyList<string> Items { get; init; } = [];
    public string? Details { get; init; }

    [ObservableProperty]
    private bool _showDetails;

    public bool HasItems => Items.Count > 0;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public bool HasCancel => CancelText is not null;

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    [RelayCommand]
    private void CopyDetails()
    {
        try { System.Windows.Clipboard.SetText(Details ?? string.Empty); } catch { /* clipboard busy */ }
    }
}

public interface IDialogService
{
    DialogViewModel? Current { get; }
    event EventHandler? CurrentChanged;
    Task<bool> ShowAsync(DialogViewModel dialog);
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue", bool danger = false, IReadOnlyList<string>? items = null);
    Task ShowErrorAsync(string title, string message, string? details = null);
    Task ShowInfoAsync(string title, string message, string? details = null);
    Task ShowResultAsync(OperationResult result);
}

public sealed class DialogService : IDialogService
{
    private readonly Stack<DialogViewModel> _stack = new();

    public DialogViewModel? Current => _stack.Count > 0 ? _stack.Peek() : null;

    public event EventHandler? CurrentChanged;

    public Task<bool> ShowAsync(DialogViewModel dialog)
    {
        _stack.Push(dialog);
        dialog.Closed += (_, _) =>
        {
            var remaining = _stack.Where(d => !ReferenceEquals(d, dialog)).Reverse().ToList();
            _stack.Clear();
            foreach (var d in remaining) _stack.Push(d);
            CurrentChanged?.Invoke(this, EventArgs.Empty);
        };
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        return dialog.Result;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Continue", bool danger = false, IReadOnlyList<string>? items = null) =>
        ShowAsync(new ConfirmDialogViewModel
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            IsDanger = danger,
            Icon = danger ? "Icon.Alert" : "Icon.ShieldCheck",
            Tone = danger ? "danger" : "info",
            Items = items ?? [],
        });

    public Task ShowErrorAsync(string title, string message, string? details = null) =>
        ShowAsync(new ConfirmDialogViewModel
        {
            Title = title,
            Message = message,
            Details = details,
            ConfirmText = "Close",
            CancelText = null,
            Icon = "Icon.CircleX",
            Tone = "danger",
        });

    public Task ShowInfoAsync(string title, string message, string? details = null) =>
        ShowAsync(new ConfirmDialogViewModel
        {
            Title = title,
            Message = message,
            Details = details,
            ConfirmText = "OK",
            CancelText = null,
            Icon = "Icon.Info",
            Tone = "info",
        });

    public Task ShowResultAsync(OperationResult result) => result.Status == OperationStatus.Failed
        ? ShowErrorAsync(result.Title, result.Message, result.Details)
        : ShowInfoAsync(result.Title, result.Message, result.Details);
}

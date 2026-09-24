using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;

namespace FpsLol.Services;

public sealed partial class Toast
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string Tone { get; init; }
    public required string Icon { get; init; }
    public string? Details { get; init; }
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    internal Action<Toast>? DismissAction { get; set; }
    internal Action<Toast>? DetailsAction { get; set; }

    [RelayCommand]
    private void Dismiss() => DismissAction?.Invoke(this);

    [RelayCommand]
    private void OpenDetails() => DetailsAction?.Invoke(this);
}

public interface IToastService
{
    ObservableCollection<Toast> Toasts { get; }
    event EventHandler<Toast>? Shown;
    void Success(string title, string message);
    void Info(string title, string message);
    void Warning(string title, string message, string? details = null);
    void Error(string title, string message, string? details = null);
    void Show(OperationResult result);
}

/// <summary>Non-blocking toast notifications (bottom-right). Errors keep a "Details" action.</summary>
public sealed class ToastService(IDialogService dialogs) : IToastService
{
    private const int MaxVisible = 4;

    public ObservableCollection<Toast> Toasts { get; } = [];

    public event EventHandler<Toast>? Shown;

    public void Success(string title, string message) => Add(title, message, "success", "Icon.CircleCheck", null, 4000);
    public void Info(string title, string message) => Add(title, message, "info", "Icon.Info", null, 4000);
    public void Warning(string title, string message, string? details = null) => Add(title, message, "warning", "Icon.Alert", details, 6500);
    public void Error(string title, string message, string? details = null) => Add(title, message, "danger", "Icon.CircleX", details, 8000);

    public void Show(OperationResult result)
    {
        switch (result.Status)
        {
            case OperationStatus.Success: Success(result.Title, result.Message); break;
            case OperationStatus.Failed: Error(result.Title, result.Message, result.Details); break;
            case OperationStatus.Skipped: Warning(result.Title, result.Message, result.Details); break;
            default: Info(result.Title, result.Message); break;
        }
    }

    private void Add(string title, string message, string tone, string icon, string? details, int lifetimeMs)
    {
        void Run()
        {
            var toast = new Toast { Title = title, Message = message, Tone = tone, Icon = icon, Details = details };
            toast.DismissAction = t => Toasts.Remove(t);
            toast.DetailsAction = t =>
            {
                Toasts.Remove(t);
                _ = dialogs.ShowErrorAsync(t.Title, t.Message, t.Details);
            };
            Toasts.Add(toast);
            while (Toasts.Count > MaxVisible) Toasts.RemoveAt(0);
            Shown?.Invoke(this, toast);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(lifetimeMs) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Toasts.Remove(toast);
            };
            timer.Start();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Run();
        else dispatcher.Invoke(Run);
    }
}

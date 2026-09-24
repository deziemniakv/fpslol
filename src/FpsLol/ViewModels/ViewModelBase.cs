using CommunityToolkit.Mvvm.ComponentModel;

namespace FpsLol.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}

/// <summary>Base for every page shown in the main content area.</summary>
public abstract partial class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }

    public abstract string Subtitle { get; }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>Called every time the page becomes visible.</summary>
    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    /// <summary>Called when the user leaves the page.</summary>
    public virtual void OnNavigatedFrom()
    {
    }
}

using FpsLol.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FpsLol.Services;

public interface INavigationService
{
    PageViewModel? Current { get; }
    event EventHandler<PageViewModel>? Navigated;
    void Navigate<T>() where T : PageViewModel;
    void Navigate(Type pageType);
}

public sealed class NavigationService(IServiceProvider services) : INavigationService
{
    public PageViewModel? Current { get; private set; }

    public event EventHandler<PageViewModel>? Navigated;

    public void Navigate<T>() where T : PageViewModel => Navigate(typeof(T));

    public void Navigate(Type pageType)
    {
        var page = (PageViewModel)services.GetRequiredService(pageType);
        if (ReferenceEquals(page, Current)) return;

        Current?.OnNavigatedFrom();
        Current = page;
        Navigated?.Invoke(this, page);
        _ = page.OnNavigatedToAsync();
    }
}

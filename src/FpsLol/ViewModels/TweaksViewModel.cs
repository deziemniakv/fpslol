using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FpsLol.Models;
using FpsLol.Services;
using FpsLol.Utilities;
using FpsLol.ViewModels.Items;

namespace FpsLol.ViewModels;

public sealed partial class TweaksViewModel : PageViewModel
{
    private const string AllCategories = "All";
    private readonly ISystemScanService _scan;
    private readonly ITweakActionService _actions;

    public TweaksViewModel(ISystemScanService scan, ITweakActionService actions)
    {
        _scan = scan;
        _actions = actions;
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = Filter;
        Categories = [AllCategories, .. Enum.GetValues<TweakCategory>().Select(CategoryNames.Of)];
        scan.Scanned += (_, r) => Ui.Post(() => Apply(r));
    }

    public override string Title => "Tweaks";
    public override string Subtitle => "Individual Windows settings with apply, restore and risk level";

    public ObservableCollection<TweakItemViewModel> Items { get; } = [];
    public ICollectionView ItemsView { get; }
    public IReadOnlyList<string> Categories { get; }

    [ObservableProperty] private string _selectedCategory = AllCategories;
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;

    partial void OnSelectedCategoryChanged(string value) => ItemsView.Refresh();
    partial void OnSearchChanged(string value) => ItemsView.Refresh();

    private bool Filter(object o)
    {
        if (o is not TweakItemViewModel t) return false;
        if (SelectedCategory != AllCategories && t.CategoryName != SelectedCategory) return false;
        return string.IsNullOrWhiteSpace(Search)
               || t.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
               || t.Description.Contains(Search, StringComparison.OrdinalIgnoreCase);
    }

    public override async Task OnNavigatedToAsync()
    {
        Apply(await _scan.EnsureAsync());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            Apply(await _scan.ScanAsync());
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(ScanResult result)
    {
        foreach (var status in result.Tweaks)
        {
            var existing = Items.FirstOrDefault(i => i.Id == status.Definition.Id);
            if (existing is null) Items.Add(new TweakItemViewModel(status, _actions));
            else if (!existing.IsBusy) existing.Update(status);
        }
        var supported = result.Tweaks.Count(t => t.Detection.Supported);
        var optimal = result.Tweaks.Count(t => t.Detection.Supported && t.Detection.IsOptimal);
        Summary = $"{optimal} of {supported} supported tweaks are in the recommended state";
        IsLoaded = true;
    }
}

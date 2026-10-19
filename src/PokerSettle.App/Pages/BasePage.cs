using PokerSettle.Core.ViewModels;

namespace PokerSettle.App.Pages;

/// <summary>
/// All pages: binds the ViewModel, applies the shared page template (DEMO banner + snackbar),
/// forwards Shell query parameters and appearing/disappearing to the ViewModel.
/// </summary>
public abstract class BasePage : ContentPage, IQueryAttributable
{
    protected BasePage(ViewModelBase viewModel)
    {
        // The context is set before InitializeComponent adds the XAML toolbar items, and MAUI doesn't
        // hand it to items added later, so pass it on as they arrive (otherwise their commands are null).
        if (ToolbarItems is System.Collections.Specialized.INotifyCollectionChanged items)
        {
            items.CollectionChanged += (_, e) =>
            {
                if (e.NewItems is null) return;
                foreach (var item in e.NewItems.OfType<ToolbarItem>()) SetInheritedBindingContext(item, BindingContext);
            };
        }
        BindingContext = viewModel;
        if (Application.Current?.Resources.TryGetValue("PageTemplate", out var template) == true)
            ControlTemplate = (ControlTemplate)template;
    }

    protected ViewModelBase ViewModel => (ViewModelBase)BindingContext;

    /// <summary>Height of the page's bottom action bar, so the snackbar floats above it instead of covering it.</summary>
    public double SnackbarInset { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (ViewModel is IQueryReceiver receiver) receiver.ApplyQuery(query);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // OnAppearingAsync never throws: it runs through the ViewModel's safe wrapper.
        await ViewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ViewModel.OnDisappearing();
    }
}

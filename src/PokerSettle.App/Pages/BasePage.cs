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
        BindingContext = viewModel;
        if (Application.Current?.Resources.TryGetValue("PageTemplate", out var template) == true)
            ControlTemplate = (ControlTemplate)template;
    }

    protected ViewModelBase ViewModel => (ViewModelBase)BindingContext;

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

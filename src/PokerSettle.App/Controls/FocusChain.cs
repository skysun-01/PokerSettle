namespace PokerSettle.App.Controls;

/// <summary>
/// Keyboard flow for forms: the Next key (or Enter on Windows) on an Entry moves focus to the next field.
/// Wire it in code-behind: <c>FocusChain.Link(TitleEntry, BuyInEntry, SearchEntry)</c>.
/// </summary>
public static class FocusChain
{
    public static readonly BindableProperty NextProperty = BindableProperty.CreateAttached(
        "Next", typeof(VisualElement), typeof(FocusChain), null, propertyChanged: OnNextChanged);

    public static VisualElement? GetNext(BindableObject view) => (VisualElement?)view.GetValue(NextProperty);

    public static void SetNext(BindableObject view, VisualElement? value) => view.SetValue(NextProperty, value);

    /// <summary>Each entry's Next key focuses the following element.</summary>
    public static void Link(params VisualElement[] elements)
    {
        for (var i = 0; i < elements.Length - 1; i++)
        {
            if (elements[i] is Entry entry)
            {
                entry.ReturnType = ReturnType.Next;
                SetNext(entry, elements[i + 1]);
            }
        }
    }

    /// <summary>Focuses the Entry that follows <paramref name="current"/> inside <paramref name="container"/> (for lists).</summary>
    public static bool FocusFollowing(Element container, Entry current)
    {
        var entries = container.GetVisualTreeDescendants().OfType<Entry>().Where(e => e.IsVisible && e.IsEnabled).ToList();
        var index = entries.IndexOf(current);
        if (index < 0 || index + 1 >= entries.Count) return false;
        entries[index + 1].Focus();
        return true;
    }

    private static void OnNextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not Entry entry) return;
        entry.Completed -= OnCompleted;
        if (newValue is not null) entry.Completed += OnCompleted;
    }

    private static void OnCompleted(object? sender, EventArgs e)
    {
        if (sender is Entry entry && GetNext(entry) is { } next) next.Focus();
    }
}

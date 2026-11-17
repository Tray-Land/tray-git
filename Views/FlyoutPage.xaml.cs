using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using TrayGit.Core;
using TrayGit.Services;
using TrayGit.ViewModels;
using Windows.System;

namespace TrayGit.Views;

/// <summary>The repository list view. Focus, pointer, and keyboard behavior stay here.</summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private static readonly TimeSpan DoubleClickWindow = TimeSpan.FromMilliseconds(400);

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _clickTimer;
    private RepoItemViewModel? _pendingClick;

    public FlyoutPage()
    {
        ViewModel = new FlyoutViewModel(App.Current, new ClipboardService());
        InitializeComponent();
    }

    public FlyoutViewModel ViewModel { get; }

    public void OnShown()
    {
        ViewModel.OnShown();
        DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => SearchBox.Focus(FocusState.Programmatic));
    }

    public bool ClearFilter()
    {
        if (!ViewModel.ClearFilter())
        {
            return false;
        }

        SearchBox.Focus(FocusState.Programmatic);
        return true;
    }

    public void OnHidden()
    {
        _clickTimer?.Stop();
        _pendingClick = null;
        ViewModel.OnHidden();
    }

    public void Dispose() => ViewModel.Dispose();

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        ViewModel.SubmitSearchCommand.Execute(null);

    private void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            if (!ClearFilter())
            {
                App.Current.HideFlyout();
            }
        }
        else if (e.Key == VirtualKey.Down && RepoList.Items.Count > 0)
        {
            e.Handled = true;
            RepoList.SelectedIndex = 0;
            FocusRow(0);
        }
        else if (e.Key == VirtualKey.Right
            && FocusManager.GetFocusedElement(XamlRoot) is TextBox textBox
            && SearchNavigationPolicy.EnterFirstResultActions(
                textBox.Text.Length,
                textBox.SelectionStart,
                textBox.SelectionLength,
                IsKeyDown(VirtualKey.Control)
                    || IsKeyDown(VirtualKey.Shift)
                    || IsKeyDown(VirtualKey.Menu)
                    || IsKeyDown(VirtualKey.LeftWindows)
                    || IsKeyDown(VirtualKey.RightWindows),
                RepoList.Items.Count > 0))
        {
            e.Handled = true;
            RepoList.SelectedIndex = 0;
            FocusRow(0);
            StepAction(0, 1);
        }
    }

    private void RepoList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        int index = RepoList.SelectedIndex;
        switch (e.Key)
        {
            case VirtualKey.Up when index <= 0:
                e.Handled = true;
                RepoList.SelectedItem = null;
                SearchBox.Focus(FocusState.Keyboard);
                break;

            case VirtualKey.Left or VirtualKey.Right when index >= 0:
                e.Handled = true;
                StepAction(index, e.Key == VirtualKey.Right ? 1 : -1);
                break;

            case VirtualKey.Enter when index >= 0
                && FocusManager.GetFocusedElement(XamlRoot) is ListViewItem
                && RepoList.SelectedItem is RepoItemViewModel item:
                e.Handled = true;
                ViewModel.RunEnterActionCommand.Execute(item.Repo);
                break;

            case VirtualKey.C when index >= 0
                && IsKeyDown(VirtualKey.Control)
                && FocusManager.GetFocusedElement(XamlRoot) is not TextBox
                && RepoList.SelectedItem is RepoItemViewModel selected:
                e.Handled = true;
                selected.CopyPathCommand.Execute(null);
                break;

            case var key when index >= 0
                && NumberOf(key) is int number
                && !IsKeyDown(VirtualKey.Control)
                && !IsKeyDown(VirtualKey.Menu)
                && !IsKeyDown(VirtualKey.Shift)
                && FocusManager.GetFocusedElement(XamlRoot) is not TextBox
                && RepoList.SelectedItem is RepoItemViewModel target
                && number <= target.Actions.Count:
                e.Handled = true;
                RepoActionViewModel action = target.Actions[number - 1];
                if (action.IsEnabled)
                {
                    action.RunCommand.Execute(null);
                }

                break;
        }
    }

    // Only the selected row carries access keys, so tapping Alt shows its 1..n hints.
    private void RepoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (object removed in e.RemovedItems)
        {
            SetAccessKeys(removed, false);
        }

        foreach (object added in e.AddedItems)
        {
            SetAccessKeys(added, true);
        }
    }

    private void SetAccessKeys(object row, bool enable)
    {
        if (row is not RepoItemViewModel item
            || RepoList.ContainerFromItem(item) is not ListViewItem container)
        {
            return;
        }

        List<Button> buttons = [];
        CollectButtons(container, buttons);
        for (int i = 0; i < Math.Min(item.Actions.Count, 9) && i < buttons.Count; i++)
        {
            buttons[i].AccessKey = enable ? (i + 1).ToString() : string.Empty;
        }
    }

    private static int? NumberOf(VirtualKey key) => key switch
    {
        >= VirtualKey.Number1 and <= VirtualKey.Number9 => key - VirtualKey.Number0,
        >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => key - VirtualKey.NumberPad0,
        _ => null,
    };

    private void FocusRow(int index)
    {
        if (RepoList.ContainerFromIndex(index) is not ListViewItem item)
        {
            RepoList.UpdateLayout();
            item = (ListViewItem)RepoList.ContainerFromIndex(index);
        }

        item?.Focus(FocusState.Keyboard);
    }

    private void StepAction(int index, int delta)
    {
        if (RepoList.ContainerFromIndex(index) is not ListViewItem item)
        {
            return;
        }

        List<Button> buttons = [];
        CollectButtons(item, buttons);
        List<Control> stops = [item, .. buttons.Where(button => button.IsEnabled)];

        object? focused = FocusManager.GetFocusedElement(XamlRoot);
        int current = Math.Max(0, stops.FindIndex(stop => ReferenceEquals(stop, focused)));
        int next = Math.Clamp(current + delta, 0, stops.Count - 1);
        stops[next].Focus(FocusState.Keyboard);
    }

    private static void CollectButtons(DependencyObject parent, List<Button> buttons)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button)
            {
                buttons.Add(button);
            }
            else
            {
                CollectButtons(child, buttons);
            }
        }
    }

    private void RepoRow_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        ViewModel.SetHoverPath(ItemOfRow(sender)?.Repo.Path);

    private void RepoRow_PointerExited(object sender, PointerRoutedEventArgs e)
        => ViewModel.ClearHoverPath(ItemOfRow(sender)?.Repo.Path);

    private void RepoList_GotFocus(object sender, RoutedEventArgs e) =>
        ViewModel.SetFocusPath(
            e.OriginalSource is Control { FocusState: FocusState.Keyboard }
                ? RowPathOf(e.OriginalSource)
                : null);

    private void RepoList_LostFocus(object sender, RoutedEventArgs e) =>
        ViewModel.SetFocusPath(null);

    private static string? RowPathOf(object? source)
    {
        for (DependencyObject? element = source as DependencyObject;
             element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewItem { Content: RepoItemViewModel item })
            {
                return item.Repo.Path;
            }
        }

        return null;
    }

    private void RepoRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!ViewModel.HasClickAction || IsFromButton(e.OriginalSource) || ItemOfRow(sender) is not { } item)
        {
            return;
        }

        if (!ViewModel.HasDoubleClickAction)
        {
            ViewModel.RunClickActionCommand.Execute(item.Repo);
            return;
        }

        _pendingClick = item;
        if (_clickTimer is null)
        {
            _clickTimer = DispatcherQueue.CreateTimer();
            _clickTimer.IsRepeating = false;
            _clickTimer.Interval = DoubleClickWindow;
            _clickTimer.Tick += (_, _) =>
            {
                if (_pendingClick is { } pending)
                {
                    _pendingClick = null;
                    ViewModel.RunClickActionCommand.Execute(pending.Repo);
                }
            };
        }

        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void RepoRow_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _clickTimer?.Stop();
        _pendingClick = null;
        if (!IsFromButton(e.OriginalSource) && ItemOfRow(sender) is { } item)
        {
            ViewModel.RunDoubleClickActionCommand.Execute(item.Repo);
        }
    }

    private static bool IsFromButton(object? source)
    {
        for (DependencyObject? element = source as DependencyObject;
             element is not null and not ListViewItem;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private static RepoItemViewModel? ItemOfRow(object sender) =>
        (sender as FrameworkElement)?.DataContext as RepoItemViewModel;

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}

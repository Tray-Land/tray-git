using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TrayGit.Core;
using TrayGit.Services;
using TrayGit.ViewModels;

namespace TrayGit.Views;

public sealed partial class RepoDetailPage : Page, IDisposable
{
    public RepoDetailPage(GitRepo repo, Action goBack, Action<GitRepo> showDiff)
    {
        ViewModel = new RepoDetailViewModel(repo, goBack, showDiff, new ClipboardService());
        InitializeComponent();
    }

    public RepoDetailViewModel ViewModel { get; }

    public void OnShown()
    {
        ViewModel.OnShown();
        BackButton.Focus(FocusState.Programmatic);
    }

    public void OnHidden() => ViewModel.OnHidden();

    public void Dispose() => ViewModel.Dispose();

    private void CopyBranch_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetCopyBranchOpacity(sender, 1);

    private void CopyBranch_PointerExited(object sender, PointerRoutedEventArgs e) =>
        SetCopyBranchOpacity(sender, 0);

    private void CopyBranchButton_GotFocus(object sender, RoutedEventArgs e) =>
        ((UIElement)sender).Opacity = 1;

    private void CopyBranchButton_LostFocus(object sender, RoutedEventArgs e) =>
        ((UIElement)sender).Opacity = 0;

    private void BranchCopy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GitBranch branch)
        {
            ViewModel.CopyBranchNameCommand.Execute(branch.Name);
        }
    }

    private static void SetCopyBranchOpacity(object sender, double opacity)
    {
        DependencyObject root = (DependencyObject)sender;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button
                && AutomationProperties.GetName(button) == "Copy branch name")
            {
                button.Opacity = opacity;
                return;
            }

            SetCopyBranchOpacity(child, opacity);
        }
    }
}

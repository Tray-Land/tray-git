using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayGit.Core;
using TrayGit.ViewModels;

namespace TrayGit.Views;

public sealed partial class RepoDiffPage : Page, IDisposable
{
    public RepoDiffPage(GitRepo repo, Action goBack)
    {
        ViewModel = new RepoDiffViewModel(repo, goBack);
        InitializeComponent();
    }

    public RepoDiffViewModel ViewModel { get; }

    public void OnShown()
    {
        ViewModel.OnShown();
        BackButton.Focus(FocusState.Programmatic);
    }

    public void OnHidden() => ViewModel.OnHidden();

    public void Dispose() => ViewModel.Dispose();
}

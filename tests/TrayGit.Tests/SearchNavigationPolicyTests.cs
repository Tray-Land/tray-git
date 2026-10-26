using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class SearchNavigationPolicyTests
{
    [TestMethod]
    public void RightAtEndEntersFirstResultActions()
    {
        Assert.IsTrue(SearchNavigationPolicy.EnterFirstResultActions(4, 4, 0, false, true));
        Assert.IsTrue(SearchNavigationPolicy.EnterFirstResultActions(0, 0, 0, false, true));
    }

    [TestMethod]
    public void EditingOrMissingResultsKeepsFocusInSearch()
    {
        Assert.IsFalse(SearchNavigationPolicy.EnterFirstResultActions(4, 2, 0, false, true));
        Assert.IsFalse(SearchNavigationPolicy.EnterFirstResultActions(4, 3, 1, false, true));
        Assert.IsFalse(SearchNavigationPolicy.EnterFirstResultActions(4, 4, 0, true, true));
        Assert.IsFalse(SearchNavigationPolicy.EnterFirstResultActions(4, 4, 0, false, false));
    }
}

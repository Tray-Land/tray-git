using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class RepoListPolicyTests
{
    private static readonly GitRepo[] Repos =
    [
        Repo("alpha"),
        Repo("beta"),
        Repo("tray-git", @"apps\tray-git"),
        Repo("gamma"),
    ];

    [TestMethod]
    public void RecentFirstThenByName()
    {
        Dictionary<string, long> lastUsed = new() { [@"C:\src\gamma"] = 20, [@"C:\src\beta"] = 10 };
        CollectionAssert.AreEqual(
            new[] { "gamma", "beta", "alpha", "tray-git" },
            RepoListPolicy.Arrange(Repos, lastUsed, null).Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void FiltersOnEveryWordAndRanksNamePrefixFirst()
    {
        CollectionAssert.AreEqual(new[] { "tray-git" }, Names("apps git"));
        CollectionAssert.AreEqual(new[] { "tray-git", "beta" }, Names("t"));
        CollectionAssert.AreEqual(new[] { "gamma" }, Names("GA"));
    }

    [TestMethod]
    public void RecordUseKeepsTheNewest()
    {
        Dictionary<string, long> map = [];
        for (int i = 0; i < RepoListPolicy.MaxRecent + 5; i++)
        {
            map = RepoListPolicy.RecordUse(map, $"p{i}", new DateTime(2026, 1, 1).AddMinutes(i));
        }

        Assert.AreEqual(RepoListPolicy.MaxRecent, map.Count);
        Assert.IsFalse(map.ContainsKey("p0"));
        Assert.IsTrue(map.ContainsKey($"P{RepoListPolicy.MaxRecent + 4}"));
    }

    [TestMethod]
    public void DescribesBranchAndParent()
    {
        Assert.AreEqual("main · apps", RepoListPolicy.Describe(Repo("x", @"apps\x") with { Branch = "main" }));
        Assert.AreEqual("main", RepoListPolicy.Describe(Repo("x") with { Branch = "main" }));
        Assert.AreEqual("apps", RepoListPolicy.Describe(Repo("x", @"apps\x")));
        Assert.AreEqual("", RepoListPolicy.Describe(Repo("x")));
    }

    private static string[] Names(string query) =>
        RepoListPolicy.Arrange(Repos, new Dictionary<string, long>(), query).Select(r => r.Name).ToArray();

    private static GitRepo Repo(string name, string? relative = null) =>
        new(name, $@"C:\src\{relative ?? name}", relative ?? name, null, null, null);
}

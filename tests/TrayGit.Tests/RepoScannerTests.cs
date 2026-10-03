using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class RepoScannerTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "TrayGitTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public void FindsReposAndReadsBranchAndRemote()
    {
        MakeRepo("alpha", "main", "git@github.com:me/alpha.git");
        MakeRepo(Path.Combine("work", "beta"), "dev", null);
        Directory.CreateDirectory(Path.Combine(_root, "not-a-repo", "child"));

        List<GitRepo> repos = RepoScanner.Scan(_root);

        CollectionAssert.AreEqual(new[] { "alpha", "beta" }, repos.Select(r => r.Name).ToArray());
        Assert.AreEqual("main", repos[0].Branch);
        Assert.AreEqual("https://github.com/me/alpha", repos[0].WebUrl);
        Assert.AreEqual(Path.Combine("work", "beta"), repos[1].RelativePath);
        Assert.IsNull(repos[1].RemoteUrl);
        Assert.AreEqual("dev · work", repos[1].Description);
    }

    [TestMethod]
    public void DoesNotDescendIntoReposOrSkippedFolders()
    {
        MakeRepo("outer", "main", null);
        MakeRepo(Path.Combine("outer", "submodule"), "main", null);
        MakeRepo(Path.Combine("app", "node_modules", "pkg"), "main", null);

        CollectionAssert.AreEqual(new[] { "outer" }, RepoScanner.Scan(_root).Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void RespectsMaxDepth()
    {
        MakeRepo(Path.Combine("a", "b", "c"), "main", null);

        Assert.AreEqual(0, RepoScanner.Scan(_root, maxDepth: 2).Count);
        Assert.AreEqual(1, RepoScanner.Scan(_root, maxDepth: 3).Count);
    }

    [TestMethod]
    public void FollowsWorktreeGitFileToCommonConfig()
    {
        string main = MakeRepo("main-repo", "main", "https://github.com/me/proj.git");
        string worktreeGitDir = Path.Combine(main, ".git", "worktrees", "wt");
        Directory.CreateDirectory(worktreeGitDir);
        File.WriteAllText(Path.Combine(worktreeGitDir, "HEAD"), "ref: refs/heads/feature\n");
        File.WriteAllText(Path.Combine(worktreeGitDir, "commondir"), "../..\n");

        string worktree = Path.Combine(_root, "wt");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {worktreeGitDir}\n");

        GitRepo wt = RepoScanner.Scan(_root).Single(r => r.Name == "wt");
        Assert.AreEqual("feature", wt.Branch);
        Assert.AreEqual("https://github.com/me/proj", wt.WebUrl);
    }

    [TestMethod]
    public void MissingRootIsEmpty() => Assert.AreEqual(0, RepoScanner.Scan(Path.Combine(_root, "nope")).Count);

    private string MakeRepo(string relative, string branch, string? remote)
    {
        string dir = Path.Combine(_root, relative);
        string gitDir = Path.Combine(dir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "HEAD"), $"ref: refs/heads/{branch}\n");
        File.WriteAllText(Path.Combine(gitDir, "config"),
            remote is null ? "[core]\n" : $"[core]\n[remote \"origin\"]\n\turl = {remote}\n");
        return dir;
    }
}

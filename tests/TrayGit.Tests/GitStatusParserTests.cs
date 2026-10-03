using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class GitStatusParserTests
{
    [TestMethod]
    public void SumsAddedAndRemovedLinesAndSkipsBinaryFiles()
    {
        const string output = "12\t3\tsrc/a.cs\n4\t0\tb.txt\n-\t-\timage.png\n0\t7\tdir/with space.md\n";
        Assert.AreEqual((16, 10), GitStatusParser.ParseNumstat(output));
    }

    [TestMethod]
    public void EmptyDiffIsZero()
    {
        Assert.AreEqual((0, 0), GitStatusParser.ParseNumstat(""));
    }

    [TestMethod]
    public void ReadsAheadAndBehindFromBranchHeaders()
    {
        const string output = "# branch.oid abc\r\n# branch.head main\r\n# branch.upstream origin/main\r\n# branch.ab +2 -5\r\n";
        Assert.AreEqual((2, 5), GitStatusParser.ParseBranchAheadBehind(output));
    }

    [TestMethod]
    public void NoUpstreamMeansNothingToPullOrPush()
    {
        Assert.AreEqual((0, 0), GitStatusParser.ParseBranchAheadBehind("# branch.oid abc\n# branch.head topic\n"));
    }

    [TestMethod]
    public void CombineMapsAheadAndBehind()
    {
        RepoStatus status = GitStatusParser.Combine("1\t2\tf\n", "# branch.ab +3 -4\n");
        Assert.AreEqual(new RepoStatus(Added: 1, Removed: 2, Behind: 4, Ahead: 3), status);
        Assert.IsFalse(status.IsClean);
        Assert.IsTrue(GitStatusParser.Combine("", "").IsClean);
    }

    [TestMethod]
    public void FavoriteTextAndChangeNotification()
    {
        GitRepo repo = new("x", @"C:\src\x", "x", null, null, null);
        int raised = 0;
        repo.PropertyChanged += (_, _) => raised++;

        Assert.AreEqual("Favorite", repo.FavoriteActionText);
        repo.SetFavorite(true);
        repo.SetFavorite(true);

        Assert.IsTrue(repo.IsFavorite);
        Assert.AreEqual("Unfavorite", repo.FavoriteActionText);
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void RepoStatusTextAndChangeNotification()
    {
        GitRepo repo = new("x", @"C:\src\apps\x", @"apps\x", "main", null, null);
        int raised = 0;
        repo.PropertyChanged += (_, _) => raised++;

        repo.SetStatus(new RepoStatus(12, 3, 2, 1));

        Assert.AreEqual("+12", repo.AddedText);
        Assert.AreEqual("\u22123", repo.RemovedText);
        Assert.AreEqual("\u21932", repo.BehindText);
        Assert.AreEqual("\u21911", repo.AheadText);
        Assert.AreEqual("\u00B7 apps", repo.ParentCaption);
        Assert.AreEqual(1, raised);

        repo.SetStatus(new RepoStatus(12, 3, 2, 1));
        Assert.AreEqual(1, raised);

        repo.SetStatus(new RepoStatus(0, 0, 0, 0));
        Assert.IsFalse(repo.HasAdded || repo.HasRemoved || repo.HasBehind || repo.HasAhead);
    }
}

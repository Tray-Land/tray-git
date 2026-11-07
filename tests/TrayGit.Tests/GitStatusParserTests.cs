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
    public void LocalChangesAreDistinctFromAheadAndBehind()
    {
        RepoStatus changed = GitStatusParser.Combine("-\t-\timage.png\n", "1 .M N... 100644 100644 100644 abc def file.png\n");
        RepoStatus untracked = GitStatusParser.Combine("", "# branch.head main\n? new.txt\n");
        RepoStatus ahead = GitStatusParser.Combine("", "# branch.ab +2 -0\n");

        Assert.IsTrue(changed.IsDirty);
        Assert.IsTrue(untracked.IsDirty);
        Assert.IsFalse(ahead.IsDirty);
        Assert.IsFalse(ahead.IsClean);
        Assert.IsFalse(GitStatusParser.HasLocalChanges("# branch.oid abc\n# branch.head main\n"));
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

    [TestMethod]
    public void ParsesWorkingChangesAndLineCounts()
    {
        const string status = "M  tracked.cs\0 M unstaged.md\0MM both.txt\0?? new file.txt\0";
        const string numstat = "12\t3\ttracked.cs\04\t2\tunstaged.md\00\t0\tboth.txt\0-\t-\timage.png\0";

        List<GitFileChange> changes = [.. GitDetailsParser.ParseChanges(status, numstat)];

        Assert.AreEqual(4, changes.Count);
        Assert.AreEqual("Modified", changes[0].Status);
        Assert.IsTrue(changes[0].IsStaged);
        Assert.IsFalse(changes[0].IsUnstaged);
        Assert.AreEqual(12, changes[0].Added);
        Assert.AreEqual(3, changes[0].Removed);
        Assert.AreEqual("Unstaged", changes[1].StateText);
        Assert.AreEqual("Staged and unstaged", changes[2].StateText);
        Assert.AreEqual("Untracked", changes[3].Status);
        Assert.AreEqual("new file.txt", changes[3].FileName);
        Assert.AreEqual("", changes[3].LineChangeText);
        Assert.AreEqual("Binary", new GitFileChange("image.png", "Modified", false, true, null, null).LineChangeText);
    }

    [TestMethod]
    public void ParsesCommitHistory()
    {
        const string output = "abcdef0123456789\u001fabcdef0\u001fAda Lovelace\u001f2026-01-02T10:11:12+00:00\u001fAdd notes\u001e";

        GitCommit commit = GitDetailsParser.ParseCommits(output).Single();

        Assert.AreEqual("abcdef0", commit.ShortHash);
        Assert.AreEqual("Ada Lovelace", commit.Author);
        Assert.AreEqual("Add notes", commit.Subject);
        Assert.AreEqual(DateTimeOffset.Parse("2026-01-02T10:11:12+00:00"), commit.AuthoredAt);
    }

    [TestMethod]
    public void ParsesLocalAndRemoteBranches()
    {
        const string output =
            "refs/heads/main\u001fmain\u001fabcdef0\u001f*\u001forigin/main\u001f\n"
            + "refs/remotes/origin/main\u001forigin/main\u001fabcdef0\u001f\u001f\u001f\n"
            + "refs/remotes/origin/HEAD\u001forigin/HEAD\u001fabcdef0\u001f\u001f\u001forigin/main\n";

        List<GitBranch> branches = [.. GitDetailsParser.ParseBranches(output)];

        Assert.AreEqual(2, branches.Count);
        Assert.IsTrue(branches[0].IsCurrent);
        Assert.AreEqual("origin/main", branches[0].Upstream);
        Assert.IsTrue(branches[1].IsRemote);
        Assert.AreEqual("Remote", branches[1].KindText);
    }
}

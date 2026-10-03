using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class GitMetadataTests
{
    [TestMethod]
    [DataRow("ref: refs/heads/main\n", "main")]
    [DataRow("ref: refs/heads/feature/login", "feature/login")]
    [DataRow("0123456789abcdef0123456789abcdef01234567\n", "0123456")]
    [DataRow("garbage", null)]
    public void ParsesHead(string head, string? expected) =>
        Assert.AreEqual(expected, GitMetadata.ParseHead(head));

    [TestMethod]
    public void PrefersOrigin()
    {
        const string config = """
            [core]
            	bare = false
            [remote "upstream"]
            	url = https://github.com/upstream/repo.git
            [remote "origin"]
            	url = git@github.com:me/repo.git
            	fetch = +refs/heads/*:refs/remotes/origin/*
            [branch "main"]
            	remote = origin
            """;
        Assert.AreEqual("git@github.com:me/repo.git", GitMetadata.ParseRemoteUrl(config));
    }

    [TestMethod]
    public void FallsBackToFirstRemote()
    {
        const string config = "[remote \"fork\"]\r\n\turl = \"https://example.com/a.git\" ; note\r\n[remote \"b\"]\r\n\turl = https://example.com/b.git\r\n";
        Assert.AreEqual("https://example.com/a.git", GitMetadata.ParseRemoteUrl(config));
    }

    [TestMethod]
    public void NoRemote() => Assert.IsNull(GitMetadata.ParseRemoteUrl("[core]\n\tbare = false\n[branch \"main\"]\n\turl = nope\n"));
}

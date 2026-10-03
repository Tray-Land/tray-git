using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class RemoteUrlPolicyTests
{
    [TestMethod]
    [DataRow("https://github.com/owner/repo.git", "https://github.com/owner/repo")]
    [DataRow("https://github.com/owner/repo", "https://github.com/owner/repo")]
    [DataRow("https://github.com/owner/repo/", "https://github.com/owner/repo")]
    [DataRow("https://user:token@github.com/owner/repo.git", "https://github.com/owner/repo")]
    [DataRow("git@github.com:owner/repo.git", "https://github.com/owner/repo")]
    [DataRow("github.com:owner/repo", "https://github.com/owner/repo")]
    [DataRow("ssh://git@gitlab.com/group/sub/repo.git", "https://gitlab.com/group/sub/repo")]
    [DataRow("ssh://git@git.example.com:2222/team/repo.git", "https://git.example.com/team/repo")]
    [DataRow("git://example.org/project.git", "https://example.org/project")]
    [DataRow("http://intranet:8080/scm/repo.git", "http://intranet:8080/scm/repo")]
    [DataRow("https://git.example.com:8443/repo.git", "https://git.example.com:8443/repo")]
    [DataRow("git@ssh.dev.azure.com:v3/org/project/repo", "https://dev.azure.com/org/project/_git/repo")]
    [DataRow("https://org@dev.azure.com/org/project/_git/repo", "https://dev.azure.com/org/project/_git/repo")]
    public void MapsRemotesToWebPages(string remote, string expected) =>
        Assert.AreEqual(expected, RemoteUrlPolicy.ToWebUrl(remote));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(@"C:\repos\shared.git")]
    [DataRow("D:/repos/shared.git")]
    [DataRow("../other")]
    [DataRow("/srv/git/repo.git")]
    [DataRow("file:///C:/repos/shared.git")]
    [DataRow("https://github.com/")]
    public void LocalOrEmptyRemotesHaveNoWebPage(string? remote) =>
        Assert.IsNull(RemoteUrlPolicy.ToWebUrl(remote));
}

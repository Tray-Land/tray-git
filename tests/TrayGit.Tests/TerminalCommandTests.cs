using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class TerminalCommandTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void No_command_only_sets_the_directory(string? command) =>
        Assert.AreEqual("-d \"D:\\src\\app\"", TerminalCommand.BuildWindowsTerminalArguments("D:\\src\\app\\", "pwsh.exe", command));

    [TestMethod]
    public void Drive_root_and_semicolons_are_normalized() =>
        Assert.AreEqual("-d \"D:\\.\"", TerminalCommand.BuildWindowsTerminalArguments("D:\\", "pwsh.exe", null));

    [TestMethod]
    public void Command_runs_in_the_shell_and_keeps_the_tab_open() =>
        Assert.AreEqual(
            "-d \"C:\\r\" pwsh.exe -NoExit -Command \"claude\"",
            TerminalCommand.BuildWindowsTerminalArguments("C:\\r", "pwsh.exe", " claude "));

    [TestMethod]
    public void Semicolons_in_the_command_do_not_split_wt_commands() =>
        Assert.AreEqual(
            "-d \"C:\\r\" pwsh.exe -NoExit -Command \"a\\; b\"",
            TerminalCommand.BuildWindowsTerminalArguments("C:\\r", "pwsh.exe", "a; b"));

    [TestMethod]
    public void Quotes_and_trailing_backslashes_are_escaped()
    {
        Assert.AreEqual("\"say \\\"hi\\\"\"", TerminalCommand.Quote("say \"hi\""));
        Assert.AreEqual("\"C:\\dir\\\\\"", TerminalCommand.Quote("C:\\dir\\"));
    }

    [TestMethod]
    public void Shell_arguments_are_empty_without_a_command()
    {
        Assert.AreEqual("", TerminalCommand.BuildShellArguments(null));
        Assert.AreEqual("-NoExit -Command \"copilot\"", TerminalCommand.BuildShellArguments("copilot"));
    }
}

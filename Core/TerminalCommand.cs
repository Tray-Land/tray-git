using System.Text;

namespace TrayGit.Core;

/// <summary>Builds the command lines that open a terminal in a repo, optionally running a command.</summary>
public static class TerminalCommand
{
    /// <summary>Arguments for <c>wt.exe</c>: start in <paramref name="dir"/>, then run <paramref name="command"/> in <paramref name="shell"/> if one is set.</summary>
    public static string BuildWindowsTerminalArguments(string dir, string shell, string? command)
    {
        // wt treats ';' as a command separator, and a trailing '\' before the closing quote
        // escapes it ("D:\" breaks the argument), so normalize both.
        dir = dir.TrimEnd('\\');
        if (dir.EndsWith(':'))
        {
            dir += "\\.";
        }

        string args = $"-d \"{dir.Replace(";", "\\;")}\"";
        if (string.IsNullOrWhiteSpace(command))
        {
            return args;
        }

        return $"{args} {shell} -NoExit -Command {Quote(command.Trim()).Replace(";", "\\;")}";
    }

    /// <summary>Arguments for a bare shell (no Windows Terminal): keep the window open after <paramref name="command"/>.</summary>
    public static string BuildShellArguments(string? command) =>
        string.IsNullOrWhiteSpace(command) ? "" : $"-NoExit -Command {Quote(command.Trim())}";

    /// <summary>Quotes one argument per the Windows command-line rules (backslashes before a quote are doubled).</summary>
    internal static string Quote(string value)
    {
        StringBuilder sb = new("\"");
        int backslashes = 0;
        foreach (char c in value)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
            }

            backslashes = 0;
        }

        return sb.Append('\\', backslashes * 2).Append('"').ToString();
    }
}

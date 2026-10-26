namespace TrayGit.Core;

public static class SearchNavigationPolicy
{
    public static bool EnterFirstResultActions(
        int textLength, int selectionStart, int selectionLength, bool hasModifiers, bool hasResults) =>
        hasResults && !hasModifiers && selectionLength == 0 && selectionStart == textLength;
}

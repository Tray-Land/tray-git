using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class ActionCatalogTests
{
    [TestMethod]
    public void Default_toolbar_has_no_ai_tools()
    {
        List<ActionDef> toolbar = ActionCatalog.Toolbar(ActionCatalog.DefaultToolbar, []);
        CollectionAssert.AreEqual(new[] { "terminal", "folder", "remote" }, toolbar.Select(a => a.Id).ToArray());
    }

    [TestMethod]
    public void Built_in_ai_tools_run_their_cli_with_an_icon()
    {
        string[] commands = ["copilot", "claude", "codex", "gemini"];
        foreach (string command in commands)
        {
            ActionDef action = ActionCatalog.BuiltIn.Single(a => a.Command == command);
            Assert.AreEqual(ActionKind.Command, action.Kind);
            Assert.IsNotNull(action.Image);
        }
    }

    [TestMethod]
    public void Toolbar_follows_catalog_order_and_ignores_unknown_ids()
    {
        ActionDef mine = ActionCatalog.NewCustom("Aider", "aider");
        List<ActionDef> toolbar = ActionCatalog.Toolbar([mine.Id, "claude", "nope", "terminal"], [mine]);
        CollectionAssert.AreEqual(new[] { "terminal", "claude", mine.Id }, toolbar.Select(a => a.Id).ToArray());
    }

    [TestMethod]
    public void Invalid_or_clashing_custom_actions_are_dropped()
    {
        ActionDef[] custom =
        [
            new("terminal", "Hijack", ActionKind.Command, "evil"),
            new("custom:1", "", ActionKind.Command, "x"),
            new("custom:2", "No command", ActionKind.Command, " "),
            new("custom:3", "Good", ActionKind.Command, "good"),
        ];
        List<ActionDef> all = ActionCatalog.All(custom);
        Assert.AreEqual(ActionCatalog.BuiltIn.Count + 1, all.Count);
        Assert.AreEqual("Open terminal", all.Single(a => a.Id == "terminal").Name);
        Assert.IsTrue(all.Any(a => a.Id == "custom:3"));
    }

    [TestMethod]
    public void Resolve_falls_back_then_to_none()
    {
        Assert.AreEqual("claude", ActionCatalog.Resolve("claude", [], "terminal").Id);
        Assert.AreEqual("none", ActionCatalog.Resolve("none", [], "terminal").Id);
        Assert.AreEqual("folder", ActionCatalog.Resolve("custom:gone", [], "folder").Id);
        Assert.AreEqual("none", ActionCatalog.Resolve("gone", [], "gone too").Id);
    }

    [TestMethod]
    [DataRow("Aider", "Ai")]
    [DataRow("my tool", "MT")]
    [DataRow("x", "X")]
    [DataRow("  ", "?")]
    public void Badge_uses_initials(string name, string expected) =>
        Assert.AreEqual(expected, ActionCatalog.BadgeFor(name));
}

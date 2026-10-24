using TrayGit.Core;

namespace TrayGit.Tests;

[TestClass]
public sealed class HotKeyTests
{
    [TestMethod]
    public void Default_is_valid_and_formats()
    {
        Assert.IsTrue(HotKey.Default.IsValid);
        Assert.AreEqual("Ctrl+Alt+G", HotKey.Default.Format());
    }

    [TestMethod]
    [DataRow(0u, (uint)'G')]          // no modifier
    [DataRow(HotKey.Control, 0x11u)]  // key is a modifier
    [DataRow(0x100u, (uint)'G')]      // unknown modifier bit
    public void Invalid_combinations_are_rejected(uint modifiers, uint key) =>
        Assert.IsFalse(new HotKey(modifiers, key).IsValid);

    [TestMethod]
    public void Serialize_round_trips()
    {
        HotKey original = new(HotKey.Win | HotKey.Shift, 0x70);
        Assert.IsTrue(HotKey.TryParse(original.Serialize(), out HotKey parsed));
        Assert.AreEqual(original, parsed);
        Assert.AreEqual("Shift+Win+F1", parsed.Format());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("abc")]
    [DataRow("0,71")]
    [DataRow("2,71,5")]
    public void TryParse_rejects_garbage(string? text) => Assert.IsFalse(HotKey.TryParse(text, out _));
}

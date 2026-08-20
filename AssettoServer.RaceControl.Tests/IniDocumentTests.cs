using AssettoServer.RaceControl.Core.Configuration;
using NUnit.Framework;

namespace AssettoServer.RaceControl.Tests;

public sealed class IniDocumentTests
{
    [Test]
    public void ParseAndSet_AreCaseInsensitiveAndKeepSectionOrder()
    {
        var document = IniDocument.Parse("[SERVER]\nNAME=Old\n\n[RACE]\nLAPS=3\n");

        document.Set("server", "name", "New");
        document.Set("SERVER", "MAX_CLIENTS", 8);

        Assert.Multiple(() =>
        {
            Assert.That(document.Get("SERVER", "NAME"), Is.EqualTo("New"));
            Assert.That(document.Sections.Select(section => section.Name), Is.EqualTo(new[] { "SERVER", "RACE" }));
            Assert.That(document.ToString(), Does.Contain("MAX_CLIENTS=8"));
        });
    }
}

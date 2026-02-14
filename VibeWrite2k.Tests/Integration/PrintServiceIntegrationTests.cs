using System.IO;
using NUnit.Framework;
using VibePlatform.Services;

namespace VibePlatform.Tests.Integration;

[TestFixture]
public class PrintServiceIntegrationTests
{
    [Test]
    [Category("Integration")]
    public void GeneratePdf_WritesPdfHeader()
    {
        var service = new PrintService();
        using var stream = new MemoryStream();

        service.GeneratePdf("# Title\n\nHello world.", stream);

        var bytes = stream.ToArray();
        Assert.That(bytes.Length, Is.GreaterThan(4));
        var header = System.Text.Encoding.ASCII.GetString(bytes, 0, 4);
        Assert.That(header, Is.EqualTo("%PDF"));
    }
}

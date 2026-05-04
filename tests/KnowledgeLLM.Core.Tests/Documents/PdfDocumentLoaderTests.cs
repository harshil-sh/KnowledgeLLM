using FluentAssertions;
using KnowledgeLLM.Core.Documents;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Core;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Documents;

public sealed class PdfDocumentLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly PdfDocumentLoader _sut = new();

    public PdfDocumentLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a minimal single-page PDF containing <paramref name="text"/>.</summary>
    private static byte[] BuildPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842); // A4 in points
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(text, 12, new PdfPoint(50, 700), font);
        return builder.Build();
    }

    private string WritePdf(string fileName, string text)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, BuildPdf(text));
        return path;
    }

    // ── Invalid input ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LoadAsync_NullOrWhitespaceSource_ReturnsInvalidInput(string? source)
    {
        var result = await _sut.LoadAsync(source!, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // ── NotFound ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_NonExistentFilePath_ReturnsNotFound()
    {
        var result = await _sut.LoadAsync(
            Path.Combine(_tempDir, "missing.pdf"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task LoadAsync_NonExistentDirectoryPath_ReturnsNotFound()
    {
        var result = await _sut.LoadAsync(
            Path.Combine(_tempDir, "no-such-dir"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NotFound");
    }

    // ── Single file ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_SinglePdfFile_ReturnsSingleDocumentWithExtractedText()
    {
        var path = WritePdf("doc.pdf", "Hello PDF");

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Id.Should().Be(path);
        result.Value[0].Content.Should().Contain("Hello PDF");
    }

    [Fact]
    public async Task LoadAsync_MultiPagePdf_ConcatenatesAllPageText()
    {
        // Build a two-page PDF
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page1 = builder.AddPage(595, 842); // A4 in points
        page1.AddText("Page one content", 12, new PdfPoint(50, 700), font);
        var page2 = builder.AddPage(595, 842); // A4 in points
        page2.AddText("Page two content", 12, new PdfPoint(50, 700), font);
        var pdfBytes = builder.Build();

        var path = Path.Combine(_tempDir, "two-pages.pdf");
        File.WriteAllBytes(path, pdfBytes);

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Content.Should().Contain("Page one content");
        result.Value[0].Content.Should().Contain("Page two content");
    }

    // ── Directory ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_DirectoryWithPdfFiles_ReturnsOneDocumentPerFile()
    {
        WritePdf("alpha.pdf", "alpha text");
        WritePdf("beta.pdf", "beta text");
        File.WriteAllText(Path.Combine(_tempDir, "ignore.txt"), "not a pdf");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(d => d.Content.Should().NotBeEmpty());
    }

    [Fact]
    public async Task LoadAsync_DirectoryWithNoPdfFiles_ReturnsEmptyList()
    {
        File.WriteAllText(Path.Combine(_tempDir, "data.csv"), "a,b,c");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_RecursiveDirectory_LoadsNestedPdfFiles()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        WritePdf("root.pdf", "root text");
        var nested = Path.Combine(sub.FullName, "nested.pdf");
        File.WriteAllBytes(nested, BuildPdf("nested text"));

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    // ── Invalid PDF format ────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_FileExistsButNotValidPdf_ReturnsProviderError()
    {
        var path = Path.Combine(_tempDir, "corrupt.pdf");
        await File.WriteAllTextAsync(path, "this is not a PDF");

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProviderError");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_SingleFile_AlreadyCancelledToken_ReturnsCancelled()
    {
        var path = WritePdf("cancel.pdf", "some text");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.LoadAsync(path, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Cancelled");
    }

    [Fact]
    public async Task LoadAsync_Directory_AlreadyCancelledToken_ReturnsCancelled()
    {
        WritePdf("a.pdf", "content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.LoadAsync(_tempDir, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Cancelled");
    }
}

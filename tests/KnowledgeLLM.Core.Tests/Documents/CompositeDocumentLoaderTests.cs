using FluentAssertions;
using KnowledgeLLM.Core.Documents;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Core;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Documents;

public sealed class CompositeDocumentLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly CompositeDocumentLoader _sut = new(new PlainTextDocumentLoader(), new PdfDocumentLoader());

    public CompositeDocumentLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string WriteTxt(string fileName, string text)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, text);
        return path;
    }

    private string WritePdf(string fileName, string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);
        page.AddText(text, 12, new PdfPoint(50, 700), font);
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, builder.Build());
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

    [Fact]
    public async Task LoadAsync_NonExistentFilePath_ReturnsNotFound()
    {
        var result = await _sut.LoadAsync(Path.Combine(_tempDir, "missing.txt"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task LoadAsync_NonExistentDirectoryPath_ReturnsNotFound()
    {
        var result = await _sut.LoadAsync(Path.Combine(_tempDir, "no-such-dir"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task LoadAsync_UnsupportedFileExtension_ReturnsInvalidInput()
    {
        var path = Path.Combine(_tempDir, "doc.docx");
        File.WriteAllBytes(path, []);

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("INVALID_INPUT");
    }

    // ── File dispatch ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_TxtFile_ReturnsSingleDocumentWithContent()
    {
        var path = WriteTxt("hello.txt", "plain text content");

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Content.Should().Be("plain text content");
    }

    [Fact]
    public async Task LoadAsync_PdfFile_ReturnsSingleDocumentWithExtractedText()
    {
        var path = WritePdf("doc.pdf", "pdf content");

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Content.Should().Contain("pdf content");
    }

    // ── Directory merge ────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_Directory_ReturnsBothTxtAndPdfDocuments()
    {
        WriteTxt("a.txt", "text doc");
        WritePdf("b.pdf", "pdf doc");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(d => d.Content == "text doc");
        result.Value.Should().Contain(d => d.Content.Contains("pdf doc"));
    }

    [Fact]
    public async Task LoadAsync_DirectoryWithOnlyTxtFiles_ReturnsOnlyTextDocuments()
    {
        WriteTxt("a.txt", "text only");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Content.Should().Be("text only");
    }

    [Fact]
    public async Task LoadAsync_DirectoryWithOnlyPdfFiles_ReturnsOnlyPdfDocuments()
    {
        WritePdf("a.pdf", "pdf only");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Content.Should().Contain("pdf only");
    }

    [Fact]
    public async Task LoadAsync_EmptyDirectory_ReturnsEmptyList()
    {
        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ── Cancellation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_TxtFile_AlreadyCancelledToken_ReturnsCancelled()
    {
        var path = WriteTxt("cancel.txt", "content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.LoadAsync(path, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task LoadAsync_Directory_AlreadyCancelledToken_ReturnsCancelled()
    {
        WriteTxt("a.txt", "content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.LoadAsync(_tempDir, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }
}

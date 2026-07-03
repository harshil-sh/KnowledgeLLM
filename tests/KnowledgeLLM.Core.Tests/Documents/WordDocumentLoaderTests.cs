using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Wp = DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using KnowledgeLLM.Core.Documents;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Documents;

public sealed class WordDocumentLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly WordDocumentLoader _sut = new();

    public WordDocumentLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteDocx(string fileName, params string[] paragraphs)
    {
        var path = Path.Combine(_tempDir, fileName);
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Wp.Document(new Wp.Body(paragraphs.Select(text =>
            new Wp.Paragraph(new Wp.Run(new Wp.Text(text)))).ToArray()));
        return path;
    }

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
    public async Task LoadAsync_SingleDocxFile_ReturnsDocumentWithExtractedText()
    {
        var path = WriteDocx("doc.docx", "Hello Word", "Second paragraph");

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Id.Should().Be(path);
        result.Value[0].Content.Should().Contain("Hello Word");
        result.Value[0].Content.Should().Contain("Second paragraph");
        result.Value[0].SafeMetadata["source"].Should().Be("docx");
    }

    [Fact]
    public async Task LoadAsync_DirectoryWithDocxFiles_ReturnsOneDocumentPerFile()
    {
        WriteDocx("alpha.docx", "alpha text");
        WriteDocx("beta.docx", "beta text");
        File.WriteAllText(Path.Combine(_tempDir, "ignore.txt"), "not docx");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(d => d.Content.Contains("alpha text"));
        result.Value.Should().Contain(d => d.Content.Contains("beta text"));
    }

    [Fact]
    public async Task LoadAsync_MissingPath_ReturnsNotFound()
    {
        var result = await _sut.LoadAsync(Path.Combine(_tempDir, "missing.docx"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task LoadAsync_CorruptDocx_ReturnsProviderError()
    {
        var path = Path.Combine(_tempDir, "corrupt.docx");
        File.WriteAllText(path, "not a real docx");

        var result = await _sut.LoadAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("PROVIDER_ERROR");
    }

    [Fact]
    public async Task LoadAsync_AlreadyCancelledToken_ReturnsCancelled()
    {
        var path = WriteDocx("cancel.docx", "content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.LoadAsync(path, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }
}

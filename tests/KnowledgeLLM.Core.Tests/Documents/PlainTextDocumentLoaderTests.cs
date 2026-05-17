using FluentAssertions;
using KnowledgeLLM.Core.Documents;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Documents;

public sealed class PlainTextDocumentLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly PlainTextDocumentLoader _sut = new();

    public PlainTextDocumentLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // --- InvalidInput ---

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

    // --- NotFound ---

    [Fact]
    public async Task LoadAsync_NonExistentPath_ReturnsNotFound()
    {
        var result = await _sut.LoadAsync(Path.Combine(_tempDir, "missing.txt"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NOT_FOUND");
    }

    // --- Single file ---

    [Fact]
    public async Task LoadAsync_SingleFile_ReturnsSingleDocumentWithCorrectContent()
    {
        var file = Path.Combine(_tempDir, "doc.txt");
        await File.WriteAllTextAsync(file, "hello world");

        var result = await _sut.LoadAsync(file, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Id.Should().Be(file);
        result.Value[0].Content.Should().Be("hello world");
    }

    // --- Directory ---

    [Fact]
    public async Task LoadAsync_DirectoryWithTxtFiles_ReturnsOneDocumentPerFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.txt"), "beta");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ignore.csv"), "csv");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().AllSatisfy(d => d.Content.Should().NotBeEmpty());
    }

    [Fact]
    public async Task LoadAsync_DirectoryWithNoTxtFiles_ReturnsEmptyList()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "data.csv"), "x,y");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_RecursiveDirectory_LoadsNestedTxtFiles()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(sub.FullName, "nested.txt"), "nested");

        var result = await _sut.LoadAsync(_tempDir, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    // --- Cancellation ---

    [Fact]
    public async Task LoadAsync_CancelledToken_ReturnsCancelled()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.LoadAsync(_tempDir, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CANCELLED");
    }
}

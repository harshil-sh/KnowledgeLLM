using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Documents;

/// <summary>
/// An <see cref="IDocumentLoader"/> that dispatches to <see cref="PlainTextDocumentLoader"/> or
/// <see cref="PdfDocumentLoader"/> based on file extension, and merges both when loading a directory.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Single <c>.txt</c> file → <see cref="PlainTextDocumentLoader"/></item>
///   <item>Single <c>.pdf</c> file → <see cref="PdfDocumentLoader"/></item>
///   <item>Any other file extension → <c>INVALID_INPUT</c> failure</item>
///   <item>Directory → loads <c>*.txt</c> and <c>*.pdf</c> files and merges the results</item>
/// </list>
/// </remarks>
public sealed class CompositeDocumentLoader : IDocumentLoader
{
    private readonly PlainTextDocumentLoader _textLoader;
    private readonly PdfDocumentLoader _pdfLoader;

    /// <summary>
    /// Initialises a new <see cref="CompositeDocumentLoader"/> with the two underlying loaders.
    /// </summary>
    /// <param name="textLoader">Loader for plain-text (<c>.txt</c>) files.</param>
    /// <param name="pdfLoader">Loader for PDF (<c>.pdf</c>) files.</param>
    public CompositeDocumentLoader(PlainTextDocumentLoader textLoader, PdfDocumentLoader pdfLoader)
    {
        _textLoader = textLoader;
        _pdfLoader = pdfLoader;
    }

    /// <summary>
    /// Loads documents from <paramref name="source"/>, dispatching by extension for files
    /// and merging all <c>.txt</c> and <c>.pdf</c> results for directories.
    /// </summary>
    /// <param name="source">File path or directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<IReadOnlyList<Document>>> LoadAsync(string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source))
            return ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.InvalidInput("source must not be null or whitespace."));

        if (File.Exists(source))
        {
            var ext = Path.GetExtension(source).ToLowerInvariant();
            return ext switch
            {
                ".txt" => await _textLoader.LoadAsync(source, ct),
                ".pdf" => await _pdfLoader.LoadAsync(source, ct),
                _ => ChainResult<IReadOnlyList<Document>>.Failure(
                    WeaveLLMError.InvalidInput($"Unsupported file type '{ext}'. Only .txt and .pdf files are supported."))
            };
        }

        if (Directory.Exists(source))
        {
            var textResult = await _textLoader.LoadAsync(source, ct);
            if (!textResult.IsSuccess) return textResult;

            var pdfResult = await _pdfLoader.LoadAsync(source, ct);
            if (!pdfResult.IsSuccess) return pdfResult;

            IReadOnlyList<Document> combined = textResult.Value
                .Concat(pdfResult.Value)
                .ToList()
                .AsReadOnly();
            return ChainResult<IReadOnlyList<Document>>.Success(combined);
        }

        return ChainResult<IReadOnlyList<Document>>.Failure(
            new WeaveLLMError($"Path not found: {source}", "NotFound", null));
    }
}

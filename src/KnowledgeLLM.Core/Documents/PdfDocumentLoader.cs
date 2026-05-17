using System.Text;
using UglyToad.PdfPig;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Documents;

/// <summary>Loads PDF documents from a single file path or a directory of <c>.pdf</c> files.</summary>
public sealed class PdfDocumentLoader : IDocumentLoader
{
    /// <summary>
    /// Loads documents from <paramref name="source"/>.
    /// <list type="bullet">
    ///   <item>File path — loads the file as one <see cref="Document"/> with Id set to the full path.</item>
    ///   <item>Directory path — loads all <c>*.pdf</c> files recursively, one <see cref="Document"/> per file.</item>
    /// </list>
    /// </summary>
    /// <param name="source">File or directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<ChainResult<IReadOnlyList<Document>>> LoadAsync(string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source))
            return Task.FromResult(ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.InvalidInput("source must not be null or whitespace.")));

        try
        {
            if (File.Exists(source))
            {
                ct.ThrowIfCancellationRequested();
                var (text, pages) = ExtractText(source);
                var meta = new Dictionary<string, string>
                {
                    ["pages"] = pages.ToString(),
                    ["source"] = "pdf",
                };
                IReadOnlyList<Document> single = [new Document(source, text, meta)];
                return Task.FromResult(ChainResult<IReadOnlyList<Document>>.Success(single));
            }

            if (Directory.Exists(source))
            {
                var files = Directory.GetFiles(source, "*.pdf", SearchOption.AllDirectories);
                var documents = new List<Document>(files.Length);

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var (text, pages) = ExtractText(file);
                    var meta = new Dictionary<string, string>
                    {
                        ["pages"] = pages.ToString(),
                        ["source"] = "pdf",
                    };
                    documents.Add(new Document(file, text, meta));
                }

                return Task.FromResult(ChainResult<IReadOnlyList<Document>>.Success(
                    (IReadOnlyList<Document>)documents.AsReadOnly()));
            }

            return Task.FromResult(ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.NotFound($"Path not found: {source}")));
        }
        catch (OperationCanceledException ex)
        {
            return Task.FromResult(ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.Cancelled("Operation was cancelled.", ex)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.ProviderError("PdfPig", ex.Message, ex)));
        }
    }

    private static (string Text, int Pages) ExtractText(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var sb = new StringBuilder();
        var pages = 0;
        foreach (var page in pdf.GetPages())
        {
            sb.Append(page.Text);
            pages++;
        }
        return (sb.ToString(), pages);
    }
}

using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Wp = DocumentFormat.OpenXml.Wordprocessing;
using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Documents;

/// <summary>Loads Word documents from a single file path or a directory of <c>.docx</c> files.</summary>
public sealed class WordDocumentLoader : IDocumentLoader
{
    /// <summary>
    /// Loads documents from <paramref name="source"/>.
    /// <list type="bullet">
    ///   <item>File path — loads the file as one <see cref="Document"/> with Id set to the full path.</item>
    ///   <item>Directory path — loads all <c>*.docx</c> files recursively, one <see cref="Document"/> per file.</item>
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
                var text = ExtractText(source);
                var meta = new Dictionary<string, string>
                {
                    ["source"] = "docx",
                };
                IReadOnlyList<Document> single = [new Document(source, text, meta)];
                return Task.FromResult(ChainResult<IReadOnlyList<Document>>.Success(single));
            }

            if (Directory.Exists(source))
            {
                var files = Directory.GetFiles(source, "*.docx", SearchOption.AllDirectories);
                var documents = new List<Document>(files.Length);

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var text = ExtractText(file);
                    var meta = new Dictionary<string, string>
                    {
                        ["source"] = "docx",
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
                WeaveLLMError.ProviderError("OpenXml", ex.Message, ex)));
        }
    }

    private static string ExtractText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var paragraph in body.Elements<Wp.Paragraph>())
        {
            var paragraphText = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(paragraphText))
                sb.AppendLine(paragraphText);
        }

        return sb.ToString().TrimEnd();
    }
}

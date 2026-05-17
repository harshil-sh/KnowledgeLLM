using WeaveLLM.Core.Models;

namespace KnowledgeLLM.Core.Documents;

/// <summary>Loads plain-text documents from a single file path or a directory of <c>.txt</c> files.</summary>
public sealed class PlainTextDocumentLoader : IDocumentLoader
{
    /// <summary>
    /// Loads documents from <paramref name="source"/>.
    /// <list type="bullet">
    ///   <item>File path — loads the file as one <see cref="Document"/> with Id set to the full path.</item>
    ///   <item>Directory path — loads all <c>*.txt</c> files recursively, one <see cref="Document"/> per file.</item>
    /// </list>
    /// </summary>
    /// <param name="source">File or directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ChainResult<IReadOnlyList<Document>>> LoadAsync(string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source))
            return ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.InvalidInput("source must not be null or whitespace."));

        try
        {
            if (File.Exists(source))
            {
                var content = await File.ReadAllTextAsync(source, ct);
                IReadOnlyList<Document> single = new[] { new Document(source, content) };
                return ChainResult<IReadOnlyList<Document>>.Success(single);
            }

            if (Directory.Exists(source))
            {
                var files = Directory.GetFiles(source, "*.txt", SearchOption.AllDirectories);
                var documents = new List<Document>(files.Length);

                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = await File.ReadAllTextAsync(file, ct);
                    documents.Add(new Document(file, content));
                }

                return ChainResult<IReadOnlyList<Document>>.Success(documents.AsReadOnly());
            }

            return ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.NotFound($"Path not found: {source}"));
        }
        catch (OperationCanceledException ex)
        {
            return ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.Cancelled("Operation was cancelled.", ex));
        }
        catch (Exception ex)
        {
            return ChainResult<IReadOnlyList<Document>>.Failure(
                WeaveLLMError.ProviderError("FileSystem", ex.Message, ex));
        }
    }
}

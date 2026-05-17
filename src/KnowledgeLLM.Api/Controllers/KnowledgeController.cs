using KnowledgeLLM.Core.Pipeline;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeLLM.Api.Controllers;

/// <summary>Exposes the RAG pipeline over HTTP.</summary>
[ApiController]
[Route("api/knowledge")]
public sealed class KnowledgeController : ControllerBase
{
    private readonly IRagPipeline _pipeline;

    /// <summary>Initialises the controller with the pipeline.</summary>
    public KnowledgeController(IRagPipeline pipeline) => _pipeline = pipeline;

    /// <summary>Indexes all documents found at the given source path.</summary>
    [HttpPost("index")]
    public async Task<IActionResult> IndexAsync([FromBody] IndexRequest request, CancellationToken ct)
    {
        var result = await _pipeline.IndexAsync(request.Source, ct);

        if (result.IsSuccess)
            return Ok(new IndexResponse(result.Value, request.Source));

        return MapError(result.Error.Code, result.Error.Message);
    }

    /// <summary>Answers a question by retrieving relevant document chunks.</summary>
    [HttpPost("ask")]
    public async Task<IActionResult> AskAsync([FromBody] AskRequest request, CancellationToken ct)
    {
        var result = await _pipeline.AskAsync(request.Question, request.TopK, ct);

        if (result.IsSuccess)
        {
            var sources = result.Value.Sources
                .Select(s => new SourceDto(s.Chunk.Id, s.Chunk.DocumentId, s.Chunk.Content, s.Score))
                .ToList();
            return Ok(new AskResponse(result.Value.Answer, sources));
        }

        return MapError(result.Error.Code, result.Error.Message);
    }

    /// <summary>
    /// Streams answer tokens as Server-Sent Events.
    /// Each token is sent as <c>data: {token}\n\n</c>; completion is signalled with <c>data: [DONE]\n\n</c>.
    /// </summary>
    [HttpPost("ask/stream")]
    public async Task AskStreamAsync([FromBody] AskRequest request, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            await foreach (var token in _pipeline.AskStreamAsync(request.Question, request.TopK, ct))
            {
                await Response.WriteAsync($"data: {token}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            await Response.WriteAsync("data: [DONE]\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Normal client disconnect — no further writes needed.
        }
    }

    private IActionResult MapError(string code, string message)
    {
        var body = new ErrorResponse(code, message);
        return code is "INVALID_INPUT" or "INVALID_CONFIGURATION" or "NOT_FOUND"
            ? BadRequest(body)
            : StatusCode(500, body);
    }
}

/// <summary>Request body for the index endpoint.</summary>
public sealed record IndexRequest(string Source);

/// <summary>Response body for the index endpoint.</summary>
public sealed record IndexResponse(int ChunksIndexed, string Source);

/// <summary>Request body for the ask endpoint.</summary>
public sealed record AskRequest(string Question, int TopK = 5);

/// <summary>Response body for the ask endpoint.</summary>
public sealed record AskResponse(string Answer, IReadOnlyList<SourceDto> Sources);

/// <summary>A single retrieved source chunk.</summary>
public sealed record SourceDto(string ChunkId, string DocumentId, string Content, float Score);

/// <summary>Error response body.</summary>
public sealed record ErrorResponse(string Code, string Message);

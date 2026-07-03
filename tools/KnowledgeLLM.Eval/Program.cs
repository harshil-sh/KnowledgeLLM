using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

var options = ParseArgs(args);
if (options.ShowHelp)
{
    PrintUsage();
    return 0;
}

var dataset = await LoadDatasetAsync(options.DatasetPath);
using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };

var results = new List<QuestionEvaluation>();
foreach (var question in dataset.Questions)
{
    using var response = await httpClient.PostAsJsonAsync(
        "/api/knowledge/ask",
        new AskRequest(question.Question, options.TopK));

    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        results.Add(QuestionEvaluation.Failed(question.Id, question.Question, $"HTTP {(int)response.StatusCode}: {error}"));
        continue;
    }

    var answer = await response.Content.ReadFromJsonAsync<AskResponse>()
        ?? throw new InvalidOperationException("The /api/knowledge/ask response body was empty.");

    var retrievedSources = answer.Sources.Select(s => s.DocumentId).ToArray();
    var hasExpectedSource = question.ExpectedSources.Any(expected =>
        answer.Sources.Any(source => ContainsNormalized(source.DocumentId, expected) || ContainsNormalized(source.ChunkId, expected)));
    var sourceRelevance = answer.Sources.Count == 0 ? 0 : answer.Sources.Average(s => s.Score);
    var groundingMatches = question.ExpectedAnswerContains.Count(expected => ContainsNormalized(answer.Answer, expected));
    var isGrounded = groundingMatches == question.ExpectedAnswerContains.Count;

    results.Add(new QuestionEvaluation(
        question.Id,
        question.Question,
        hasExpectedSource,
        sourceRelevance,
        isGrounded,
        groundingMatches,
        question.ExpectedAnswerContains.Count,
        retrievedSources,
        null));
}

var successful = results.Where(r => r.Error is null).ToArray();
var retrievalHitRate = successful.Length == 0 ? 0 : successful.Count(r => r.HasExpectedSource) / (double)successful.Length;
var averageSourceRelevance = successful.Length == 0 ? 0 : successful.Average(r => r.SourceRelevance);
var groundingValidationRate = successful.Length == 0 ? 0 : successful.Count(r => r.IsGrounded) / (double)successful.Length;

var report = new EvaluationReport(
    dataset.Version,
    results.Count,
    results.Count(r => r.Error is not null),
    retrievalHitRate,
    averageSourceRelevance,
    groundingValidationRate,
    results);

var reportJson = JsonSerializer.Serialize(report, CreateJsonOptions());
Console.WriteLine(reportJson);

return report.FailedQuestions == 0 ? 0 : 1;

static async Task<EvaluationDataset> LoadDatasetAsync(string path)
{
    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<EvaluationDataset>(stream, CreateJsonOptions())
        ?? throw new InvalidOperationException($"Evaluation dataset '{path}' is empty or invalid.");
}

static EvaluationOptions ParseArgs(string[] args)
{
    var options = new EvaluationOptions();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--dataset" when i + 1 < args.Length:
                options.DatasetPath = args[++i];
                break;
            case "--base-url" when i + 1 < args.Length:
                options.BaseUrl = args[++i].TrimEnd('/');
                break;
            case "--top-k" when i + 1 < args.Length && int.TryParse(args[++i], out var topK):
                options.TopK = topK;
                break;
            case "--help":
            case "-h":
                options.ShowHelp = true;
                break;
            default:
                throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
        }
    }

    if (options.TopK < 1)
        throw new ArgumentOutOfRangeException(nameof(options.TopK), "--top-k must be >= 1.");

    return options;
}

static bool ContainsNormalized(string value, string expected) =>
    value.Contains(expected, StringComparison.OrdinalIgnoreCase)
    || value.Replace('\\', '/').Contains(expected.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

static void PrintUsage()
{
    Console.WriteLine("""
KnowledgeLLM RAG evaluation runner

Usage:
  dotnet run --project tools/KnowledgeLLM.Eval -- [options]

Options:
  --dataset <path>    Evaluation dataset path. Default: eval/questions.json
  --base-url <url>    Running KnowledgeLLM API base URL. Default: http://localhost:5000
  --top-k <n>         Number of chunks to request per question. Default: 5
  -h, --help          Show this help text.
""");
}

static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

sealed class EvaluationOptions
{
    public string DatasetPath { get; set; } = "eval/questions.json";
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public int TopK { get; set; } = 5;
    public bool ShowHelp { get; set; }
}

sealed record EvaluationDataset(int Version, string Description, IReadOnlyList<EvaluationQuestion> Questions);
sealed record EvaluationQuestion(string Id, string Question, IReadOnlyList<string> ExpectedSources, IReadOnlyList<string> ExpectedAnswerContains);
sealed record AskRequest(string Question, int TopK);
sealed record AskResponse(string Answer, IReadOnlyList<SourceDto> Sources);
sealed record SourceDto(string ChunkId, string DocumentId, string Content, float Score);
sealed record EvaluationReport(int DatasetVersion, int TotalQuestions, int FailedQuestions, double RetrievalHitRate, double AverageSourceRelevance, double GroundingValidationRate, IReadOnlyList<QuestionEvaluation> Questions);
sealed record QuestionEvaluation(string Id, string Question, bool HasExpectedSource, double SourceRelevance, bool IsGrounded, int GroundingMatches, int GroundingExpected, IReadOnlyList<string> RetrievedSources, string? Error)
{
    public static QuestionEvaluation Failed(string id, string question, string error) => new(id, question, false, 0, false, 0, 0, Array.Empty<string>(), error);
}

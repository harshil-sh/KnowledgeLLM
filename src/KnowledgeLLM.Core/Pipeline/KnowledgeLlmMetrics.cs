using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace KnowledgeLLM.Core.Pipeline;

/// <summary>Records operational metrics for KnowledgeLLM pipeline stages.</summary>
public sealed class KnowledgeLlmMetrics
{
    /// <summary>The meter name used for KnowledgeLLM pipeline metrics.</summary>
    public const string MeterName = "KnowledgeLLM.Pipeline";

    private readonly Histogram<double> _indexingLatency;
    private readonly Histogram<double> _retrievalLatency;
    private readonly Histogram<double> _llmResponseLatency;

    /// <summary>Initialises pipeline metric instruments from the supplied meter.</summary>
    public KnowledgeLlmMetrics(Meter meter)
    {
        _indexingLatency = meter.CreateHistogram<double>(
            "knowledgellm.indexing.duration",
            unit: "ms",
            description: "Duration of document indexing requests.");
        _retrievalLatency = meter.CreateHistogram<double>(
            "knowledgellm.retrieval.duration",
            unit: "ms",
            description: "Duration of retrieval work before answer generation.");
        _llmResponseLatency = meter.CreateHistogram<double>(
            "knowledgellm.llm_response.duration",
            unit: "ms",
            description: "Duration of LLM response generation.");
    }

    /// <summary>Records document indexing latency.</summary>
    public void RecordIndexingLatency(long startTimestamp, bool success) =>
        _indexingLatency.Record(ElapsedMilliseconds(startTimestamp), new KeyValuePair<string, object?>("success", success));

    /// <summary>Records retrieval latency.</summary>
    public void RecordRetrievalLatency(long startTimestamp, bool success) =>
        _retrievalLatency.Record(ElapsedMilliseconds(startTimestamp), new KeyValuePair<string, object?>("success", success));

    /// <summary>Records LLM response latency.</summary>
    public void RecordLlmResponseLatency(long startTimestamp, bool success, string mode) =>
        _llmResponseLatency.Record(
            ElapsedMilliseconds(startTimestamp),
            new KeyValuePair<string, object?>("success", success),
            new KeyValuePair<string, object?>("mode", mode));

    private static double ElapsedMilliseconds(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
}

# RAG Evaluation

`eval/questions.json` contains benchmark questions for checking KnowledgeLLM's retrieval and grounded-answer behavior against the repository documentation.

## Run the evaluation

Start and index a local KnowledgeLLM API first, then run:

```bash
dotnet run --project tools/KnowledgeLLM.Eval -- \
  --dataset eval/questions.json \
  --base-url http://localhost:5000 \
  --top-k 5
```

The runner calls `POST /api/knowledge/ask` for each benchmark question and prints a JSON report with:

- `retrievalHitRate`: the share of successful questions whose returned source chunk IDs or document IDs match one of the expected sources.
- `averageSourceRelevance`: the average retrieval score across returned sources.
- `groundingValidationRate`: the share of successful answers that contain all expected answer terms.

A non-zero exit code means at least one API request failed. Metric values are reported for inspection so they can be tracked over time in CI or manual evaluation runs.

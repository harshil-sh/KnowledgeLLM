using FluentAssertions;
using KnowledgeLLM.Core.Chunking;
using KnowledgeLLM.Core.Pipeline;
using KnowledgeLLM.Core.Retrieval;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Pipeline;

public sealed class PromptBuilderTests
{
    // ---- Helpers ---------------------------------------------------------------

    private static TextChunk MakeChunk(string content, int index = 0) =>
        new($"doc_{index}", "doc", content, index);

    private static RetrievalResult MakeSource(string content, int index = 0) =>
        new(MakeChunk(content, index), Score: 0.9f);

    // ---- Guard: invalid question ----------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildRagPrompt_NullOrWhitespaceQuestion_ThrowsArgumentException(string? question)
    {
        var sources = new[] { MakeSource("some context") };

        var act = () => PromptBuilder.BuildRagPrompt(question!, sources);

        act.Should().Throw<ArgumentException>()
           .WithParameterName("question");
    }

    // ---- Guard: invalid sources -----------------------------------------------

    [Fact]
    public void BuildRagPrompt_NullSources_ThrowsArgumentException()
    {
        var act = () => PromptBuilder.BuildRagPrompt("What is AI?", null!);

        act.Should().Throw<ArgumentException>()
           .WithParameterName("sources");
    }

    [Fact]
    public void BuildRagPrompt_EmptySources_ThrowsArgumentException()
    {
        var act = () => PromptBuilder.BuildRagPrompt("What is AI?", Array.Empty<RetrievalResult>());

        act.Should().Throw<ArgumentException>()
           .WithParameterName("sources");
    }

    // ---- Structural content checks --------------------------------------------

    [Fact]
    public void BuildRagPrompt_ValidInputs_ContainsSystemInstruction()
    {
        var prompt = PromptBuilder.BuildRagPrompt("What is AI?", new[] { MakeSource("context") });

        prompt.Should().Contain("You are a helpful assistant.");
    }

    [Fact]
    public void BuildRagPrompt_ValidInputs_InstructsModelToUseOnlyContext()
    {
        var prompt = PromptBuilder.BuildRagPrompt("What is AI?", new[] { MakeSource("context") });

        prompt.Should().Contain("ONLY the context");
    }

    [Fact]
    public void BuildRagPrompt_ValidInputs_ContainsContextHeader()
    {
        var prompt = PromptBuilder.BuildRagPrompt("What is AI?", new[] { MakeSource("context") });

        prompt.Should().Contain("CONTEXT:");
    }

    [Fact]
    public void BuildRagPrompt_ValidInputs_ContainsQuestionSection()
    {
        const string question = "What is the meaning of life?";

        var prompt = PromptBuilder.BuildRagPrompt(question, new[] { MakeSource("context") });

        prompt.Should().Contain($"QUESTION: {question}");
    }

    [Fact]
    public void BuildRagPrompt_ValidInputs_ContainsAnswerMarker()
    {
        var prompt = PromptBuilder.BuildRagPrompt("What is AI?", new[] { MakeSource("context") });

        prompt.Should().Contain("ANSWER:");
    }

    // ---- Context numbering and ordering ---------------------------------------

    [Fact]
    public void BuildRagPrompt_SingleSource_ContextNumberedWithBracketOne()
    {
        var prompt = PromptBuilder.BuildRagPrompt("question?", new[] { MakeSource("the sky is blue") });

        prompt.Should().Contain("[1] the sky is blue");
    }

    [Fact]
    public void BuildRagPrompt_MultipleSources_AllContextsNumberedInOrder()
    {
        var sources = new[]
        {
            MakeSource("first context",  index: 0),
            MakeSource("second context", index: 1),
            MakeSource("third context",  index: 2),
        };

        var prompt = PromptBuilder.BuildRagPrompt("question?", sources);

        prompt.Should().Contain("[1] first context");
        prompt.Should().Contain("[2] second context");
        prompt.Should().Contain("[3] third context");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void BuildRagPrompt_NSources_ContainsNBracketedEntries(int count)
    {
        var sources = Enumerable.Range(0, count)
            .Select(i => MakeSource($"context {i}", i))
            .ToArray();

        var prompt = PromptBuilder.BuildRagPrompt("question?", sources);

        for (var i = 1; i <= count; i++)
            prompt.Should().Contain($"[{i}]");
    }

    // ---- Section ordering -----------------------------------------------------

    [Fact]
    public void BuildRagPrompt_ContextHeader_AppearsBefore_QuestionSection()
    {
        var prompt = PromptBuilder.BuildRagPrompt("question?", new[] { MakeSource("ctx") });

        prompt.IndexOf("CONTEXT:", StringComparison.Ordinal)
              .Should().BeLessThan(prompt.IndexOf("QUESTION:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRagPrompt_QuestionSection_AppearsBefore_AnswerMarker()
    {
        var prompt = PromptBuilder.BuildRagPrompt("question?", new[] { MakeSource("ctx") });

        prompt.IndexOf("QUESTION:", StringComparison.Ordinal)
              .Should().BeLessThan(prompt.IndexOf("ANSWER:", StringComparison.Ordinal));
    }
}

using FluentAssertions;
using KnowledgeLLM.Api.Controllers;
using KnowledgeLLM.Api.Validation;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Validation;

// ---------------------------------------------------------------------------
// IndexRequestValidator
// ---------------------------------------------------------------------------

public sealed class IndexRequestValidatorTests
{
    private readonly IndexRequestValidator _sut = new();

    [Fact]
    public void Validate_NullSource_Fails()
    {
        var request = new IndexRequest(null!);

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Source");
    }

    [Fact]
    public void Validate_ValidSource_Passes()
    {
        var request = new IndexRequest("C:\\docs\\knowledge");

        var result = _sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(500, true)]   // exactly at the limit — valid
    [InlineData(501, false)]  // one over — invalid
    public void Validate_SourceLength_RespectsMaximum(int length, bool expectedValid)
    {
        var source = new string('x', length);
        var request = new IndexRequest(source);

        var result = _sut.Validate(request);

        result.IsValid.Should().Be(expectedValid);
    }
}

// ---------------------------------------------------------------------------
// AskRequestValidator
// ---------------------------------------------------------------------------

public sealed class AskRequestValidatorTests
{
    private readonly AskRequestValidator _sut = new();

    [Fact]
    public void Validate_EmptyQuestion_Fails()
    {
        var request = new AskRequest(string.Empty, TopK: 5);

        var result = _sut.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Question");
    }

    [Theory]
    [InlineData(2, false)]   // below minimum length — invalid
    [InlineData(3, true)]    // exactly at minimum — valid
    [InlineData(1000, true)] // exactly at maximum — valid
    [InlineData(1001, false)] // one over maximum — invalid
    public void Validate_QuestionLength_RespectsBoundaries(int length, bool expectedValid)
    {
        var question = new string('q', length);
        var request = new AskRequest(question, TopK: 5);

        var result = _sut.Validate(request);

        result.IsValid.Should().Be(expectedValid);
    }

    [Theory]
    [InlineData(0, false)]  // below minimum — invalid
    [InlineData(1, true)]   // lower boundary — valid
    [InlineData(20, true)]  // upper boundary — valid
    [InlineData(21, false)] // above maximum — invalid
    public void Validate_TopK_RespectsBoundaries(int topK, bool expectedValid)
    {
        var request = new AskRequest("What is this document about?", TopK: topK);

        var result = _sut.Validate(request);

        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var request = new AskRequest("What is the main topic of this document?", TopK: 5);

        var result = _sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }
}

using FluentValidation;
using KnowledgeLLM.Api.Controllers;

namespace KnowledgeLLM.Api.Validation;

/// <summary>
/// Validates <see cref="IndexRequest"/> at the controller boundary before the request
/// reaches the pipeline.
/// </summary>
public sealed class IndexRequestValidator : AbstractValidator<IndexRequest>
{
    /// <summary>Initialises validation rules for <see cref="IndexRequest"/>.</summary>
    public IndexRequestValidator()
    {
        RuleFor(r => r.Source)
            .NotNull().WithMessage("Source must not be null.")
            .NotEmpty().WithMessage("Source must not be empty.")
            .MaximumLength(500).WithMessage("Source must not exceed 500 characters.");
    }
}

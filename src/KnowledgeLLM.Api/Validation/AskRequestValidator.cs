using FluentValidation;
using KnowledgeLLM.Api.Controllers;

namespace KnowledgeLLM.Api.Validation;

/// <summary>
/// Validates <see cref="AskRequest"/> at the controller boundary before the request
/// reaches the pipeline.
/// </summary>
public sealed class AskRequestValidator : AbstractValidator<AskRequest>
{
    /// <summary>Initialises validation rules for <see cref="AskRequest"/>.</summary>
    public AskRequestValidator()
    {
        RuleFor(r => r.Question)
            .NotNull().WithMessage("Question must not be null.")
            .NotEmpty().WithMessage("Question must not be empty.")
            .MinimumLength(3).WithMessage("Question must be at least 3 characters.")
            .MaximumLength(1000).WithMessage("Question must not exceed 1000 characters.");

        RuleFor(r => r.TopK)
            .InclusiveBetween(1, 20).WithMessage("TopK must be between 1 and 20 inclusive.");
    }
}

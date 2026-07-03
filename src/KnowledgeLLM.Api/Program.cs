using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using HealthChecks.UI.Client;
using KnowledgeLLM.Api.HealthChecks;
using KnowledgeLLM.Api.Middleware;
using KnowledgeLLM.Api.Validation;
using KnowledgeLLM.Core.Configuration;
using KnowledgeLLM.Core.Extensions;
using KnowledgeLLM.Core.Pipeline;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage);
            var message = string.Join(" ", errors);
            return new BadRequestObjectResult(new { code = "INVALID_INPUT", message });
        };
    });

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<AskRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddKnowledgeLLM(builder.Configuration);

// ── Health checks ────────────────────────────────────────────────────────────
var pgOpts = builder.Configuration
    .GetSection($"{KnowledgeLLMOptions.SectionName}:PgVector")
    .Get<PgVectorOptions>() ?? new PgVectorOptions();

var hcBuilder = builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<OpenAiConnectivityCheck>("openai", tags: ["ready"]);

if (pgOpts.Enabled && !string.IsNullOrWhiteSpace(pgOpts.ConnectionString))
{
    hcBuilder.AddNpgSql(
        pgOpts.ConnectionString,
        name: "postgres",
        tags: ["ready"],
        failureStatus: HealthStatus.Degraded);
}

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(KnowledgeLlmMetrics.MeterName);

        if (builder.Environment.IsDevelopment())
            metrics.AddConsoleExporter();
        else
            metrics.AddOtlpExporter();
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource(KnowledgeLLM.Core.Extensions.ServiceCollectionExtensions.PipelineActivitySourceName);

        if (builder.Environment.IsDevelopment())
            tracing.AddConsoleExporter();
        else
            tracing.AddOtlpExporter();
    });

var rlOpts = builder.Configuration
    .GetSection($"{KnowledgeLLMOptions.SectionName}:RateLimit")
    .Get<RateLimitOptions>() ?? new RateLimitOptions();

builder.Services.AddRateLimiter(limiter =>
{
    static string PartitionKey(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-Api-Key", out var key) && !string.IsNullOrWhiteSpace(key)
            ? key.ToString()
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    limiter.AddPolicy<string>("index-limit", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rlOpts.IndexPermitLimit,
                Window = TimeSpan.FromSeconds(rlOpts.WindowSeconds),
                QueueLimit = rlOpts.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));

    limiter.AddPolicy<string>("ask-limit", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rlOpts.AskPermitLimit,
                Window = TimeSpan.FromSeconds(rlOpts.WindowSeconds),
                QueueLimit = rlOpts.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));

    limiter.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.HttpContext.Response.ContentType = "application/json";
        ctx.HttpContext.Response.Headers.Append("Retry-After", rlOpts.WindowSeconds.ToString());

        var body = $$"""{"code":"RATE_LIMIT_EXCEEDED","message":"Too many requests. Retry after {{rlOpts.WindowSeconds}} seconds.","retryAfterSeconds":{{rlOpts.WindowSeconds}}}""";
        await ctx.HttpContext.Response.WriteAsync(body, ct);
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ApiKeyMiddleware>();
app.UseSerilogRequestLogging(opts =>
{
    opts.EnrichDiagnosticContext = (diag, httpCtx) =>
    {
        diag.Set("RequestPath", httpCtx.Request.Path);
        diag.Set("StatusCode", httpCtx.Response.StatusCode);
        diag.Set("QueryString", httpCtx.Request.QueryString);
    };
});
app.UseRateLimiter();

// ── Health check endpoints ───────────────────────────────────────────────────
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready")
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapControllers();
app.Run();

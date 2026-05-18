using System.Threading.RateLimiting;
using KnowledgeLLM.Api.Middleware;
using KnowledgeLLM.Core.Configuration;
using KnowledgeLLM.Core.Extensions;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddKnowledgeLLM(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource(ServiceCollectionExtensions.PipelineActivitySourceName);

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
app.UseRateLimiter();
app.MapControllers();
app.Run();

using System.Text;
using FluentAssertions;
using KnowledgeLLM.Api.Middleware;
using KnowledgeLLM.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace KnowledgeLLM.Core.Tests.Middleware;

public sealed class ApiKeyMiddlewareTests
{
    // ---- Builder helpers -------------------------------------------------------

    private static ApiKeyMiddleware BuildSut(string configuredKey, RequestDelegate next)
    {
        var options = Options.Create(new KnowledgeLLMOptions
        {
            Api = new ApiOptions { ApiKey = configuredKey }
        });
        var logger = Substitute.For<ILogger<ApiKeyMiddleware>>();
        return new ApiKeyMiddleware(next, options, logger);
    }

    private static DefaultHttpContext BuildContext(
        string method = "POST",
        string path = "/api/knowledge/index",
        string? apiKeyHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path   = path;
        ctx.Response.Body  = new MemoryStream();
        if (apiKeyHeader is not null)
            ctx.Request.Headers["X-Api-Key"] = apiKeyHeader;
        return ctx;
    }

    private static async Task<string> ReadBodyAsync(HttpResponse response)
    {
        response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(response.Body, Encoding.UTF8).ReadToEndAsync();
    }

    // ---- Correct key → next() called -------------------------------------------

    [Fact]
    public async Task InvokeAsync_CorrectApiKey_CallsNext()
    {
        var nextCalled = false;
        var sut = BuildSut("secret", _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(apiKeyHeader: "secret");

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }

    // ---- Wrong key → 403 -------------------------------------------------------

    [Fact]
    public async Task InvokeAsync_WrongApiKey_Returns403WithForbiddenCode()
    {
        var sut = BuildSut("secret", _ => Task.CompletedTask);
        var ctx = BuildContext(apiKeyHeader: "wrong-key");

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(403);
        var body = await ReadBodyAsync(ctx.Response);
        body.Should().Contain("FORBIDDEN");
    }

    // ---- Missing header → 401 --------------------------------------------------

    [Fact]
    public async Task InvokeAsync_MissingApiKeyHeader_Returns401WithUnauthorizedCode()
    {
        var sut = BuildSut("secret", _ => Task.CompletedTask);
        var ctx = BuildContext(); // no X-Api-Key header

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
        var body = await ReadBodyAsync(ctx.Response);
        body.Should().Contain("UNAUTHORIZED");
    }

    // ---- Health / swagger GET paths bypass auth --------------------------------

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    [InlineData("/swagger/index.html")]
    public async Task InvokeAsync_GetHealthOrSwaggerPath_BypassesAuthAndCallsNext(string path)
    {
        var nextCalled = false;
        var sut = BuildSut("secret", _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(method: "GET", path: path); // no key header

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(200);
    }

    // ---- Non-GET to health path is NOT exempt ----------------------------------

    [Fact]
    public async Task InvokeAsync_PostToHealthPath_EnforcesAuth()
    {
        var sut = BuildSut("secret", _ => Task.CompletedTask);
        var ctx = BuildContext(method: "POST", path: "/health"); // POST is not exempt

        await sut.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(401);
    }

    // ---- Empty configured key → dev bypass (next always called) ----------------

    [Fact]
    public async Task InvokeAsync_EmptyConfiguredKey_CallsNextWithoutCheckingHeader()
    {
        var nextCalled = false;
        var sut = BuildSut(string.Empty, _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(); // no header

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_EmptyConfiguredKeyWithAnyHeader_CallsNext()
    {
        var nextCalled = false;
        var sut = BuildSut(string.Empty, _ => { nextCalled = true; return Task.CompletedTask; });
        var ctx = BuildContext(apiKeyHeader: "any-value-ignored");

        await sut.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }
}

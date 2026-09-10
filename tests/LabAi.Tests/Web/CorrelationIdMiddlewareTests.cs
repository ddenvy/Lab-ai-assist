using LabAi.Web.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace LabAi.Tests.Web;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task GeneratesAnId_WhenTheRequestCarriesNone()
    {
        var context = new DefaultHttpContext();

        await InvokeAsync(context);

        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString()
            .Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PreservesAnUpstreamId_SoTheCallerChainIsNotBroken()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "upstream-trace-42";

        await InvokeAsync(context);

        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString()
            .Should().Be("upstream-trace-42");
    }

    [Fact]
    public async Task IgnoresAWhitespaceUpstreamId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "   ";

        await InvokeAsync(context);

        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString().Trim()
            .Should().NotBeEmpty();
    }

    [Fact]
    public async Task PublishesTheSameId_OnTheHeaderAndInContextItems()
    {
        var context = new DefaultHttpContext();
        string? seenByDownstream = null;

        await InvokeAsync(context, downstream: ctx =>
        {
            seenByDownstream = ctx.Items[CorrelationIdMiddleware.ContextItemKey] as string;
            return Task.CompletedTask;
        });

        seenByDownstream.Should().NotBeNull();
        seenByDownstream.Should().Be(context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task InvokesTheNextDelegate()
    {
        var invoked = false;
        var context = new DefaultHttpContext();

        await InvokeAsync(context, downstream: _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        invoked.Should().BeTrue();
    }

    private static Task InvokeAsync(
        HttpContext context,
        Func<HttpContext, Task>? downstream = null)
    {
        var middleware = new CorrelationIdMiddleware(downstream is null
            ? _ => Task.CompletedTask
            : new RequestDelegate(downstream));

        return middleware.InvokeAsync(context);
    }
}

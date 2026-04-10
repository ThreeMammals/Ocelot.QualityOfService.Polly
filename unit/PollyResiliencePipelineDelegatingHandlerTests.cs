using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq.Protected;
using Ocelot.Configuration;
using Ocelot.Configuration.Builder;
using Ocelot.Logging;
using Polly;
using Polly.Retry;
using Shouldly;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ocelot.QualityOfService.Polly.UnitTests;

public class PollyResiliencePipelineDelegatingHandlerTests
{
    private readonly Mock<DelegatingHandler> _innerHandler = new();
    private readonly Mock<IOcelotLogger> _logger = new();
    private readonly Mock<IPollyQoSResiliencePipelineProvider<HttpResponseMessage>> _pipelineProvider = new();
    private readonly Mock<IHttpContextAccessor> _contextAccessor = new();
    private readonly Mock<IOcelotLoggerFactory> _loggerFactory = new();
    private PollyResiliencePipelineDelegatingHandler _sut;
    private Func<string>? _loggerMessage;

    public PollyResiliencePipelineDelegatingHandlerTests()
    {
        _loggerFactory.Setup(x => x.CreateLogger<PollyResiliencePipelineDelegatingHandler>())
            .Returns(_logger.Object);
        _logger.Setup(x => x.LogDebug(It.IsAny<Func<string>>()))
            .Callback<Func<string>>(f => _loggerMessage = f);
        _logger.Setup(x => x.LogInformation(It.IsAny<Func<string>>()))
            .Callback<Func<string>>(f => _loggerMessage = f);
        _sut = new PollyResiliencePipelineDelegatingHandler(DownstreamRouteFactory(), _contextAccessor.Object, _loggerFactory.Object);
    }

    [Fact]
    public async Task SendAsync_WithPipeline_ExecutedByPipeline()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.com");
        var cancellationToken = TestContext.Current.CancellationToken;
        var fakeResponse = GivenHttpResponseMessage();
        SetupInnerHandler(fakeResponse);
        SetupResiliencePipelineProvider();

        // Act
        var actual = await SendAsync(request, cancellationToken);

        // Assert
        ShouldHaveTestHeaderWithoutContent(actual);
        ShouldHaveCalledThePipelineProvider(Times.Once());
#if DEBUG
        ShouldLogInformation("The Polly.ResiliencePipeline`1[System.Net.Http.HttpResponseMessage] pipeline has detected by QoS provider for the route with downstream URL 'https://test.com/'. Going to execute request...");
#endif
        ShouldHaveCalledTheInnerHandlerOnce();
    }

    [Fact]
    public async Task SendAsync_NoPipeline_SentWithoutPipeline()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.com");
        var cancellationToken = TestContext.Current.CancellationToken;
        const bool PipelineIsNull = true;
        var fakeResponse = GivenHttpResponseMessage();
        SetupInnerHandler(fakeResponse);
        SetupResiliencePipelineProvider(PipelineIsNull);

        // Act
        var actual = await SendAsync(request, cancellationToken);

        // Assert
        ShouldHaveTestHeaderWithoutContent(actual);
        ShouldHaveCalledThePipelineProvider(Times.Once());
#if DEBUG
        ShouldLogDebug("No pipeline was detected by QoS provider for the route with downstream URL 'https://test.com/'.");
#endif
        ShouldHaveCalledTheInnerHandlerOnce();
    }

    [Fact]
    public async Task SendAsync_WhenResiliencePipelineProviderIsNull_ShouldNotCallGetResiliencePipelineAndContinueWithoutPipeline()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "https://test.com");
        var cancellationToken = TestContext.Current.CancellationToken;

        // Setup so that the provider resolution returns null
        _contextAccessor.Setup(x => x.HttpContext)
            .Returns(new DefaultHttpContext());
        IServiceProvider requestServices = new ServiceCollection().BuildServiceProvider();
        _contextAccessor.Setup(x => x.HttpContext!.RequestServices)
            .Returns(requestServices); // empty service provider → GetService returns null

        _sut = new PollyResiliencePipelineDelegatingHandler(DownstreamRouteFactory(), _contextAccessor.Object, _loggerFactory.Object);
        var fakeResponse = GivenHttpResponseMessage();
        SetupInnerHandler(fakeResponse);

        // Act
        var response = await SendAsync(request, cancellationToken);

        // Assert
        Assert.NotNull(response);

        // Verify that GetResiliencePipeline was never called because provider was null
        ShouldHaveCalledThePipelineProvider(Times.Never());
#if DEBUG
        ShouldLogDebug("No pipeline was detected by QoS provider for the route with downstream URL 'https://test.com/'.");
#endif
    }

    private void SetupInnerHandler(HttpResponseMessage fakeResponse)
    {
        _innerHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(nameof(SendAsync), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(fakeResponse);
        _sut.InnerHandler = _innerHandler.Object;
    }

    private void SetupResiliencePipelineProvider(bool pipelineIsNull = false)
    {
        var resiliencePipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<Exception>(),
            })
            .Build();
        _pipelineProvider.Setup(x => x.GetResiliencePipeline(It.IsAny<DownstreamRoute>()))
            .Returns(!pipelineIsNull ? resiliencePipeline : null!);
        var httpContext = new Mock<HttpContext>();
        httpContext.Setup(x => x.RequestServices.GetService(typeof(IPollyQoSResiliencePipelineProvider<HttpResponseMessage>)))
            .Returns(_pipelineProvider.Object);
        _contextAccessor.Setup(x => x.HttpContext)
            .Returns(httpContext.Object);
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var m = _sut.GetType().GetMethod(nameof(SendAsync), BindingFlags.Instance | BindingFlags.NonPublic);
        var task = m!.Invoke(_sut, [request, cancellationToken]) as Task<HttpResponseMessage>;
        return task!;
    }

    private static HttpResponseMessage GivenHttpResponseMessage([CallerMemberName] string headerValue = nameof(PollyResiliencePipelineDelegatingHandlerTests))
    {
        var fakeResponse = new HttpResponseMessage(HttpStatusCode.NoContent);
        fakeResponse.Headers.Add("X-Xunit", headerValue);
        return fakeResponse;
    }

    private static void ShouldHaveTestHeaderWithoutContent(HttpResponseMessage actual, [CallerMemberName] string headerValue = nameof(PollyResiliencePipelineDelegatingHandlerTests))
    {
        actual.ShouldNotBeNull();
        actual.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        actual.Headers.GetValues("X-Xunit").ShouldContain(headerValue);
    }

    private void ShouldHaveCalledThePipelineProvider(Times times)
    {
        _pipelineProvider.Verify(
            x => x.GetResiliencePipeline(It.IsAny<DownstreamRoute>()),
            times);
        _pipelineProvider.VerifyNoOtherCalls();
    }

    private void ShouldHaveCalledTheInnerHandlerOnce()
    {
        _innerHandler.Protected().Verify<Task<HttpResponseMessage>>(
            nameof(SendAsync), Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    private void ShouldLogDebug(string expected)
    {
        _logger.Verify(x => x.LogDebug(It.IsAny<Func<string>>()), Times.Once);
        var msg = _loggerMessage.ShouldNotBeNull().Invoke();
        msg.ShouldBe(expected);
    }

    private void ShouldLogInformation(string expected)
    {
        _logger.Verify(x => x.LogInformation(It.IsAny<Func<string>>()), Times.Once);
        var msg = _loggerMessage.ShouldNotBeNull().Invoke();
        msg.ShouldBe(expected);
    }

    private static DownstreamRoute DownstreamRouteFactory()
    {
        var options = new QoSOptions(2, 200)
        {
            Timeout = 100,
        };
        var upstreamPath = new UpstreamPathTemplateBuilder()
            .WithTemplate("/")
            .WithContainsQueryString(false)
            .WithPriority(1)
            .WithOriginalValue("/").Build();
        return new DownstreamRouteBuilder()
            .WithQosOptions(options)
            .WithUpstreamPathTemplate(upstreamPath)
            .Build();
    }
}

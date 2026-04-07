using Microsoft.Extensions.DependencyInjection;
using Ocelot.Configuration;
using Ocelot.Configuration.File;
using Ocelot.DependencyInjection;
using Ocelot.LoadBalancer.Balancers;
using Ocelot.Testing.Steps;
using System.Net;
using TestStack.BDDfy;
using TestStack.BDDfy.Xunit;

namespace Ocelot.QualityOfService.Polly.Acceptance;

public sealed class DynamicRoutingTests : DiscoverySteps
{
    [BddfyFact]
    [Trait("Feat", "585")] // https://github.com/ThreeMammals/Ocelot/issues/585
    [Trait("Feat", "2338")] // https://github.com/ThreeMammals/Ocelot/issues/2338
    [Trait("PR", "2339")] // https://github.com/ThreeMammals/Ocelot/pull/2339
    public async Task ShouldApplyGlobalQosOptions_ForAllDynamicRoutes()
    {
        var ports = PortFinder.GetPorts(3);
        var serviceName = ServiceName();
        var serviceUrls = ports.Select(DownstreamUrl).ToArray();
        var configuration = GivenDynamicRouting(new()
        {
            { serviceName, serviceUrls },
        });
        FileQoSOptions globalOptions = configuration.GlobalConfiguration.QoSOptions = new()
        {
            BreakDuration = CircuitBreakerStrategy.LowBreakDuration + 1, // 501
            MinimumThroughput = 2, // exceptions-errors
            Timeout = 500, // ms
        };
        using var steps = new QosSteps(this);
        Counters = new int[serviceUrls.Length];
        steps.CounterStrategy = (port) =>
        {
            int index = Array.FindIndex(serviceUrls, url => new Uri(url).Port == port);
            int count = Interlocked.Increment(ref Counters[index]);
        };

        this.Given(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithDiscoveryAndPolly))
            .When(x => steps.TestRouteCircuitBreaker(ports, $"/{serviceName}/", globalOptions, 0, true)) // test global scenario
            .When(x => steps.TestRouteTimeout(ports, $"/{serviceName}/", globalOptions))
            .Then(x => ThenServicesShouldHaveBeenCalledTimes(2, 2, 1))
            .BDDfy();
    }

    [BddfyFact]
    [Trait("Feat", "585")] // https://github.com/ThreeMammals/Ocelot/issues/585
    [Trait("Feat", "2338")] // https://github.com/ThreeMammals/Ocelot/issues/2338
    [Trait("PR", "2339")] // https://github.com/ThreeMammals/Ocelot/pull/2339
    public async Task ShouldApplyGlobalQosOptions_ForAllDynamicRoutes_WithGroupedOpts()
    {
        const int GlobalTimeout = 1500, GlobalExceptions = 3, GlobalBreakMs = 2000;
        var ports1 = PortFinder.GetPorts(2);

        // 1st route
        var route1 = GivenLbRoute("route1", key: null); // 1st route is not in the global group
        route1.QoSOptions = null; // 1st route has no opts
        GivenDiscoveryMetadata(route1, ports1);

        // 2nd route
        var ports2 = PortFinder.GetPorts(2);
        var route2 = GivenLbRoute("route2", key: "R2"); // 2nd route is in the group
        route2.QoSOptions = null; // 2nd route opts will be applied from global ones
        GivenDiscoveryMetadata(route2, ports2);

        // 3rd route
        var ports3 = PortFinder.GetPorts(2);
        var route3 = GivenLbRoute("noCircuitBreaker", loadBalancer: nameof(NoLoadBalancer), key: null);
        route3.QoSOptions = new()
        {
            MinimumThroughput = 0, // disable Circuit Breaker via disallowing of global opts to substitute
            BreakDuration = 0,
            Timeout = GlobalTimeout,
        };
        GivenDiscoveryMetadata(route3, ports3);

        var configuration = GivenDynamicRouting(new(), route1, route2, route3);
        var globalOptions = configuration.GlobalConfiguration.QoSOptions
            = new(new QoSOptions(GlobalExceptions, GlobalBreakMs))
            {
                RouteKeys = ["R2"],
            };
        var downstreamUrls = ports1.Union(ports2).Union(ports3).Select(DownstreamUrl).ToArray();
        var responses = Enumerable.Repeat(Body(), downstreamUrls.Length).ToArray();
        var codes = Enumerable.Repeat(HttpStatusCode.NotFound, ports1.Length)
                    .Concat(Enumerable.Repeat(HttpStatusCode.InternalServerError, ports2.Length))
                    .Concat(Enumerable.Repeat(HttpStatusCode.OK, ports3.Length))
                    .ToArray();
        using var steps = new QosSteps(this);
        steps.CounterStrategy = (port) =>
        {
            int index = Array.FindIndex(downstreamUrls, url => new Uri(url).Port == port);
            int count = Interlocked.Increment(ref Counters[index]);
        };
        var body = Body();
        this.Given(x => GivenThereIsAConfiguration(configuration))
            .And(x => GivenOcelotIsRunning(WithDiscoveryAndPolly))
            .And(x => GivenMultipleServiceInstancesAreRunning(downstreamUrls, responses, codes))
            .When(x => WhenIGetUrlOnTheApiGatewayConcurrently($"/{route1.ServiceName}/", 2))
            .Then(x => ThenAllStatusCodesShouldBe(HttpStatusCode.NotFound)) // QoS is switched off and the scope doesn't matter
            .And(x => ThenAllResponseBodiesShouldBe(body))
            .When(x => steps.TestRouteCircuitBreaker(ports2, $"/{route2.ServiceName}/", globalOptions, 0, true)) // test global scenario
            .And(x => steps.TestRouteTimeout(ports3, $"/{route3.ServiceName}/", route3.QoSOptions))
            .Then(x => ThenServicesShouldHaveBeenCalledTimes(1, 1, 3, 1, 2, 0))
            .BDDfy();
    }

    private static void WithDiscoveryAndPolly(IServiceCollection services) => services
        .AddSingleton(DynamicRoutingDiscoveryFinder)
        .AddOcelot().AddPolly();

    private FileDynamicRoute GivenLbRoute(string serviceName,
        string? serviceNamespace = null, string? loadBalancer = null, string? key = null)
        => new()
        {
            ServiceName = serviceName,
            ServiceNamespace = serviceNamespace ?? ServiceNamespace(),
            LoadBalancerOptions = new(loadBalancer ?? nameof(RoundRobin)),
            Key = key,
        };
}

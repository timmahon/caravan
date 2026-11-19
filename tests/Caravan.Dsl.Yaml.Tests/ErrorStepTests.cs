using Caravan.Model;

namespace Caravan.Dsl.Yaml.Tests;

public class ErrorStepTests : YamlTestBase
{
    [Fact]
    public async Task DoTry_doCatch_doFinally()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - doTry:
                      steps:
                        - to: mock:try
                        - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: "boom ${body}" }
                      doCatch:
                        - exception: [ java.lang.IllegalArgumentException ]
                          steps: [ { to: mock:wrong } ]
                        - exception: [ Caravan.Dsl.Yaml.Tests.TestBusinessException ]
                          onWhen: { simple: "${exception.message} contains 'boom'" }
                          steps:
                            - setBody: { simple: "caught ${exception.message}" }
                            - to: mock:catch
                      doFinally:
                        steps: [ { to: mock:finally } ]
                  - to: mock:after
            """);
        Mock("try").ExpectedMessageCount(1);
        Mock("wrong").ExpectedMessageCount(0);
        Mock("catch").ExpectedBodiesReceived("caught boom ${body}");
        Mock("finally").ExpectedMessageCount(1);
        Mock("after").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
    }

    [Fact]
    public async Task Route_level_onException_and_onCompletion()
    {
        await StartAsync("""
            - route:
                id: guarded
                from:
                  uri: direct:start
                  steps:
                    - onException:
                        exception: [ Caravan.Dsl.Yaml.Tests.TestBusinessException ]
                        handled: { constant: true }
                        redeliveryPolicy: { maximumRedeliveries: 2, redeliveryDelay: 0 }
                        steps:
                          - setBody: { constant: handled }
                          - to: mock:handled
                    - onCompletion:
                        onCompleteOnly: true
                        steps: [ { to: mock:completed } ]
                    - to: mock:attempt
                    - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: fail }
            """);
        Mock("attempt").ExpectedMessageCount(1);
        Mock("handled").ExpectedBodiesReceived("handled");
        Mock("completed").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Components.Mock.MockEndpoint.AssertIsSatisfiedAsync(Context);
        var route = Context.RouteDefinitions[0];
        Assert.True(Assert.Single(route.OnExceptions).RouteScoped);
        Assert.True(Assert.Single(route.OnCompletions).RouteScoped);
        Assert.Equal(2, route.OnExceptions[0].RedeliveryPolicy.MaximumRedeliveries);
        Assert.Equal(2, route.Outputs.Count);
    }

    [Fact]
    public async Task ThrowException_by_ref_and_rollback_variants()
    {
        Context.Registry.Bind("error", new TestBusinessException("from bean"));
        await StartAsync("""
            - from:
                uri: direct:ref
                steps:
                  - throwException: { ref: error }
            - from:
                uri: direct:rollback
                steps:
                  - rollback: { message: rolled back }
            - from:
                uri: direct:mark
                steps:
                  - rollback: { markRollbackOnly: true }
                  - to: mock:never
            """);
        Mock("never").ExpectedMessageCount(0);

        var byRef = await Template.SendAsync("direct:ref", e => e.Message.Body = "x");
        var rollback = await Template.SendAsync("direct:rollback", e => e.Message.Body = "x");
        var mark = await Template.SendAsync("direct:mark", e => e.Message.Body = "x");

        Assert.Equal("from bean", Assert.IsType<TestBusinessException>(byRef.Exception).Message);
        Assert.IsType<RollbackExchangeException>(rollback.Exception);
        Assert.True(mark.IsRollbackOnly);
        await Mock("never").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task CircuitBreaker_with_fallback_and_resilience4j_configuration()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - circuitBreaker:
                      resilience4jConfiguration:
                        failureRateThreshold: 50
                        minimumNumberOfCalls: 5
                        slidingWindowSize: 10
                        waitDurationInOpenState: 30
                        timeoutEnabled: true
                        timeoutDuration: 2000
                        bulkheadEnabled: true
                        bulkheadMaxConcurrentCalls: 4
                        automaticTransitionFromOpenToHalfOpenEnabled: true
                      steps:
                        - throwException: { exceptionType: java.io.IOException, message: down }
                      onFallback:
                        steps:
                          - setBody: { constant: fallback }
                  - to: mock:result
            """);
        Mock("result").ExpectedBodiesReceived("fallback");

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("result").AssertIsSatisfiedAsync();
        var breaker = Assert.IsType<CircuitBreakerDefinition>(Context.RouteDefinitions[0].Outputs[0]);
        var configuration = breaker.Configuration;
        Assert.Equal(0.5, configuration.FailureRatio);
        Assert.Equal(5, configuration.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(10), configuration.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), configuration.BreakDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), configuration.Timeout);
        Assert.Equal(4, configuration.BulkheadMaxConcurrentCalls);
    }

    [Fact]
    public async Task CircuitBreaker_with_native_configuration()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - circuitBreaker:
                      circuitBreakerConfiguration:
                        failureRatio: 0.2
                        breakDuration: 1m
                        recordExceptions: [ System.IO.IOException ]
                      steps: [ { to: mock:protected } ]
            """);
        Mock("protected").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("protected").AssertIsSatisfiedAsync();
        var configuration = Assert.IsType<CircuitBreakerDefinition>(Context.RouteDefinitions[0].Outputs[0]).Configuration;
        Assert.Equal(0.2, configuration.FailureRatio);
        Assert.Equal(TimeSpan.FromMinutes(1), configuration.BreakDuration);
        Assert.Equal([typeof(IOException)], configuration.RecordExceptions);
    }

    [Fact]
    public async Task Saga_compensates_on_failure_and_completes_on_success()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - saga:
                      propagation: RequiresNew
                      compensation: { uri: mock:compensate }
                      completion: mock:complete
                      option:
                        - key: orderId
                          header: id
                      steps:
                        - filter:
                            simple: "${body} == 'fail'"
                            steps:
                              - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: failed }
            """);
        Mock("compensate").ExpectedHeaderReceived("orderId", 1);
        Mock("complete").ExpectedHeaderReceived("orderId", 2);

        await Template.SendAsync("direct:start", e =>
        {
            e.Message.Body = "fail";
            e.Message.SetHeader("id", 1);
        });
        await Template.SendBodyAndHeaderAsync("direct:start", "ok", "id", 2);

        await Mock("compensate").AssertIsSatisfiedAsync();
        await Mock("complete").AssertIsSatisfiedAsync();
    }

    [Fact]
    public async Task Saga_without_steps_covers_the_rest_of_the_route()
    {
        await StartAsync("""
            - from:
                uri: direct:start
                steps:
                  - saga:
                      completion: { uri: mock:complete }
                  - to: mock:inside
            """);
        Mock("inside").ExpectedMessageCount(1);
        Mock("complete").ExpectedMessageCount(1);

        await Template.SendBodyAsync("direct:start", "x");

        await Mock("inside").AssertIsSatisfiedAsync();
        await Mock("complete").AssertIsSatisfiedAsync();
        Assert.True(Assert.IsType<SagaDefinition>(Context.RouteDefinitions[0].Outputs[0]).ScopeIsRestOfRoute);
    }

    [Fact]
    public async Task Top_level_dead_letter_channel_with_redelivery()
    {
        await StartAsync("""
            - errorHandler:
                deadLetterChannel:
                  deadLetterUri: mock:dead
                  useOriginalMessage: true
                  redeliveryPolicy:
                    maximumRedeliveries: 2
                    redeliveryDelay: 0
                    retryAttemptedLogLevel: WARN
            - from:
                uri: direct:start
                steps:
                  - setBody: { constant: changed }
                  - to: mock:attempt
                  - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: fail }
            """);
        Mock("attempt").ExpectedMessageCount(1);
        Mock("dead").ExpectedBodiesReceived("original");

        await Template.SendBodyAsync("direct:start", "original");

        await Mock("attempt").AssertIsSatisfiedAsync();
        await Mock("dead").AssertIsSatisfiedAsync();
        var handler = Assert.IsType<DeadLetterChannelDefinition>(Context.RouteDefinitions[0].ErrorHandler);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, handler.RedeliveryPolicy.RetryAttemptedLogLevel);
        Assert.Equal(2, handler.RedeliveryPolicy.MaximumRedeliveries);
        Assert.True(handler.UseOriginalMessage);
    }

    [Fact]
    public async Task Route_error_handler_inline_by_ref_and_none()
    {
        Context.Registry.Bind("dlc", new DeadLetterChannelDefinition("mock:refDead"));
        await StartAsync("""
            - route:
                errorHandler: { deadLetterChannel: { deadLetterUri: mock:inlineDead } }
                from:
                  uri: direct:inline
                  steps:
                    - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: a }
            - route:
                errorHandlerRef: dlc
                from:
                  uri: direct:ref
                  steps:
                    - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: b }
            - route:
                errorHandler: { refErrorHandler: dlc }
                from:
                  uri: direct:refHandler
                  steps:
                    - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: c }
            - route:
                errorHandler: { noErrorHandler: {} }
                from:
                  uri: direct:none
                  steps:
                    - throwException: { exceptionType: Caravan.Dsl.Yaml.Tests.TestBusinessException, message: d }
            """);
        Mock("inlineDead").ExpectedMessageCount(1);
        Mock("refDead").ExpectedMessageCount(2);

        await Template.SendBodyAsync("direct:inline", "x");
        await Template.SendBodyAsync("direct:ref", "x");
        await Template.SendBodyAsync("direct:refHandler", "x");
        var none = await Template.SendAsync("direct:none", e => e.Message.Body = "x");

        await Mock("inlineDead").AssertIsSatisfiedAsync();
        await Mock("refDead").AssertIsSatisfiedAsync();
        Assert.IsType<TestBusinessException>(none.Exception);
    }
}

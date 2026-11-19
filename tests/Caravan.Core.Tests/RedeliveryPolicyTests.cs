using Caravan.Model;
using Caravan.Processors;

namespace Caravan.Core.Tests;

public class RedeliveryPolicyTests
{
    [Fact]
    public void Defaults_match_camel()
    {
        var policy = new RedeliveryPolicy();

        Assert.Equal(0, policy.MaximumRedeliveries);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.RedeliveryDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), policy.MaximumRedeliveryDelay);
        Assert.Equal(2, policy.BackOffMultiplier);
        Assert.Equal(0.15, policy.CollisionAvoidanceFactor);
        Assert.False(policy.UseExponentialBackOff);
    }

    [Fact]
    public void Fixed_delay_is_the_redelivery_delay()
    {
        var policy = new RedeliveryPolicy { RedeliveryDelay = TimeSpan.FromMilliseconds(250) };

        Assert.Equal([250, 250, 250], Delays(policy, 3));
    }

    [Fact]
    public void Exponential_back_off_multiplies_the_previous_delay_up_to_the_maximum()
    {
        var policy = new RedeliveryPolicy
        {
            UseExponentialBackOff = true,
            RedeliveryDelay = TimeSpan.FromSeconds(1),
            MaximumRedeliveryDelay = TimeSpan.FromSeconds(10),
        };

        Assert.Equal([1000, 2000, 4000, 8000, 10000, 10000], Delays(policy, 6));
    }

    [Fact]
    public void Back_off_multiplier_is_configurable()
    {
        var policy = new RedeliveryPolicy { UseExponentialBackOff = true, BackOffMultiplier = 3, RedeliveryDelay = TimeSpan.FromMilliseconds(100) };

        Assert.Equal([100, 300, 900], Delays(policy, 3));
    }

    [Fact]
    public void Delay_pattern_picks_the_delay_of_the_last_group_reached()
    {
        var policy = new RedeliveryPolicy { DelayPattern = "0:1000;3:5000;5:10000" };

        Assert.Equal([1000, 1000, 5000, 5000, 10000, 10000], Delays(policy, 6));
    }

    [Fact]
    public void Delay_pattern_starting_after_the_first_attempt_has_no_delay_before_it()
    {
        var policy = new RedeliveryPolicy { DelayPattern = "2:500" };

        Assert.Equal([0, 500, 500], Delays(policy, 3));
    }

    [Fact]
    public void Invalid_delay_pattern_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => RedeliveryPolicy.ValidateDelayPattern("1000;5:x"));
    }

    [Fact]
    public void Collision_avoidance_varies_the_delay_within_the_factor()
    {
        var policy = new RedeliveryPolicy { UseCollisionAvoidance = true, RedeliveryDelay = TimeSpan.FromSeconds(1) };

        for (var i = 0; i < 200; i++)
        {
            var delay = policy.CalculateRedeliveryDelay(TimeSpan.Zero, 1).TotalMilliseconds;
            Assert.InRange(delay, 850, 1150);
        }
    }

    [Fact]
    public void Should_redeliver_up_to_the_maximum_or_forever_or_while_the_predicate_matches()
    {
        var exchange = new Exchange(new CaravanContext());
        var policy = new RedeliveryPolicy { MaximumRedeliveries = 2 };
        Assert.True(policy.ShouldRedeliver(exchange, 1, null));
        Assert.True(policy.ShouldRedeliver(exchange, 2, null));
        Assert.False(policy.ShouldRedeliver(exchange, 3, null));

        policy.MaximumRedeliveries = -1;
        Assert.True(policy.ShouldRedeliver(exchange, 1000, null));

        Assert.False(policy.ShouldRedeliver(exchange, 1, Pred.Constant(false)));
        policy.MaximumRedeliveries = 0;
        Assert.True(policy.ShouldRedeliver(exchange, 50, Pred.Constant(true)));
    }

    [Fact]
    public void Definition_overrides_only_the_options_it_sets()
    {
        var parent = new RedeliveryPolicy { MaximumRedeliveries = 5, RedeliveryDelay = TimeSpan.FromMilliseconds(10), UseExponentialBackOff = true };
        var definition = new RedeliveryPolicyDefinition { MaximumRedeliveries = 1 };

        var policy = definition.CreatePolicy(parent);

        Assert.Equal(1, policy.MaximumRedeliveries);
        Assert.Equal(TimeSpan.FromMilliseconds(10), policy.RedeliveryDelay);
        Assert.True(policy.UseExponentialBackOff);
        Assert.Equal(5, parent.MaximumRedeliveries);
    }

    private static double[] Delays(RedeliveryPolicy policy, int count)
    {
        var result = new double[count];
        var previous = TimeSpan.Zero;
        for (var i = 1; i <= count; i++)
        {
            previous = policy.CalculateRedeliveryDelay(previous, i);
            result[i - 1] = previous.TotalMilliseconds;
        }

        return result;
    }
}

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.Caching;
using Polly.Registry;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public partial class DurableCircuitBreakerClient : IDurableCircuitBreakerClient
    {
        private const string DefaultPerformancePriorityCheckCircuitInterval = "PT5S";

        private readonly IPolicyRegistry<string> policyRegistry;
        private readonly IServiceProvider serviceProvider;

        public DurableCircuitBreakerClient(
            IPolicyRegistry<string> policyRegistry,
            IServiceProvider serviceProvider)
        {
            this.policyRegistry = policyRegistry;
            this.serviceProvider = serviceProvider;
        }

        public async Task<bool> IsExecutionPermitted(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient)
        {
            // The performance priority approach reads the circuit-breaker entity state from outside.
            // Per Azure Entity Functions documentation, this may be stale if other operations on the entity have been queued but not yet actioned,
            // but it returns faster than actually executing an operation on the entity (which would queue as a serialized operation against others).

            // The trade-off is that a true half-open state (permitting only one execution per breakDuration) cannot be maintained.

            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Asking IsExecutionPermitted (performance priority) for circuit-breaker = '{circuitBreakerId}'.");

            var breakerState = await GetBreakerStateWithCaching(circuitBreakerId, () => GetBreakerState(circuitBreakerId, log, orchestrationClient));

            bool isExecutionPermitted;
            if (breakerState == null)
            {
                // We permit execution if the breaker is not yet initialized; a not-yet-initialized breaker is deemed closed, for simplicity.
                // It will be initialized when the first success or failure is recorded against it.
                isExecutionPermitted = true;
            }
            else if (breakerState.CircuitState == CircuitState.HalfOpen || breakerState.CircuitState == CircuitState.Open)
            {
                // If the circuit is open or half-open, we permit executions if the broken-until period has passed.
                // Unlike the Consistency mode, we cannot control (since we only read state, not update it) how many executions are permitted in this state.
                // However, the first execution to fail in half-open state will push out the BrokenUntil time by BreakDuration, blocking executions until the next BreakDuration has passed.
                // (Or a success first will close the circuit again.)
                isExecutionPermitted = DateTime.UtcNow > breakerState.BrokenUntil;
            }
            else if (breakerState.CircuitState == CircuitState.Closed)
            {
                isExecutionPermitted = true;
            }
            else
            {
                throw new InvalidOperationException();
            }

            log?.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted (performance priority) for circuit-breaker = '{circuitBreakerId}' returned: {isExecutionPermitted}.");
            return isExecutionPermitted;
        }

        private async Task<BreakerState> GetBreakerStateWithCaching(string circuitBreakerId, Func<Task<BreakerState>> getBreakerState)
        {
            var cachePolicy = GetCachePolicy(circuitBreakerId);
            var context = new Context($"{DurableCircuitBreakerKeyPrefix}{circuitBreakerId}");
            return await cachePolicy.ExecuteAsync(ctx => getBreakerState(), context);
        }

        private IAsyncPolicy<BreakerState> GetCachePolicy(string circuitBreakerId)
        {
            var key = $"{DurableCircuitBreakerKeyPrefix}{circuitBreakerId}";

            IAsyncPolicy<BreakerState> cachePolicy;
            if (policyRegistry.TryGet(key, out cachePolicy))
            {
                return cachePolicy;
            }

            TimeSpan checkCircuitInterval = ConfigurationHelper.GetCircuitConfigurationTimeSpan(circuitBreakerId, "PerformancePriorityCheckCircuitInterval", DefaultPerformancePriorityCheckCircuitInterval);
            cachePolicy = Policy.CacheAsync<BreakerState>(
                serviceProvider
                    .GetRequiredService<IAsyncCacheProvider>()
                    .AsyncFor<BreakerState>(),
                checkCircuitInterval);

            policyRegistry[key] = cachePolicy;

            return cachePolicy;
        }

    }
}
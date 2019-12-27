using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.Caching;
using Polly.Registry;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public partial class DurableCircuitBreakerClient : IDurableCircuitBreakerClient
    {
        private const string DurableCircuitBreakerKeyPrefix = "DurableCircuitBreaker-";

        private const string DefaultPerformancePriorityCheckCircuitInterval = "PT2S";

        private readonly IPolicyRegistry<string> policyRegistry;
        private readonly IServiceProvider serviceProvider;

        public DurableCircuitBreakerClient(
            IPolicyRegistry<string> policyRegistry,
            IServiceProvider serviceProvider)
        {
            this.policyRegistry = policyRegistry;
            this.serviceProvider = serviceProvider;
        }

        public async Task<bool> IsExecutionPermitted(string circuitBreakerId, ILogger log,
            IDurableClient durableClient)
        {
            // The performance priority approach reads the circuit-breaker entity state from outside.
            // Per Azure Entity Functions documentation, this may be stale if other operations on the entity have been queued but not yet actioned,
            // but it returns faster than actually executing an operation on the entity (which would queue as a serialized operation against others).

            // The trade-off is that a true half-open state (permitting only one execution per breakDuration) cannot be maintained.

            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Asking IsExecutionPermitted (performance priority) for circuit-breaker = '{circuitBreakerId}'.");

            var breakerState = await GetBreakerStateWithCaching(circuitBreakerId, () => GetBreakerState(circuitBreakerId, log, durableClient));

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

        public async Task RecordSuccess(string circuitBreakerId, ILogger log, IDurableClient durableClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Recording success for circuit-breaker = '{circuitBreakerId}'.");

            await durableClient.SignalEntityAsync<IDurableCircuitBreaker>(circuitBreakerId, breaker => breaker.RecordSuccess());
        }

        public async Task RecordFailure(string circuitBreakerId, ILogger log, IDurableClient durableClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Recording failure for circuit-breaker = '{circuitBreakerId}'.");

            await durableClient.SignalEntityAsync<IDurableCircuitBreaker>(circuitBreakerId, breaker => breaker.RecordFailure());
        }

        public async Task<CircuitState> GetCircuitState(string circuitBreakerId, ILogger log, IDurableClient durableClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Getting circuit state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await durableClient.ReadEntityStateAsync<DurableCircuitBreaker>(DurableCircuitBreaker.GetEntityId(circuitBreakerId));

            // To keep the return type simple, we present a not-yet-initialized circuit-breaker as closed (it will be closed when first used).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState.CircuitState : CircuitState.Closed;
        }

        public async Task<DurableCircuitBreaker> GetBreakerState(string circuitBreakerId, ILogger log, IDurableClient durableClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Getting breaker state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await durableClient.ReadEntityStateAsync<DurableCircuitBreaker>(DurableCircuitBreaker.GetEntityId(circuitBreakerId));

            // We present a not-yet-initialized circuit-breaker as null (it will be initialized when successes or failures are first posted against it).
            if (!readState.EntityExists || readState.EntityState == null)
            {
                return null;
            }

            return readState.EntityState;
        }

        private async Task<DurableCircuitBreaker> GetBreakerStateWithCaching(string circuitBreakerId, Func<Task<DurableCircuitBreaker>> getBreakerState)
        {
            var cachePolicy = GetCachePolicy(circuitBreakerId);
            var context = new Context($"{DurableCircuitBreakerKeyPrefix}{circuitBreakerId}");
            return await cachePolicy.ExecuteAsync(ctx => getBreakerState(), context);
        }

        private IAsyncPolicy<DurableCircuitBreaker> GetCachePolicy(string circuitBreakerId)
        {
            var key = $"{DurableCircuitBreakerKeyPrefix}{circuitBreakerId}";

            if (policyRegistry.TryGet(key, out IAsyncPolicy<DurableCircuitBreaker> cachePolicy))
            {
                return cachePolicy;
            }

            TimeSpan checkCircuitInterval = ConfigurationHelper.GetCircuitConfigurationTimeSpan(circuitBreakerId, "PerformancePriorityCheckCircuitInterval", DefaultPerformancePriorityCheckCircuitInterval);
            if (checkCircuitInterval > TimeSpan.Zero)
            {
                cachePolicy = Policy.CacheAsync<DurableCircuitBreaker>(
                    serviceProvider
                        .GetRequiredService<IAsyncCacheProvider>()
                        .AsyncFor<DurableCircuitBreaker>(),
                    checkCircuitInterval);
            }
            else
            {
                cachePolicy = Policy.NoOpAsync<DurableCircuitBreaker>();
            }

            policyRegistry[key] = cachePolicy;

            return cachePolicy;
        }

    }
}
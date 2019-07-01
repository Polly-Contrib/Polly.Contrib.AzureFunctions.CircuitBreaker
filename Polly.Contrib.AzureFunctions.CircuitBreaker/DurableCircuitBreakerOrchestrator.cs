using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.Caching;
using Polly.Registry;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public class DurableCircuitBreakerOrchestrator : IDurableCircuitBreakerOrchestrator
    {
        private const string DefaultFidelityPriorityCheckCircuitTimeout = "PT2S";
        private const string DefaultFidelityPriorityCheckCircuitRetryInterval = "PT0.25S";

        private const string DurableCircuitBreakerKeyPrefix = nameof(DurableCircuitBreakerOrchestrator) + "-";
        private const string DefaultThroughputPriorityCheckCircuitInterval = "PT5S";


        private readonly IPolicyRegistry<string> policyRegistry;
        private readonly IServiceProvider serviceProvider;

        public DurableCircuitBreakerOrchestrator(
            IPolicyRegistry<string> policyRegistry,
            IServiceProvider serviceProvider)
        {
            this.policyRegistry = policyRegistry;
            this.serviceProvider = serviceProvider;
        }

        public async Task<bool> IsExecutionPermittedByBreaker_FidelityPriority(
            IDurableOrchestrationClient orchestrationClient, 
            string circuitBreakerId,
            ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Asking IsExecutionPermitted (fidelity priority) for circuit-breaker = '{circuitBreakerId}'.");

            // The circuit-breaker can be configured with a maximum time you are prepared to wait to obtain the current circuit state; this allows you to limit the circuit-breaker itself introducing unwanted excessive latency.
            var checkCircuitConfiguration = GetCheckCircuitConfiguration(circuitBreakerId);
            if (checkCircuitConfiguration.retryInterval > checkCircuitConfiguration.timeout)
            {
                throw new ArgumentException($"Total timeout {checkCircuitConfiguration.timeout.TotalSeconds} should be bigger than retry timeout {checkCircuitConfiguration.retryInterval.TotalSeconds}");
            }

            string executionPermittedInstanceId = await orchestrationClient.StartNewAsync(IsExecutionPermittedInternalOrchestratorName, circuitBreakerId);

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                DurableOrchestrationStatus status = await orchestrationClient.GetStatusAsync(executionPermittedInstanceId);
                if (status != null)
                {
                    if (status.RuntimeStatus == OrchestrationRuntimeStatus.Completed)
                    {
                        try
                        {
                            bool isExecutionPermitted = status.Output.ToObject<bool>();
                            log.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted (fidelity priority) for circuit-breaker = '{circuitBreakerId}' returned: {isExecutionPermitted}.");
                            return isExecutionPermitted;
                        }
                        catch
                        {
                            return LogAndReturnForFailure("Faulted", circuitBreakerId, log);
                        }
                    }

                    if (status.RuntimeStatus == OrchestrationRuntimeStatus.Canceled ||
                        status.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
                        status.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
                    {
                        return LogAndReturnForFailure(status.RuntimeStatus.ToString(), circuitBreakerId, log);
                    }
                }

                TimeSpan elapsed = stopwatch.Elapsed;
                if (elapsed < checkCircuitConfiguration.timeout)
                {
                    TimeSpan remainingTime = checkCircuitConfiguration.timeout.Subtract(elapsed);
                    await Task.Delay(remainingTime > checkCircuitConfiguration.retryInterval ? checkCircuitConfiguration.retryInterval : remainingTime);
                }
                else
                {
                    // Timed out.
                    return LogAndReturnForFailure("TimedOut", circuitBreakerId, log);
                }
            }
        }

        private bool LogAndReturnForFailure(string failure, string circuitBreakerId, ILogger log)
        {
            // We have to choose a course of action for when the circuit-breaker does not report state in a timely manner.
            
            // We log.  Production apps could of course alert (directly here or indirectly by configured alerts) on the circuit-breaker not responding in a timely manner.
            log.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted (fidelity priority) for circuit-breaker = '{circuitBreakerId}': {failure}.");

            // Here, we choose to gracefully drop the circuit-breaker functionality and permit the execution
            // (rather than a more aggressive option of, say, failing the execution).
            return true;
        }

        public async Task RecordSuccessForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Recording success for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordSuccess);
        }

        public async Task RecordFailureForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Recording failure for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordFailure);
        }

        public async Task<CircuitState> GetCircuitStateForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Getting state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await orchestrationClient.ReadEntityStateAsync<BreakerState>(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId));

            // To keep the return type simple, we present a not-yet-initialized circuit-breaker as closed (it will be closed when first used).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState.CircuitState : CircuitState.Closed;
        }

        public async Task<BreakerState> GetBreakerStateForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Getting state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await orchestrationClient.ReadEntityStateAsync<BreakerState>(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId));

            // We present a not-yet-initialized circuit-breaker as null (it will be initialized when successes or failures are first posted against it).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState : null;
        }

        private const string IsExecutionPermittedInternalOrchestratorName = "IsExecutionPermittedInternalOrchestratorName";

        [FunctionName(IsExecutionPermittedInternalOrchestratorName)]
        public async Task<bool> IsExecutionPermittedInternalOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            string breakerId = context.GetInput<string>();
            if (string.IsNullOrEmpty(breakerId))
            {
                throw new InvalidOperationException($"{IsExecutionPermittedInternalOrchestratorName}: Could not determine breakerId of circuit-breaker requested.");
            }

            return await context.CallEntityAsync<bool>(DurableCircuitBreakerEntity.GetEntityId(breakerId), DurableCircuitBreakerEntity.Operation.IsExecutionPermitted);
        }

        private (TimeSpan timeout, TimeSpan retryInterval) GetCheckCircuitConfiguration(string circuitBreakerId)
        {
            TimeSpan checkCircuitTimeout = GetCircuitConfigurationTimeSpan(circuitBreakerId, "FidelityPriorityCheckCircuitTimeout", DefaultFidelityPriorityCheckCircuitTimeout);
            TimeSpan checkCircuitRetryInterval = GetCircuitConfigurationTimeSpan(circuitBreakerId, "FidelityPriorityCheckCircuitRetryInterval", DefaultFidelityPriorityCheckCircuitRetryInterval);

            return (checkCircuitTimeout, checkCircuitRetryInterval);
        }

        private TimeSpan GetCircuitConfigurationTimeSpan(string circuitBreakerId, string configurationItem, string defaultTimeSpan)
        {
            return XmlConvert.ToTimeSpan(DurableCircuitBreakerEntity.GetCircuitConfiguration(circuitBreakerId, configurationItem) ?? defaultTimeSpan);
        }

        public async Task<bool> IsExecutionPermittedByBreaker_ThroughputPriority(
            IDurableOrchestrationClient orchestrationClient, 
            string circuitBreakerId,
            ILogger log)
        {
            // The throughput priority approach reads the circuit-breaker entity state from outside.
            // Per Azure Entity Functions documentation, this may be stale if other operations on the entity have been queued but not yet actioned,
            // but it returns faster than actually executing an operation on the entity (which would queue as a serialized operation against others).

            // The trade-off is that a true half-open state (permitting only one execution per breakDuration) cannot be maintained.

            log.LogCircuitBreakerMessage(circuitBreakerId, $"Asking IsExecutionPermitted (throughput priority) for circuit-breaker = '{circuitBreakerId}'.");

            var cachePolicy = GetCachePolicyForBreaker(circuitBreakerId);
            var context = new Context($"{DurableCircuitBreakerKeyPrefix}{circuitBreakerId}");
            var breakerState = await
                cachePolicy.ExecuteAsync(ctx => GetBreakerStateForBreaker(orchestrationClient, circuitBreakerId, log), context);

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
                // Unlike the Fidelity mode, we cannot control (since we only read state, not update it) how many executions are permitted in this state.
                // However, the first execution to fail in half-open state will push out the BrokenUntil time by BreakDuration, blocking executions until the next BreakDuration has passed.
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

            log.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted (throughput priority) for circuit-breaker = '{circuitBreakerId}' returned: {isExecutionPermitted}.");
            return isExecutionPermitted;
        }

        private IAsyncPolicy<BreakerState> GetCachePolicyForBreaker(string circuitBreakerId)
        {
            var key = $"{DurableCircuitBreakerKeyPrefix}{circuitBreakerId}";

            IAsyncPolicy<BreakerState> cachePolicy;
            if (policyRegistry.TryGet(key, out cachePolicy))
            {
                return cachePolicy;
            }

            TimeSpan checkCircuitInterval = GetCircuitConfigurationTimeSpan(circuitBreakerId, "ThroughputPriorityCheckCircuitInterval", DefaultThroughputPriorityCheckCircuitInterval);
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
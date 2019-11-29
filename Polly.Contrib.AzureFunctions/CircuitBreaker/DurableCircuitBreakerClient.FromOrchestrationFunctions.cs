using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public partial class DurableCircuitBreakerClient : IDurableCircuitBreakerClient
    {
        private const string DefaultConsistencyPriorityCheckCircuitTimeout = "PT2S";
        private const string DefaultConsistencyPriorityCheckCircuitRetryInterval = "PT0.25S";

        public async Task<bool> IsExecutionPermitted_StrongConsistency(string circuitBreakerId, ILogger log, IDurableClient orchestrationClient)
        {
            // The circuit-breaker can be configured with a maximum time you are prepared to wait to obtain the current circuit state; this allows you to limit the circuit-breaker itself introducing unwanted excessive latency.
            var checkCircuitConfiguration = GetCheckCircuitConfiguration(circuitBreakerId);

            string executionPermittedInstanceId = await orchestrationClient.StartNewAsync(IsExecutionPermittedInternalOrchestratorName, circuitBreakerId);

            // We have to choose a course of action for when the circuit-breaker entity does not report state in a timely manner.
            // We choose to gracefully drop the circuit-breaker functionality and permit the execution (rather than a more aggressive option of, say, failing the execution).
            const bool permitExecutionOnCircuitStateQueryFailure = true;

            (bool? isExecutionPermitted, OrchestrationRuntimeStatus status) = await WaitForCompletionOrTimeout(executionPermittedInstanceId, checkCircuitConfiguration, orchestrationClient);

            switch (status)
            {
                case OrchestrationRuntimeStatus.Completed:
                    log?.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted (consistency priority) for circuit-breaker = '{circuitBreakerId}' returned: {isExecutionPermitted}.");
                    return isExecutionPermitted.Value;
                default:
                    OnCircuitStateQueryFailure(status.ToString(), circuitBreakerId, log);
                    return permitExecutionOnCircuitStateQueryFailure;
            }

        }

        private const string IsExecutionPermittedInternalOrchestratorName = "IsExecutionPermittedInternalOrchestratorName";

        [FunctionName(IsExecutionPermittedInternalOrchestratorName)]
        public Task<bool> IsExecutionPermittedInternalOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            string breakerId = context.GetInput<string>();
            if (string.IsNullOrEmpty(breakerId))
            {
                throw new InvalidOperationException($"{IsExecutionPermittedInternalOrchestratorName}: Could not determine breakerId of circuit-breaker requested.");
            }
            return IsExecutionPermitted(breakerId, log, context);
        }

        public async Task<bool> IsExecutionPermitted(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext)
        {
            if (string.IsNullOrEmpty(circuitBreakerId)) { throw new ArgumentNullException($"{nameof(circuitBreakerId)}"); }

            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Asking IsExecutionPermitted (consistency priority) for circuit-breaker = '{circuitBreakerId}'.");

            return await orchestrationContext.CreateEntityProxy<IDurableCircuitBreaker>(circuitBreakerId).IsExecutionPermitted();
        }

        public async Task RecordSuccess(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Recording success for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationContext.CreateEntityProxy<IDurableCircuitBreaker>(circuitBreakerId).RecordSuccess();
        }

        public async Task RecordFailure(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Recording failure for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationContext.CreateEntityProxy<IDurableCircuitBreaker>(circuitBreakerId).RecordFailure();
        }

        public async Task<CircuitState> GetCircuitState(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Getting circuit state for circuit-breaker = '{circuitBreakerId}'.");

            return await orchestrationContext.CreateEntityProxy<IDurableCircuitBreaker>(circuitBreakerId).GetCircuitState();
        }

        public async Task<DurableCircuitBreaker> GetBreakerState(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Getting breaker state for circuit-breaker = '{circuitBreakerId}'.");

            return await orchestrationContext.CreateEntityProxy<IDurableCircuitBreaker>(circuitBreakerId).GetBreakerState();
        }

        private async Task<(bool?, OrchestrationRuntimeStatus)> WaitForCompletionOrTimeout(
            string executionPermittedInstanceId,
            (TimeSpan timeout, TimeSpan retryInterval) checkCircuitConfiguration,
            IDurableOrchestrationClient orchestrationClient)
        {
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
                            return (status.Output.ToObject<bool>(), OrchestrationRuntimeStatus.Completed);
                        }
                        catch
                        {
                            return (null, OrchestrationRuntimeStatus.Unknown);
                        }
                    }

                    if (status.RuntimeStatus == OrchestrationRuntimeStatus.Canceled ||
                        status.RuntimeStatus == OrchestrationRuntimeStatus.Failed ||
                        status.RuntimeStatus == OrchestrationRuntimeStatus.Terminated)
                    {
                        return (null, status.RuntimeStatus);
                    }
                }

                TimeSpan elapsed = stopwatch.Elapsed;
                if (elapsed >= checkCircuitConfiguration.timeout)
                {
                    // Timed out.
                    return (null, OrchestrationRuntimeStatus.Pending);
                }

                TimeSpan remainingTime = checkCircuitConfiguration.timeout.Subtract(elapsed);
                await Task.Delay(remainingTime <= checkCircuitConfiguration.retryInterval
                    ? remainingTime
                    : checkCircuitConfiguration.retryInterval);
            }
        }

        private void OnCircuitStateQueryFailure(string failure, string circuitBreakerId, ILogger log)
        {
            // We log any circuit state query failure.
            // Production apps could of course alert (directly here or indirectly by configured alerts) on the circuit-breaker not responding in a timely manner; or choose other options.
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted (consistency priority) for circuit-breaker = '{circuitBreakerId}': {failure}.");
        }

        private (TimeSpan timeout, TimeSpan retryInterval) GetCheckCircuitConfiguration(string circuitBreakerId)
        {
            TimeSpan checkCircuitTimeout = ConfigurationHelper.GetCircuitConfigurationTimeSpan(circuitBreakerId, "ConsistencyPriorityCheckCircuitTimeout", DefaultConsistencyPriorityCheckCircuitTimeout);
            TimeSpan checkCircuitRetryInterval = ConfigurationHelper.GetCircuitConfigurationTimeSpan(circuitBreakerId, "ConsistencyPriorityCheckCircuitRetryInterval", DefaultConsistencyPriorityCheckCircuitRetryInterval);

            if (checkCircuitRetryInterval > checkCircuitTimeout)
            {
                throw new ArgumentException($"Total timeout {checkCircuitTimeout.TotalSeconds} should be bigger than retry timeout {checkCircuitRetryInterval.TotalSeconds}");
            }

            return (checkCircuitTimeout, checkCircuitRetryInterval);
        }
    }
}
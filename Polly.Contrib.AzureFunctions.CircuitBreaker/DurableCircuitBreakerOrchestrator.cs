using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public static class DurableCircuitBreakerOrchestrator
    {
        private const string DefaultCheckCircuitTimeout = "PT2S";
        private const string DefaultCheckCircuitTimeoutRetryInterval = "PT0.25S";

        internal static async Task<bool> IsExecutionPermittedByBreaker(
            this IDurableOrchestrationClient orchestrationClient,
            string circuitBreakerId,
            ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Asking IsExecutionPermitted for circuit-breaker = '{circuitBreakerId}'.");

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
                            log.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted for circuit-breaker = '{circuitBreakerId}' returned: {isExecutionPermitted}.");
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

        private static bool LogAndReturnForFailure(string failure, string circuitBreakerId, ILogger log)
        {
            // We have to choose a course of action for when the circuit-breaker does not report state in a timely manner.
            
            // We log.  Production apps could of course alert (directly here or indirectly by configured alerts) on the circuit-breaker not responding in a timely manner.
            log.LogCircuitBreakerMessage(circuitBreakerId, $"IsExecutionPermitted for circuit-breaker = '{circuitBreakerId}': {failure}.");

            // Here, we choose to gracefully drop the circuit-breaker functionality and permit the execution
            // (rather than a more aggressive option of, say, failing the execution).
            return true;
        }
        
        internal static async Task RecordSuccessForBreaker(this IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Recording success for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordSuccess);
        }

        internal static async Task RecordFailureForBreaker(this IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Recording failure for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordFailure);
        }

        internal static async Task<CircuitState> GetCircuitStateForBreaker(this IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Getting state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await orchestrationClient.ReadEntityStateAsync<BreakerState>(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId));

            // To keep the return type simple, we present a not-yet-initialized circuit-breaker as closed (it will be closed when first used).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState.CircuitState : CircuitState.Closed;
        }

        private const string IsExecutionPermittedInternalOrchestratorName = "IsExecutionPermittedInternalOrchestratorName";

        [FunctionName(IsExecutionPermittedInternalOrchestratorName)]
        public static async Task<bool> IsExecutionPermittedInternalOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            string breakerId = context.GetInput<string>();
            if (string.IsNullOrEmpty(breakerId))
            {
                throw new InvalidOperationException($"{IsExecutionPermittedInternalOrchestratorName}: Could not determine breakerId of circuit-breaker requested.");
            }

            return await context.CallEntityAsync<bool>(DurableCircuitBreakerEntity.GetEntityId(breakerId), DurableCircuitBreakerEntity.Operation.IsExecutionPermitted);
        }

        private static (TimeSpan timeout, TimeSpan retryInterval) GetCheckCircuitConfiguration(string circuitBreakerId)
        {
            TimeSpan checkCircuitTimeout = XmlConvert.ToTimeSpan(DurableCircuitBreakerEntity.GetCircuitConfiguration(circuitBreakerId, "CheckCircuitTimeout") ?? DefaultCheckCircuitTimeout);
            TimeSpan checkCircuitRetryInterval = XmlConvert.ToTimeSpan(DurableCircuitBreakerEntity.GetCircuitConfiguration(circuitBreakerId, "CheckCircuitRetryInterval") ?? DefaultCheckCircuitTimeoutRetryInterval);

            return (checkCircuitTimeout, checkCircuitRetryInterval);
        }
    }
}
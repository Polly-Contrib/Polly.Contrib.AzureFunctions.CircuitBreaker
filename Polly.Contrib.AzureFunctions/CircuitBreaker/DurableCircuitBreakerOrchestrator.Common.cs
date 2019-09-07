using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public partial class DurableCircuitBreakerOrchestrator : IDurableCircuitBreakerOrchestrator
    {
        private const string DurableCircuitBreakerKeyPrefix = "DurableCircuitBreaker-";

        public async Task RecordSuccess(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Recording success for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordSuccess);
        }

        public async Task RecordFailure(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Recording failure for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordFailure);
        }

        public async Task<CircuitState> GetCircuitState(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Getting state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await orchestrationClient.ReadEntityStateAsync<BreakerState>(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId));

            // To keep the return type simple, we present a not-yet-initialized circuit-breaker as closed (it will be closed when first used).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState.CircuitState : CircuitState.Closed;
        }

        public async Task<BreakerState> GetBreakerState(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log)
        {
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Getting state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await orchestrationClient.ReadEntityStateAsync<BreakerState>(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId));

            // We present a not-yet-initialized circuit-breaker as null (it will be initialized when successes or failures are first posted against it).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState : null;
        }
    }
}
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public partial class DurableCircuitBreakerClient : IDurableCircuitBreakerClient
    {
        private const string DurableCircuitBreakerKeyPrefix = "DurableCircuitBreaker-";

        public async Task RecordSuccess(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Recording success for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordSuccess);
        }

        public async Task RecordFailure(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Recording failure for circuit-breaker = '{circuitBreakerId}'.");

            await orchestrationClient.SignalEntityAsync(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId), DurableCircuitBreakerEntity.Operation.RecordFailure);
        }

        public async Task<CircuitState> GetCircuitState(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Getting circuit state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await orchestrationClient.ReadEntityStateAsync<BreakerState>(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId));

            // To keep the return type simple, we present a not-yet-initialized circuit-breaker as closed (it will be closed when first used).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState.CircuitState : CircuitState.Closed;
        }

        public async Task<BreakerState> GetBreakerState(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient)
        {
            log?.LogCircuitBreakerMessage(circuitBreakerId, $"Getting breaker state for circuit-breaker = '{circuitBreakerId}'.");

            var readState = await orchestrationClient.ReadEntityStateAsync<BreakerState>(DurableCircuitBreakerEntity.GetEntityId(circuitBreakerId));

            // We present a not-yet-initialized circuit-breaker as null (it will be initialized when successes or failures are first posted against it).
            return readState.EntityExists && readState.EntityState != null ? readState.EntityState : null;
        }
    }
}
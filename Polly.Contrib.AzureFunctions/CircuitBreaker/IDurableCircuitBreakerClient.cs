using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public interface IDurableCircuitBreakerClient
    {
        Task<bool> IsExecutionPermitted(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient);

        Task<bool> IsExecutionPermitted_StrongConsistency(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient);

        Task RecordSuccess(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient);

        Task RecordFailure(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient);

        Task<CircuitState> GetCircuitState(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient);

        Task<BreakerState> GetBreakerState(string circuitBreakerId, ILogger log, IDurableOrchestrationClient orchestrationClient);

        Task<bool> IsExecutionPermitted(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task RecordSuccess(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task RecordFailure(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task<CircuitState> GetCircuitState(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task<BreakerState> GetBreakerState(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);
    }
}
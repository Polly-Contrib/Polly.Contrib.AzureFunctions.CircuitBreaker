using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public interface IDurableCircuitBreakerClient
    {
        Task<bool> IsExecutionPermitted(string circuitBreakerId, ILogger log, IDurableClient durableClient);

        Task<bool> IsExecutionPermitted_StrongConsistency(string circuitBreakerId, ILogger log,
            IDurableClient durableClient);

        Task RecordSuccess(string circuitBreakerId, ILogger log, IDurableClient durableClient);

        Task RecordFailure(string circuitBreakerId, ILogger log, IDurableClient durableClient);

        Task<CircuitState> GetCircuitState(string circuitBreakerId, ILogger log, IDurableClient durableClient);

        Task<DurableCircuitBreaker> GetBreakerState(string circuitBreakerId, ILogger log, IDurableClient durableClient);

        Task<bool> IsExecutionPermitted(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task RecordSuccess(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task RecordFailure(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task<CircuitState> GetCircuitState(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);

        Task<DurableCircuitBreaker> GetBreakerState(string circuitBreakerId, ILogger log, IDurableOrchestrationContext orchestrationContext);
    }
}
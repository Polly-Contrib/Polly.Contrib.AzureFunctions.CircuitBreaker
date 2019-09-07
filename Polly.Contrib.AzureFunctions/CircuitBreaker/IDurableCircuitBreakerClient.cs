using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public interface IDurableCircuitBreakerClient
    {
        Task<bool> IsExecutionPermitted(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);

        Task<bool> IsExecutionPermitted_StrongConsistency(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);

        Task<bool> IsExecutionPermitted(IDurableOrchestrationContext orchestrationContext, string circuitBreakerId, ILogger log);

        Task RecordSuccess(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);

        Task RecordFailure(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);

        Task<CircuitState> GetCircuitState(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);

        Task<BreakerState> GetBreakerState(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);
    }
}
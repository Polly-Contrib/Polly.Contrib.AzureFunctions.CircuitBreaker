using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public interface IDurableCircuitBreakerOrchestrator
    {
        Task<bool> IsExecutionPermittedByBreaker_FidelityPriority(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);

        Task<bool> IsExecutionPermittedByBreaker_ThroughputPriority(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);

        Task RecordSuccessForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);
        Task RecordFailureForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);
        Task<CircuitState> GetCircuitStateForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);
        Task<BreakerState> GetBreakerStateForBreaker(IDurableOrchestrationClient orchestrationClient, string circuitBreakerId, ILogger log);
    }
}
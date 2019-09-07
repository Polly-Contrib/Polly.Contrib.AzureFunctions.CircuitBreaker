using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public class DurableCircuitBreakerExternalApi
    {
        private readonly IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator;

        public DurableCircuitBreakerExternalApi(
            IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator)
        {
            this.durableCircuitBreakerOrchestrator = durableCircuitBreakerOrchestrator;
        }

        [FunctionName("IsExecutionPermitted")]
        public async Task<IActionResult> IsExecutionPermitted(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/IsExecutionPermitted")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, even IsExecutionPermitted may change circuit state (to Half-Open), so might be considered POST-only.
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            return new OkObjectResult(await durableCircuitBreakerOrchestrator.IsExecutionPermittedByBreaker_PerformancePriority(orchestrationClient, circuitBreakerId, log));
        }

        [FunctionName("GetCircuitState")]
        public async Task<IActionResult> GetCircuitState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/GetCircuitState")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            return new OkObjectResult((await durableCircuitBreakerOrchestrator.GetCircuitStateForBreaker(orchestrationClient, circuitBreakerId, log)).ToString());
        }

        [FunctionName("RecordSuccess")]
        public async Task<IActionResult> RecordSuccess(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/RecordSuccess")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, it should be only a POST operation, as it amends circuit statistics and potentially state.
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            await durableCircuitBreakerOrchestrator.RecordSuccessForBreaker(orchestrationClient, circuitBreakerId, log);
            return new OkResult();
        }

        [FunctionName("RecordFailure")]
        public async Task<IActionResult> RecordFailure(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/RecordFailure")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, it should be only a POST operation, as it amends circuit statistics and potentially state.
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            await durableCircuitBreakerOrchestrator.RecordFailureForBreaker(orchestrationClient, circuitBreakerId, log);
            return new OkResult();
        }

    }
}

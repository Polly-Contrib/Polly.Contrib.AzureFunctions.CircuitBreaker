using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public static class DurableCircuitBreakerExternalApi
    {
        [FunctionName("IsExecutionPermitted")]
        public static async Task<IActionResult> IsExecutionPermitted(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/IsExecutionPermitted/{circuitBreakerId:alpha}")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, even IsExecutionPermitted may change circuit state (to Half-Open), so might be considered POST-only.
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient // Used to drive the circuit-breaker.
        )
        {
            return new OkObjectResult(await orchestrationClient.IsExecutionPermittedByBreaker(circuitBreakerId, log));
        }

        [FunctionName("GetCircuitState")]
        public static async Task<IActionResult> GetCircuitState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/GetCircuitState/{circuitBreakerId:alpha}")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient // Used to drive the circuit-breaker.
        )
        {
            return new OkObjectResult((await orchestrationClient.GetCircuitStateForBreaker(circuitBreakerId, log)).ToString());
        }

        [FunctionName("RecordSuccess")]
        public static async Task<IActionResult> RecordSuccess(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/RecordSuccess/{circuitBreakerId:alpha}")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, it should be only a POST operation, as it amends circuit statistics and potentially state.
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient // Used to drive the circuit-breaker.
        )
        {
            await orchestrationClient.RecordSuccessForBreaker(circuitBreakerId, log);
            return new OkResult();
        }

        [FunctionName("RecordFailure")]
        public static async Task<IActionResult> RecordFailure(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/RecordFailure/{circuitBreakerId:alpha}")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, it should be only a POST operation, as it amends circuit statistics and potentially state.
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient // Used to drive the circuit-breaker.
        )
        {
            await orchestrationClient.RecordFailureForBreaker(circuitBreakerId, log);
            return new OkResult();
        }

    }
}

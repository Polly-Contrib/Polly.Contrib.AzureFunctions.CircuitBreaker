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
        private readonly IDurableCircuitBreakerClient durableCircuitBreakerClient;

        public DurableCircuitBreakerExternalApi(
            IDurableCircuitBreakerClient durableCircuitBreakerClient)
        {
            this.durableCircuitBreakerClient = durableCircuitBreakerClient;
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
            return new OkObjectResult(await durableCircuitBreakerClient.IsExecutionPermitted(orchestrationClient, circuitBreakerId, log));
        }

        [FunctionName("GetCircuitState")]
        public async Task<IActionResult> GetCircuitState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/GetCircuitState")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            return new OkObjectResult((await durableCircuitBreakerClient.GetCircuitState(orchestrationClient, circuitBreakerId, log)).ToString());
        }

        [FunctionName("GetBreakerState")]
        public async Task<IActionResult> GetBreakerState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/GetBreakerState")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            return new OkObjectResult((await durableCircuitBreakerClient.GetBreakerState(orchestrationClient, circuitBreakerId, log)));
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
            await durableCircuitBreakerClient.RecordSuccess(orchestrationClient, circuitBreakerId, log);
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
            await durableCircuitBreakerClient.RecordFailure(orchestrationClient, circuitBreakerId, log);
            return new OkResult();
        }

    }
}

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
        // The purpose of this class is to provide an external, public http/s API to the circuit-breakers.

        // Note: The Microsoft.Azure.WebJobs.Extensions.DurableTask package as at 2.0.0-beta2 offers a new means to
        // expose a direct REST API to the entities, of a form like:
        // https://<my-functions-app>/runtime/webhooks/durabletask/entities/DurableCircuitBreaker/{circuitId}?op=OperationName
        // This should obviate the need for the below explicitly declared extra http-triggered functions in the functions app,
        // for providing the http/s api.


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
            return new OkObjectResult(await durableCircuitBreakerClient.IsExecutionPermitted(circuitBreakerId, log, orchestrationClient));
        }

        [FunctionName("GetCircuitState")]
        public async Task<IActionResult> GetCircuitState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/GetCircuitState")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            return new OkObjectResult((await durableCircuitBreakerClient.GetCircuitState(circuitBreakerId, log, orchestrationClient)).ToString());
        }

        [FunctionName("GetBreakerState")]
        public async Task<IActionResult> GetBreakerState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/GetBreakerState")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            return new OkObjectResult((await durableCircuitBreakerClient.GetBreakerState(circuitBreakerId, log, orchestrationClient)));
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
            await durableCircuitBreakerClient.RecordSuccess(circuitBreakerId, log, orchestrationClient);
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
            await durableCircuitBreakerClient.RecordFailure(circuitBreakerId, log, orchestrationClient);
            return new OkResult();
        }

    }
}

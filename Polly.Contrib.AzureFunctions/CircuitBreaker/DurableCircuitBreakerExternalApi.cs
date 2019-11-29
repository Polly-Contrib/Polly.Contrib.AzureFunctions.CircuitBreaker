using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public class DurableCircuitBreakerExternalApi
    {
        // The purpose of this class is to provide an external, public http/s API to the circuit-breakers.

        // Note: The GA of Durable Functions v2.0.0 also exposes a direct REST API to the entities, of a form:
        // https://<my-functions-app>/runtime/webhooks/durabletask/entities/DurableCircuitBreaker/{circuitId}?op=OperationName
        // More detail at: https://docs.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-http-api#signal-entity 
        // This could be used as an alternative for some operations we have exposed here.

        // This projects retains its own custom HTTP API because IsExecutionPermitted requires POSTing to an entity and getting a response (ie calling an entity):
        // That calling mode does not appear to be supported by either of the MS APIs  https://docs.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-http-api#signal-entity or https://docs.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-http-api#query-entity



        private readonly IDurableCircuitBreakerClient durableCircuitBreakerClient;

        public DurableCircuitBreakerExternalApi(IDurableCircuitBreakerClient durableCircuitBreakerClient)
        {
            this.durableCircuitBreakerClient = durableCircuitBreakerClient;
        }

        [FunctionName("IsExecutionPermitted")]
        public async Task<IActionResult> IsExecutionPermitted(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/IsExecutionPermitted")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, even IsExecutionPermitted may change circuit state (to Half-Open), so might be considered POST-only.
            string circuitBreakerId,
            ILogger log,
            [DurableClient]IDurableClient durableClient
        )
        {
            return new OkObjectResult(await durableCircuitBreakerClient.IsExecutionPermitted(circuitBreakerId, log, durableClient));
        }

        [FunctionName("GetCircuitState")]
        public async Task<IActionResult> GetCircuitState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/GetCircuitState")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [DurableClient]IDurableClient durableClient
        )
        {
            return new OkObjectResult((await durableCircuitBreakerClient.GetCircuitState(circuitBreakerId, log, durableClient)).ToString());
        }

        [FunctionName("GetBreakerState")]
        public async Task<IActionResult> GetBreakerState(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/GetBreakerState")]  HttpRequestMessage req,
            string circuitBreakerId,
            ILogger log,
            [DurableClient]IDurableClient durableClient
        )
        {
            return new OkObjectResult(await durableCircuitBreakerClient.GetBreakerState(circuitBreakerId, log, durableClient));
        }

        [FunctionName("RecordSuccess")]
        public async Task<IActionResult> RecordSuccess(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/RecordSuccess")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, it should be only a POST operation, as it amends circuit statistics and potentially state.
            string circuitBreakerId,
            ILogger log,
            [DurableClient]IDurableClient durableClient
        )
        {
            await durableCircuitBreakerClient.RecordSuccess(circuitBreakerId, log, durableClient);
            return new OkResult();
        }

        [FunctionName("RecordFailure")]
        public async Task<IActionResult> RecordFailure(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "DurableCircuitBreaker/{circuitBreakerId:alpha}/RecordFailure")] HttpRequestMessage req,
            // GET http method left accessible for easy demonstration from a browser. Conceptually, it should be only a POST operation, as it amends circuit statistics and potentially state.
            string circuitBreakerId,
            ILogger log,
            [DurableClient]IDurableClient durableClient
        )
        {
            await durableCircuitBreakerClient.RecordFailure(circuitBreakerId, log, durableClient);
            return new OkResult();
        }

    }
}

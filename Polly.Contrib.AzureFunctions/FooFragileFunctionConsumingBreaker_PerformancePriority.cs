using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Polly.Contrib.AzureFunctions.CircuitBreaker;

namespace Polly.Contrib.AzureFunctions
{
    public class FooFragileFunctionConsumingBreaker_PerformancePriority
    {
        // Uniquely identifies the circuit-breaker instance guarding this operation.
        private const string CircuitBreakerId = nameof(FooFragileFunctionConsumingBreaker_PerformancePriority);

        private readonly IDurableCircuitBreakerClient durableCircuitBreakerClient;

        public FooFragileFunctionConsumingBreaker_PerformancePriority(IDurableCircuitBreakerClient durableCircuitBreakerClient)
        {
            this.durableCircuitBreakerClient = durableCircuitBreakerClient;
        }

        [FunctionName("FooFragileFunctionConsumingBreaker_PerformancePriority")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
            )
        {
            // In the _PerformancePriority example, the underlying method determines whether execution is permitted
            // from a read-only version of the entity state which may omit changes from operations on the entity which have been queued but not yet executed.
            // 
            // This returns faster (prioritizing performance).
            // The trade-off is that a true half-open state (permitting only one execution per breakDuration) cannot be maintained.
            // In half-open state in the performance priority example, any number of executions will be permitted until one succeeds or fails.

            if (!await durableCircuitBreakerClient.IsExecutionPermitted(orchestrationClient, CircuitBreakerId, log))
            {
                log?.LogError($"{nameof(FooFragileFunctionConsumingBreaker_PerformancePriority)}: Service unavailable.");

                return new StatusCodeResult((int)HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                var result = await Foo.DoFragileWork(req, log, "circuit breaker, performance priority");

                await durableCircuitBreakerClient.RecordSuccess(orchestrationClient, CircuitBreakerId, log);

                return result;
            }
            catch (Exception exception)
            {
                await durableCircuitBreakerClient.RecordFailure(orchestrationClient, CircuitBreakerId, log);

                log?.LogError(exception, $"{nameof(FooFragileFunctionConsumingBreaker_PerformancePriority)}: Exception: {exception.Message}");

                return new InternalServerErrorResult();
            }
        }
    }
}
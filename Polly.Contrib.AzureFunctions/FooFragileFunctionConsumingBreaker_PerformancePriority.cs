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

        private readonly IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator;

        public FooFragileFunctionConsumingBreaker_PerformancePriority(IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator)
        {
            this.durableCircuitBreakerOrchestrator = durableCircuitBreakerOrchestrator;
        }

        [FunctionName("FooFragileFunctionConsumingBreaker_PerformancePriority")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
            )
        {
            // In the _PerformancePriority example, the underlying method determines whether execution is permitted
            // from a read-only version of the entity state which may be cached by the entity functions runtime.
            // 
            // This returns faster (prioritizing performance).
            // The trade-off is that a true half-open state (permitting only one execution per breakDuration) cannot be maintained.
            // In half-open state, any number of executions may be permitted until one succeeds or fails.

            if (!await durableCircuitBreakerOrchestrator.IsExecutionPermittedByBreaker_PerformancePriority(orchestrationClient, CircuitBreakerId, log))
            {
                log.LogError($"{nameof(FooFragileFunctionConsumingBreaker_PerformancePriority)}: Service unavailable.");

                return new StatusCodeResult((int)HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                var result = await Foo.DoFragileWork(req, log, "circuit breaker, performance priority");

                await durableCircuitBreakerOrchestrator.RecordSuccessForBreaker(orchestrationClient, CircuitBreakerId, log);

                return result;
            }
            catch (Exception exception)
            {
                await durableCircuitBreakerOrchestrator.RecordFailureForBreaker(orchestrationClient, CircuitBreakerId, log);

                log.LogError(exception, $"{nameof(FooFragileFunctionConsumingBreaker_PerformancePriority)}: Exception: {exception.Message}");

                return new InternalServerErrorResult();
            }
        }
    }
}
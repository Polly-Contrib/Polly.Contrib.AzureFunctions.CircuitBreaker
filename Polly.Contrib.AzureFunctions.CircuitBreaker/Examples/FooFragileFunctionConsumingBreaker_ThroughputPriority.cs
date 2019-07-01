using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker.Examples
{
    public class FooFragileFunctionConsumingBreaker_ThroughputPriority
    {
        // Uniquely identifies the circuit-breaker instance guarding this operation.
        private const string CircuitBreakerId = nameof(FooFragileFunctionConsumingBreaker_ThroughputPriority);

        // Used by this demonstration code to generate random failures of the simulated work.
        private static readonly Random Rand = new Random();

        private readonly IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator;

        public FooFragileFunctionConsumingBreaker_ThroughputPriority(IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator)
        {
            this.durableCircuitBreakerOrchestrator = durableCircuitBreakerOrchestrator;
        }

        [FunctionName("FooFragileFunctionConsumingBreaker_ThroughputPriority")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
            )
        {
            // In the _ThroughputPriority example, the underlying method determines whether execution is permitted
            // from a read-only version of the entity state which may be cached by the entity functions runtime.
            // 
            // This returns faster (prioritizing throughput).
            // The trade-off is that a true half-open state (permitting only one execution per breakDuration) cannot be maintained.
            // In half-open state, any number of executions may be permitted until one succeeds or fails.

            if (!await durableCircuitBreakerOrchestrator.IsExecutionPermittedByBreaker_ThroughputPriority(orchestrationClient, CircuitBreakerId, log))
            {
                // We throw an exception here to indicate the circuit is not permitting calls. Other logic could be adopted if preferred.
                throw new BrokenCircuitException();
            }

            try
            {
                var result = await OriginalFunctionMethod(req, log);

                await durableCircuitBreakerOrchestrator.RecordSuccessForBreaker(orchestrationClient, CircuitBreakerId, log);

                return result;
            }
            catch
            {
                await durableCircuitBreakerOrchestrator.RecordFailureForBreaker(orchestrationClient, CircuitBreakerId, log);

                throw;
            }
        }
        
        private static async Task<IActionResult> OriginalFunctionMethod(HttpRequestMessage req, ILogger log)
        {
            // Do something fragile!
            if (Rand.Next(2) == 0)
            {
                /*await Task.Delay(TimeSpan.FromSeconds(1));*/
                throw new Exception("Something fragile went wrong.");
            }

            // Do some work and return some result.
            await Task.CompletedTask;

            var helloWorld = "Hello world: from inside the function guarded by the circuit-breaker.";
            log.LogInformation(helloWorld);

            return new OkObjectResult(helloWorld);
        }
    }
}
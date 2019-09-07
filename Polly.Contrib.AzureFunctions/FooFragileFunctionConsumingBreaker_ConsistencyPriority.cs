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
    public class FooFragileFunctionConsumingBreaker_ConsistencyPriority
    {
        // Uniquely identifies the circuit-breaker instance guarding this operation.
        private const string CircuitBreakerId = nameof(FooFragileFunctionConsumingBreaker_ConsistencyPriority);

        // Used by this demonstration code to generate random failures of the simulated work.
        private static readonly Random Rand = new Random();

        private readonly IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator;

        public FooFragileFunctionConsumingBreaker_ConsistencyPriority(IDurableCircuitBreakerOrchestrator durableCircuitBreakerOrchestrator)
        {
            this.durableCircuitBreakerOrchestrator = durableCircuitBreakerOrchestrator;
        }


        [FunctionName("FooFragileFunctionConsumingBreaker_ConsistencyPriority")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
        )
        {
            // In the _ConsistencyPriority example, we await hearing from the circuit-breaker whether execution is permitted.
            // This makes operations entirely faithful to the state of the breaker at any time,
            // and allows us to restrict executions in the half-open state to a limited number of trial executions.

            if (!await durableCircuitBreakerOrchestrator.IsExecutionPermittedByBreaker_ConsistencyPriority(orchestrationClient, CircuitBreakerId, log))
            {
                log.LogError($"{nameof(FooFragileFunctionConsumingBreaker_ConsistencyPriority)}: Service unavailable.");

                return new StatusCodeResult((int)HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                var result = await OriginalFunctionMethod(req, log);

                await durableCircuitBreakerOrchestrator.RecordSuccessForBreaker(orchestrationClient, CircuitBreakerId, log);

                return result;
            }
            catch (Exception exception)
            {
                await durableCircuitBreakerOrchestrator.RecordFailureForBreaker(orchestrationClient, CircuitBreakerId, log);

                log.LogError(exception, $"{nameof(FooFragileFunctionConsumingBreaker_ConsistencyPriority)}: Exception: {exception.Message}");

                return new InternalServerErrorResult();
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
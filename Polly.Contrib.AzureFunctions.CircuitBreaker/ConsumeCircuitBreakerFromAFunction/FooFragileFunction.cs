using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker.ConsumeCircuitBreakerFromAFunction
{
    public static class FooFragileFunction
    {
        // Uniquely identifies the circuit-breaker instance guarding this operation.
        private const string CircuitBreakerId = nameof(FooFragileFunction);

        // Used by this demonstration code to generate random failures of the simulated work.
        private static readonly Random Rand = new Random();

        [FunctionName("FooFragileFunction")]
        public static Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient // Used to drive the circuit-breaker.
            )
        {
            return orchestrationClient.ExecuteThroughCircuitBreakerAsync(() => OriginalFunctionMethod(req, log), CircuitBreakerId, log);
        }
        
        private static async Task<IActionResult> OriginalFunctionMethod(HttpRequestMessage req, ILogger log)
        {
            // Do something fragile!
            if (Rand.Next(2) == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
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
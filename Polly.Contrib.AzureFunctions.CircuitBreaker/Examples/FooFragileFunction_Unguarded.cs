using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker.Examples
{
    public class FooFragileFunction_Unguarded
    {
        // Used by this demonstration code to generate random failures of the simulated work.
        private static readonly Random Rand = new Random();

        public FooFragileFunction_Unguarded()
        {
        }

        [FunctionName("FooFragileFunction_Unguarded")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [OrchestrationClient]IDurableOrchestrationClient orchestrationClient
            )
        {
            return await OriginalFunctionMethod(req, log);
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

            var helloWorld = "Hello world: from inside the function.";
            log.LogInformation(helloWorld);

            return new OkObjectResult(helloWorld);
        }
    }
}
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions
{
    public class FooFragileFunctionNoCircuitBreaker
    {
        [FunctionName("FooFragileFunctionNoCircuitBreaker")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [DurableClient]IDurableOrchestrationClient orchestrationClient
            )
        {
            return await Foo.DoFragileWork(req, log, "no circuit breaker");
        }
        
    }
}
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Polly.Contrib.AzureFunctions.CircuitBreaker;

namespace Polly.Contrib.AzureFunctions
{
    public class FooFragileFunctionWithCircuitBreaker
    {
        // Uniquely identifies the circuit-breaker instance guarding this operation.
        private const string CircuitBreakerId = nameof(FooFragileFunctionWithCircuitBreaker);

        private readonly IDurableCircuitBreakerClient durableCircuitBreakerClient;

        public FooFragileFunctionWithCircuitBreaker(IDurableCircuitBreakerClient durableCircuitBreakerClient)
        {
            this.durableCircuitBreakerClient = durableCircuitBreakerClient;
        }

        [FunctionName("FooFragileFunctionWithCircuitBreaker")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [DurableClient]IDurableOrchestrationClient orchestrationClient
            )
        {
            if (!await durableCircuitBreakerClient.IsExecutionPermitted(CircuitBreakerId, log, (IDurableClient) orchestrationClient))
            {
                log?.LogError($"{nameof(FooFragileFunctionWithCircuitBreaker)}: Service unavailable.");

                return new StatusCodeResult((int)HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                var result = await Foo.DoFragileWork(req, log, "circuit breaker, performance priority");

                await durableCircuitBreakerClient.RecordSuccess(CircuitBreakerId, log, (IDurableClient) orchestrationClient);

                return result;
            }
            catch (Exception exception)
            {
                await durableCircuitBreakerClient.RecordFailure(CircuitBreakerId, log, (IDurableClient) orchestrationClient);

                log?.LogError(exception, $"{nameof(FooFragileFunctionWithCircuitBreaker)}: Exception: {exception.Message}");

                return new InternalServerErrorResult();
            }
        }
    }
}
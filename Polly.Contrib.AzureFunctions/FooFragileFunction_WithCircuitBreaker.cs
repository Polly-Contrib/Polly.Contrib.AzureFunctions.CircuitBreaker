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
    public class FooFragileFunction_WithCircuitBreaker
    {
        // Uniquely identifies the circuit-breaker instance guarding this operation.
        private const string CircuitBreakerId = nameof(FooFragileFunction_WithCircuitBreaker);

        private readonly IDurableCircuitBreakerClient durableCircuitBreakerClient;

        public FooFragileFunction_WithCircuitBreaker(IDurableCircuitBreakerClient durableCircuitBreakerClient)
        {
            this.durableCircuitBreakerClient = durableCircuitBreakerClient;
        }

        [FunctionName("FooFragileFunction_WithCircuitBreaker")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req,
            ILogger log,
            [DurableClient]IDurableOrchestrationClient orchestrationClient
            )
        {
            if (!await durableCircuitBreakerClient.IsExecutionPermitted((string) (string) CircuitBreakerId, log, (IDurableClient) (IDurableEntityClient) orchestrationClient))
            {
                log?.LogError($"{nameof(FooFragileFunction_WithCircuitBreaker)}: Service unavailable.");

                return new StatusCodeResult((int)HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                var result = await Foo.DoFragileWork(req, log, "circuit breaker, performance priority");

                await durableCircuitBreakerClient.RecordSuccess((string) (string) CircuitBreakerId, log, (IDurableClient) (IDurableEntityClient) orchestrationClient);

                return result;
            }
            catch (Exception exception)
            {
                await durableCircuitBreakerClient.RecordFailure((string) (string) CircuitBreakerId, log, (IDurableClient) (IDurableEntityClient) orchestrationClient);

                log?.LogError(exception, $"{nameof(FooFragileFunction_WithCircuitBreaker)}: Exception: {exception.Message}");

                return new InternalServerErrorResult();
            }
        }
    }
}
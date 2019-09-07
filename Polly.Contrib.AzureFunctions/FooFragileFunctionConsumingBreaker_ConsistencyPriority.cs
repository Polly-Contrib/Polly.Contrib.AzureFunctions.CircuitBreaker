﻿using System;
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

        private readonly IDurableCircuitBreakerClient durableCircuitBreakerClient;

        public FooFragileFunctionConsumingBreaker_ConsistencyPriority(IDurableCircuitBreakerClient durableCircuitBreakerClient)
        {
            this.durableCircuitBreakerClient = durableCircuitBreakerClient;
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

            if (!await durableCircuitBreakerClient.IsExecutionPermitted_StrongConsistency(CircuitBreakerId, log, orchestrationClient))
            {
                log?.LogError($"{nameof(FooFragileFunctionConsumingBreaker_ConsistencyPriority)}: Service unavailable.");

                return new StatusCodeResult((int)HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                var result = await Foo.DoFragileWork(req, log, "circuit breaker, consistency priority");

                await durableCircuitBreakerClient.RecordSuccess(CircuitBreakerId, log, orchestrationClient);

                return result;
            }
            catch (Exception exception)
            {
                await durableCircuitBreakerClient.RecordFailure(CircuitBreakerId, log, orchestrationClient);

                log?.LogError(exception, $"{nameof(FooFragileFunctionConsumingBreaker_ConsistencyPriority)}: Exception: {exception.Message}");

                return new InternalServerErrorResult();
            }
        }
    }
}
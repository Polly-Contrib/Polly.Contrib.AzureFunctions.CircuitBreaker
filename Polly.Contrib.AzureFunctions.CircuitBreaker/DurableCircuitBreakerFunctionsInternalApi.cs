using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public static class DurableCircuitBreakerFunctionsInternalApi
    {
        /// <summary>
        /// Executes the action through the durable circuit-breaker of the given <paramref name="circuitBreakerId"/>
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="circuitBreakerId">The id of the durable circuit-breaker through which to execute the action.</param>
        /// <param name="log">An <see cref="ILogger"/> for this function invocation.</param>
        /// <param name="orchestrationClient">An <see cref="IDurableOrchestrationClient"/> to Used to drive the circuit-breaker.</param>
        /// <returns></returns>
        public static async Task<T> ExecuteThroughCircuitBreakerAsync<T>(
            this IDurableOrchestrationClient orchestrationClient,
            Func<Task<T>> action,
            string circuitBreakerId,
            ILogger log)
        {
            if (!await orchestrationClient.IsExecutionPermittedByBreaker(circuitBreakerId, log))
            {
                // We throw an exception here to indicate the circuit is not permitting calls. Other logic could be adopted if preferred.
                throw new BrokenCircuitException();
            }

            try
            {
                var result = await action();

                await orchestrationClient.RecordSuccessForBreaker(circuitBreakerId, log);

                return result;
            }
            catch
            {
                await orchestrationClient.RecordFailureForBreaker(circuitBreakerId, log);

                throw;
            }
        }
    }
}

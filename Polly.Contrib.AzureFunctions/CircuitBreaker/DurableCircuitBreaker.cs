using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    [JsonObject(MemberSerialization.OptIn)]
    public class DurableCircuitBreaker : IDurableCircuitBreaker
    {
        private const string EntityName = nameof(DurableCircuitBreaker);

        private readonly ILogger log;

        public DurableCircuitBreaker(ILogger log)
        {
            this.log = log; // Do not throw for null; sometimes it will be initialized by the functions runtime with log == null, just to create a value instance to return. When operations are invoked, a logger is passed correctly.
        }

        #region State that will be maintained by the entity (the circuit-breaker)

        [JsonProperty]
        public DateTime BrokenUntil { get; set; }

        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        public CircuitState CircuitState { get; set; }

        [JsonProperty]
        public int ConsecutiveFailureCount { get; set; }

        [JsonProperty]
        public int MaxConsecutiveFailures { get; set; }

        [JsonProperty]
        public TimeSpan BreakDuration { get; set; }

        #endregion

        #region Operations that can be performed on the entity (the circuit-breaker)

        /// <summary>
        /// Function entry point; d
        /// </summary>
        /// <param name="context">An <see cref="IDurableEntityContext"/>, provided by dependency-injection.</param>
        /// <param name="logger">An <see cref="ILogger"/>, provided by dependency-injection.</param>
        [FunctionName(EntityName)]
        public static async Task Run([EntityTrigger] IDurableEntityContext context, ILogger logger)
        {
            // The first time the circuit-breaker is accessed, it will self-configure.
            if (!context.HasState)
            {
                context.SetState(ConfigurationHelper.ConfigureCircuitBreaker(Entity.Current, logger));
            }

            await context.DispatchAsync<DurableCircuitBreaker>(logger);
        }

        public Task<bool> IsExecutionPermitted()
        {
            string circuitBreakerId = Entity.Current.EntityKey;

            switch (CircuitState)
            {
                case CircuitState.Closed:
                    return Task.FromResult(true);

                case CircuitState.Open:
                case CircuitState.HalfOpen:

                    // When the breaker is Open or HalfOpen, we permit a single test execution after BreakDuration has passed.
                    // The test execution phase is known as HalfOpen state.
                    if (DateTime.UtcNow > BrokenUntil)
                    {
                        log?.LogCircuitBreakerMessage(circuitBreakerId, $"Permitting a test execution in half-open state: {circuitBreakerId}.");

                        CircuitState = CircuitState.HalfOpen;
                        BrokenUntil = DateTime.UtcNow + BreakDuration;

                        return Task.FromResult(true);
                    }
                    else
                    {
                        // - Not time yet to test the circuit again.
                        return Task.FromResult(false);
                    }

                default:
                    throw new InvalidOperationException();
            }
        }

        public Task<CircuitState> RecordSuccess()
        {
            string circuitBreakerId = Entity.Current.EntityKey;

            ConsecutiveFailureCount = 0;

            // A success result in HalfOpen state causes the circuit to close (permit executions) again.
            if (IsHalfOpen())
            {
                log?.LogCircuitBreakerMessage(circuitBreakerId, $"Circuit re-closing: {circuitBreakerId}.");

                BrokenUntil = DateTime.MinValue;
                CircuitState = CircuitState.Closed;
            }

            return Task.FromResult(CircuitState);
        }

        public Task<CircuitState> RecordFailure()
        {
            string circuitBreakerId = Entity.Current.EntityKey;

            ConsecutiveFailureCount++;

            // If we have too many consecutive failures, open the circuit.
            // Or a failure when in the HalfOpen 'testing' state? That also breaks the circuit again.
            if (
                (CircuitState == CircuitState.Closed && ConsecutiveFailureCount >= MaxConsecutiveFailures) 
                || IsHalfOpen())
            {
                log?.LogCircuitBreakerMessage(circuitBreakerId, $"Circuit {(IsHalfOpen() ? "re-opening" : "opening")}: {circuitBreakerId}.");

                CircuitState = CircuitState.Open;
                BrokenUntil = DateTime.UtcNow + BreakDuration;
            }

            return Task.FromResult(CircuitState);
        }

        public Task<CircuitState> GetCircuitState()
        {
            return Task.FromResult(CircuitState);
        }

        public Task<DurableCircuitBreaker> GetBreakerState()
        {
            return Task.FromResult(this);
        }

        #endregion

        #region Helper methods

        public static EntityId GetEntityId(string circuitBreakerId) => new EntityId(EntityName, circuitBreakerId);

        private bool IsHalfOpen()
        {
            return CircuitState == CircuitState.HalfOpen
                   || CircuitState == CircuitState.Open && DateTime.UtcNow > BrokenUntil;
        }

        #endregion
    }
}
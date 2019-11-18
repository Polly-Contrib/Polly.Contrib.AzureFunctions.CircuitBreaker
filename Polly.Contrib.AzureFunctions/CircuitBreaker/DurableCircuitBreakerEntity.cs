﻿using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public class DurableCircuitBreakerEntity
    {
        private const string EntityName = "DurableCircuitBreaker";

        public static EntityId GetEntityId(string circuitBreakerId) => new EntityId(EntityName, circuitBreakerId);

        public class Operation
        {
            public const string IsExecutionPermitted = "IsExecutionPermitted";
            public const string RecordSuccess = "RecordSuccess";
            public const string RecordFailure = "RecordFailure";
            public const string GetBreakerState = "GetBreakerState";
            public const string GetCircuitState = "GetCircuitState";
        }

        [FunctionName("DurableCircuitBreaker")]
        public static Task CircuitOperation(
            [EntityTrigger] IDurableEntityContext context,
            ILogger log)
        {
            // Note: The Microsoft.Azure.WebJobs.Extensions.DurableTask package as at 2.0.0-beta2 has a new class-based approach
            // to expressing operations on and property state of entities, and it should be possible to simplify the below code to that approach.
            // See: https://docs.microsoft.com/en-us/azure/azure-functions/durable/durable-functions-preview#net-programing-models for more information.

            switch (context.OperationName)
            {
                case Operation.IsExecutionPermitted:
                    context.Return(IsExecutionPermitted(context, log));
                    break;

                case Operation.RecordSuccess:
                    context.Return(RecordSuccess(context, log));
                    break;

                case Operation.RecordFailure:
                    context.Return(RecordFailure(context, log));
                    break;

                case Operation.GetBreakerState:
                    context.Return(GetBreakerState(context, log));
                    break;

                case Operation.GetCircuitState:
                    context.Return(GetCircuitState(context, log));
                    break;

                default:
                    throw new InvalidOperationException();
            }

            return Task.CompletedTask;
        }
        
        private static bool IsExecutionPermitted(IDurableEntityContext context, ILogger log)
        {
            string circuitBreakerId = context.EntityKey;
            var breakerState = GetBreakerState(context, log);

            switch (breakerState.CircuitState)
            {
                case CircuitState.Closed:
                    return true;

                case CircuitState.Open:
                case CircuitState.HalfOpen:

                    // When the breaker is Open or HalfOpen, we permit a single test execution after BreakDuration has passed.
                    // The test execution phase is known as HalfOpen state.
                    if (DateTime.UtcNow > breakerState.BrokenUntil)
                    {
                        log?.LogCircuitBreakerMessage(circuitBreakerId, $"Permitting a test execution in half-open state: {circuitBreakerId}.");

                        breakerState.CircuitState = CircuitState.HalfOpen;
                        breakerState.BrokenUntil = DateTime.UtcNow + breakerState.BreakDuration;

                        context.SetState(breakerState);

                        return true;
                    }
                    else
                    {
                        // - Not time yet to test the circuit again.
                        return false;
                    }

                default:
                    throw new InvalidOperationException();
            }
        }

        private static CircuitState RecordSuccess(IDurableEntityContext context, ILogger log)
        {
            string circuitBreakerId = context.EntityKey;
            var breakerState = GetBreakerState(context, log);

            breakerState.ConsecutiveFailureCount = 0;

            // A success result in HalfOpen state causes the circuit to close (permit executions) again.
            if (IsHalfOpen(breakerState))
            {
                log?.LogCircuitBreakerMessage(circuitBreakerId, $"Circuit re-closing: {circuitBreakerId}.");

                breakerState.BrokenUntil = DateTime.MinValue;
                breakerState.CircuitState = CircuitState.Closed;
            }

            context.SetState(breakerState);

            return breakerState.CircuitState;
        }

        private static CircuitState RecordFailure(IDurableEntityContext context, ILogger log)
        {
            string circuitBreakerId = context.EntityKey;
            var breakerState = GetBreakerState(context, log);

            breakerState.ConsecutiveFailureCount++;

            // If we have too many consecutive failures, open the circuit.
            // Or a failure when in the HalfOpen 'testing' state? That also breaks the circuit again.
            if (
                (breakerState.CircuitState == CircuitState.Closed && breakerState.ConsecutiveFailureCount >= breakerState.MaxConsecutiveFailures) 
                || IsHalfOpen(breakerState))
            {
                log?.LogCircuitBreakerMessage(circuitBreakerId, $"Circuit {(IsHalfOpen(breakerState) ? "re-opening" : "opening")}: {circuitBreakerId}.");

                breakerState.CircuitState = CircuitState.Open;
                breakerState.BrokenUntil = DateTime.UtcNow + breakerState.BreakDuration;
            }

            context.SetState(breakerState);

            return breakerState.CircuitState;
        }

        private static bool IsHalfOpen(BreakerState breakerState)
        {
            return breakerState.CircuitState == CircuitState.HalfOpen
                || breakerState.CircuitState == CircuitState.Open && DateTime.UtcNow > breakerState.BrokenUntil;
        }

        private static CircuitState GetCircuitState(IDurableEntityContext context, ILogger log)
        {
            return GetBreakerState(context, log).CircuitState;
        }

        private static BreakerState GetBreakerState(IDurableEntityContext context, ILogger log)
        {
            var breakerState = context.GetState<BreakerState>();

            // The first time the circuit-breaker is accessed, it will self-configure.
            if (breakerState == null)
            {
                breakerState = ConfigurationHelper.ConfigureCircuitBreaker(context, log);
            }

            return breakerState;
        }
    }
}
﻿using System;
using System.Xml;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    internal static class ConfigurationHelper
    {
        internal static string GetCircuitConfiguration(string circuitBreakerId, string configurationItem)
            => Environment.GetEnvironmentVariable($"CircuitBreakerSettings:{circuitBreakerId}:{configurationItem}", EnvironmentVariableTarget.Process);

        internal static BreakerState ConfigureCircuitBreaker(IDurableEntityContext context, ILogger log)
        {
            string circuitBreakerId = context.Self.EntityKey;
            log.LogCircuitBreakerMessage(circuitBreakerId, $"Setting configuration for circuit-breaker {circuitBreakerId}.");

            // Intentionally no defaults - users should consciously decide what tolerances suit the operations invoked through the circuit-breaker.
            TimeSpan breakDuration = XmlConvert.ToTimeSpan(ConfigurationHelper.GetCircuitConfiguration(circuitBreakerId, "BreakDuration"));
            int maxConsecutiveFailures = Convert.ToInt32(ConfigurationHelper.GetCircuitConfiguration(circuitBreakerId, "MaxConsecutiveFailures"));

            var breakerState = new BreakerState
            {
                CircuitState = CircuitState.Closed,
                BrokenUntil = DateTime.MinValue,
                ConsecutiveFailureCount = 0,
                MaxConsecutiveFailures = maxConsecutiveFailures,
                BreakDuration = breakDuration
            };

            if (breakerState.BreakDuration <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"Circuit-breaker {circuitBreakerId} must be configured with a positive break-duration.");
            }

            if (breakerState.MaxConsecutiveFailures <= 0)
            {
                throw new InvalidOperationException($"Circuit-breaker {circuitBreakerId}  must be configured with a max number of consecutive failures greater than or equal to 1.");
            }

            context.SetState(breakerState);
            return breakerState;
        }
    }
}
using System;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public class BreakerState
    {
        public DateTime BrokenUntil { get; set; }

        public CircuitState CircuitState { get; set; }

        public int ConsecutiveFailureCount { get; set; }

        public int MaxConsecutiveFailures { get; set; }

        public TimeSpan BreakDuration { get; set; }
    }
}

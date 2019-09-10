using System;
using Microsoft.Extensions.Logging;

namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    internal static class LoggerExtensions
    {
        private const LogLevel DefaultLogLevel = LogLevel.Information;

        public static void LogCircuitBreakerMessage(this ILogger logger, string circuitBreakerId, string message)
        {
            logger.Log(GetLogLevel(circuitBreakerId), message);
        }

        private static LogLevel GetLogLevel(string circuitBreakerId)
        {
            LogLevel level;
            try
            {
                level = (LogLevel)Enum.Parse(typeof(LogLevel), ConfigurationHelper.GetCircuitConfiguration(circuitBreakerId, "LogLevel") ?? DefaultLogLevel.ToString());
            }
            catch
            {
                level = DefaultLogLevel;
            }

            return level;
        }
    }
}

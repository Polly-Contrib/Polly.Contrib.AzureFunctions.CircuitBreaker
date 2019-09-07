namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public enum CircuitState
    {
        Closed,
        Open,
        HalfOpen,
    }
}

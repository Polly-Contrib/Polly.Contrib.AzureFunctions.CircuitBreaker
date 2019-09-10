namespace Polly.Contrib.AzureFunctions.CircuitBreaker
{
    public enum CircuitState
    {
        Closed = 0,
        Open = 1,
        HalfOpen = 2,
    }
}

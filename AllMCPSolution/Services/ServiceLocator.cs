namespace AllMCPSolution.Services;

public static class ServiceLocator
{
    public static IServiceProvider? Provider { get; set; }

    public static IServiceScope CreateScope()
    {
        if (Provider is null) throw new InvalidOperationException("ServiceLocator.Provider is not initialized.");
        return Provider.CreateScope();
    }
}
using AppBlocks.DataSources;

namespace AppBlocks.Code;

/// <summary>
/// The compiled folder's classes for one request scope, made through <see cref="ActivatorUtilities"/> on the scope's provider so their
/// constructors take services as a registered class's would. Each extension point is made once per scope from the generation serving when
/// it is first asked for, so a recompile reaches the next request. A class that cannot be made is left out and logged; the section has
/// already reported it.
/// </summary>
public sealed class CompiledTypes(CodeCompilation compilation, IServiceProvider services, ILogger<CompiledTypes> logger) : IDisposable, IAsyncDisposable
{
    private readonly Dictionary<Type, object> made = [];
    private readonly HashSet<Type> making = [];
    private readonly List<object> disposables = [];

    /// <summary>
    /// The compiled classes implementing <typeparamref name="T"/>. A class whose constructor asks, through a registry, for the classes of its own
    /// extension point while they are being made gets none, rather than making itself again without end.
    /// </summary>
    public IReadOnlyList<T> Create<T>() where T : class
    {
        if (made.TryGetValue(typeof(T), out var known)) return (IReadOnlyList<T>)known;
        if (!making.Add(typeof(T))) return [];
        try
        {
            var instances = new List<T>();
            foreach (var type in compilation.Serving.Of(typeof(T)))
            {
                try
                {
                    var instance = (T)ActivatorUtilities.CreateInstance(services, type);
                    instances.Add(instance);
                    if (instance is IDisposable or IAsyncDisposable) disposables.Add(instance);
                }
                catch (Exception error) { logger.LogWarning(error, "The compiled class {Type} could not be constructed and is not used.", type.FullName); }
            }
            made[typeof(T)] = instances;
            return instances;
        }
        finally { making.Remove(typeof(T)); }
    }

    public void Dispose()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
        {
            if (disposables[i] is IDisposable disposable) disposable.Dispose();
        }
        disposables.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        for (var i = disposables.Count - 1; i >= 0; i--)
        {
            if (disposables[i] is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (disposables[i] is IDisposable disposable) disposable.Dispose();
        }
        disposables.Clear();
    }
}

/// <summary>
/// Every compiled <see cref="IDataSourcePolicy"/> as one registered policy: like registered ones, each must allow the operation, and a
/// compiled <see cref="IScopedDataSourcePolicy"/> is skipped where its scope does not match.
/// </summary>
internal sealed class CompiledDataSourcePolicies(CompiledTypes compiled) : IDataSourcePolicy
{
    public ValueTask<bool> IsAllowedAsync(DataSourceRequest request, CancellationToken ct) => compiled.Create<IDataSourcePolicy>().AllowAsync(request, ct);
}

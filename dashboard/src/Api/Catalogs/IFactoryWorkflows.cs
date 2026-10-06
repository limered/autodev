namespace Api.Catalogs;

/// <summary>
/// The factory catalog's named workflows as the claim path's fallback identity:
/// names parsed from the factory agents.json. Unreadable answers null, so a claim
/// merely skips picks it cannot verify instead of substituting a default silently.
/// </summary>
public interface IFactoryWorkflows
{
    FactoryWorkflows? TryRead();
}

/// <summary>
/// Reads the factory agents.json above the API's content root once and memoizes it:
/// a missing or invalid file answers null for every later read. Fixing the file
/// needs a process restart.
/// </summary>
public sealed class FactoryWorkflowsProvider(string contentRootPath) : IFactoryWorkflows
{
    private FactoryWorkflows? _cached;
    private bool _read;

    public FactoryWorkflows? TryRead()
    {
        if (!_read)
        {
            try
            {
                _cached = FactoryCatalog.Read(FactoryCatalog.Locate(contentRootPath));
            }
            catch (FactoryCatalogException)
            {
                _cached = null;
            }

            _read = true;
        }

        return _cached;
    }
}

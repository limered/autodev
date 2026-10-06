using Api.Catalogs;

namespace Api.Tests;

/// <summary>
/// A fixed catalog source for claim/pick tests: answers one preset catalog read,
/// or null when none was given, so tests control catalog drift by re-seeding.
/// </summary>
public sealed class FixedFactoryWorkflows(FactoryWorkflows? workflows) : IFactoryWorkflows
{
    public FactoryWorkflows? TryRead() => workflows;
}

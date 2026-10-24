namespace TechLandier.Infrastructure;

/// <summary>
/// Removes cached Inertia props by tag without pulling Inertia.Net into the domain library.
/// MainApp implements it with <c>IInertiaCache</c>; Worker implements it with the shared cache store.
/// </summary>
public interface IPropCacheInvalidator
{
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default);
}

using System.Reflection;

namespace TSIC.API.Services.Shared.Utilities;

/// <summary>
/// Service to check if a controller/action pair exists in the application using reflection.
/// Includes translation mapping for legacy routes to new routes.
/// Results are cached for performance.
/// </summary>
public class RouteAvailabilityService
{
    private readonly Dictionary<string, bool> _cache = new();

    // Translation map: (oldController, oldAction) -> (newController, newAction)
    // Add mappings here as you refactor routes without updating the database
    private readonly Dictionary<(string, string), (string, string)> _routeTranslations = new()
    {
        // Example: [("Player", "Register")] = ("PlayerRegistration", "Start"),
        // Add more translations as needed
    };

    /// <summary>
    /// Checks if a controller/action pair exists in the application.
    /// Applies route translations before checking.
    /// </summary>
    /// <param name="controller">Controller name (without "Controller" suffix)</param>
    /// <param name="action">Action method name</param>
    /// <returns>True if the controller and action exist, false otherwise</returns>
    public bool IsRouteImplemented(string? controller, string? action)
    {
        if (string.IsNullOrWhiteSpace(controller) || string.IsNullOrWhiteSpace(action))
            return false;

        // Apply translation if exists
        if (_routeTranslations.TryGetValue((controller, action), out var translated))
        {
            (controller, action) = translated;
        }

        var key = $"{controller}.{action}";

        // Return cached result if available
        if (_cache.TryGetValue(key, out var exists))
            return exists;

        // Find controller type using reflection in the API assembly
        var apiAssembly = Assembly.GetExecutingAssembly();
        var controllerType = apiAssembly.GetTypes()
            .FirstOrDefault(t =>
                t.Name.Equals($"{controller}Controller", StringComparison.OrdinalIgnoreCase) &&
                t.IsClass && !t.IsAbstract);

        if (controllerType == null)
        {
            _cache[key] = false;
            return false;
        }

        // Check if action method exists
        var hasAction = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(m => m.Name.Equals(action, StringComparison.OrdinalIgnoreCase));

        _cache[key] = hasAction;
        return hasAction;
    }
}

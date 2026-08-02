using System.Collections.Concurrent;
using System.Reflection;

namespace TSIC.API.Services.Admin;

/// <summary>
/// The COPY-EVERYTHING mechanism (clone philosophy, Todd-decided 08-02): every scalar
/// column of a cloned entity copies mechanically; the hand-maintained exceptions live in
/// JobCloneResetRules. A new column added to any cloned table copies automatically with
/// zero clone-code change — the frequent drift class (new column silently dropped from
/// the clone) is structurally impossible.
///
/// Scalar = public read/write property whose getter is NOT virtual. EF scaffolding marks
/// every navigation property virtual and every column property non-virtual, so this is
/// the exact mapped-column set. Store-generated members (identity ints, rowversion) ARE
/// copied here and must be reset by the entity's reset rules (new PK block) — EF throws
/// on an explicit identity value otherwise.
/// </summary>
public static class JobCloneEntityCopier
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    public static T CopyScalars<T>(T source) where T : class, new()
    {
        var clone = new T();
        foreach (var p in GetScalarProperties(typeof(T)))
            p.SetValue(clone, p.GetValue(source));
        return clone;
    }

    /// <summary>
    /// The scalar (mapped-column) property set for an entity type. Public so the D1
    /// property-snapshot drift tests enumerate the same list the copier copies.
    /// </summary>
    public static PropertyInfo[] GetScalarProperties(Type entityType) =>
        Cache.GetOrAdd(entityType, t => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite
                        && p.GetGetMethod() is { IsVirtual: false })
            .ToArray());
}

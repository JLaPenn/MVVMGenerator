using System;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;

namespace MVVM.Generator.Utilities;

/// <summary>
/// Decides the name generated code uses to refer to the emitted chain observer.
/// </summary>
/// <remarks>
/// Generated code imports the runtime namespace and writes the type unqualified,
/// which reads far better than a fully qualified name but is not collision-proof.
/// A consuming type of the same name in the generated file's own namespace would
/// silently win over the using directive, and one reached through another import
/// would produce CS0104. Both cases are resolved here by falling back to an alias
/// rather than by qualifying every reference.
/// </remarks>
internal static class ObserverNaming
{
    public const string RuntimeNamespace = "MVVM.Generator.Runtime";
    public const string RuntimeTypeName = "ChainObserver";

    private const string AliasPrefix = "MG" + RuntimeTypeName;

    /// <summary>
    /// Upper bound on the numbered fallbacks. Reaching it would mean the
    /// compilation declares a thousand colliding names, so the last one is used
    /// and the compiler reports the clash.
    /// </summary>
    private const int MaxNumberedAttempts = 1000;

    /// <summary>
    /// True when the name is the runtime type's own, so the namespace can simply
    /// be imported instead of aliased.
    /// </summary>
    public static bool IsDirectImport(string observerTypeName) =>
        observerTypeName == RuntimeTypeName;

    /// <summary>
    /// Renders the using directive that brings <paramref name="observerTypeName"/>
    /// into scope.
    /// </summary>
    public static string RenderUsing(string observerTypeName) =>
        IsDirectImport(observerTypeName)
            ? $"using {RuntimeNamespace};"
            : $"using {observerTypeName} = {RuntimeNamespace}.{RuntimeTypeName};";

    /// <summary>
    /// Picks a name for the observer that nothing else in the compilation declares:
    /// <c>ChainObserver</c>, else <c>MGChainObserver</c>, else <c>MGChainObserver2</c>
    /// and upwards.
    /// </summary>
    /// <remarks>
    /// Only source declarations are searched, which is what
    /// <see cref="Compilation.GetSymbolsWithName(Func{string, bool}, SymbolFilter, System.Threading.CancellationToken)"/>
    /// covers; a same-named type coming from a referenced assembly is not detected.
    /// That is deliberate: scanning every referenced namespace on each compilation
    /// costs more than the case is worth, and a collision there still surfaces as a
    /// compiler error rather than as wrong behaviour.
    /// </remarks>
    public static string Resolve(Compilation compilation)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var symbol in compilation.GetSymbolsWithName(IsCandidate, SymbolFilter.Type))
        {
            // The runtime observer is itself a source declaration once the
            // post-initialization output is added, so it never counts as a clash.
            if (symbol.ContainingNamespace?.ToDisplayString() == RuntimeNamespace) continue;

            taken.Add(symbol.Name);
        }

        if (taken.Count == 0) return RuntimeTypeName;

        foreach (var candidate in CandidateNames())
        {
            if (!taken.Contains(candidate)) return candidate;
        }

        return $"{AliasPrefix}{MaxNumberedAttempts}";
    }

    private static IEnumerable<string> CandidateNames()
    {
        yield return RuntimeTypeName;
        yield return AliasPrefix;

        for (var suffix = 2; suffix <= MaxNumberedAttempts; suffix++)
        {
            yield return $"{AliasPrefix}{suffix}";
        }
    }

    /// <summary>
    /// Narrows the symbol scan to names that could occupy a candidate.
    /// </summary>
    private static bool IsCandidate(string name) =>
        name == RuntimeTypeName || name.StartsWith(AliasPrefix, StringComparison.Ordinal);
}

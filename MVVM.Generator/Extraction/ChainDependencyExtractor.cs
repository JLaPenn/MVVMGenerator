using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using MVVM.Generator.Attributes;
using MVVM.Generator.Diagnostics;
using MVVM.Generator.Models;
using MVVM.Generator.Utilities;

namespace MVVM.Generator.Extraction;

/// <summary>
/// Finds the dependency paths a computed property reads through other objects,
/// which same-class dependency inference cannot see.
/// </summary>
/// <remarks>
/// Resolution is done entirely through symbols rather than a SemanticModel, for
/// one specific reason: a link frequently lands on a property this generator is
/// itself producing elsewhere in the same compilation, which no semantic query
/// can see yet. Every link therefore falls back to matching an [AutoNotify]
/// field by the property name it will generate.
/// </remarks>
internal static class ChainDependencyExtractor
{
    private const string AutoNotifyAttributeName = nameof(AutoNotifyAttribute);
    private const string InccName = "INotifyCollectionChanged";
    private const string InpcName = "INotifyPropertyChanged";
    private const string LogPrefix = "ChainDependencyExtractor: ";

    public static ImmutableArray<ChainModel> Extract(
        INamedTypeSymbol classSymbol,
        List<DiagnosticInfo> diagnostics)
    {
        var heads = BuildHeadMap(classSymbol);
        if (heads.Count == 0) return ImmutableArray<ChainModel>.Empty;

        // Keyed by head plus link signature so several dependents sharing a path
        // share one observer.
        var chains = new Dictionary<string, ChainBuilder>();

        foreach (var property in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsImplicitlyDeclared || property.GetMethod == null) continue;

            var getter = GetterSyntax(property);
            if (getter == null) continue;

            foreach (var path in ReadPaths(getter))
            {
                var resolved = Resolve(path, heads, property, diagnostics);
                if (resolved == null) continue;

                var key = resolved.Value.Key;
                if (!chains.TryGetValue(key, out var builder))
                {
                    builder = new ChainBuilder(resolved.Value.HeadName, resolved.Value.Links, resolved.Value.Usings);
                    chains[key] = builder;
                }

                builder.Dependents.Add(property.Name);
            }
        }

        if (chains.Count == 0) return ImmutableArray<ChainModel>.Empty;

        // Ordered so the generated field names and emission order stay stable
        // across runs, which incremental caching depends on.
        var ordered = chains
            .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
            .ToList();

        var models = ImmutableArray.CreateBuilder<ChainModel>(ordered.Count);
        var index = 0;
        foreach (var pair in ordered)
        {
            var builder = pair.Value;
            models.Add(new ChainModel(
                ObserverFieldName: $"_{Camel(builder.HeadName)}Chain{index}",
                HeadPropertyName: builder.HeadName,
                Links: EquatableArray.From(builder.Links),
                DependentProperties: EquatableArray.From(
                    builder.Dependents.OrderBy(name => name, System.StringComparer.Ordinal)),
                Usings: EquatableArray.From(builder.Usings.Distinct())));
            index++;
        }

        if (LogManager.IsEnabled)
            LogManager.Log($"{LogPrefix}{classSymbol.Name}: {models.Count} chain(s)");

        return models.ToImmutable();
    }

    private sealed class ChainBuilder
    {
        public ChainBuilder(string headName, List<ChainLinkModel> links, List<string> usings)
        {
            HeadName = headName;
            Links = links;
            Usings = usings;
        }

        public string HeadName { get; }
        public List<ChainLinkModel> Links { get; }
        public List<string> Usings { get; }
        public SortedSet<string> Dependents { get; } = new(System.StringComparer.Ordinal);
    }

    /// <summary>
    /// Maps the property name each [AutoNotify] field will generate to that
    /// field's type, giving the set of paths that can be rooted in this class.
    /// </summary>
    private static Dictionary<string, ITypeSymbol> BuildHeadMap(INamedTypeSymbol classSymbol)
    {
        var map = new Dictionary<string, ITypeSymbol>(System.StringComparer.Ordinal);
        foreach (var field in classSymbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.GetAttributes().Any(a => a.AttributeClass?.Name == AutoNotifyAttributeName)) continue;
            if (field.IsStatic) continue;

            map[FieldAttributeReader.GetPropertyName(field)] = field.Type;
        }
        return map;
    }

    private static SyntaxNode? GetterSyntax(IPropertySymbol property)
    {
        if (property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not PropertyDeclarationSyntax syntax)
            return null;

        if (syntax.ExpressionBody != null) return syntax.ExpressionBody.Expression;

        var getter = syntax.AccessorList?.Accessors
            .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));

        return (SyntaxNode?)getter?.ExpressionBody?.Expression ?? getter?.Body;
    }

    /// <summary>
    /// Yields the longest member-access path under each root, so a subpath is
    /// not also observed on its own.
    /// </summary>
    private static IEnumerable<List<string>> ReadPaths(SyntaxNode getter)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var node in getter.DescendantNodesAndSelf())
        {
            // Invocations count as roots too: in `a.B.Any()` the member access is
            // consumed by the call, so skipping calls would drop the path entirely.
            if (node is not (MemberAccessExpressionSyntax
                or ConditionalAccessExpressionSyntax
                or InvocationExpressionSyntax)) continue;
            if (ContinuesIntoParent(node)) continue;

            var path = Flatten(node);
            if (path == null || path.Count < 2) continue;

            if (seen.Add(string.Join(".", path))) yield return path;
        }
    }

    /// <summary>
    /// True when this node is only a segment of a longer access handled higher up.
    /// </summary>
    private static bool ContinuesIntoParent(SyntaxNode node)
    {
        return node.Parent switch
        {
            MemberAccessExpressionSyntax parent => parent.Expression == node,
            ConditionalAccessExpressionSyntax parent => parent.Expression == node,
            MemberBindingExpressionSyntax => true,
            InvocationExpressionSyntax parent => parent.Expression == node,
            _ => false,
        };
    }

    /// <summary>
    /// Reduces an access expression to its property-name segments, or null when
    /// it contains something this analysis will not model (indexers, casts,
    /// method arguments, static access).
    /// </summary>
    private static List<string>? Flatten(SyntaxNode node)
    {
        var segments = new List<string>();

        while (true)
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    segments.Add(memberAccess.Name.Identifier.Text);
                    node = memberAccess.Expression;
                    continue;

                case ConditionalAccessExpressionSyntax conditional:
                    var whenNotNull = Flatten(conditional.WhenNotNull);
                    if (whenNotNull == null) return null;
                    // WhenNotNull is relative to the conditional's Expression, so it
                    // sits outward of everything collected so far.
                    segments.InsertRange(0, whenNotNull);
                    node = conditional.Expression;
                    continue;

                case MemberBindingExpressionSyntax binding:
                    segments.Add(binding.Name.Identifier.Text);
                    return Reversed(segments);

                case InvocationExpressionSyntax invocation:
                    node = invocation.Expression;
                    continue;

                case ParenthesizedExpressionSyntax parenthesized:
                    node = parenthesized.Expression;
                    continue;

                case IdentifierNameSyntax identifier:
                    segments.Add(identifier.Identifier.Text);
                    return Reversed(segments);

                case ThisExpressionSyntax:
                    return Reversed(segments);

                default:
                    return null;
            }
        }
    }

    private static List<string> Reversed(List<string> segments)
    {
        segments.Reverse();
        return segments;
    }

    private static (string Key, string HeadName, List<ChainLinkModel> Links, List<string> Usings)? Resolve(
        List<string> path,
        Dictionary<string, ITypeSymbol> heads,
        IPropertySymbol dependent,
        List<DiagnosticInfo> diagnostics)
    {
        if (!heads.TryGetValue(path[0], out var currentType)) return null;

        var links = new List<ChainLinkModel>();
        var usings = new List<string>();

        for (var i = 1; i < path.Count; i++)
        {
            var segment = path[i];
            var owner = currentType;

            // Anything read off an observable collection -- Count, an indexer, a
            // LINQ call -- changes when its contents change, and CollectionChanged
            // is the only signal that covers all of them. ObservableCollection also
            // raises PropertyChanged for "Count", but a derived collection is not
            // obliged to, so the path stops here and watches the collection itself.
            if (links.Count > 0 && Implements(owner, InccName))
            {
                links[links.Count - 1] = links[links.Count - 1] with { ObserveCollection = true };
                return Build(path, links, usings);
            }

            var member = ResolveMember(owner, segment);

            if (member == null)
            {
                Report(diagnostics, dependent, path, segment, $"'{segment}' is not a property of '{owner.Name}'");
                return null;
            }

            if (!Implements(owner, InpcName))
            {
                Report(diagnostics, dependent, path, segment,
                    $"'{owner.Name}' does not implement {InpcName}, so changes to '{segment}' cannot be observed");
                return null;
            }

            NamespaceExtractor.AddNamespaceUsings(usings, member.Value.DeclaringType);

            links.Add(new ChainLinkModel(
                PropertyName: segment,
                OwnerTypeName: TypeHelper.GetTypeName(member.Value.DeclaringType),
                ObserveCollection: false));

            currentType = member.Value.Type;
        }

        if (links.Count == 0) return null;

        // A path ending on an observable collection is read for its contents in
        // practice, not merely for the reference.
        if (Implements(currentType, InccName))
            links[links.Count - 1] = links[links.Count - 1] with { ObserveCollection = true };

        return Build(path, links, usings);
    }

    private static string Camel(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static (string, string, List<ChainLinkModel>, List<string>) Build(
        List<string> path,
        List<ChainLinkModel> links,
        List<string> usings)
    {
        var headName = path[0];
        var signature = string.Join(
            ".",
            links.Select(link => link.ObserveCollection ? $"{link.PropertyName}[]" : link.PropertyName));

        return ($"{headName}.{signature}", headName, links, usings);
    }

    /// <summary>
    /// Resolves one segment against a type, falling back to the [AutoNotify]
    /// field that will generate a property of that name.
    /// </summary>
    private static (ITypeSymbol Type, ITypeSymbol DeclaringType)? ResolveMember(ITypeSymbol owner, string name)
    {
        for (var type = owner; type != null; type = type.BaseType)
        {
            var property = type.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is { IsStatic: false }) return (property.Type, type);

            var generated = type.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field =>
                    !field.IsStatic
                    && field.GetAttributes().Any(a => a.AttributeClass?.Name == AutoNotifyAttributeName)
                    && FieldAttributeReader.GetPropertyName(field) == name);

            if (generated != null) return (generated.Type, type);
        }

        return null;
    }

    private static bool Implements(ITypeSymbol type, string interfaceName)
    {
        if (type.Name == interfaceName) return true;
        if (type.AllInterfaces.Any(i => i.Name == interfaceName)) return true;

        // A class carrying [AutoNotify] gets INotifyPropertyChanged from this
        // generator, so the interface is not on the symbol yet.
        if (interfaceName == InpcName)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.GetMembers().OfType<IFieldSymbol>().Any(field =>
                    field.GetAttributes().Any(a => a.AttributeClass?.Name == AutoNotifyAttributeName)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void Report(
        List<DiagnosticInfo> diagnostics,
        IPropertySymbol dependent,
        List<string> path,
        string segment,
        string reason)
    {
        diagnostics.Add(DiagnosticInfo.Create(
            Descriptors.Generator.AutoNotify.UnobservableDependency,
            dependent,
            dependent.Name,
            string.Join(".", path),
            reason));
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

using MVVM.Generator.Attributes;
using MVVM.Generator.Diagnostics;
using MVVM.Generator.Models;
using MVVM.Generator.Utilities;

namespace MVVM.Generator.Extraction;

/// <summary>
/// Builds the complete ClassModel for one attributed class.
/// </summary>
/// <remarks>
/// Reproduces the previous gating exactly: a generator ran only if at least one
/// of its members validated, and it then processed every member carrying its
/// attribute -- including ones that failed validation.
/// </remarks>
internal static class ClassModelExtractor
{
    public static ClassModel? Extract(INamedTypeSymbol classSymbol)
    {
        var notifyFields = MembersWith<IFieldSymbol>(classSymbol, nameof(AutoNotifyAttribute));
        var commandMethods = MembersWith<IMethodSymbol>(classSymbol, nameof(AutoCommandAttribute));
        var dependencyFields = MembersWith<IFieldSymbol>(classSymbol, nameof(AutoDPropAttribute));
        var styledFields = MembersWith<IFieldSymbol>(classSymbol, nameof(AutoSPropAttribute));

        if (notifyFields.Length == 0
            && commandMethods.Length == 0
            && dependencyFields.Length == 0
            && styledFields.Length == 0)
        {
            return null;
        }

        var diagnostics = new List<DiagnosticInfo>();

        var dependencies = notifyFields.Length > 0
            ? new AttributeProcessor().AnalyzeDependencies(classSymbol, default)
            : ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;

        var chains = notifyFields.Length > 0
            ? ChainDependencyExtractor.Extract(classSymbol, diagnostics)
            : ImmutableArray<ChainModel>.Empty;

        if (notifyFields.Length > 0)
            ValidateDependsOn(classSymbol, notifyFields, diagnostics);

        var notifyActive = ValidateAll(notifyFields, field => ValidateNotify(field, diagnostics));
        var commandActive = ValidateAll(commandMethods, method => CommandExtractor.Validate(method, diagnostics));

        if (notifyActive && HasCircularDependencies(dependencies))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Descriptors.Generator.AutoNotify.CircularDependency, classSymbol,
                classSymbol.Name, DescribeCycle(dependencies)));
            notifyActive = false;
        }

        var model = new ClassModel(
            Namespace: classSymbol.ContainingNamespace.ToDisplayString(),
            ClassName: classSymbol.Name,
            HintName: $"{classSymbol.Name}.ViewModel.cs",
            BaseImplementsInpc: NotifyFieldExtractor.BaseImplementsInpc(classSymbol),
            NotifyFields: notifyActive
                ? EquatableArray.From(notifyFields.Select(f => NotifyFieldExtractor.Extract(f, dependencies)))
                : EquatableArray<NotifyFieldModel>.Empty,
            Commands: commandActive
                ? EquatableArray.From(commandMethods.Select(CommandExtractor.Extract))
                : EquatableArray<CommandModel>.Empty,
            DependencyProperties: EquatableArray.From(
                dependencyFields.Select(f => BackingPropertyExtractor.Extract(f, "using System.Windows;"))),
            StyledProperties: EquatableArray.From(
                styledFields.Select(f => BackingPropertyExtractor.Extract(f, "using Avalonia;"))),
            Chains: notifyActive ? EquatableArray.From(chains) : EquatableArray<ChainModel>.Empty,
            Diagnostics: EquatableArray.From(diagnostics));

        return model.HasContent || !model.Diagnostics.IsEmpty ? model : null;
    }

    private static ImmutableArray<TSymbol> MembersWith<TSymbol>(INamedTypeSymbol classSymbol, string attributeName)
        where TSymbol : ISymbol
    {
        return classSymbol.GetMembers()
            .Where(member => member.GetAttributes()
                .Any(attribute => attribute.AttributeClass?.Name == attributeName))
            .OfType<TSymbol>()
            .ToImmutableArray();
    }

    // Every member is validated so its diagnostics are recorded, then the
    // generator runs if any one of them passed.
    private static bool ValidateAll<TSymbol>(ImmutableArray<TSymbol> symbols, System.Func<TSymbol, bool> validate)
    {
        var anyValid = false;
        foreach (var symbol in symbols)
        {
            if (validate(symbol)) anyValid = true;
        }
        return anyValid;
    }

    private static bool ValidateNotify(IFieldSymbol fieldSymbol, List<DiagnosticInfo> diagnostics)
    {
        if (fieldSymbol.Type.IsStatic)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Descriptors.Generator.AutoNotify.StaticType, fieldSymbol,
                fieldSymbol.Name, fieldSymbol.Type.Name));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reports [DependsOn] arguments that name nothing this class can notify on.
    /// </summary>
    /// <remarks>
    /// This check previously looked the dependency map up by field name while the
    /// map is keyed by property name, so it never matched and the diagnostic never
    /// fired -- a [DependsOn] naming a nonexistent property silently did nothing.
    /// It is resolved here against the generated property names instead.
    /// </remarks>
    private static void ValidateDependsOn(
        INamedTypeSymbol classSymbol,
        ImmutableArray<IFieldSymbol> notifyFields,
        List<DiagnosticInfo> diagnostics)
    {
        var notifiable = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var field in notifyFields)
        {
            notifiable.Add(field.Name);
            notifiable.Add(FieldAttributeReader.GetPropertyName(field));
        }

        foreach (var member in classSymbol.GetMembers())
        {
            var attribute = member.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == nameof(DependsOnAttribute));
            if (attribute == null) continue;

            var names = attribute.ConstructorArguments.FirstOrDefault().Values;
            foreach (var argument in names)
            {
                if (argument.Value?.ToString() is not { Length: > 0 } name) continue;
                if (notifiable.Contains(name)) continue;

                // A name that resolves to a real member is a no-op rather than a
                // typo: the attribute cannot generate notification for a property it
                // does not also generate, but the class may notify it by hand.
                var descriptor = ResolvesToAnyMember(classSymbol, name)
                    ? Descriptors.Generator.AutoNotify.DependencyNotNotifying
                    : Descriptors.Generator.AutoNotify.DependencyNotFound;

                diagnostics.Add(DiagnosticInfo.Create(descriptor, member, member.Name, name));
            }
        }
    }

    /// <summary>
    /// True when the name matches any property or field on the class or a base,
    /// including an [AutoNotify] field declared by a base class.
    /// </summary>
    private static bool ResolvesToAnyMember(INamedTypeSymbol classSymbol, string name)
    {
        for (var type = (ITypeSymbol?)classSymbol; type != null; type = type.BaseType)
        {
            if (type.GetMembers(name).Any(m => m is IPropertySymbol or IFieldSymbol)) return true;

            var generated = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Any(field =>
                    field.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(AutoNotifyAttribute))
                    && FieldAttributeReader.GetPropertyName(field) == name);

            if (generated) return true;
        }

        return false;
    }

    private static bool HasCircularDependencies(ImmutableDictionary<string, ImmutableHashSet<string>> dependencies)
    {
        var visited = new HashSet<string>();
        var stack = new HashSet<string>();

        bool HasCycle(string property)
        {
            if (stack.Contains(property)) return true;
            if (visited.Contains(property)) return false;

            visited.Add(property);
            stack.Add(property);

            if (dependencies.TryGetValue(property, out var deps))
            {
                foreach (var dependency in deps)
                {
                    if (HasCycle(dependency)) return true;
                }
            }

            stack.Remove(property);
            return false;
        }

        return dependencies.Keys.Any(HasCycle);
    }

    private static string DescribeCycle(ImmutableDictionary<string, ImmutableHashSet<string>> dependencies)
    {
        return string.Join(
            ", ",
            dependencies
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} -> {string.Join("/", pair.Value.OrderBy(v => v, System.StringComparer.Ordinal))}"));
    }
}

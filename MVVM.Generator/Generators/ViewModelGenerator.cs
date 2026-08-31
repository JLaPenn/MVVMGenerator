using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using MVVM.Generator.Attributes;
using MVVM.Generator.Extraction;
using MVVM.Generator.Models;
using MVVM.Generator.Rendering;
using MVVM.Generator.Runtime;
using MVVM.Generator.Utilities;

namespace MVVM.Generator.Generators;

[Generator]
public sealed class ViewModelGenerator : IIncrementalGenerator
{
    public const string Suffix = ".ViewModel.cs";

    private static readonly string[] TriggerAttributes =
    [
        typeof(AutoNotifyAttribute).FullName!,
        typeof(AutoCommandAttribute).FullName!,
        typeof(AutoDPropAttribute).FullName!,
        typeof(AutoSPropAttribute).FullName!,
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Emitted unconditionally so the chain plumbing a property setter renders
        // is always present, without the output stage depending on any model.
        context.RegisterPostInitializationOutput(static postInit =>
            postInit.AddSource(
                ChainObserverSource.HintName,
                SourceText.From(ChainObserverSource.Source, Encoding.UTF8)));

        // Projected to a string first: AnalyzerConfigOptionsProvider itself has
        // no value equality and combining it directly would invalidate every
        // model on each compilation.
        var logPath = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => LogConfiguration.Resolve(provider));

        // Projected to a bool so downstream models keep value equality: WPF's
        // CommandManager lives in PresentationCore and is absent on Avalonia.
        var targetsWpf = context.CompilationProvider
            .Select(static (compilation, _) =>
                compilation.GetTypeByMetadataName("System.Windows.Input.CommandManager") is not null);

        // Also projected to a value: generated code refers to the chain observer by
        // an unqualified name, which has to dodge any same-named type in the
        // compilation.
        var observerTypeName = context.CompilationProvider
            .Select(static (compilation, _) => ObserverNaming.Resolve(compilation));

        var renderOptions = targetsWpf
            .Combine(observerTypeName)
            .Select(static (pair, _) => new RenderOptions(pair.Left, pair.Right));

        // Extraction runs once per class, then SelectMany hands each model
        // downstream on its own so an unchanged class skips its output step.
        var models = CollectAttributedClasses(context)
            .Combine(logPath)
            .Select(static (pair, _) =>
            {
                LogManager.Configure(pair.Right);
                return ExtractModels(pair.Left);
            })
            .SelectMany(static (extracted, _) => extracted);

        context.RegisterSourceOutput(
            models.Combine(renderOptions),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    /// <summary>
    /// Compilation-wide choices the output stage needs, as a value so combining
    /// them does not defeat incremental caching.
    /// </summary>
    private sealed record RenderOptions(bool TargetsWpf, string ObserverTypeName);

    private static IncrementalValueProvider<ImmutableArray<INamedTypeSymbol>> CollectAttributedClasses(
        IncrementalGeneratorInitializationContext context)
    {
        var perAttribute = TriggerAttributes
            .Select(attributeName => CollectOwningClasses(context, attributeName))
            .ToArray();

        var merged = perAttribute[0];
        for (var i = 1; i < perAttribute.Length; i++)
        {
            merged = merged
                .Combine(perAttribute[i])
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        }

        return merged;
    }

    private static IncrementalValueProvider<ImmutableArray<INamedTypeSymbol>> CollectOwningClasses(
        IncrementalGeneratorInitializationContext context,
        string attributeName)
    {
        return context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeName,
                // Attributed fields surface as VariableDeclaratorSyntax rather than
                // MemberDeclarationSyntax, so no syntactic narrowing is safe here.
                predicate: static (_, _) => true,
                transform: static (attributeContext, _) => attributeContext.TargetSymbol.ContainingType)
            .Where(static owner => owner is { TypeKind: TypeKind.Class, IsRecord: false })
            .Select(static (owner, _) => owner!)
            .Collect();
    }

    /// <summary>
    /// Deduplicates classes reached through several attributes or partial
    /// declarations, then orders the result so downstream slots stay stable.
    /// </summary>
    private static ImmutableArray<ClassModel> ExtractModels(ImmutableArray<INamedTypeSymbol> classSymbols)
    {
        if (classSymbols.IsDefaultOrEmpty) return ImmutableArray<ClassModel>.Empty;

        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var models = new List<ClassModel>();

        foreach (var classSymbol in classSymbols)
        {
            if (!seen.Add(classSymbol)) continue;

            var model = ClassModelExtractor.Extract(classSymbol);
            if (model != null) models.Add(model);
        }

        return models
            .OrderBy(model => model.Namespace, StringComparer.Ordinal)
            .ThenBy(model => model.ClassName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void Emit(SourceProductionContext context, ClassModel model, RenderOptions options)
    {
        foreach (var diagnostic in model.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        var generatedCode = ClassRenderer.Render(model, options.TargetsWpf, options.ObserverTypeName);
        if (generatedCode == null) return;

        context.AddSource(model.HintName, SourceText.From(generatedCode, Encoding.UTF8));
    }
}

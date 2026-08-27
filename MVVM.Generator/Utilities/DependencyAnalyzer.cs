using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using MVVM.Generator.Attributes;
using MVVM.Generator.Extraction;

namespace MVVM.Generator.Utilities;

/// <summary>
/// Analyzes method/property bodies to find referenced fields and properties
/// for automatic CanExecuteChanged dependency discovery.
/// </summary>
public static class DependencyAnalyzer
{
    private const string LogPrefix = "DependencyAnalyzer: ";

    /// <summary>
    /// Finds all property names that a method or property depends on.
    /// Maps backing fields (e.g., _isPlaying) to their property names (IsPlaying).
    /// </summary>
    public static IReadOnlyList<string> GetDependencies(ISymbol canExecuteSymbol, SemanticModel? semanticModel)
    {
        if (LogManager.IsEnabled)
            LogManager.Log($"{LogPrefix}Analyzing dependencies for {canExecuteSymbol.Name}");

        var dependencies = new HashSet<string>();
        var containingType = canExecuteSymbol.ContainingType;
        var observableMembers = GetObservableMembers(containingType);

        // Get the syntax node for analysis
        var syntaxRef = canExecuteSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef == null)
        {
            if (LogManager.IsEnabled)
                LogManager.Log($"{LogPrefix}No syntax reference found for {canExecuteSymbol.Name}");
            return dependencies.ToList();
        }

        var syntax = syntaxRef.GetSyntax();

        // Find all identifier references in the body
        var identifiers = GetBodyIdentifiers(syntax);

        foreach (var identifier in identifiers)
        {
            var name = identifier.Identifier.Text;

            if (observableMembers.TryGetValue(name, out var propertyName))
            {
                dependencies.Add(propertyName);
                if (LogManager.IsEnabled)
                    LogManager.Log($"{LogPrefix}Found observable dependency: {name} -> {propertyName}");
            }
        }

        if (LogManager.IsEnabled)
            LogManager.Log($"{LogPrefix}Total dependencies for {canExecuteSymbol.Name}: {dependencies.Count}");
        return dependencies.ToList();
    }

    private static IReadOnlyDictionary<string, string> GetObservableMembers(INamedTypeSymbol containingType)
    {
        var members = containingType.GetMembers()
            .OfType<IPropertySymbol>()
            .ToDictionary(property => property.Name, property => property.Name);

        foreach (var field in containingType.GetMembers().OfType<IFieldSymbol>())
        {
            if (!field.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.Name == nameof(AutoNotifyAttribute)))
            {
                continue;
            }

            var propertyName = FieldAttributeReader.GetPropertyName(field);
            members[field.Name] = propertyName;
            members[propertyName] = propertyName;
        }

        return members;
    }

    /// <summary>
    /// Gets all identifier names from the body of a method or property.
    /// </summary>
    private static IEnumerable<IdentifierNameSyntax> GetBodyIdentifiers(SyntaxNode syntax)
    {
        // Handle expression-bodied members: => expression
        var arrowClause = syntax.DescendantNodes().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (arrowClause != null)
        {
            return arrowClause.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
        }

        // Handle block-bodied methods
        var block = syntax.DescendantNodes().OfType<BlockSyntax>().FirstOrDefault();
        if (block != null)
        {
            return block.DescendantNodes().OfType<IdentifierNameSyntax>();
        }

        // Handle property getters
        var getter = syntax.DescendantNodes().OfType<AccessorDeclarationSyntax>()
            .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
        if (getter != null)
        {
            if (getter.ExpressionBody != null)
                return getter.ExpressionBody.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>();
            if (getter.Body != null)
                return getter.Body.DescendantNodes().OfType<IdentifierNameSyntax>();
        }

        return Enumerable.Empty<IdentifierNameSyntax>();
    }

}

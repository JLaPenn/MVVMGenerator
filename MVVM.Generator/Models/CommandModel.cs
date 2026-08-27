namespace MVVM.Generator.Models;

internal sealed record CommandEventInvalidation(
    string SourceTypeName,
    string EventName,
    string DelegateTypeName);

/// <summary>
/// One [AutoCommand] method. Overrides of an already-commanded method still
/// contribute usings but emit no command class, matching existing behaviour.
/// </summary>
internal sealed record CommandModel(
    string MethodName,
    string FieldName,
    string ClassName,
    string OwnerTypeName,
    bool IsStatic,
    bool IsAsync,
    bool IsOverrideOfCommand,
    string? ParameterTypeName,
    string CanExecuteName,
    bool CanExecuteIsProperty,
    EquatableArray<string> Dependencies,
    EquatableArray<CommandEventInvalidation> EventInvalidations,
    EquatableArray<string> AdditionalAttributes,
    EquatableArray<string> Usings);

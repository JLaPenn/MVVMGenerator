namespace MVVM.Generator.Models;

/// <summary>
/// One property access along an observed dependency path, resolved to values so
/// no symbol reaches the output stage.
/// </summary>
/// <param name="PropertyName">
/// Name of the property read on this link's owner. Matched against
/// PropertyChangedEventArgs.PropertyName at runtime.
/// </param>
/// <param name="OwnerTypeName">
/// Type that declares <paramref name="PropertyName"/>, used as the cast target in
/// the generated accessor lambda. Written unqualified, with its namespace carried
/// in the owning chain's usings.
/// </param>
/// <param name="ObserveCollection">
/// True when the getter read this link's contents rather than only its
/// reference, so it also needs a CollectionChanged subscription.
/// </param>
internal sealed record ChainLinkModel(
    string PropertyName,
    string OwnerTypeName,
    bool ObserveCollection);

/// <summary>
/// A dependency path that leaves the declaring class, rooted at an [AutoNotify]
/// property of that class.
/// </summary>
/// <remarks>
/// The head is deliberately not part of <paramref name="Links"/>: reassigning it
/// is already observable through its own generated setter, which is where the
/// re-Attach call is rendered.
/// </remarks>
/// <param name="ObserverFieldName">Backing field for the emitted observer.</param>
/// <param name="HeadPropertyName">
/// The [AutoNotify] property in this class that the path starts from.
/// </param>
/// <param name="Links">The path below the head, ordered outward.</param>
/// <param name="DependentProperties">
/// Properties of this class whose value depends on the path, notified whenever
/// any link changes.
/// </param>
/// <param name="Usings">Namespaces the rendered accessor lambdas need.</param>
internal sealed record ChainModel(
    string ObserverFieldName,
    string HeadPropertyName,
    EquatableArray<ChainLinkModel> Links,
    EquatableArray<string> DependentProperties,
    EquatableArray<string> Usings);

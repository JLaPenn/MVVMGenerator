# MVVM.Generator — LLM Reference

MVVM.Generator is a C# Roslyn incremental source generator that eliminates MVVM boilerplate. It generates `INotifyPropertyChanged` properties, `ICommand` implementations, WPF `DependencyProperty`, and Avalonia `StyledProperty` declarations from annotated fields and methods. All generated code emits into partial classes.

## Installation

Add the `MVVM.Generator` NuGet package. Use `using MVVM.Generator.Attributes;` in consuming files. All target classes **must** be declared `partial`.

The package supplies the generator/analyzers under `analyzers/dotnet/cs`, the attribute assembly under `lib/netstandard2.0`, and `build`/`buildTransitive` props. Local packages build to `artifacts/packages` by default; set MSBuild `PackageOutputPath` to override that location.

---

## Attributes

### `[AutoNotify]` — on fields

Generates a public property with `INotifyPropertyChanged` plumbing. If the class hierarchy doesn't already implement `INotifyPropertyChanged`, the generator adds the interface, event, and `OnPropertyChanged` method.

| Parameter | Type | Default | Effect |
|---|---|---|---|
| `GetterAccess` | `Access` enum | `Public` | Getter visibility (`Private`, `Internal`, `Protected`, `Public`) |
| `SetterAccess` | `Access` enum | `Public` | Setter visibility |
| `IsVirtual` | `bool` | `false` | Emits `virtual` on the property |
| `PropertyChangedHandlerName` | `string?` | `null` | Method invoked on assignment: `void()` or `void(object, EventArgs-or-derived)` |
| `CollectionChangedHandlerName` | `string?` | `null` | Collection handler: `void(object, NotifyCollectionChangedEventArgs-or-derived)`; automatically unsubscribed/resubscribed when the property is assigned |

**Naming convention:** `_fieldName` or `fieldName` → `FieldName`. Prefix `s_` is stripped.

Setters assign and notify on every call; no equality guard is generated. Attributes on the field whose `AttributeUsage` includes `Property` are reconstructed on the generated property. Collection handlers require the field type to implement `INotifyCollectionChanged`.

**Example:**
```csharp
public partial class ViewModel
{
    [AutoNotify(SetterAccess = Access.Private)]
    private string _name;

    [AutoNotify(PropertyChangedHandlerName = nameof(OnStatusChanged))]
    private bool isActive;
    private void OnStatusChanged(object? sender, EventArgs e) { }

    [AutoNotify(CollectionChangedHandlerName = nameof(OnItemsChanged))]
    private ObservableCollection<string> items = new();
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) { }
}
```

### `[DependsOn]` — on fields or properties

Declares that a property depends on one or more other properties. When the referenced property changes, `PropertyChanged` fires for the dependent too.

```csharp
public partial class ViewModel
{
    [AutoNotify] private string firstName;
    [AutoNotify] private string lastName;

    // Automatic: generator scans property body and detects references to AutoNotify fields
    public string FullName => $"{firstName} {lastName}";

    // Explicit: DependsOn adds additional dependency edges
    [DependsOn(nameof(firstName), nameof(lastName))]
    public bool HasName => !string.IsNullOrEmpty(firstName);
}
```

**Dependency direction:** If property A reads field B, then A *depends on* B. When B changes, A is notified. The generator also builds automatic dependencies by scanning property bodies for AutoNotify field references.

`DependsOn` accepts a `params string[]`; names may refer to generated property names or their backing fields. Circular dependencies and references that cannot be resolved to `[AutoNotify]` properties are rejected.

### `[AutoCommand]` — on methods

Generates a nested `ICommand` class and a lazy-initialized command property.

| Parameter | Type | Effect |
|---|---|---|
| `canExecuteMethod` | `string?` | Name of a `bool`-returning method with matching parameters |
| `InvalidatedBy` | `string[]` | Additional owner property names that raise `CanExecuteChanged` |
| `InvalidatedByEventSources` | `Type[]` | Types declaring external static invalidation events |
| `InvalidatedByEvents` | `string[]` | Event names paired by index with `InvalidatedByEventSources` |

**Rules:**
- Method must be `public`
- 0 or 1 parameters
- Return type: `void` or `Task`
- CanExecute method (if specified) must return `bool` with matching parameter count and types
- A parameterless command may use a parameterless `bool` method or `bool` property for CanExecute
- Without a supplied CanExecute member, a parameterless command returns `true`; a parameterized command requires a parameter of the declared type
- Event source and event name arrays must have equal lengths
- External invalidation events must be static, accessible from the generated partial class, and use a void delegate with two parameters

```csharp
public partial class ViewModel
{
    [AutoCommand(nameof(CanSave))]
    public void Save() { /* ... */ }
    public bool CanSave() => IsDirty;

    [AutoCommand]
    public void Delete(int id) { /* ... */ }

    [AutoCommand]
    public static void Reset() { /* ... */ }  // Static commands supported

    [AutoCommand]
    public async Task LoadAsync() { /* ... */ }  // Async commands supported
}
```

**Generated names:** Method `Save` → lazy `ICommand` property `SaveCommand`, nested class `SaveCommandClass`, and explicit invalidator `NotifySaveCommandCanExecuteChanged()`.

`Task` command methods generate `async void ICommand.Execute` and are awaited inside it. Overrides of an already attributed command method reuse the inherited generated command instead of emitting a duplicate.

The generator automatically raises `CanExecuteChanged` when an inferred or explicitly configured owner property changes. Use paired external event arrays for state owned by services:

```csharp
[AutoCommand(
    nameof(CanAddItem),
    InvalidatedBy = new[] { nameof(Current) },
    InvalidatedByEventSources = new[] { typeof(ItemDataCache) },
    InvalidatedByEvents = new[] { nameof(ItemDataCache.CacheUpdated) })]
public void AddItem(Item item) { /* ... */ }
```

Inferred dependencies include owner properties and fields carrying `[AutoNotify]`; explicit `InvalidatedBy` entries are merged with them. The generated command subscribes to owner `PropertyChanged` and raises `CanExecuteChanged` only for those property names. This framework-neutral mechanism works in WPF and Avalonia.

Generated external-event subscriptions hold the command weakly and detach after observing that it was collected. For parameter state without an observable configured event, call the generated `NotifyAddItemCommandCanExecuteChanged()` method explicitly.

### `[AddAttribute]` — on methods (with `[AutoCommand]`)

Adds arbitrary attributes to the generated command property. `AllowMultiple = true`.

Constructor arguments are reproduced on the generated attribute, and the attribute type's namespace is imported.

```csharp
[AutoCommand]
[AddAttribute(typeof(JsonIgnoreAttribute), [])]
public void DoWork() { }
// Generated: [JsonIgnore] public ICommand DoWorkCommand => ...
```

### `[AutoDProp]` — on fields (WPF)

Generates a WPF `DependencyProperty` with static registration and CLR wrapper.

```csharp
public partial class MyControl : Control
{
    [AutoDProp] private string header;
    [AutoDProp] private bool isEnabled = true;
}
// Generated: public static readonly DependencyProperty HeaderProperty = ...
//            public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
```

### `[AutoSProp]` — on fields (Avalonia)

Generates an Avalonia `StyledProperty<T>` with static registration and CLR wrapper.

```csharp
public partial class MyControl : TemplatedControl
{
    [AutoSProp] private string title;
}
// Generated: public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<MyControl, string>(nameof(Title));
//            public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
```

---

## Diagnostics

### Generator Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| MGAN001 | Error | Circular property dependency detected |
| MGAN002 | Error | Cannot generate property for static type |
| MGAN003 | Error | DependsOn references nonexistent/non-AutoNotify property |
| MGAC001 | Error | AutoCommand method must be public |
| MGAC002 | Error | Invalid command method signature (must be void/Task, 0-1 params) |
| MGAC003 | Error | CanExecute method must return bool with matching parameters |
| MGAC004 | Error | Invalid command invalidation arrays, event source, or event signature |
| MGAC101 | Info | Suggestion to add a CanExecute method (disabled by default) |

### Analyzer Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| MAANA001 | Error | Static field cannot have AutoNotify |
| MAANA002 | Error | Generated property name conflicts with existing member |
| MAANA003 | Error | PropertyChangedHandler method invalid (wrong signature or missing) |
| MAANA004 | Error | CollectionChangedHandler method invalid (wrong signature or missing) |
| MAACA001 | Error | AutoCommand method must be public |
| MAACA002 | Error | AutoCommand method must have 0 or 1 parameters |
| MAACA003 | Error | CanExecute method invalid (wrong return type or parameter mismatch) |
| MAACA004 | Error | Generated command class name conflicts with existing member |
| MAACA005 | Warning | Conventionally named CanExecute member exists but is not referenced by `[AutoCommand]` |

### Code Fix

| Diagnostic | Fix |
|---|---|
| MAACA001 | "Make method public" — adds `public` modifier |

---

## Architecture

- **ViewModelGenerator** (`IIncrementalGenerator`): Discovers owning classes with `ForAttributeWithMetadataName`, deduplicates partial declarations, extracts immutable value models, and emits one source file per class.
- **ClassModelExtractor**: Coordinates validation and extraction for all supported attributes.
- **NotifyFieldExtractor**, **CommandExtractor**, **BackingPropertyExtractor**: Convert Roslyn symbols and attribute data into structurally equatable models; symbols do not reach rendering.
- **ClassRenderer**: Assembles usings, interfaces, nested command classes, fields, and properties into the generated partial class.
- **NotifyPropertyRenderer**: Emits notifying properties, configured handlers, collection subscriptions, and dependent-property notifications.
- **CommandClassRenderer**: Emits nested `ICommand` implementations, owner/external invalidation subscriptions, execution, and explicit invalidators.
- **BackingPropertyRenderer**: Emits WPF dependency properties and Avalonia styled properties.
- **AttributeProcessor** (`IDependencyAnalyzer`): Builds the reverse property dependency map from property bodies and `[DependsOn]` attributes.
- **DependencyAnalyzer**: Discovers owner-property and `[AutoNotify]` field references used by CanExecute members.
- **CanExecuteResolver**: Shared CanExecute lookup/signature logic used by generation and analysis.
- **TypeHelper**: Resolves type names including generics, nullables, arrays, and C# keyword aliases.

Incremental models use `EquatableArray<T>` for structural equality so unchanged classes can skip downstream emission.

## Generator Logging

Logging is disabled by default. Set `MVVMGeneratorLogPath` in a consuming project to opt in:

```xml
<PropertyGroup>
    <MVVMGeneratorLogPath>artifacts/mvvm-generator.log</MVVMGeneratorLogPath>
</PropertyGroup>
```

Absolute paths are used directly. Relative paths are anchored to `MSBuildProjectDirectory`; logging remains disabled when the path is empty or cannot be anchored.

## Key Constraints

1. Target classes must be `partial`
2. AutoNotify fields must not be `static`
3. AutoCommand methods must be `public` with 0–1 parameters returning `void` or `Task`
4. Handler signatures are strict: `void()` or `void(object, EventArgs-or-derived)` for property changes; `void(object, NotifyCollectionChangedEventArgs-or-derived)` for collection changes
5. Circular dependencies are rejected
6. `DependsOn` can reference either field names or property names
7. Inheritance is supported — derived classes reuse base `INotifyPropertyChanged` implementation
8. Static methods generate static commands (no owner reference)
9. External command invalidation currently supports accessible static two-parameter events
10. Parameter-object property changes are not inferred; configure an observable event or call the generated command invalidator

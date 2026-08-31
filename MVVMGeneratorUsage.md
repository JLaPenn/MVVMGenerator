# MVVM Generator Usage Guide

## Property Generation

### Basic Properties
```csharp
public partial class ViewModel
{
    [AutoNotify] private string name = string.Empty;
    [AutoNotify] private int age;
}
```

### Access Control
```csharp
public partial class ViewModel
{
    // Public get, private set
    [AutoNotify(GetterAccess = Access.Public, SetterAccess = Access.Private)]
    private string internalValue;

    // Protected get, internal set
    [AutoNotify(GetterAccess = Access.Protected, SetterAccess = Access.Internal)]
    private int restrictedValue;
}
```

### Change Notifications
```csharp
public partial class ViewModel
{
    // Custom property changed handler
    [AutoNotify(PropertyChangedHandlerName = nameof(OnBalanceChanged))]
    private decimal balance;

    private void OnBalanceChanged(object? sender, EventArgs e)
    {
        // Custom handling when balance changes
    }

    // Collection change notifications
    [AutoNotify(CollectionChangedHandlerName = nameof(OnItemsChanged))]
    private ObservableCollection<string> items = new();

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            // Handle new items
        }
    }
}
```

## Property Dependencies

### Automatic Dependencies
```csharp
public partial class ViewModel
{
    [AutoNotify] private string firstName;
    [AutoNotify] private string lastName;
    
    // Automatically updates when firstName or lastName change
    public string FullName => $"{firstName} {lastName}";
}
```

### Complex Dependencies
```csharp
public partial class ViewModel
{
    public bool IsReset {
        get => !string.IsNullOrEmpty(firstName) || !string.IsNullOrEmpty(lastName);
        set {
            firstName = null;
            lastName = null;
        }
    }
    [AutoNotify] private string firstName;
    [AutoNotify] private string lastName;
    
    [DependsOn(nameof(IsReset))]
    public string DisplayName => $"{firstName} {lastName}";
}
```

### Chained Dependencies

A computed property that reads through another object is wired up automatically —
no attribute is needed, and `[DependsOn]` cannot express a path.

```csharp
public partial class EditorViewModel
{
    [AutoNotify] private bool isEnabled = true;
    [AutoNotify] private Document document;

    // Observed automatically: isEnabled, Document.IsReadOnly,
    // Document.Sections (contents) and Document.Title.
    public bool CanEdit =>
        isEnabled
        && !Document.IsReadOnly
        && (Document.Sections.Count > 0 || !string.IsNullOrEmpty(Document.Title));
}
```

Every link is re-subscribed when it is replaced, so swapping `Document` for a new
instance, or assigning a new collection to `Sections`, moves the subscriptions and
releases the old ones. Reads off an observable collection —
`Count`, an indexer, a LINQ call — are watched through `CollectionChanged`, which
covers collection types that do not raise `PropertyChanged` for `Count`.

Requirements and limits:

- The path must start at an `[AutoNotify]` property of the same class. That
  property's generated setter is where the subscription is (re)attached, so a
  path rooted in a hand-written property or a base-class property is not observed.
- Every intermediate link must implement `INotifyPropertyChanged` — a class using
  `[AutoNotify]` counts, even though the generator has not added the interface yet.
- Paths through indexers, casts, method arguments or static members are not
  modelled. Enable `MGAN101` to have the generator report the reads it could not
  observe, which is the practical way to audit a codebase migrated from a
  framework that wove notifications automatically.

The generated code imports `MVVM.Generator.Runtime` and writes `ChainObserver`
unqualified. If your own code declares a type called `ChainObserver`, the
generator detects it and emits an alias instead — `MGChainObserver`, then
`MGChainObserver2` and upwards — so the unqualified form is only used when it is
unambiguous. Only types declared in the compilation's own source are checked, which
is what detection needs to cover: C# resolves a type in the enclosing namespace
ahead of any using directive, alias included, so an undetected same-named type
would win. A type arriving from a referenced assembly is not detected, but the
generated code uses `ChainObserver.Link` and a specific constructor, so an
impostor of that name fails to compile rather than binding silently.

## Commands

### Basic Commands
```csharp
public partial class ViewModel
{
    [AutoCommand]
    public void Save()
    {
        // Implementation
    }
}
```

### Parameterized Commands
```csharp
public partial class ViewModel
{
    [AutoCommand]
    public void DeleteItem(int id)
    {
        // Delete implementation
    }
}
```

### Commands with Validation
```csharp
public partial class ViewModel
{
    [AutoCommand(nameof(CanSubmit))]
    public void Submit()
    {
        // Submit implementation
    }

    public bool CanSubmit() => IsValid && !IsBusy;
}
```

## WPF Dependency Properties
```csharp
public partial class CustomControl : Control
{
    [AutoDProp]
    private string header;

    [AutoDProp]
    private bool isEnabled = true;
}
```

## Usage Notes

1. Classes must be partial
2. Include 

MVVM.Generator

 NuGet package
3. Import namespace: `using MVVM.Generator.Attributes`
4. Commands must be public methods
5. Property changed handlers must match signature: `void (object?, EventArgs)`
6. Collection changed handlers must match signature: `void (object?, NotifyCollectionChangedEventArgs)`
7. Dependency properties only work in WPF controls
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

### Refreshing Command Availability

The generator automatically refreshes a command when its `CanExecute` member reads an observable property on the view model. Add owner properties that cannot be inferred with `InvalidatedBy`:

```csharp
[AutoCommand(
    nameof(CanSubmit),
    InvalidatedBy = new[] { nameof(SessionState) })]
public void Submit() { }
```

External static events use paired source and event arrays. Entries at the same index form one subscription:

```csharp
[AutoCommand(
    nameof(CanAddItem),
    InvalidatedByEventSources = new[]
    {
        typeof(ItemDataCache),
        typeof(LoginManager)
    },
    InvalidatedByEvents = new[]
    {
        nameof(ItemDataCache.CacheUpdated),
        nameof(LoginManager.LoginChanged)
    })]
public void AddItem(Item item) { }
```

Event sources must be accessible static events using a void delegate with two parameters. Generated subscriptions hold commands weakly. When availability depends on parameter state that has no configured event, notify the command explicitly:

```csharp
public partial class ViewModel
{
    [AutoCommand(nameof(CanDelete))]
    public void Delete(Item item) { }

    public bool CanDelete(Item item) => item.CanBeDeleted;

    public void OnItemStateChanged()
    {
        NotifyDeleteCommandCanExecuteChanged();
    }
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
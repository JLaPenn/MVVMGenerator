using MVVM.Generator.Attributes;

namespace TestViewModels;

public static class CommandState
{
    public static event System.EventHandler? Changed;

    public static void NotifyChanged() => Changed?.Invoke(null, System.EventArgs.Empty);
}

public partial class Class1
{
    [AutoNotify] private string name = string.Empty;
    [AutoNotify] private bool _isBusy;

    [AutoCommand(
        nameof(CanSave),
        InvalidatedBy = new[] { nameof(Identity) },
        InvalidatedByEventSources = new[] { typeof(CommandState) },
        InvalidatedByEvents = new[] { nameof(CommandState.Changed) })]
    public void Save() { }

    public bool CanSave() => !string.IsNullOrEmpty(Name) && !_isBusy;
}

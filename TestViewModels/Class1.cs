using MVVM.Generator.Attributes;

namespace TestViewModels;

public partial class Class1
{
    [AutoNotify] private string name = string.Empty;
    [AutoNotify] private bool _isBusy;

    [AutoCommand(nameof(CanSave))]
    public void Save() { }

    public bool CanSave() => !string.IsNullOrEmpty(Name) && !_isBusy;
}

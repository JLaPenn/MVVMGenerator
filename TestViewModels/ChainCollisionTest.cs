using MVVM.Generator.Attributes;

namespace TestViewModels;

/// <summary>
/// A consuming type occupying the observer's preferred name, so the generated
/// code has to fall back to an alias.
/// </summary>
public class ChainObserver
{
}

public partial class CollisionInner
{
    [AutoNotify] private bool enabled;
}

public partial class CollisionOuter
{
    [AutoNotify] private CollisionInner inner = new();

    public bool IsEnabled => Inner.Enabled;
}

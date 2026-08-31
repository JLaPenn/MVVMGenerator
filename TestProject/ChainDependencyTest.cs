using System.Collections.ObjectModel;

using MVVM.Generator.Attributes;

namespace TestProject;

/// <summary>
/// Compile-shape coverage for chained dependencies: each computed property is
/// named for the path shape it exercises.
/// </summary>
public partial class ChainRoot
{
    [AutoNotify] private bool localField = true;
    [AutoNotify] private ChainMiddle middle = new();

    /// <summary>A same-class field combined with a single link outward.</summary>
    public bool ReadsLocalFieldAndOneLink => localField && Middle.Flag;

    /// <summary>Collection contents, reached through one link.</summary>
    public bool ReadsCollectionCount => Middle.Items.Count > 1;

    /// <summary>Three links deep, so intermediate swaps must re-subscribe.</summary>
    public bool ReadsThreeLinks => Middle.Leaf.Flag;

    /// <summary>The same path written with null-conditional access.</summary>
    public string? ReadsNullConditionalLink => Middle?.Label;

    /// <summary>A LINQ call on the collection rather than a Count read.</summary>
    public bool ReadsCollectionViaLinq => Middle.Items.Any();
}

public partial class ChainMiddle
{
    [AutoNotify] private bool flag;
    [AutoNotify] private string label = string.Empty;
    [AutoNotify] private ObservableCollection<string> items = new();
    [AutoNotify] private ChainLeaf leaf = new();
}

public partial class ChainLeaf
{
    [AutoNotify] private bool flag;
}

/// <summary>Coverage for the [DependsOn] validation paths.</summary>
public partial class DependsOnValidation
{
    [AutoNotify] private int notifyingSource;

    private int manualBackingField;

    /// <summary>Names a real but hand-written property: MGAN102 warning.</summary>
    [DependsOn(nameof(ManualProperty))]
    public int ReadsManualProperty => ManualProperty;

    public int ManualProperty
    {
        get => manualBackingField;
        set => manualBackingField = value;
    }

    /// <summary>Names an [AutoNotify] property: no diagnostic.</summary>
    [DependsOn(nameof(NotifyingSource))]
    public int ReadsNotifyingSource => NotifyingSource * 2;
}

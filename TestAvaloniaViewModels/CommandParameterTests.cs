using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;

using Xunit;

namespace TestAvaloniaViewModels;

public sealed class CommandParameterTests
{
    [AvaloniaFact]
    public void MenuItemReevaluatesGeneratedCommandWhenParameterChanges()
    {
        var viewModel = new StyledPropertyTest();
        var menuItem = new MenuItem
        {
            Command = viewModel.SelectCommand,
            CommandParameter = string.Empty
        };
        var window = new Window { Content = menuItem };

        window.Show();
        var callsBeforeChange = viewModel.CanSelectCallCount;

        Assert.True(((ILogical)menuItem).IsAttachedToLogicalTree);
        Assert.True(callsBeforeChange > 0);
        Assert.False(menuItem.IsEffectivelyEnabled);

        menuItem.CommandParameter = "second";

        Assert.True(viewModel.CanSelectCallCount > callsBeforeChange);
        Assert.Equal("second", viewModel.LastCanSelectValue);
        Assert.True(menuItem.IsEffectivelyEnabled);

        window.Close();
    }
}
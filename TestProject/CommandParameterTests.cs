using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

using MVVM.Generator.Attributes;

using Xunit;

namespace TestProject;

public sealed class CommandParameterTests
{
    [Fact]
    public void ObservableParameterPropertyRaisesCanExecuteChangedAfterEvaluation()
    {
        var viewModel = new WpfCommandViewModel();
        var item = new ObservableCommandItem();
        var canExecuteChangedCount = 0;
        viewModel.DeleteCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        Assert.False(viewModel.DeleteCommand.CanExecute(item));

        item.CanDelete = true;

        Assert.Equal(1, canExecuteChangedCount);
        Assert.True(viewModel.DeleteCommand.CanExecute(item));
    }

    [Fact]
    public void ExternalEventRaisesCanExecuteChanged()
    {
        var viewModel = new WpfCommandViewModel();
        var canExecuteChangedCount = 0;
        viewModel.RefreshCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;

        CommandInvalidationState.NotifyChanged();

        Assert.Equal(1, canExecuteChangedCount);
    }

    [WpfFact]
    public void ReusedContextMenuUsesCurrentPlacementTargetDataContext()
    {
        var viewModel = new WpfCommandViewModel();
        var firstItem = new ObservableCommandItem { CanDelete = false };
        var secondItem = new ObservableCommandItem { CanDelete = true };
        var contextMenu = new ContextMenu();
        var menuItem = new MenuItem();
        menuItem.SetBinding(MenuItem.CommandParameterProperty, new Binding
        {
            Path = new PropertyPath($"{nameof(ContextMenu.PlacementTarget)}.{nameof(FrameworkElement.DataContext)}"),
            RelativeSource = new RelativeSource(
                RelativeSourceMode.FindAncestor,
                typeof(ContextMenu),
                1)
        });
        menuItem.Command = viewModel.DeleteCommand;
        contextMenu.Items.Add(menuItem);
        var target = new Button
        {
            ContextMenu = contextMenu,
            DataContext = firstItem
        };
        var window = new Window { Content = target };

        try
        {
            window.Show();
            OpenContextMenu(target, contextMenu);

            var callsBeforeChange = viewModel.CanDeleteCallCount;

            Assert.Equal(DependencyProperty.UnsetValue, contextMenu.ReadLocalValue(FrameworkElement.DataContextProperty));
            Assert.Same(firstItem, menuItem.CommandParameter);
            Assert.True(callsBeforeChange > 0);
            Assert.False(menuItem.IsEnabled);

            contextMenu.IsOpen = false;
            target.DataContext = secondItem;
            OpenContextMenu(target, contextMenu);

            Assert.Equal(DependencyProperty.UnsetValue, contextMenu.ReadLocalValue(FrameworkElement.DataContextProperty));
            Assert.Same(secondItem, menuItem.CommandParameter);
            Assert.True(viewModel.CanDeleteCallCount > callsBeforeChange);
            Assert.Same(secondItem, viewModel.LastCanDeleteItem);
            Assert.True(menuItem.IsEnabled);
        }
        finally
        {
            contextMenu.IsOpen = false;
            window.Close();
        }
    }

    private static void OpenContextMenu(FrameworkElement target, ContextMenu contextMenu)
    {
        contextMenu.PlacementTarget = target;
        contextMenu.IsOpen = true;
        contextMenu.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.True(contextMenu.IsOpen);
        Assert.Same(target, contextMenu.PlacementTarget);
    }
}

public partial class WpfCommandViewModel
{
    private ObservableCommandItem? lastCanDeleteItem;
    private int canDeleteCallCount;

    public ObservableCommandItem? LastCanDeleteItem => lastCanDeleteItem;
    public int CanDeleteCallCount => canDeleteCallCount;

    [AutoCommand(nameof(CanDelete))]
    public void Delete(ObservableCommandItem item) { }

    [AutoCommand(
        InvalidatedByEventSources = new[] { typeof(CommandInvalidationState) },
        InvalidatedByEvents = new[] { nameof(CommandInvalidationState.Changed) })]
    public void Refresh() { }

    public bool CanDelete(ObservableCommandItem item)
    {
        lastCanDeleteItem = item;
        canDeleteCallCount++;
        return item.CanDelete;
    }
}

public sealed class ObservableCommandItem : INotifyPropertyChanged
{
    private bool canDelete;

    public bool CanDelete
    {
        get => canDelete;
        set
        {
            canDelete = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDelete)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class CommandInvalidationState
{
    public static event EventHandler? Changed;

    public static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);
}
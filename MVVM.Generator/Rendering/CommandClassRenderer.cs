using System.Collections.Generic;
using System.Linq;

using MVVM.Generator.Models;

namespace MVVM.Generator.Rendering;

/// <summary>
/// Renders the nested ICommand implementation for one [AutoCommand] method.
/// Literals are carried over unchanged from the previous generator.
/// </summary>
internal static class CommandClassRenderer
{
    public static void AddCommandClass(List<string> definitions, CommandModel command, bool targetsWpf)
    {
        string callerSource = command.IsStatic ? command.OwnerTypeName : "_owner";
        bool hasDependencies = command.Dependencies.Length > 0;

        // For async methods, use await; for sync, just call directly
        var awaitPrefix = command.IsAsync ? "await " : "";
        var methodCall = $"""
                {awaitPrefix}{callerSource}.{command.MethodName}();
""";

        // For properties, access directly; for methods, call with ()
        var canExecuteInvocation = command.CanExecuteIsProperty
            ? command.CanExecuteName
            : $"{command.CanExecuteName}()";
        var canExecute = !string.IsNullOrEmpty(command.CanExecuteName)
            ? $"""
                return {callerSource}.{canExecuteInvocation};
"""
            : """
                return true;
""";

        if (command.ParameterTypeName is { } parameterType)
        {
            methodCall = $$"""
                if(parameter is not {{parameterType}} typedParameter) return;
                {{awaitPrefix}}{{callerSource}}.{{command.MethodName}}(typedParameter);
""";

            // For parameterized commands, CanExecute must be a method (properties can't take parameters)
            canExecute = !string.IsNullOrEmpty(command.CanExecuteName)
                ? $$"""
                if(parameter is not {{parameterType}} typedParameter) return false;
                return {{callerSource}}.{{command.CanExecuteName}}(typedParameter);
"""
                : $"""
                return parameter is {parameterType};
""";
        }

        var ownerField = command.IsStatic
            ? """

"""
            : $$"""
            readonly {{command.OwnerTypeName}} _owner;

""";

        string ctorBody;
        string disposeMethod = "";

        if (command.IsStatic)
        {
            ctorBody = "";
        }
        else if (hasDependencies)
        {
            ctorBody = $"""
                _owner = owner;
                _owner.PropertyChanged += OnOwnerPropertyChanged;
""";
            disposeMethod = PropertyChangedHandler(command.Dependencies, targetsWpf);
        }
        else
        {
            ctorBody = """
                _owner = owner;
""";
        }

        var constructor = $$"""
            public {{command.ClassName}}({{(command.IsStatic ? string.Empty : $"{command.OwnerTypeName} owner")}})
            {
{{ctorBody}}
            }
""";

        // Use async void for Execute when method is async (standard ICommand pattern)
        var asyncModifier = command.IsAsync ? "async " : "";

        // WPF: route CanExecuteChanged through CommandManager so reused command
        // sources (e.g. a ContextMenu across targets) requery on interaction.
        // Avalonia has no CommandManager and requeries on open/parameter change,
        // so the plain field-like event is sufficient there.
        var eventDeclaration = targetsWpf
            ? """
            public event EventHandler? CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }
"""
            : """
            public event EventHandler? CanExecuteChanged;
""";

        // A CommandManager-delegated event has explicit accessors, so CS0067
        // never applies; only the plain Avalonia event can go unused.
        var pragmaDisable = (!targetsWpf && !hasDependencies) ? "#pragma warning disable CS0067 // Event is never used\n" : "";
        var pragmaRestore = (!targetsWpf && !hasDependencies) ? "#pragma warning restore CS0067\n" : "";

        definitions.Add($$"""
{{pragmaDisable}}        public class {{command.ClassName}} : ICommand
        {
{{eventDeclaration}}

{{ownerField}}
{{constructor}}
            public bool CanExecute(object? parameter)
            {
{{canExecute}}
            }

            public {{asyncModifier}}void Execute(object? parameter)
            {
{{methodCall}}
            }
{{disposeMethod}}
        }
{{pragmaRestore}}
""");
    }

    private static string PropertyChangedHandler(EquatableArray<string> dependencies, bool targetsWpf)
    {
        var propertyChecks = string.Join(" || ", dependencies.Select(d => $"e.PropertyName == \"{d}\""));

        // WPF pumps requery globally; Avalonia listens to the command's own event.
        var raiseInvalidation = targetsWpf
            ? "CommandManager.InvalidateRequerySuggested();"
            : "CanExecuteChanged?.Invoke(this, EventArgs.Empty);";

        return $$"""

            private void OnOwnerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if ({{propertyChecks}})
                {
                    {{raiseInvalidation}}
                }
            }
""";
    }
}

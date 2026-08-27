using System;

namespace MVVM.Generator.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AutoCommandAttribute : Attribute
{
    public string? CanExecuteMethod { get; }
    public string[] InvalidatedBy { get; set; } = Array.Empty<string>();
    public Type[] InvalidatedByEventSources { get; set; } = Array.Empty<Type>();
    public string[] InvalidatedByEvents { get; set; } = Array.Empty<string>();

    public AutoCommandAttribute() { }
    public AutoCommandAttribute(string canExecuteMethod)
    {
        CanExecuteMethod = canExecuteMethod;
    }
}
using System.Reflection;

namespace FODevManager.Operations;

// Capture dialog intent before dispatching work, then apply it to freshly loaded state
public sealed class PropertyEdits<T>
{
    private readonly List<(PropertyInfo Property, object? Value)> changes = new();

    public PropertyEdits(T original, T edited, params string[] editableProperties)
    {
        foreach (var name in editableProperties)
        {
            var property = typeof(T).GetProperty(name) ?? throw new ArgumentException($"Unknown property '{name}'");
            var value = property.GetValue(edited);
            if (!Equals(property.GetValue(original), value)) changes.Add((property, value));
        }
    }

    public bool HasChanges => changes.Count != 0;

    public void Apply(T fresh)
    {
        foreach (var change in changes) change.Property.SetValue(fresh, change.Value);
    }

    public void Write(Action<string, object?> write)
    {
        foreach (var change in changes) write(change.Property.Name, change.Value);
    }
}

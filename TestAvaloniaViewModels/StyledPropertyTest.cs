using Avalonia;

using MVVM.Generator.Attributes;

namespace TestAvaloniaViewModels
{
    public partial class StyledPropertyTest : AvaloniaObject
    {
        [AutoSProp] private string title;
        [AutoSProp] private int count;

        private string? lastCanSelectValue;
        private int canSelectCallCount;

        public string? LastCanSelectValue => lastCanSelectValue;
        public int CanSelectCallCount => canSelectCallCount;

        [AutoCommand(nameof(CanSelect))]
        public void Select(string value) => Title = value;

        public bool CanSelect(string value)
        {
            lastCanSelectValue = value;
            canSelectCallCount++;
            return !string.IsNullOrEmpty(value);
        }
    }
}

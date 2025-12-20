
using System;
using System.Threading;

namespace FODevManager.Utils
{
    public static class OperationScope
    {
        private static readonly AsyncLocal<Guid?> _current = new();

        public static Guid? CurrentId
        {
            get => _current.Value;
            private set => _current.Value = value;
        }

        public static IDisposable Begin(Guid id) => new Scope(id);

        private sealed class Scope : IDisposable
        {
            private readonly Guid? _prev;
            public Scope(Guid id)
            {
                _prev = CurrentId;
                CurrentId = id;
            }
            public void Dispose() => CurrentId = _prev;
        }
    }
}

// FODevManager.WinUI/BusyOverlayViewModel.cs
using FODevManager.Messages;
using FODevManager.Utils;
using FODevManager.WinUI.Framework;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FODevManager.WinUI
{
    public sealed class BusyOverlayViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly BusyUiMessageSubscriber _subscriber;

        private bool _isBusy;
        private string _busyText = "Working…";
        private Guid? _activeOperationId;

        public ObservableCollection<string> LogLines { get; } = new();

        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnChanged(); } }
        public string BusyText { get => _busyText; set { _busyText = value; OnChanged(); } }

        public BusyOverlayViewModel()
        {
            _dispatcher = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("Create BusyOverlayViewModel on the UI thread.");

            var busy = Singleton<BusyHandler>.Instance;

            busy.BusyChanged += (isBusy, text, opId) =>
            {
                _dispatcher.TryEnqueue(() =>
                {
                    if (isBusy)
                    {
                        // new operation begins: clear and lock to this id
                        LogLines.Clear();
                        _activeOperationId = opId;
                    }
                    else
                    {
                        // operation ends
                        _activeOperationId = null;
                    }

                    IsBusy = isBusy;
                    if (!string.IsNullOrWhiteSpace(text))
                        BusyText = text!;
                });
            };

            // pass a delegate that returns the currently active operation id
            _subscriber = new BusyUiMessageSubscriber(LogLines, _dispatcher, () => _activeOperationId);
        }

        public void Dispose() => _subscriber.Dispose();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

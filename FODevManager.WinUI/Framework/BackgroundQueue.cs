using FODevManager.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FODevManager.WinUI.Framework
{
    public sealed class BackgroundQueue : IAsyncDisposable
    {
        private readonly Channel<Func<CancellationToken, Task>> _channel;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _runnerTask;

        public BackgroundQueue()
        {
            _channel = Channel.CreateUnbounded<Func<CancellationToken, Task>>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _runnerTask = Task.Run(RunAsync);
        }

        public bool TryEnqueue(Func<CancellationToken, Task> workItem)
        {
            if (workItem == null)
                throw new ArgumentNullException(nameof(workItem));

            return _channel.Writer.TryWrite(workItem);
        }

        private async Task RunAsync()
        {
            try
            {
                await foreach (var workItem in _channel.Reader.ReadAllAsync(_cancellationTokenSource.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await workItem(_cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown
                    }
                    catch (Exception exception)
                    {
                        MessageLogger.Error($"BackgroundQueue item failed: {exception.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"BackgroundQueue runner crashed: {exception.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource.Cancel();
            _channel.Writer.TryComplete();

            try
            {
                await _runnerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cancellationTokenSource.Dispose();
            }
        }
    }
}

using System;
using System.Threading;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Transfers
{
    // Moves transfers on once a second for as long as Nodearr runs. A transfer that was under way
    // when Nodearr last stopped is picked up again on the first pass.
    public class TransferRunner : IHandle<ApplicationStartedEvent>, IHandle<ApplicationShutdownRequested>
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        private readonly ITransferService _transfers;
        private readonly Logger _logger;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly object _lock = new();

        private Thread _thread;

        public TransferRunner(ITransferService transfers, Logger logger)
        {
            _transfers = transfers;
            _logger = logger;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            lock (_lock)
            {
                if (_thread != null)
                {
                    return;
                }

                _thread = new Thread(Run) { IsBackground = true, Name = "transfers" };
                _thread.Start();
            }
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _shutdown.Cancel();
        }

        private void Run()
        {
            while (!_shutdown.Token.WaitHandle.WaitOne(Interval))
            {
                try
                {
                    _transfers.Tick();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to move transfers on");
                }
            }
        }
    }
}

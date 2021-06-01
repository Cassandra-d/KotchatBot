using NLog;
using System;
using System.Threading.Tasks;
using KotchatBot.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace KotchatBot.Core
{
    public class Manager : IWorker
    {
        private const int NEXT_MESSAGE_WAIT_INTERVAL_SECONDS = 1;

        private readonly MessageSender _messageSender;
        private readonly UserMessagesParser _userMessagesParser;
        private readonly IRandomImageSource[] _imagesSource;
        private readonly Logger _log;
        private readonly CancellationTokenSource _cts;

        public Manager(MessageSender messageSender, UserMessagesParser userMessagesParser, IEnumerable<IRandomImageSource> imagesSource)
        {
            _messageSender = messageSender;
            _userMessagesParser = userMessagesParser;
            _imagesSource = imagesSource.ToArray();
            _log = LogManager.GetCurrentClassLogger();
            _cts = new CancellationTokenSource();

            Task.Factory.StartNew(() => WorkerLoop());

            _log.Info($"Manager started");
        }

        private async Task WorkerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                var message = _userMessagesParser.GetNextMessage(TimeSpan.FromSeconds(NEXT_MESSAGE_WAIT_INTERVAL_SECONDS));
                if (message == null)
                {
                    continue;
                }

                try
                {
                    var image = await _imagesSource[0].NextFile();
                    _messageSender.Send($">>{message.Value.id}", image);
                }
                catch (Exception ex)
                {
                    _log.Error(ex);
                }
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _messageSender.Stop();
            _userMessagesParser.Stop();
            _log.Info("Cancelling");
        }
    }
}
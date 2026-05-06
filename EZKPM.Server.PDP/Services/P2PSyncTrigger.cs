using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EZKPM.Server.PDP.Services
{
    public class P2PSyncTrigger
    {
        private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });

        public void Trigger()
        {
            _channel.Writer.TryWrite(true);
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            await _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}

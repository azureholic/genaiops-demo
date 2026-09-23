using System.Collections.Concurrent;
using System.Threading.Channels;
using GenAIOps.Application.Shadow;

namespace GenAIOps.Infrastructure.Shadow;

public sealed class InMemoryShadowWorkQueue : IShadowWorkQueue
{
    private readonly Channel<ShadowWorkDelivery> channel =
        Channel.CreateUnbounded<ShadowWorkDelivery>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly ConcurrentQueue<(ShadowWorkDelivery Delivery, string Reason)> deadLetters = new();

    public IReadOnlyCollection<(ShadowWorkDelivery Delivery, string Reason)> DeadLetters =>
        deadLetters.ToArray();

    public bool TryPublish(ShadowWorkItem work) =>
        channel.Writer.TryWrite(new ShadowWorkDelivery(work, Attempt: 1));

    public ValueTask<ShadowWorkDelivery> ReceiveAsync(
        CancellationToken cancellationToken = default) =>
        channel.Reader.ReadAsync(cancellationToken);

    public void Complete(ShadowWorkDelivery delivery)
    {
    }

    public void Retry(ShadowWorkDelivery delivery)
    {
        if (!channel.Writer.TryWrite(delivery with { Attempt = delivery.Attempt + 1 }))
        {
            throw new InvalidOperationException("The shadow queue is not accepting retries.");
        }
    }

    public void DeadLetter(ShadowWorkDelivery delivery, string reason) =>
        deadLetters.Enqueue((delivery, reason));
}

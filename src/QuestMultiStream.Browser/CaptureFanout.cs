using System.Buffers.Binary;
using System.Threading.Channels;

namespace QuestMultiStream.Browser;

// Transient shared packet references only. No video evidence files or producer
// cancellation on observer disconnect/overflow. A late observer needs a bounded
// complete codec/keyframe GOP, or waits for the next genuine keyframe.
public sealed class CaptureFanout
{
    public const int MaximumQueuedBytes = 4 * 1024 * 1024, MaximumQueuedPackets = 128;
    public const int MaximumObservers = 8, MaximumConfigBytes = 256 * 1024;
    private readonly object sync = new();
    private readonly List<Subscription> observers = [];
    private readonly List<byte[]> gop = [];
    private byte[]? config; private int gopBytes; private bool completed,invalidConfig;
    private DateTimeOffset keyUtc;
    public readonly TaskCompletionSource<object> Metadata = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void SetMetadata(object metadata) => Metadata.TrySetResult(metadata);
    public Subscription Subscribe()
    {
        lock(sync) {
            if (completed) throw new InvalidOperationException("Capture already retired");
            if(invalidConfig)throw new InvalidOperationException("Observer codec configuration unavailable");
            if(observers.Count>=MaximumObservers)throw new InvalidOperationException("Observer capacity reached");
            var observer = new Subscription(this);
            if (config is not null && !observer.Offer(config)){observer.Dispose();throw new InvalidOperationException("Observer bootstrap exceeds bound");}
            if (gop.Count > 0 && DateTimeOffset.UtcNow-keyUtc <= TimeSpan.FromSeconds(2)) {
                foreach(var packet in gop)if(!observer.Offer(packet)){observer.Dispose();throw new InvalidOperationException("Observer GOP bootstrap exceeds bound");}
                observer.WaitingKey=false;
            }
            observers.Add(observer); return observer;
        }
    }
    public void Publish(byte[] packet)
    {
        if(packet.Length < 12) throw new InvalidDataException("Malformed fanout packet");
        var flags=BinaryPrimitives.ReadUInt64BigEndian(packet);
        var isConfig=(flags & (1UL<<63))!=0; var isKey=(flags & (1UL<<62))!=0;
        lock(sync) {
            if(completed)return;
            if(isConfig && packet.Length>MaximumConfigBytes) {
                invalidConfig=true;config=null;gop.Clear();gopBytes=0;
                foreach(var observer in observers)observer.End(new IOException("Observer codec configuration exceeds bound"));observers.Clear();return;
            }
            if(isConfig) { invalidConfig=false;config=packet;gop.Clear();gopBytes=0;foreach(var observer in observers)observer.WaitingKey=true; }
            else if(isKey) { gop.Clear();gopBytes=0;keyUtc=DateTimeOffset.UtcNow; }
            if(!isConfig && (isKey || gop.Count > 0)) {
                if(gopBytes+packet.Length <= MaximumQueuedBytes-(config?.Length??0) && gop.Count < MaximumQueuedPackets-1) {gop.Add(packet);gopBytes+=packet.Length;}
                else {gop.Clear();gopBytes=0;}
            }
            foreach(var observer in observers.ToArray()) {
                if(!isConfig && observer.WaitingKey && !isKey)continue;
                if(isKey)observer.WaitingKey=false;
                if(!observer.Offer(packet)) {observers.Remove(observer);observer.End(new IOException("Observer too slow; reconnect viewer only"));}
            }
        }
    }
    public void Complete(Exception? error=null)
    {
        lock(sync) {
            completed=true;Metadata.TrySetException(error??new IOException("Capture ended before metadata"));
            foreach(var observer in observers)observer.End(error);observers.Clear();gop.Clear();gopBytes=0;config=null;
        }
    }
    private void Remove(Subscription observer) {lock(sync){observers.Remove(observer);observer.End();}}
    public sealed class Subscription(CaptureFanout owner) : IDisposable
    {
        private readonly Channel<byte[]> packets=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaximumQueuedPackets){SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait});
        private int bytes;
        private readonly CancellationTokenSource ended=new();private int finished,disposed;
        public CancellationToken Closed=>ended.Token;
        public bool WaitingKey=true;
        internal bool Offer(byte[] packet) {
            if(packet.Length > MaximumQueuedBytes || Interlocked.Add(ref bytes,packet.Length)>MaximumQueuedBytes) {Interlocked.Add(ref bytes,-packet.Length);return false;}
            if(packets.Writer.TryWrite(packet))return true;
            Interlocked.Add(ref bytes,-packet.Length);return false;
        }
        internal void End(Exception? error=null){if(Interlocked.Exchange(ref finished,1)==0){packets.Writer.TryComplete(error);ended.Cancel();}}
        public async IAsyncEnumerable<byte[]> Read([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            await foreach(var packet in packets.Reader.ReadAllAsync(token)){Interlocked.Add(ref bytes,-packet.Length);yield return packet;}
        }
        public void Dispose(){if(Interlocked.Exchange(ref disposed,1)==0){owner.Remove(this);ended.Dispose();}}
    }
    public static async Task SelfTest()
    {
        byte[] Packet(ulong flags,int length=16) {var packet=new byte[length];BinaryPrimitives.WriteUInt64BigEndian(packet,flags);BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8),(uint)(length-12));return packet;}
        var hub=new CaptureFanout();hub.SetMetadata(new {kind="video",Width=10,Height=10});
        var config=Packet(1UL<<63);var key=Packet(1UL<<62);var delta=Packet(1);
        hub.Publish(config);hub.Publish(key);hub.Publish(delta);
        using var first=hub.Subscribe();using var second=hub.Subscribe();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var a=first.Read(timeout.Token).GetAsyncEnumerator();await using var b=second.Read(timeout.Token).GetAsyncEnumerator();
        foreach(var expected in new[]{config,key,delta}) {if(!await a.MoveNextAsync()||!await b.MoveNextAsync()||!ReferenceEquals(a.Current,expected)||!ReferenceEquals(b.Current,expected))throw new Exception("Fanout codec/GOP order or transient reference reuse failed");}
        first.Dispose();hub.Publish(delta);if(!await b.MoveNextAsync())throw new Exception("Observer disconnect affected producer/other observer");
        using var slow=hub.Subscribe();for(var i=0;i<MaximumQueuedPackets+5;i++)hub.Publish(delta);
        if(!slow.Closed.IsCancellationRequested)throw new Exception("Slow observer delivery token remained active");
        // Overflow closes a slow observer without ending the actual producer.
        using var fresh=hub.Subscribe();hub.Publish(key);hub.Publish(delta);hub.Complete();
        var parsed=await new ScrcpyVideoReader(new MemoryStream(key)).Packet(timeout.Token);
        if(parsed is null || !parsed.SequenceEqual(key))throw new Exception("Fanout production packet parser fixture failed");
        var capped=new CaptureFanout();var leases=new List<Subscription>();
        try {for(var i=0;i<MaximumObservers;i++)leases.Add(capped.Subscribe());try{capped.Subscribe();throw new Exception("Observer cap accepted ninth reader");}catch(InvalidOperationException){}}
        finally{foreach(var lease in leases)lease.Dispose();}
        for(var i=0;i<100;i++)capped.Subscribe().Dispose();
        capped.Publish(config);capped.Publish(Packet(1UL<<62,MaximumQueuedBytes));
        using(var late=capped.Subscribe()){if(!late.WaitingKey)throw new Exception("Oversize GOP falsely bootstrapped a reader");capped.Publish(key);if(late.WaitingKey)throw new Exception("Late genuine keyframe did not recover observer");}
        capped.Publish(Packet(1UL<<63,MaximumConfigBytes+1));
        try{capped.Subscribe();throw new Exception("Oversize config created unusable observer");}catch(InvalidOperationException){}
        capped.Publish(config);using(var recovered=capped.Subscribe()){}capped.Complete();
        Console.WriteLine("PASS fanout real codec/keyframe/GOP order, shared transient references, independent observers and bounded slow-consumer eviction; no video files/device operations");
    }
}

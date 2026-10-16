using System.Threading.Channels;

namespace PKForge.Sync.Server.Web;

/// <summary>One change notification: the SSE event name and its JSON data.</summary>
public sealed record AccountEvent(string Kind, string Data)
{
    public const string Ready = "ready";
    public const string Bank = "bank";
    public const string Devices = "devices";
    public const string DeviceRemoved = "device-removed";
    public const string Prefs = "prefs";
    public const string Account = "account";
}

/// <summary>
/// In-process change notifications for <c>GET /v1/sync/events</c>. Events are only nudges (the client then asks
/// for what changed), so each subscriber's queue is small and drops the oldest when full. The server runs as a
/// single instance, so there is no cross-process bus.
/// </summary>
public sealed class AccountEvents
{
    private const int QueueSize = 16;
    /// <summary>Open streams one device may hold (tabs of one browser share a device); a new one closes the oldest.</summary>
    public const int MaxStreamsPerDevice = 8;
    private readonly Dictionary<Guid, List<Subscription>> byAccount = [];
    private readonly Lock gate = new();

    public Subscription Subscribe(Guid accountId, Guid deviceId)
    {
        var subscription = new Subscription(this, accountId, deviceId,
            Channel.CreateBounded<AccountEvent>(new BoundedChannelOptions(QueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true,
            }));
        Subscription? oldest = null;
        lock (gate)
        {
            if (!byAccount.TryGetValue(accountId, out var list)) byAccount[accountId] = list = [];
            if (list.Count(s => s.DeviceId == deviceId) >= MaxStreamsPerDevice)
            {
                oldest = list.First(s => s.DeviceId == deviceId);
                list.Remove(oldest);
            }
            list.Add(subscription);
        }
        oldest?.Writer.TryComplete(); // its stream ends; the client reconnects if it still wants one
        return subscription;
    }

    /// <summary>A push stored something; <paramref name="version"/> is the account's new version.</summary>
    public void BankChanged(Guid accountId, long version) => Publish(accountId, new(AccountEvent.Bank, $"{{\"version\":{version}}}"));

    /// <summary>A device was added to the account.</summary>
    public void DevicesChanged(Guid accountId) => Publish(accountId, new(AccountEvent.Devices, "{}"));

    /// <summary>Devices were removed: their own streams hear <c>device-removed</c> and end; the rest hear <c>devices</c>.</summary>
    public void DevicesRemoved(Guid accountId, IReadOnlyCollection<Guid> deviceIds)
    {
        if (deviceIds.Count == 0) return;
        var removed = new AccountEvent(AccountEvent.DeviceRemoved, "{}");
        var devices = new AccountEvent(AccountEvent.Devices, "{}");
        foreach (var s in Snapshot(accountId))
        {
            if (deviceIds.Contains(s.DeviceId))
            {
                s.Writer.TryWrite(removed);
                s.Writer.TryComplete();
            }
            else
            {
                s.Writer.TryWrite(devices);
            }
        }
    }

    /// <summary>The account is gone: every stream hears <c>device-removed</c> and ends.</summary>
    public void AccountRemoved(Guid accountId)
    {
        var removed = new AccountEvent(AccountEvent.DeviceRemoved, "{}");
        foreach (var s in Snapshot(accountId))
        {
            s.Writer.TryWrite(removed);
            s.Writer.TryComplete();
        }
    }

    /// <summary>The account's preferences (theme) were saved.</summary>
    public void PreferencesChanged(Guid accountId) => Publish(accountId, new(AccountEvent.Prefs, "{}"));

    /// <summary>
    /// Something about the account's standing changed; clients should refetch. Nothing in this server calls it; a
    /// deployment's own <see cref="Accounts.IAccountWritePolicy"/> can call it when its answer for the account
    /// changes, so open web Banks let the user try changes again.
    /// </summary>
    public void AccountChanged(Guid accountId) => Publish(accountId, new(AccountEvent.Account, "{}"));

    /// <summary>Open streams, for tests and diagnostics.</summary>
    public int Count(Guid accountId) => Snapshot(accountId).Length;

    private void Publish(Guid accountId, AccountEvent e)
    {
        foreach (var s in Snapshot(accountId)) s.Writer.TryWrite(e);
    }

    private Subscription[] Snapshot(Guid accountId)
    {
        lock (gate) return byAccount.TryGetValue(accountId, out var list) ? [.. list] : [];
    }

    private void Remove(Subscription subscription)
    {
        lock (gate)
        {
            if (!byAccount.TryGetValue(subscription.AccountId, out var list)) return;
            list.Remove(subscription);
            if (list.Count == 0) byAccount.Remove(subscription.AccountId);
        }
    }

    public sealed class Subscription : IDisposable
    {
        private readonly AccountEvents owner;
        private readonly Channel<AccountEvent> channel;

        internal Subscription(AccountEvents owner, Guid accountId, Guid deviceId, Channel<AccountEvent> channel)
        {
            this.owner = owner;
            this.channel = channel;
            AccountId = accountId;
            DeviceId = deviceId;
        }

        public Guid AccountId { get; }
        public Guid DeviceId { get; }
        /// <summary>Completes after <c>device-removed</c>: the stream should end.</summary>
        public ChannelReader<AccountEvent> Reader => channel.Reader;
        internal ChannelWriter<AccountEvent> Writer => channel.Writer;

        public void Dispose()
        {
            channel.Writer.TryComplete();
            owner.Remove(this);
        }
    }
}

using System.Collections.Generic;

namespace Xpace.Net;

/// <summary>
/// What carries packets between the machines in a session: EOS P2P through
/// <see cref="StarTransport"/>, <see cref="EnetTransport"/> on a LAN, <see cref="SimulatedNetwork"/>
/// in tests. Peers are named by a
/// byte; the host is peer 0.
/// </summary>
public interface INetTransport
{
    /// <summary>The other machines this one can talk to.</summary>
    IReadOnlyList<byte> Peers { get; }

    /// <summary>Unreliable packets may be lost or come out of order; reliable ones always arrive.</summary>
    void Send(byte peer, byte[] packet, bool reliable);

    /// <summary>The next packet that has arrived, if any.</summary>
    bool Poll(out byte peer, out byte[] packet);

    /// <summary>Sends now what is queued (the session calls it at the end of each tick).</summary>
    void Flush() { }
}

/// <summary>
/// Several machines in one process, joined by <see cref="SimulatedLink"/>s:
/// every pair has an unreliable link with the given faults and a reliable
/// one with the same latency and jitter and no loss. Set <see cref="Now"/>
/// before each step.
/// </summary>
public sealed class SimulatedNetwork
{
    public double Latency = 0.05, Jitter = 0.01, Loss = 0.02;
    public double Now;

    private readonly Dictionary<(byte, byte, bool), SimulatedLink> _links = new();
    private readonly List<byte> _peers = new();
    private int _seed = 100;

    public INetTransport Join(byte peer)
    {
        _peers.Add(peer);
        return new Endpoint(this, peer);
    }

    /// <summary>Changes one direction's faults, from one peer to another.</summary>
    public void SetLink(byte from, byte to, double latency, double jitter, double loss)
    {
        Link(from, to, false).Latency = Link(from, to, true).Latency = latency;
        Link(from, to, false).Jitter = Link(from, to, true).Jitter = jitter;
        Link(from, to, false).Loss = loss;
    }

    private SimulatedLink Link(byte from, byte to, bool reliable)
    {
        if (!_links.TryGetValue((from, to, reliable), out SimulatedLink link))
            _links[(from, to, reliable)] = link = new SimulatedLink(_seed++)
            {
                Latency = Latency, Jitter = Jitter, Loss = reliable ? 0 : Loss,
            };
        return link;
    }

    private sealed class Endpoint : INetTransport
    {
        private readonly SimulatedNetwork _net;
        private readonly byte _self;
        private readonly Queue<(byte, byte[])> _arrived = new();

        public Endpoint(SimulatedNetwork net, byte self) { _net = net; _self = self; }

        public IReadOnlyList<byte> Peers => _net._peers.FindAll(p => p != _self);

        public void Send(byte peer, byte[] packet, bool reliable) =>
            _net.Link(_self, peer, reliable).Send(packet, _net.Now);

        public bool Poll(out byte peer, out byte[] packet)
        {
            if (_arrived.Count == 0)
                foreach (byte from in _net._peers)
                    if (from != _self)
                        foreach (bool reliable in new[] { false, true })
                            foreach (byte[] p in _net.Link(from, _self, reliable).Receive(_net.Now))
                                _arrived.Enqueue((from, p));
            if (_arrived.Count == 0)
            {
                peer = 0;
                packet = null;
                return false;
            }
            (peer, packet) = _arrived.Dequeue();
            return true;
        }
    }
}

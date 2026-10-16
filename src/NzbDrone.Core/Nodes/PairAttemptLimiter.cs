using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Nodes
{
    public interface IPairAttemptLimiter
    {
        // Whether this node has failed to pair too often lately to be listened to now.
        bool IsLimited(string remoteNodeId, DateTime now);

        // Records a failed attempt, and says how it should be logged.
        PairFailureLogging Failed(string remoteNodeId, DateTime now);

        // A node that paired starts again from nothing.
        void Succeeded(string remoteNodeId);

        PairAttemptSummary Summary(DateTime now);
    }

    public enum PairFailureLogging
    {
        // As a warning of its own.
        Log,

        // The first one too many to log one by one. One warning says they are now only counted.
        StopLogging,

        // Counted, and logged at debug level only.
        Count
    }

    public class PairAttemptSummary
    {
        public int Failures { get; set; }
        public int Nodes { get; set; }
        public int LimitedNodes { get; set; }
    }

    // What a node answers a pair attempt it refuses with. A node that has failed too often is told
    // so, which says nothing about which invites exist.
    public static class PairRefusals
    {
        public const string InviteInvalid = "InviteInvalid";
        public const string TooManyAttempts = "TooManyAttempts";
    }

    // Raised the first time a node is limited, so the health check looks again.
    public class PairAttemptsLimitedEvent : IEvent
    {
    }

    // An invite's secret is 256 bits, so guessing one cannot work (ADR 0013). What a stranger who
    // can reach the node can do is ask over and over: a lookup and a warning in the log each time,
    // and node IDs cost nothing to make. So a node that keeps failing is not listened to for a
    // while, and once there are many failures they are counted instead of logged.
    //
    // Pairing itself is never closed to everyone, or one stranger could keep a friend from
    // pairing. Everything is in memory: a restart forgets it, which costs nothing.
    public class PairAttemptLimiter : IPairAttemptLimiter
    {
        public const int FailuresPerNode = 5;
        public const int LoggedFailures = 10;

        // How many failures in SummaryWindow the health check takes.
        public const int WarnAtFailures = 20;

        // However many node IDs a stranger makes, this is all that is kept.
        private const int MaxTracked = 10000;

        public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan SummaryWindow = TimeSpan.FromHours(1);

        private readonly object _lock = new();
        private readonly Dictionary<string, Queue<DateTime>> _byNode = new(StringComparer.Ordinal);
        private readonly Queue<(DateTime When, string NodeId)> _all = new();
        private readonly HashSet<string> _announced = new(StringComparer.Ordinal);

        private readonly IEventAggregator _eventAggregator;

        public PairAttemptLimiter(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        public bool IsLimited(string remoteNodeId, DateTime now)
        {
            lock (_lock)
            {
                Prune(now);

                return _byNode.TryGetValue(remoteNodeId ?? string.Empty, out var failures) && failures.Count >= FailuresPerNode;
            }
        }

        public PairFailureLogging Failed(string remoteNodeId, DateTime now)
        {
            var nodeId = remoteNodeId ?? string.Empty;
            bool newlyLimited;
            int recent;

            lock (_lock)
            {
                Prune(now);

                if (!_byNode.TryGetValue(nodeId, out var failures))
                {
                    if (_byNode.Count >= MaxTracked)
                    {
                        // The node whose last failure is oldest makes room.
                        _byNode.Remove(_byNode.MinBy(n => n.Value.Last()).Key);
                    }

                    failures = new Queue<DateTime>();
                    _byNode[nodeId] = failures;
                }

                failures.Enqueue(now);
                _all.Enqueue((now, nodeId));

                while (_all.Count > MaxTracked)
                {
                    _all.Dequeue();
                }

                newlyLimited = failures.Count >= FailuresPerNode && _announced.Add(nodeId);
                recent = _all.Count(f => f.When > now - Window);
            }

            if (newlyLimited)
            {
                _eventAggregator.PublishEvent(new PairAttemptsLimitedEvent());
            }

            return recent <= LoggedFailures ? PairFailureLogging.Log : recent == LoggedFailures + 1 ? PairFailureLogging.StopLogging : PairFailureLogging.Count;
        }

        public void Succeeded(string remoteNodeId)
        {
            lock (_lock)
            {
                _byNode.Remove(remoteNodeId ?? string.Empty);
                _announced.Remove(remoteNodeId ?? string.Empty);
            }
        }

        public PairAttemptSummary Summary(DateTime now)
        {
            lock (_lock)
            {
                Prune(now);

                var recent = _all.Where(f => f.When > now - SummaryWindow).ToList();

                return new PairAttemptSummary
                {
                    Failures = recent.Count,
                    Nodes = recent.Select(f => f.NodeId).Distinct().Count(),
                    LimitedNodes = _byNode.Count(n => n.Value.Count >= FailuresPerNode)
                };
            }
        }

        private void Prune(DateTime now)
        {
            foreach (var (nodeId, failures) in _byNode.ToList())
            {
                while (failures.Count > 0 && failures.Peek() <= now - Window)
                {
                    failures.Dequeue();
                }

                if (failures.Count == 0)
                {
                    _byNode.Remove(nodeId);
                    _announced.Remove(nodeId);
                }
            }

            while (_all.Count > 0 && _all.Peek().When <= now - SummaryWindow)
            {
                _all.Dequeue();
            }
        }
    }
}

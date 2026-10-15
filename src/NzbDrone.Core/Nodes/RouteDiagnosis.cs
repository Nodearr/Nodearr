using System;
using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Nodes.Transport;

namespace NzbDrone.Core.Nodes
{
    public interface IRouteDiagnosis
    {
        // What is known about the route to a node, and where to look first, in plain sentences.
        // Written for a transfer that failed for want of a direct route.
        string Describe(Peer peer);
    }

    // "NoDirectRoute" names what went wrong and nothing more. This adds what the last connection
    // check found, and which side this node's own network points at. It can only speak for this
    // side: nothing here can see the other node's network, or a firewall on either machine.
    public class RouteDiagnosis : IRouteDiagnosis
    {
        private readonly IPeerTransport _transport;
        private readonly IContainerEnvironment _container;

        public RouteDiagnosis(IPeerTransport transport, IContainerEnvironment container)
        {
            _transport = transport;
            _container = container;
        }

        public string Describe(Peer peer)
        {
            var name = peer.Name.IsNotNullOrWhiteSpace() ? peer.Name : "the other node";
            var sentences = new List<string>();

            if (!peer.ConnectionChecked.HasValue)
            {
                sentences.Add($"No connection check has been made with {name}.");
            }
            else
            {
                var when = Before(DateTime.UtcNow - peer.ConnectionChecked.Value);

                sentences.Add(peer.ConnectionPath switch
                {
                    ConnectionPath.Direct => $"A connection check {when} found a direct route to {name}, so the route comes and goes.",
                    ConnectionPath.Relay => $"A connection check {when} reached {name} only through a relay.",
                    _ => $"A connection check {when} could not reach {name} at all."
                });
            }

            NetworkStatus network = null;

            try
            {
                network = _transport.GetNetworkStatus();
            }
            catch (TransportException)
            {
                // Not running. Nothing is known about this side.
            }

            if (network?.MappingVaries == true && _container.IsDockerDesktop)
            {
                sentences.Add("This node is in a container on Docker Desktop, which gives it a different public port for every destination whatever the router does, so forward the UDP port on this node's router or run Nodearr outside Docker.");
            }
            else if (network?.MappingVaries == true)
            {
                sentences.Add("This node's network gives a different public port to every destination, which usually means forwarding the UDP port on this node's router.");
            }
            else if (network?.Udp == false)
            {
                sentences.Add("This node could not confirm that UDP gets out of its network, so start with the firewall on this machine.");
            }
            else if (network?.Udp == true)
            {
                sentences.Add($"Nothing is wrong with this node's network as far as it can tell, so check the firewall on both machines for incoming UDP, then the router on {name}'s side.");
            }
            else
            {
                sentences.Add("Check the firewall on both machines for incoming UDP, then forward the UDP port on either router.");
            }

            return string.Join(" ", sentences);
        }

        // The sentence is kept with the failure and read later, so the time is told from the
        // failure, which stays true, and not from now.
        private static string Before(TimeSpan age)
        {
            if (age < TimeSpan.FromMinutes(2))
            {
                return "just before this";
            }

            if (age < TimeSpan.FromHours(2))
            {
                return $"{(int)age.TotalMinutes} minutes before this";
            }

            if (age < TimeSpan.FromDays(2))
            {
                return $"{(int)age.TotalHours} hours before this";
            }

            return $"{(int)age.TotalDays} days before this";
        }
    }
}

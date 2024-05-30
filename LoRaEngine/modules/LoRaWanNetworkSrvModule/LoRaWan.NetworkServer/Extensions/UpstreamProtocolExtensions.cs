// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer.Extensions;

using System;
using Microsoft.Azure.Devices.Client;

public static class UpstreamProtocolExtensions
{
    public static UpstreamProtocol StringToUpstreamProtocol(this string value)
    {
        return Enum.TryParse(value, true, out UpstreamProtocol upstreamProtocol)
            ? throw new ArgumentOutOfRangeException(nameof(value), value,
                $"Invalid value '{value}' for UpstreamProtocol")
            : upstreamProtocol;
    }

    public static TransportType UpstreamProtocolToTransportType(this UpstreamProtocol value)
    {
        return value switch
        {
            UpstreamProtocol.Amqp => TransportType.Amqp_Tcp_Only,
            UpstreamProtocol.AmqpWs => TransportType.Amqp_WebSocket_Only,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value,
                "Value '{value}' cannot be associated to a TransportType")
        };
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.NetworkServer
{
    using System;
    using LoRaTools.LoRaMessage;
    using LoRaTools.LoRaPhysical;
    using LoRaTools.Regions;
    using LoRaWan;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Composition of a <see cref="LoRaRequest"/> that logs at the end of the process.
    /// </summary>
    public class LoggingLoRaRequest : LoRaRequest
    {
        private readonly LoRaRequest wrappedRequest;
        private readonly ILogger<LoggingLoRaRequest> logger;

        public override IPacketForwarder PacketForwarder => this.wrappedRequest.PacketForwarder;

        public override Region Region => this.wrappedRequest.Region;

        public override LoRaPayload Payload => this.wrappedRequest.Payload;

        public override Rxpk Rxpk => this.wrappedRequest.Rxpk;

        public override DateTime StartTime => this.wrappedRequest.StartTime;

        public override StationEui StationEui => this.wrappedRequest.StationEui;

        public LoggingLoRaRequest(LoRaRequest wrappedRequest, ILogger<LoggingLoRaRequest> logger)
        {
            this.wrappedRequest = wrappedRequest;
            this.logger = logger;
        }

        public override void NotifyFailed(string deviceId, LoRaDeviceRequestFailedReason reason, Exception exception = null)
        {
            this.wrappedRequest.NotifyFailed(deviceId, reason, exception);
            LogProcessingTime();
        }

        public override void NotifySucceeded(LoRaDevice loRaDevice, DownlinkPktFwdMessage downlink)
        {
            this.wrappedRequest.NotifySucceeded(loRaDevice, downlink);
            LogProcessingTime();
        }

        private void LogProcessingTime()
        {
            if (!this.logger.IsEnabled(LogLevel.Debug))
                return;

            this.logger.LogDebug($"processing time: {DateTime.UtcNow.Subtract(this.wrappedRequest.StartTime)}");
        }

        public override LoRaOperationTimeWatcher GetTimeWatcher() => this.wrappedRequest.GetTimeWatcher();
    }
}
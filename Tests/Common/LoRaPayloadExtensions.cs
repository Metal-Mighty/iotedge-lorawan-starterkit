// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace LoRaWan.Tests.Common
{
    using LoRaTools.LoRaMessage;
    using LoRaTools.LoRaPhysical;

    public static class LoRaPayloadExtensions
    {
        /// <summary>
        /// Serialize a message to be sent upstream.
        /// </summary>
        public static UplinkPktFwdMessage SerializeUplink(this LoRaPayloadData payloadData, string appSKey, string nwkSKey,
                                                          DataRate datr = null, double freq = 868.3, uint tmst = 0, float lsnr = 0)
        {
            _ = payloadData.PerformEncryption(appSKey);
            payloadData.SetMic(nwkSKey);
            return new UplinkPktFwdMessage(payloadData.GetByteMessage(), datr ?? LoRaDataRate.SF10BW125, freq, tmst, lsnr);
        }

        /// <summary>
        /// Serializes uplink message, used by simulator.
        /// </summary>
        public static UplinkPktFwdMessage SerializeUplink(this LoRaPayloadJoinRequest request, string appKey,
                                                          DataRate datr = null, double freq = 868.3, uint tmst = 0)
        {
            request.SetMic(appKey);
            return new UplinkPktFwdMessage(request.GetByteMessage(), datr ?? LoRaDataRate.SF10BW125, freq, tmst);
        }
    }
}
# 010. LNS sticky affinity over multiple sessions

**Feature**: [#1475](https://github.com/Azure/iotedge-lorawan-starterkit/issues/1475)  
**Authors**: Spyros Giannakakis, Daniele Antonio Maggio, Patrick Schuler  
**Status**: Accepted  
__________

## Problem statement

Consider the topology:

```mermaid
flowchart LR;
  Device-->LBS1-->LNS1--1-->IoTHub;
  Device-->LBS2-->LNS2--2-->IoTHub;
```

IoT Hub limits active connections that an IoT device can have to one. Assuming that connection 1 is
already open and a message from LNS2 arrives, IoT Hub will close connection 1 and open connection 2.
Edge Hub on LNS1, will detect this and assume it's a transient network issue, therefore will try
proactively to reconnect to IoT Hub. IoT Hub will now drop the connection 2 to re-establish the
original connection 1.  

This connection "ping-pong" will continue happening, negatively impacting the scalability due to the
high costs of setting up/disposing the connections. From our load tests we observed that in this
scenario we were not even able to connect more than 120 devices to two LNSs, while in a single LNS
topology we could scale up to 900 devices without issues.

## Out of scope

- Deduplication strategies Mark and None: these strategies rely on multiple LNSs sending message.
Potentially we could consider other workarounds for the IoT Hub limitation of a single connection
per device but we find it acceptable for the Mark and None strategies to not be as scalable as the
Drop strategy and will only document this limitation for potential users to be aware of.

- LNS performs operations on behalf of a device/sensor and a concentrator/station. However since a
concentrator can be connected to at most one LNS, there is no ping-pong happening with operations on
stations.

## In-scope

- The problem can be manifested whenever we do operations against Iot Hub on behalf of edge devices.
  These can be:
  - Twin reads
  - Twin writes (updates/deletes)
  - D2C messages
  - C2D messages
- Roaming leaf devices (that potentially become out-of-range from an LNS) are kept in scope.
- Downstream messages for Class C devices via Direct Method

### Problematic IoT Hub operations on behalf of edge devices

- Background tasks
  - Periodically we refresh the LoRaDeviceCache, which results in device twin reads that could
    switch the connection -> see [handling of background tasks section](#handling-of-background-tasks)
- Message flows
  - Join -> see [handling of Join requests section](#Handling-of-Join-requests)
  - Data:
    - if the device is not in LoRaDeviceCache, we fetch the device twin -> see [main data flow section](#main-data-message-flow)
    - assuming we have the device twin (in the cache or fetched) in the main data flow we send upstream, downstream and write the new twin -> see [main data flow section](#main-data-message-flow)
    - if a frame counter reset happened, we update the twin immediately -> see [handling of resets section](#handling-of-device-resets)
  - C2D message via Direct method -> see [handling class C downstream messages section](#handling-class-c-downstream-messages)

Version, LNS discovery and CUPS update endpoints are not affected by this issue.

## Solution

The main idea is to give the current connection holder (as indicated from the Function), the edge to
continue processing messages for this device. The performance of that gateway will not be impacted.

The information whether the current LNS is the connection owner is stored locally. The LNS that is
the connection owner will keep the connection open. Any other network servers receiving messages,
will not maintain an active connection to IoT Hub. If the owning network server stops responding or
gets out of reach, the ownership is transferred to the next winning network server.
  
### Handling of cache refresh

When we create the (singleton) instance of the NetworkServer.LoRaDeviceCache, we start a background
periodical task to ensure the device twins for all the devices that connected to that LNS are kept
fresh. In the case where we need to get a device twin, this could trigger a connection ping-pong.

#### Decision

The preferred option for now is to refresh the twin and close the connection immediately. This could
result in a connection switch but not a permanent connection ping pong. This will be revisited with
either [this issue about the frame counter
  drift](https://github.com/Azure/iotedge-lorawan-starterkit/issues/1499) (one of the solutions
 there is to fetch twin and close connection immediately) or [this issue specific to cache
 refresh](https://github.com/Azure/iotedge-lorawan-starterkit/issues/1505)

#### Alternatives

- Check against the local store whether we are the owning gateway. If we are, refresh the twin (and
  keep the connection open). If we are not:
  - We adjust the LastSeen property but not actually refresh the entry in the cache.
    When the next data message for this devices comes in and an entry is in cache for such device, in the
    event that the Azure Function is marking our LNS as the new "winning" gateway, we have a stale
    twin in the cache that needs to be updated before validating the request (see [main data flow](#main-data-message-flow)).
  - Alternatively, we remove the device entry from the cache. This idea is discarded because if we were
    to do this, a "get twin" operation would be triggered as soon as the next data message is coming
    in from the device in question (for the resolution of devAddr).

### Handling of Join requests

Join requests in isolation currently do not have the connection stealing issue, as they already rely on the
Function for the DevNonce check. If the current LNS is not the preferred gateway, it drops the
message immediately.

Problem

- Join received from LNS1 and LNS2: LNS1 wins
- First data message received only on LNS2, Function should inform LNS1 that it's the losing one
  (information about the owning LNS needs to be "shared" between joins and data messages).

#### Decision

We decide to go with the simpler solution of closing the connection immediately after a Join and let
the data flow re-establish it if needs to be. Disadvantage is that the first data message is more likely
  to miss the window due to having re-establish the connection.
  LNS also stores locally that it was the losing LNS and delays on a future re-join or future data messages.

#### Alternative

- Do nothing and accept that a one-off connection stealing on the first Data message can happen.
- We do not close the connection after a Join. When the first data message arrives the Function checks
  somehow the preferred gateway for this device when it joined: if the data message comes from the
  same gateway it is allowed to process the message. If not, we should inform the previously owning
  LNS to drop the connection and allow the new LNS to process the message. This was not preferred as
  it has more complexity for unclear results.  

### Main data message flow

Currently, if LoRaDevice is not in the LNS cache, we search on the Function for all devices that
have that DevAddr. Then we get all their twins which could result in a connection switch.

#### Decision

After loading all the twins for the LoRaDevice(s), we close the connections immediately. This is
equivalent to how we [handle Join requests](#handling-of-join-requests) and [background cache
tasks](#handling-of-cache-refresh).

#### Alternative

- Currently the DeviceGetter.GetDevice returns a list of devices that match the provided DevAddr. We
  considered changing the Function to return a single device instead of a list and only load the
  twin for this one on the LNS side. For this the Function should perform the Mic computation which
  means that we would need to send the payload to the cloud. This is a deal breaker for us.

#### Further processing

This section uses this topology:

```mermaid
flowchart LR;
    Device-->LBS1-->LNS1-->Function;
    Device-->LBS2-->LNS2-->Function;
    LNS1-->IoTHub;
    LNS2-->IoTHub;
```

where Device sends data message A and then B.

Here is a rundown of what should happen assuming both LNSs have a fresh twin (via background refresh
or via fetching it using DeviceGetter.GetDevice). Changes are marked in **bold**:

1. Device sends first data message A.
1. We assume that LNS1 gets the message first. **LNS1 checks against
   its in-memory state** and since an owner for the device connection was not elected yet, directly contacts the function without delay.
1. The Function hasn't seen this DevEui either and therefore does not have an assigned LNS for it
  yet. LNS1 wins the race and gets immediately a response and processes the message upstream.
1. LNS2 eventually receives message A, **checks its local state** and also contacts the
   Function immediately since it does not have prior info about this device.
1. The Function responds to LNS2 that it lost the race to process this message.
1. Since deduplication strategy is Drop, LNS2 drops the message immediately, therefore no
   connection to Iot Hub is opened and only LNS1 has the connection to Iot Hub. **LNS2 updates its
   in memory state that it does not own the connection for this device**.
1. When message B gets send (with a higher frame counter*), assuming that this time LNS2 gets it
   first, it **checks again its local state that indicates it's not owning the connection for the device and
   therefore delays itself X ms before contacting the Function**.
   - Here we do *not* want to simply drop the message as LNS1 might not be available anymore (due to
     a crash, device not in range etc).
   - This delay gives LNS1 a time advantage to reach the Function first and win the race again, failing
    back to the previous case of message A. The active connection stays with LNS1.
1. If this delay is not sufficient for LNS1 to win the race, LNS2 will contact the Function which
   now awards LNS2 as the "winning" LNS. LNS2 can now process the message upstream. It also **removes the "losing flag" from its in-memory store**.
1. **The Function also proactively informs LNS1** that it's not anymore the winning LNS for this
   device.
    - The reason why we do this is to ensure that LNS1 knows connection ownership was transferred to
    another network server and it can drop the connection. This is for the case, where the upstream
    message does not reach LNS1.
    - For that we can use a C2D message to LNS. Alternative: direct method could be used here but
    delivery will not be guaranteed then.
1. If LNS1 in the meantime gets message B and contacts the Function, it will let it know
   that it lost the race for this frame counter and must therefore drop the message, **mark
   itself as the losing LNS and close the connection if it hasn't done so yet**.

Notes:

- The Function is not called in certain topologies e.g. when multiple LBSs are connected to the same LNS but these topologies are not relevant for the issue here as they employ a single connection per device by design).
- For the main flow above we consider only frame counter B > frame counter A. Resets are covered in
  [the reset section](#handling-of-abp-relax-frame-counter-reset). Resubmits (when frame counter B ==
  frame counter A) are also a current issue and should be [addressed in this issue](https://github.com/Azure/iotedge-lorawan-starterkit/issues/1468).

### Handling of ABP relax frame counter reset

A special case on the main data flow is when we detect a device reset after a message. Currently we save the twin immediately and then clear
the Function cache. This twin write could result in a connection ping-pong.

#### Decision

- We should first clear the cache in the Function.
- The Function is changed to return if we are the losing LNS. If we are the losing one, we drop the message here,
  mark ourselves as the losing gateway etc.
- Otherwise we process message normally and at the end update the twin as we currently do.

#### Alternative

- Use the Function to do both operations: idea was discarded because of a connection switch
  - update the device twin frame counter to 0. As frame counter is a reported property it needs to be
    changed via a DeviceClient that would cause a connection switch.
  - Clear or update the cache entry with frame counter down and up to 0.
  - Returns the result to the LNS: whether it was the winning or losing one
  - LNS reacts as described in the [main data flow section](#main-data-message-flow)

### Handling Class C downstream messages

For class C devices we can send C2D messages using a Direct Method that could (one-off) steal the
active connection. When the Direct method is invoked via the portal on an LNS, we should check if we
are the connection holder for that device and if not drop the message.

#### Should we delay on the LNS itself or on the Function?

We considered using a delay on the Function rather than on the LNS itself. We decided against this approach
because of the following disadvantages:

- Observability: potentially we are messing up the measurements of the Function duration for the
  LNSs that are not owning the connection. Could be documented/worked-around.
- Keeps the HTTP connection between the LNS-Function open for more time.

For the sake of completeness a scenario when it's better that the Function implements the delay is the following:

- LNS1 is the preferred LNS. LNS2 is out of range.
- LNS2 becomes in range and receives a message with a higher frame counter. It does not know that its
  not the winning LNS and contacts immediately the Function. The Function awards it the winning LNS
  and LNS1 loses the connection without having a chance to keep it.

LNS2 would need to fetch the device twin so it is likely to lose the race to LNS1 but even if it
does not we accept the possibility that there is potentially a one-off connection switch (but not a
ping pong because LNS1 will stop retrying).

A potential advantage of delaying on the LNS is that we can dynamically (based on how long we took in
previous steps) choose the delay amount before contacting the Function, so that we have higher
chances of not missing the window.
  
#### Delay amount configuration

The delay amount should be configurable to allow users to customize behavior for their scenarios.
During load testing we tested with 400ms but smaller values should be tried as well. If 0ms are
specified the stickiness feature is disabled which means potential connection switching.

## Other candidates considered

### Using direct mode (not Edge hub)

Using direct mode is less problematic in terms of connection stealing but still had the issue. The
idea was dropped because then we would miss the [offline
capabilities](https://docs.microsoft.com/en-us/azure/iot-edge/offline-capabilities?view=iotedge-2020-11#how-it-works)
that Edge Hub offers us.

### Parent-child gateways

We could utilize child-parent connections and parent multiple LNS under a single [transparent
gateway](https://docs.microsoft.com/en-us/azure/iot-edge/how-to-create-transparent-gateway?view=iotedge-2020-11)
that has the active connection to IoT Hub. The problem there is that children can have only 1 parent
and therefore we can not support roaming leaf devices that connect to different LNSs over time.

## Related changes

### Single point of connection handling on LoRaDevice

[Using a single device queue](https://github.com/Azure/iotedge-lorawan-starterkit/issues/1479) would
also ensure by design that new code does not open more connections to IoT Hub accidentally.

Independently of the resolution of the aforementioned issue, the changes on the Function side are
still required.
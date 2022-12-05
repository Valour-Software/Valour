/* Shamelessly porting https://github.com/daily-co/mediasoup-sandbox */

// export all the references we use internally to manage call state,
// to make it easy to tinker from the js console. for example:
//
//   `Client.camVideoProducer.paused`
//
export let device,
    joined,
    localCam,
    localScreen,
    recvTransport,
    sendTransport,
    camVideoProducer,
    camAudioProducer,
    screenVideoProducer,
    screenAudioProducer,
    currentActiveSpeaker = {},
    lastPollSyncData = {},
    consumers = [],
    pollingInterval;

/* Added variables */
export let client,
    mediasoup = window.mediasoup,
    deepEqual = window.deepEqual,
    cameraPaused = false,
    micPaused = false,
    screenPaused = false,
    screenAudioPaused = false,
    serverUrl = 'https://voice.valour.gg';

/* Initialized by Blazor interop */
export function initialize(clientId, dotnet) {
    try {
        device = new mediasoup.Device();
    } catch (e) {
        if (e.name === 'UnsupportedError') {
            console.error('browser not supported for video calls');
            return;
        } else {
            console.error(e);
        }
    }

    console.log("Initializing MediaSoup...");

    client = {
        device,
        id: clientId,
        mediasoup: window.mediasoup,
        dotnet
    };

    console.log(client);
}

//////////////////////////////////////////////////////
/* These functions are invoked from the Blazor side */
//////////////////////////////////////////////////////

export async function setCamPaused(value) {
    cameraPaused = value;
    if (cameraPaused) {
        pauseProducer(camVideoProducer);
    }
    else {
        resumeProducer(camVideoProducer);
    }
}

export async function setMicPaused(value) {
    micPaused = value;
    if (micPaused) {
        pauseProducer(camAudioProducer);
    }
    else {
        resumeProducer(camAudioProducer);
    }
}

export async function setScreenPaused(value) {
    screenPaused = value;
    if (screenPaused) {
        pauseProducer(screenVideoProducer);
    }
    else {
        resumeProducer(screenVideoProducer);
    }
}

export async function setScreenAudioPaused(value) {
    screenAudioPaused = value;
    if (screenAudioPaused) {
        pauseProducer(screenAudioProducer);
    }
    else {
        resumeProducer(screenAudioProducer);
    }
}

/* Joins a room */
export async function joinRoom() {
    if (joined) {
        return {
            success: false,
            errorMessage: 'Already joined'
        };
    }

    console.log('Trying to join room...')

    try {
        // signal that we're a new peer and initialize our
        // mediasoup-client device, if this is our first time connecting
        let { routerRtpCapabilities } = await sig('join-as-new-peer');
        if (!device.loaded) {
            await device.load({ routerRtpCapabilities });
        }
        joined = true;

    } catch (e) {
        console.error(e);
        return {
            success: false,
            errorMessage: e.message
        };
    }

    // super-simple signaling: let's poll at 1-second intervals
    pollingInterval = setInterval(async () => {
        console.log('Polling...');
        let { error } = await pollAndUpdate();
        if (error) {
            clearInterval(pollingInterval);
            console.error(error);
        }
    }, 1000);

    return {
        success: true
    };
}

/* Sends camera streams */
export async function sendCameraStreams() {
    console.log('Sending camera streams');

    // make sure we've joined the room and started our camera. these
    // functions don't do anything if they've already been called this
    // session
    await joinRoom();
    await startCamera();

    // create a transport for outgoing media, if we don't already have one
    if (!sendTransport) {
        sendTransport = await createTransport('send');
    }

    // start sending video. the transport logic will initiate a
    // signaling conversation with the server to set up an outbound rtp
    // stream for the camera video track. our createTransport() function
    // includes logic to tell the server to start the stream in a paused
    // state, if the checkbox in our UI is unchecked. so as soon as we
    // have a client-side camVideoProducer object, we need to set it to
    // paused as appropriate, too.
    camVideoProducer = await sendTransport.produce({
        track: localCam.getVideoTracks()[0],
        encodings: camEncodings(),
        appData: { mediaTag: 'cam-video' }
    });
    if (getCamPausedState()) {
        try {
            await camVideoProducer.pause();
        } catch (e) {
            console.error(e);
        }
    }

    // same thing for audio, but we can use our already-created
    camAudioProducer = await sendTransport.produce({
        track: localCam.getAudioTracks()[0],
        appData: { mediaTag: 'cam-audio' }
    });
    if (getMicPausedState()) {
        try {
            camAudioProducer.pause();
        } catch (e) {
            console.error(e);
        }
    }

    return { success: true };
}

/* Starts screenshare */
export async function startScreenshare() {
    console.log("Starting screen share...")

    // make sure we've joined the room and that we have a sending
    // transport
    await joinRoom();
    if (!sendTransport) {
        sendTransport = await createTransport('send');
    }

    // get a screen share track
    localScreen = await navigator.mediaDevices.getDisplayMedia({
        video: true,
        audio: true
    });

    // create a producer for video
    screenVideoProducer = await sendTransport.produce({
        track: localScreen.getVideoTracks()[0],
        encodings: screenshareEncodings(),
        appData: { mediaTag: 'screen-video' }
    });

    // create a producer for audio, if we have it
    if (localScreen.getAudioTracks().length) {
        screenAudioProducer = await sendTransport.produce({
            track: localScreen.getAudioTracks()[0],
            appData: { mediaTag: 'screen-audio' }
        });
    }

    // handler for screen share stopped event (triggered by the
    // browser's built-in screen sharing ui)
    screenVideoProducer.track.onended = async () => {
        console.log("Browser ended screen share...");

        try {
            await screenVideoProducer.pause();
            let { error } = await sig('close-producer',
                { producerId: screenVideoProducer.id });
            await screenVideoProducer.close();
            screenVideoProducer = null;
            if (error) {
                err(error);
            }
            if (screenAudioProducer) {
                let { error } = await sig('close-producer',
                    { producerId: screenAudioProducer.id });
                await screenAudioProducer.close();
                screenAudioProducer = null;
                if (error) {
                    err(error);
                }
            }
        } catch (e) {
            console.error(e);
        }

        client.dotnet.invokeMethodAsync('OnScreenshareForceStop');
    }

    return {
        success: true,
        supportsAudio: screenAudioProducer
    };
}

export async function startCamera() {
    if (localCam) {
        return;
    }

    console.log("Starting camera...");

    try {
        localCam = await navigator.mediaDevices.getUserMedia({
            video: true,
            audio: true
        });
    } catch (e) {
        console.error('Failed starting camera.', e);

        return {
            success: false,
            errorMessage: e.message,
        };
    }

    return { success: true };
}

// switch to sending video from the "next" camera device in our device
// list (if we have multiple cameras)
export async function cycleCamera() {
    if (!(camVideoProducer && camVideoProducer.track)) {
        const error = 'Cannot cycle camera. Missing camera track.';
        console.log(error)
        return {
            success: false,
            errorMessage: error
        };
    }

    console.log("Cycling camera...");

    // find "next" device in device list
    let deviceId = await getCurrentDeviceId(),
        allDevices = await navigator.mediaDevices.enumerateDevices(),
        vidDevices = allDevices.filter((d) => d.kind === 'videoinput');
    if (!vidDevices.length > 1) {
        const error = 'Cannot cycle - only one camera!';
        console.log(error);
        return {
            success: false,
            errorMessage: error
        }
    }
    let idx = vidDevices.findIndex((d) => d.deviceId === deviceId);
    if (idx === (vidDevices.length - 1)) {
        idx = 0;
    } else {
        idx += 1;
    }

    // get a new video stream. might as well get a new audio stream too,
    // just in case browsers want to group audio/video streams together
    // from the same device when possible (though they don't seem to,
    // currently)
    console.log('getting a video stream from new device', vidDevices[idx].label);

    localCam = await navigator.mediaDevices.getUserMedia({
        video: { deviceId: { exact: vidDevices[idx].deviceId } },
        audio: true
    });

    // replace the tracks we are sending
    await camVideoProducer.replaceTrack({ track: localCam.getVideoTracks()[0] });
    await camAudioProducer.replaceTrack({ track: localCam.getAudioTracks()[0] });

    return { success: true }
}

export async function stopStreams() {
    // In these two cases they are already closed
    if (!(localCam || localScreen)) {
        return { success: true };
    }
    if (!sendTransport) {
        return { success: true };
    }

    console.log('Stopping media streams...');

    let { error } = await sig('close-transport',
        { transportId: sendTransport.id });
    if (error) {
        err(error);
    }

    // closing the sendTransport closes all associated producers. when
    // the camVideoProducer and camAudioProducer are closed,
    // mediasoup-client stops the local cam tracks, so we don't need to
    // do anything except set all our local variables to null.
    try {
        await sendTransport.close();
    } catch (e) {
        console.error(e);
    }
    sendTransport = null;
    camVideoProducer = null;
    camAudioProducer = null;
    screenVideoProducer = null;
    screenAudioProducer = null;
    localCam = null;
    localScreen = null;

    return { success: true }
}

export async function leaveRoom() {
    if (!joined) {
        return { success: true };
    }

    console.log('Leaving room...');

    // stop polling
    clearInterval(pollingInterval);

    // close everything on the server-side (transports, producers, consumers)
    let { error } = await sig('leave');
    if (error) {
        console.error(error);
    }

    // closing the transports closes all producers and consumers. we
    // don't need to do anything beyond closing the transports, except
    // to set all our local variables to their initial states
    try {
        recvTransport && await recvTransport.close();
        sendTransport && await sendTransport.close();
    } catch (e) {
        console.error(e);
    }
    recvTransport = null;
    sendTransport = null;
    camVideoProducer = null;
    camAudioProducer = null;
    screenVideoProducer = null;
    screenAudioProducer = null;
    localCam = null;
    localScreen = null;
    lastPollSyncData = {};
    consumers = [];
    joined = false;

    return { success: true };
}

export async function subscribeToTrack(peerId, mediaTag) {
    console.log(`Subscribing to track ${peerId} ${mediaTag}`);

    // create a receive transport if we don't already have one
    if (!recvTransport) {
        recvTransport = await createTransport('recv');
    }

    // if we do already have a consumer, we shouldn't have called this
    // method
    let consumer = findConsumerForTrack(peerId, mediaTag);
    if (consumer) {
        console.log(`Already consuming this track: ${peerId} ${mediaTag}`);
        return { success: true }
    };

    // ask the server to create a server-side consumer object and send
    // us back the info we need to create a client-side consumer
    let consumerParameters = await sig('recv-track', {
        mediaTag,
        mediaPeerId: peerId,
        rtpCapabilities: device.rtpCapabilities
    });

    console.log(`Consumer params:`, consumerParameters);

    consumer = await recvTransport.consume({
        ...consumerParameters,
        appData: { peerId, mediaTag }
    });

    console.log(`Created new consumer ${consumer.id}`);

    // the server-side consumer will be started in paused state. wait
    // until we're connected, then send a resume request to the server
    // to get our first keyframe and start displaying video
    while (recvTransport.connectionState !== 'connected') {
        console.log(`  Transport connection state: ${recvTransport.connectionState}`);
        await sleep(100);
    }

    // okay, we're ready. let's ask the peer to send us media
    await resumeConsumer(consumer);

    // keep track of all our consumers
    consumers.push(consumer);

    await addVideoAudio(consumer);

    return {
        success: true,
        kind: consumer.kind
    };
}

export async function unsubscribeFromTrack(peerId, mediaTag) {
    let consumer = findConsumerForTrack(peerId, mediaTag);
    if (!consumer) {
        return { success: true };
    }

    console.log(`Unsubscribing from track: ${peerId} ${mediaTag}`);

    try {
        await closeConsumer(consumer);
    } catch (e) {
        console.error(e);
    }

    return { success: true }
}

export function addVideoAudio(consumer) {
    if (!(consumer && consumer.track)) {
        return;
    }

    console.log('Sending event to dotnet for peer build.');

    client.dotnet.invokeMethodAsync('OnReadyBuildPeer', consumer.appData.peerId, consumer.appData.mediaTag, consumer.kind);
}

export async function hookPeerElementMediaTrack(elementId, peerId, mediaTag) {

    console.log(`Hooking peer ${elementId}`);

    const consumer = findConsumerForTrack(peerId, mediaTag);

    console.log(consumer);

    const element = document.getElementById(elementId);
    const stream = new MediaStream();
    const track = consumer.track.clone();

    console.log(track)

    stream.addTrack(track);

    element.srcObject = stream;

    element.play()
        .then(() => { })
        .catch((e) => {
            console.error(e);
        });
}

//////////////////////////////////////////////////////////////
/* These are internal functions not used by the Blazor side */
//////////////////////////////////////////////////////////////

export async function pauseConsumer(consumer) {
    if (!consumer) {
        return;
    }

    console.log(`Pausing consumer: ${consumer.appData.peerId} ${consumer.appData.mediaTag}`);

    try {
        await sig('pause-consumer', { consumerId: consumer.id });
        await consumer.pause();
    } catch (e) {
        console.error(e);
    }
}

export async function resumeConsumer(consumer) {
    if (!consumer) {
        return;
    }

    console.log(`Resuming consumer: ${consumer.appData.peerId} ${consumer.appData.mediaTag}`);

    try {
        await sig('resume-consumer', { consumerId: consumer.id });
        await consumer.resume();
    } catch (e) {
        console.error(e);
    } 
}

export async function pauseProducer(producer) {
    if (!producer) {
        return;
    }

    console.log(`Pausing producer: ${producer.appData.mediaTag}`);

    try {
        await sig('pause-producer', { producerId: producer.id });
        await producer.pause();
    } catch (e) {
        console.error(e);
    }  
}

export async function resumeProducer(producer) {
    if (!producer) {
        return;
    }

    console.log(`Resuming producer: ${producer.appData.mediaTag}`);

    try {
        await sig('resume-producer', { producerId: producer.id });
        await producer.resume();
    } catch (e) {
        console.error(e);
    }
}

export async function closeConsumer(consumer) {
    if (!consumer) {
        return;
    }

    console.log(`Closing consumer: ${consumer.appData.peerId} ${consumer.appData.mediaTag}`);

    try {
        // tell the server we're closing this consumer. (the server-side
        // consumer may have been closed already, but that's okay.)
        await sig('close-consumer', { consumerId: consumer.id });
        await consumer.close();

        consumers = consumers.filter((c) => c !== consumer);
        removeVideoAudio(consumer);
    } catch (e) {
        console.error(e);
    }
}

// utility function to create a transport and hook up signaling logic
// appropriate to the transport's direction
//
export async function createTransport(direction) {

    console.log(`Creating transport for ${direction}...`);

    // ask the server to create a server-side transport object and send
    // us back the info we need to create a client-side transport
    let transport,
        { transportOptions } = await sig('create-transport', { direction });

    console.log(`Transport options: ${transportOptions}`);

    if (direction === 'recv') {
        transport = await device.createRecvTransport(transportOptions);
    } else if (direction === 'send') {
        transport = await device.createSendTransport(transportOptions);
    } else {
        throw new Error(`Bad transport 'direction': ${direction}`);
    }

    // mediasoup-client will emit a connect event when media needs to
    // start flowing for the first time. send dtlsParameters to the
    // server, then call callback() on success or errback() on failure.
    transport.on('connect', async ({ dtlsParameters }, callback, errback) => {

        console.log(`Transport connect event: ${direction}`);

        let { error } = await sig('connect-transport', {
            transportId: transportOptions.id,
            dtlsParameters
        });
        if (error) {
            console.error(`Error connecting transport: ${direction}, ${error}`);
            errback();
            return;
        }
        callback();
    });

    if (direction === 'send') {
        // sending transports will emit a produce event when a new track
        // needs to be set up to start sending. the producer's appData is
        // passed as a parameter
        transport.on('produce', async ({ kind, rtpParameters, appData },
            callback, errback) => {

            console.log(`Transport produce event: ${appData.mediaTag}`);

            // we may want to start out paused (if the checkboxes in the ui
            // aren't checked, for each media type. not very clean code, here
            // but, you know, this isn't a real application.)
            //  Haha yes totally - SpikeViper
            let paused = false;
            if (appData.mediaTag === 'cam-video') {
                paused = getCamPausedState();
            } else if (appData.mediaTag === 'cam-audio') {
                paused = getMicPausedState();
            }
            // tell the server what it needs to know from us in order to set
            // up a server-side producer object, and get back a
            // producer.id. call callback() on success or errback() on
            // failure.
            let { error, id } = await sig('send-track', {
                transportId: transportOptions.id,
                kind,
                rtpParameters,
                paused,
                appData
            });
            if (error) {
                console.error(`Error setting up server-side producer: ${error}`);
                errback();
                return;
            }
            callback({ id });
        });
    }

    // for this simple demo, any time a transport transitions to closed,
    // failed, or disconnected, leave the room and reset
    //
    transport.on('connectionstatechange', async (state) => {

        console.log(`Transport ${transport.id} connectionstatechange: ${state}`);

        // for this simple sample code, assume that transports being
        // closed is an error (we never close these transports except when
        // we leave the room)
        if (state === 'closed' || state === 'failed' || state === 'disconnected') {
            console.log('Transport has closed. Handling...')

            client.dotnet.invokeMethodAsync('OnUnexpectedLeaveRoom');

            leaveRoom();
        }
    });

    return transport;
}

//
// polling/update logic
//

export async function pollAndUpdate() {
    let { peers, activeSpeaker, error } = await sig('sync');
    if (error) {
        return ({ error });
    }

    if (activeSpeaker.peerId) {
        client.dotnet.invokeMethodAsync('OnSpeakerUpdate', activeSpeaker);
    }
    else {
        client.dotnet.invokeMethodAsync('OnSpeakerUpdate', null);
    }

    // decide if we need to update tracks list and video/audio
    // elements. build list of peers, sorted by join time, removing last
    // seen time and stats, so we can easily do a deep-equals
    // comparison. compare this list with the cached list from last
    // poll.
    let thisPeersList = sortPeers(peers),
        lastPeersList = sortPeers(lastPollSyncData);
    if (!deepEqual(thisPeersList, lastPeersList)) {
        client.dotnet.invokeMethodAsync('OnPeerListUpdate', peers);
        subscribeNewPeers(peers);
    }

    // if a peer has gone away, we need to close all consumers we have
    // for that peer and remove video and audio elements
    for (let id in lastPollSyncData) {
        if (!peers[id]) {
            console.log(`Peer ${id} has left.`)
            consumers.forEach((consumer) => {
                if (consumer.appData.peerId === id) {
                    closeConsumer(consumer);
                }
            });
        }
    }

    // if a peer has stopped sending media that we are consuming, we
    // need to close the consumer and remove video and audio elements
    consumers.forEach((consumer) => {
        let { peerId, mediaTag } = consumer.appData;

        if (!peers[peerId] || !peers[peerId].media[mediaTag]) {
            log(`Peer ${peerId} has stopped transmitting ${mediaTag}`);
            closeConsumer(consumer);
        }
    });

    lastPollSyncData = peers;
    return ({}); // return an empty object if there isn't an error
}

/* Automatically subscribes to new peers */
export function subscribeNewPeers(newPeers) {
    for (const [peerId, peer] of Object.entries(newPeers)) {
        for (const [mediaTag, media] of Object.entries(peer.media)) {
            // Only subscribe if not already
            if (!findConsumerForTrack(peerId, mediaTag)) {
                subscribeToTrack(peerId, mediaTag);
            }
        }
    }
}

export function sortPeers(peers) {
    return Object.entries(peers)
        .map(([id, info]) => ({ id, joinTs: info.joinTs, media: { ...info.media } }))
        .sort((a, b) => (a.joinTs > b.joinTs) ? 1 : ((b.joinTs > a.joinTs) ? -1 : 0));
}

export function findConsumerForTrack(peerId, mediaTag) {
    return consumers.find((c) => (c.appData.peerId === peerId &&
        c.appData.mediaTag === mediaTag));
}

export function getCamPausedState() {
    return cameraPaused;
}

export function getMicPausedState() {
    return micPaused;
}

export function getScreenPausedState() {
    return screenPaused;
}

export function getScreenAudioPausedState() {
    return screenAudioPaused;
}

export async function getCurrentDeviceId() {
    if (!camVideoProducer) {
        return null;
    }
    let deviceId = camVideoProducer.track.getSettings().deviceId;
    if (deviceId) {
        return deviceId;
    }
    // Firefox doesn't have deviceId in MediaTrackSettings object
    let track = localCam && localCam.getVideoTracks()[0];
    if (!track) {
        return null;
    }
    let devices = await navigator.mediaDevices.enumerateDevices(),
        deviceInfo = devices.find((d) => d.label.startsWith(track.label));
    return deviceInfo.deviceId;
}

//
// encodings for outgoing video
//

// just two resolutions, for now, as chrome 75 seems to ignore more
// than two encodings
//
export const CAM_VIDEO_SIMULCAST_ENCODINGS =
    [
        { maxBitrate: 96000, scaleResolutionDownBy: 4 },
        { maxBitrate: 680000, scaleResolutionDownBy: 1 },
    ];

export function camEncodings() {
    return CAM_VIDEO_SIMULCAST_ENCODINGS;
}

// how do we limit bandwidth for screen share streams?
//
export function screenshareEncodings() {
    null;
}

//
// our "signaling" function -- just an http fetch
//

export async function sig(endpoint, data, beacon) {
    try {
        let headers = { 'Content-Type': 'application/json' },
            body = JSON.stringify({ ...data, peerId: client.id });

        if (beacon) {
            navigator.sendBeacon(serverUrl + '/signaling/' + endpoint, body);
            return null;
        }

        let response = await fetch(
            serverUrl + '/signaling/' + endpoint, { method: 'POST', body, headers }
        );
        return await response.json();
    } catch (e) {
        console.error(e);
        return { error: e };
    }
}

/* Promise-based sleep */
export async function sleep(ms) {
    return new Promise((r) => setTimeout(() => r(), ms));
}
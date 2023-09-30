export const mediasoupClient = window.mediasoup;
export const protooClient = window.protooClient;

export const VIDEO_CONSTRAINS =
    {
        qvga : { width: { ideal: 320 }, height: { ideal: 240 } },
        vga  : { width: { ideal: 640 }, height: { ideal: 480 } },
        hd   : { width: { ideal: 1280 }, height: { ideal: 720 } }
    };

export const PC_PROPRIETARY_CONSTRAINTS =
    {
        // optional : [ { googDscp: true } ]
    };

export let
    // Dotnet ref
    dotnet,
    // Client settings
    roomId = null,
    peerId = null,
    device = null,
    forceTcp = false,
    produce = true,
    consume = true,
    dataChannel = null,
    enableWebcamLayers = true,
    enableSharingLayers = true,
    webcamScalabilityMode = null,
    sharingScalabilityMode = null,
    numSimulcastStreams = 2,
    forceVP8 = false,
    forceVP9 = false,
    forceH264 = false,
    e2eKey = null,
    consumerReplicas = 0,
    handlerName = 'valour-media-handler',
    displayName = '',

    // Client components
    protoo = null,
    protooUrl = null,
    mediasoupDevice = null,
    sendTransport = null,
    recvTransport = null,
    micProducer = null,
    webcamProducer = null,
    shareProducer = null,
    consumers = new Map(),
    dataConsumers = new Map(),
    webcams = new Map(),
    webcam = 
        {
            device: null,
            resolution: 'hd',
        },

    // Other state
    closed = false,
    audioOnly = false,
    useDataChannel = false;

export async function initialize(dotnetRef, memberId, channelId, e2e) {

    e2eKey = e2e;
    if (e2eKey && e2eIsSupported()) {
        e2eSetCryptoKey('setCryptoKey', e2eKey, true);
    }
    
    roomId = channelId;
    peerId = memberId;

    protooUrl = getProtooUrl(
        roomId,
        peerId,
        consumerReplicas
    );
}

export function close() {
    if (closed) {
        return;
    }

    closed = true;
    console.log('close()');

    // Close protoo Peer
    protoo.close();

    // Close mediasoup Transports.

    if (sendTransport) {
        sendTransport.close();
    }

    if (recvTransport) {
        recvTransport.close();
    }
}

export async function join()
{
    console.debug('join()', roomId, peerId, protooUrl);
    
    const protooTransport = new protooClient.WebSocketTransport(protooUrl);
    protoo = new protooClient.Peer(protooTransport);

    protoo.on('open', () => {
        console.debug('protoo Peer "open" event');
        joinRoom();
    });

    protoo.on('failed', () => {
        console.log('WebSocket connection failed');
    });

    protoo.on('disconnected', () => {
        console.log('WebSocket disconnected');

        // Close mediasoup Transports.
        if (sendTransport) {
            sendTransport.close();
            sendTransport = null;
        }

        if (recvTransport) {
            recvTransport.close();
            recvTransport = null;
        }

        console.log('Closed mediasoup transports; disconnected');
    });

    protoo.on('close', () => {
        if (closed) {
            return;
        }

        close();

        console.log('Server closed');
    });

    protoo.on('request', async (request, accept, reject) => {
        console.log('Proto request event', request);

        switch (request.method) {
            case 'newConsumer': {

                if (!consume) {
                    reject(403, 'I do not want to consume');
                    break;
                }

                // Get data from request

                const {
                    peerId,
                    producerId,
                    id,
                    kind,
                    rtpParameters,
                    type,
                    appData,
                    producerPaused
                } = request.data;

                try {
                    const consumer = await recvTransport.consume(
                        {
                            id,
                            producerId,
                            kind,
                            rtpParameters,
                            // NOTE: Force streamId to be same in mic and webcam and different
                            // in screen sharing so libwebrtc will just try to sync mic and
                            // webcam streams from the same remote peer.
                            streamId : `${peerId}-${appData.share ? 'share' : 'mic-webcam'}`,
                            appData  : { ...appData, peerId } // Trick.
                        });

                    if (e2eKey && e2eIsSupported())
                    {
                        e2eSetupReceiverTransform(consumer.rtpReceiver);
                    }

                    // Store in the map.
                    consumers.set(consumer.id, consumer);

                    consumer.on('transportclose', () => {
                        consumers.delete(consumer.id);
                    });

                    const { spatialLayers, temporalLayers } =
                        mediasoupClient.parseScalabilityMode(
                            consumer.rtpParameters.encodings[0].scalabilityMode);

                    // TODO: dotnet event

                    accept();

                    if (consumer.kind === 'video' && audioOnly) {
                        pauseConsumer(consumer);
                    }
                }
                catch (error) {
                    console.error('New consumer request failed', error);

                    // TODO: dotnet event

                    throw error;
                }

                break;
            }
        }
    });

    this.protoo.on('notification', (notification) =>
    {
        console.debug(
            'proto "notification" event [method:%s, data:%o]',
            notification.method, notification.data);

        switch (notification.method)
        {
            case 'producerScore':
            {
                const { producerId, score } = notification.data;

                // TODO: dotnet event

                break;
            }

            case 'newPeer':
            {
                const peer = notification.data;

                // TODO: dotnet event

                break;
            }

            case 'peerClosed':
            {
                const { peerId } = notification.data;

                // TODO: dotnet event

                break;
            }

            case 'downlinkBwe':
            {
                console.debug('\'downlinkBwe\' event:%o', notification.data);

                break;
            }

            case 'consumerClosed':
            {
                const { consumerId } = notification.data;
                const consumer = consumers.get(consumerId);

                if (!consumer)
                    break;

                consumer.close();
                consumers.delete(consumerId);

                const { peerId } = consumer.appData;

                // TODO: dotnet event

                break;
            }

            case 'consumerPaused':
            {
                const { consumerId } = notification.data;
                const consumer = consumers.get(consumerId);

                if (!consumer)
                    break;

                consumer.pause();

                // TODO: dotnet event

                break;
            }

            case 'consumerResumed':
            {
                const { consumerId } = notification.data;
                const consumer = consumers.get(consumerId);

                if (!consumer)
                    break;

                consumer.resume();

                // TODO: dotnet event

                break;
            }

            case 'consumerLayersChanged':
            {
                const { consumerId, spatialLayer, temporalLayer } = notification.data;
                const consumer = consumers.get(consumerId);

                if (!consumer)
                    break;

                // TODO: dotnet event

                break;
            }

            case 'consumerScore':
            {
                const { consumerId, score } = notification.data;

                // TODO: dotnet event

                break;
            }

            case 'dataConsumerClosed':
            {
                const { dataConsumerId } = notification.data;
                const dataConsumer = dataConsumers.get(dataConsumerId);

                if (!dataConsumer)
                    break;

                dataConsumer.close();
                dataConsumers.delete(dataConsumerId);

                const { peerId } = dataConsumer.appData;

                // TODO: dotnet event

                break;
            }

            case 'activeSpeaker':
            {
                const { peerId } = notification.data;

                // TODO: dotnet event

                break;
            }

            default:
            {
                console.error(
                    'unknown protoo notification.method "%s"', notification.method);
            }
        }
    });
    
    console.debug('join() finished');
}

export async function enableMic()
{
    console.debug('enableMic()');

    if (micProducer)
        return;

    if (!mediasoupDevice.canProduce('audio'))
    {
        console.error('enableMic() | cannot produce audio');
        return;
    }

    let track;

    console.debug('enableMic() | calling getUserMedia()');

    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

    track = stream.getAudioTracks()[0];
    
    try
    {
        micProducer = await sendTransport.produce(
            {
                track,
                codecOptions :
                    {
                        opusStereo : true,
                        opusDtx    : true,
                        opusFec    : true,
                        opusNack   : true
                    }
                // NOTE: for testing codec selection.
                // codec : this._mediasoupDevice.rtpCapabilities.codecs
                // 	.find((codec) => codec.mimeType.toLowerCase() === 'audio/pcma')
            });

        if (e2eKey && e2eIsSupported())
        {
            e2eSetupSenderTransform(micProducer.rtpSender);
        }

        // TODO: dotnet event

        /*
        store.dispatch(stateActions.addProducer(
            {
                id            : this._micProducer.id,
                paused        : this._micProducer.paused,
                track         : this._micProducer.track,
                rtpParameters : this._micProducer.rtpParameters,
                codec         : this._micProducer.rtpParameters.codecs[0].mimeType.split('/')[1]
            }));
        */

        micProducer.on('transportclose', () =>
        {
            micProducer = null;
        });

        micProducer.on('trackended', () =>
        {

            // TODO: dotnet event

            /*
            store.dispatch(requestActions.notify(
                {
                    type : 'error',
                    text : 'Microphone disconnected!'
                }));
            */

            disableMic()
                .catch(() => {});
        });
    }
    catch (error)
    {
        console.error('enableMic() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error enabling microphone: ${error}`
            }));
        */

        if (track)
            track.stop();
    }
}

export async function disableMic()
{
    console.debug('disableMic()');

    if (!micProducer)
        return;

    micProducer.close();

    // TODO: dotnet event
    
    /*
    store.dispatch(
        stateActions.removeProducer(this.micProducer.id));
     */

    try
    {
        await protoo.request(
            'closeProducer', { producerId: micProducer.id });
    }
    catch (error)
    {
        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error closing server-side mic Producer: ${error}`
            }));
        */
    }

    micProducer = null;
}

export async function muteMic()
{
    console.debug('muteMic()');

    micProducer.pause();

    try
    {
        await protoo.request(
            'pauseProducer', { producerId: micProducer.id });

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.setProducerPaused(this._micProducer.id));
        */
    }
    catch (error)
    {
        console.error('muteMic() | failed: %o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error pausing server-side mic Producer: ${error}`
            }));
        */
    }
}

export async function unmuteMic()
{
    console.debug('unmuteMic()');

    micProducer.resume();

    try
    {
        await protoo.request(
            'resumeProducer', { producerId: micProducer.id });

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.setProducerResumed(this._micProducer.id));
        */
    }
    catch (error)
    {
        console.error('unmuteMic() | failed: %o', error);

        // TODO: dotnet event

        /* 
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error resuming server-side mic Producer: ${error}`
            }));
        */
    }
}

export async function enableWebcam()
{
    console.debug('enableWebcam()');

    if (webcamProducer)
        return;
    else if (shareProducer)
        await disableShare();

    if (!mediasoupDevice.canProduce('video'))
    {
        console.error('enableWebcam() | cannot produce video');
        return;
    }

    let track;
    let device;

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setWebcamInProgress(true));
    */

    try
    {
        await updateWebcams();
        device = webcam.device;

        const { resolution } = webcam;

        if (!device)
            throw new Error('no webcam devices');

        console.debug('enableWebcam() | calling getUserMedia()');

        const stream = await navigator.mediaDevices.getUserMedia(
            {
                video :
                    {
                        deviceId : { ideal: device.deviceId },
                        ...VIDEO_CONSTRAINS[resolution]
                    }
            });

        track = stream.getVideoTracks()[0];

        let encodings;
        let codec;
        const codecOptions =
            {
                videoGoogleStartBitrate : 1000
            };

        if (forceVP8)
        {
            codec = mediasoupDevice.rtpCapabilities.codecs
                .find((c) => c.mimeType.toLowerCase() === 'video/vp8');

            if (!codec)
            {
                throw new Error('desired VP8 codec+configuration is not supported');
            }
        }
        else if (forceH264)
        {
            codec = mediasoupDevice.rtpCapabilities.codecs
                .find((c) => c.mimeType.toLowerCase() === 'video/h264');

            if (!codec)
            {
                throw new Error('desired H264 codec+configuration is not supported');
            }
        }
        else if (forceVP9)
        {
            codec = mediasoupDevice.rtpCapabilities.codecs
                .find((c) => c.mimeType.toLowerCase() === 'video/vp9');

            if (!codec)
            {
                throw new Error('desired VP9 codec+configuration is not supported');
            }
        }

        if (enableWebcamLayers)
        {
            // If VP9 is the only available video codec then use SVC.
            const firstVideoCodec = mediasoupDevice
                .rtpCapabilities
                .codecs
                .find((c) => c.kind === 'video');

            // VP9 with SVC.
            if (
                (forceVP9 && codec) ||
                firstVideoCodec.mimeType.toLowerCase() === 'video/vp9'
            )
            {
                encodings =
                    [
                        {
                            maxBitrate      : 5000000,
                            scalabilityMode : webcamScalabilityMode || 'L3T3_KEY'
                        }
                    ];
            }
            // VP8 or H264 with simulcast.
            else
            {
                encodings =
                    [
                        {
                            scaleResolutionDownBy : 1,
                            maxBitrate            : 5000000,
                            scalabilityMode       : webcamScalabilityMode || 'L1T3'
                        }
                    ];

                if (numSimulcastStreams > 1)
                {
                    encodings.unshift(
                        {
                            scaleResolutionDownBy : 2,
                            maxBitrate            : 1000000,
                            scalabilityMode       : webcamScalabilityMode || 'L1T3'
                        }
                    );
                }

                if (numSimulcastStreams > 2)
                {
                    encodings.unshift(
                        {
                            scaleResolutionDownBy : 4,
                            maxBitrate            : 500000,
                            scalabilityMode       : webcamScalabilityMode || 'L1T3'
                        }
                    );
                }
            }
        }

        webcamProducer = await sendTransport.produce(
            {
                track,
                encodings,
                codecOptions,
                codec
            });

        if (e2eKey && e2eIsSupported())
        {
            e2eSetupSenderTransform(webcamProducer.rtpSender);
        }

        // TODO: dotnet event

        /*
        store.dispatch(stateActions.addProducer(
            {
                id            : this._webcamProducer.id,
                deviceLabel   : device.label,
                type          : this._getWebcamType(device),
                paused        : this._webcamProducer.paused,
                track         : this._webcamProducer.track,
                rtpParameters : this._webcamProducer.rtpParameters,
                codec         : this._webcamProducer.rtpParameters.codecs[0].mimeType.split('/')[1]
            }));
        */

        webcamProducer.on('transportclose', () =>
        {
            webcamProducer = null;
        });

        webcamProducer.on('trackended', () =>
        {
            // TODO: dotnet event

            /*
            store.dispatch(requestActions.notify(
                {
                    type : 'error',
                    text : 'Webcam disconnected!'
                }));
            */

            disableWebcam()
                .catch(() => {});
        });
    }
    catch (error)
    {
        console.error('enableWebcam() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error enabling webcam: ${error}`
            }));
        */

        if (track)
            track.stop();
    }

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setWebcamInProgress(false));
    */
}

export async function disableWebcam()
{
    console.debug('disableWebcam()');

    if (!webcamProducer)
        return;

    webcamProducer.close();

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.removeProducer(this.webcamProducer.id));
    */

    try
    {
        await protoo.request(
            'closeProducer', { producerId: webcamProducer.id });
    }
    catch (error)
    {
        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error closing server-side webcam Producer: ${error}`
            }));
        */
    }

    webcamProducer = null;
}

export async function changeWebcam()
{
    console.debug('changeWebcam()');

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setWebcamInProgress(true));
    */

    try
    {
        await updateWebcams();

        const array = Array.from(webcams.keys());
        const len = array.length;
        const deviceId =
            webcam.device ? webcam.device.deviceId : undefined;
        let idx = array.indexOf(deviceId);

        if (idx < len - 1)
            idx++;
        else
            idx = 0;

        webcam.device = webcams.get(array[idx]);

        console.debug(
            'changeWebcam() | new selected webcam [device:%o]',
            webcam.device);

        // Reset video resolution to HD.
        webcam.resolution = 'hd';

        if (!webcam.device)
            throw new Error('no webcam devices');

        // Closing the current video track before asking for a new one (mobiles do not like
        // having both front/back cameras open at the same time).
        webcamProducer.track.stop();

        console.debug('changeWebcam() | calling getUserMedia()');

        const stream = await navigator.mediaDevices.getUserMedia(
            {
                video :
                    {
                        deviceId : { exact: webcam.device.deviceId },
                        ...VIDEO_CONSTRAINS[webcam.resolution]
                    }
            });

        const track = stream.getVideoTracks()[0];

        await webcamProducer.replaceTrack({ track });

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.setProducerTrack(this.webcamProducer.id, track));
        */
    }
    catch (error)
    {
        console.error('changeWebcam() | failed: %o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Could not change webcam: ${error}`
            }));
        */
    }

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setWebcamInProgress(false));
    */
}

export async function changeWebcamResolution()
{
    console.debug('changeWebcamResolution()');

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setWebcamInProgress(true));
    */

    try
    {
        switch (webcam.resolution)
        {
            case 'qvga':
                webcam.resolution = 'vga';
                break;
            case 'vga':
                webcam.resolution = 'hd';
                break;
            case 'hd':
                webcam.resolution = 'qvga';
                break;
            default:
                webcam.resolution = 'hd';
        }

        console.debug('changeWebcamResolution() | calling getUserMedia()');

        const stream = await navigator.mediaDevices.getUserMedia(
            {
                video :
                    {
                        deviceId : { exact: webcam.device.deviceId },
                        ...VIDEO_CONSTRAINS[webcam.resolution]
                    }
            });

        const track = stream.getVideoTracks()[0];

        await webcamProducer.replaceTrack({ track });

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.setProducerTrack(this._webcamProducer.id, track));
        */
    }
    catch (error)
    {
        console.error('changeWebcamResolution() | failed: %o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Could not change webcam resolution: ${error}`
            }));
        */
    }

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setWebcamInProgress(false));
    */
}

export async function enableShare()
{
    console.debug('enableShare()');

    if (shareProducer)
        return;
    else if (webcamProducer)
        await disableWebcam();

    if (!mediasoupDevice.canProduce('video'))
    {
        console.error('enableShare() | cannot produce video');
        return;
    }

    let track;

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setShareInProgress(true));
    */

    try
    {
        console.debug('enableShare() | calling getUserMedia()');

        const stream = await navigator.mediaDevices.getDisplayMedia(
            {
                audio : false,
                video :
                    {
                        displaySurface : 'monitor',
                        logicalSurface : true,
                        cursor         : true,
                        width          : { max: 1920 },
                        height         : { max: 1080 },
                        frameRate      : { max: 30 }
                    }
            });

        // May mean cancelled (in some implementations).
        if (!stream)
        {
            // TODO: dotnet event

            /*
            store.dispatch(
                stateActions.setShareInProgress(true));
            */

            return;
        }

        track = stream.getVideoTracks()[0];

        let encodings;
        let codec;
        const codecOptions =
            {
                videoGoogleStartBitrate : 1000
            };

        if (forceVP8)
        {
            codec = mediasoupDevice.rtpCapabilities.codecs
                .find((c) => c.mimeType.toLowerCase() === 'video/vp8');

            if (!codec)
            {
                throw new Error('desired VP8 codec+configuration is not supported');
            }
        }
        else if (forceH264)
        {
            codec = mediasoupDevice.rtpCapabilities.codecs
                .find((c) => c.mimeType.toLowerCase() === 'video/h264');

            if (!codec)
            {
                throw new Error('desired H264 codec+configuration is not supported');
            }
        }
        else if (forceVP9)
        {
            codec = mediasoupDevice.rtpCapabilities.codecs
                .find((c) => c.mimeType.toLowerCase() === 'video/vp9');

            if (!codec)
            {
                throw new Error('desired VP9 codec+configuration is not supported');
            }
        }

        if (enableSharingLayers)
        {
            // If VP9 is the only available video codec then use SVC.
            const firstVideoCodec = mediasoupDevice
                .rtpCapabilities
                .codecs
                .find((c) => c.kind === 'video');

            // VP9 with SVC.
            if (
                (forceVP9 && codec) ||
                firstVideoCodec.mimeType.toLowerCase() === 'video/vp9'
            )
            {
                encodings =
                    [
                        {
                            maxBitrate      : 5000000,
                            scalabilityMode : sharingScalabilityMode || 'L3T3',
                            dtx             : true
                        }
                    ];
            }
            // VP8 or H264 with simulcast.
            else
            {
                encodings =
                    [
                        {
                            scaleResolutionDownBy : 1,
                            maxBitrate            : 5000000,
                            scalabilityMode       : sharingScalabilityMode || 'L1T3',
                            dtx                   : true
                        }
                    ];

                if (numSimulcastStreams > 1)
                {
                    encodings.unshift(
                        {
                            scaleResolutionDownBy : 2,
                            maxBitrate            : 1000000,
                            scalabilityMode       : sharingScalabilityMode || 'L1T3',
                            dtx                   : true
                        }
                    );
                }

                if (numSimulcastStreams > 2)
                {
                    encodings.unshift(
                        {
                            scaleResolutionDownBy : 4,
                            maxBitrate            : 500000,
                            scalabilityMode       : sharingScalabilityMode || 'L1T3',
                            dtx                   : true
                        }
                    );
                }
            }
        }

        shareProducer = await sendTransport.produce(
            {
                track,
                encodings,
                codecOptions,
                codec,
                appData :
                    {
                        share : true
                    }
            });

        if (e2eKey && e2eIsSupported())
        {
            e2eSetupSenderTransform(shareProducer.rtpSender);
        }

        // TODO: dotnet event

        /*
        store.dispatch(stateActions.addProducer(
            {
                id            : this._shareProducer.id,
                type          : 'share',
                paused        : this._shareProducer.paused,
                track         : this._shareProducer.track,
                rtpParameters : this._shareProducer.rtpParameters,
                codec         : this._shareProducer.rtpParameters.codecs[0].mimeType.split('/')[1]
            }));
        */

        shareProducer.on('transportclose', () =>
        {
            shareProducer = null;
        });

        shareProducer.on('trackended', () =>
        {

            // TODO: dotnet event

            /*
            store.dispatch(requestActions.notify(
                {
                    type : 'error',
                    text : 'Share disconnected!'
                }));
            */

            disableShare()
                .catch(() => {});
        });
    }
    catch (error)
    {
        console.error('enableShare() | failed:%o', error);

        if (error.name !== 'NotAllowedError')
        {
            // TODO: dotnet event

            /*
            store.dispatch(requestActions.notify(
                {
                    type : 'error',
                    text : `Error sharing: ${error}`
                }));
            */
        }

        if (track)
            track.stop();
    }

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setShareInProgress(false));
    */
}

export async function disableShare()
{
    console.debug('disableShare()');

    if (!shareProducer)
        return;

    shareProducer.close();

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.removeProducer(this.shareProducer.id));
    */

    try
    {
        await protoo.request(
            'closeProducer', { producerId: shareProducer.id });
    }
    catch (error)
    {
        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error closing server-side share Producer: ${error}`
            }));
        */
    }

    shareProducer = null;
}

export async function enableAudioOnly()
{
    console.debug('enableAudioOnly()');

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setAudioOnlyInProgress(true));
    */

    disableWebcam();

    for (const consumer of consumers.values())
    {
        if (consumer.kind !== 'video')
            continue;

        pauseConsumer(consumer);
    }

    // TODO: dotnet event

    /* 
    store.dispatch(
        stateActions.setAudioOnlyState(true));

    store.dispatch(
        stateActions.setAudioOnlyInProgress(false));
    */
}



export async function disableAudioOnly()
{
    console.debug('disableAudioOnly()');

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setAudioOnlyInProgress(true));
    */

    if (!webcamProducer && produce &&
        (getCookieDevices() || {}).webcamEnabled)
    {
        enableWebcam();
    }

    for (const consumer of consumers.values())
    {
        if (consumer.kind !== 'video')
            continue;

        resumeConsumer(consumer);
    }

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setAudioOnlyState(false));

    store.dispatch(
        stateActions.setAudioOnlyInProgress(false));
    */
}

export async function muteAudio()
{
    console.debug('muteAudio()');

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setAudioMutedState(true));
    */
}

export async function unmuteAudio()
{
    console.debug('unmuteAudio()');

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setAudioMutedState(false));
    */
}

export async function restartIce()
{
    console.debug('restartIce()');

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setRestartIceInProgress(true));
    */

    try
    {
        if (sendTransport)
        {
            const iceParameters = await protoo.request(
                'restartIce',
                { transportId: sendTransport.id });

            await sendTransport.restartIce({ iceParameters });
        }

        if (recvTransport)
        {
            const iceParameters = await protoo.request(
                'restartIce',
                { transportId: recvTransport.id });

            await recvTransport.restartIce({ iceParameters });
        }

        // TODO: dotnet event

        /* 
        store.dispatch(requestActions.notify(
            {
                text : 'ICE restarted'
            }));
        */
    }
    catch (error)
    {
        console.error('restartIce() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `ICE restart failed: ${error}`
            }));
        */
    }

    // TODO: dotnet event

    /*
    store.dispatch(
        stateActions.setRestartIceInProgress(false));
    */
}

export async function setMaxSendingSpatialLayer(spatialLayer)
{
    console.debug('setMaxSendingSpatialLayer() [spatialLayer:%s]', spatialLayer);

    try
    {
        if (webcamProducer)
            await webcamProducer.setMaxSpatialLayer(spatialLayer);
        else if (shareProducer)
            await shareProducer.setMaxSpatialLayer(spatialLayer);
    }
    catch (error)
    {
        console.error('setMaxSendingSpatialLayer() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error setting max sending video spatial layer: ${error}`
            }));
        */
    }
}

export async function setConsumerPreferredLayers(consumerId, spatialLayer, temporalLayer)
{
    console.debug(
        'setConsumerPreferredLayers() [consumerId:%s, spatialLayer:%s, temporalLayer:%s]',
        consumerId, spatialLayer, temporalLayer);

    try
    {
        await protoo.request(
            'setConsumerPreferredLayers', { consumerId, spatialLayer, temporalLayer });

        // TODO: dotnet event

        /*
        store.dispatch(stateActions.setConsumerPreferredLayers(
            consumerId, spatialLayer, temporalLayer));
        */
    }
    catch (error)
    {
        console.error('setConsumerPreferredLayers() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error setting Consumer preferred layers: ${error}`
            }));
        */
    }
}

async function setConsumerPriority(consumerId, priority)
{
    console.debug(
        'setConsumerPriority() [consumerId:%s, priority:%d]',
        consumerId, priority);

    try
    {
        await protoo.request('setConsumerPriority', { consumerId, priority });

        // TODO: dotnet event

        /*
        store.dispatch(stateActions.setConsumerPriority(consumerId, priority));
        */
    }
    catch (error)
    {
        console.error('setConsumerPriority() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error setting Consumer priority: ${error}`
            }));
        */
    }
}

export async function requestConsumerKeyFrame(consumerId)
{
    console.debug('requestConsumerKeyFrame() [consumerId:%s]', consumerId);

    try
    {
        await protoo.request('requestConsumerKeyFrame', { consumerId });

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                text : 'Keyframe requested for video consumer'
            }));
        */
    }
    catch (error)
    {
        console.error('requestConsumerKeyFrame() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error requesting key frame for Consumer: ${error}`
            }));
        */
    }
}

export async function getSendTransportRemoteStats()
{
    console.debug('getSendTransportRemoteStats()');

    if (!sendTransport)
        return;

    return protoo.request(
        'getTransportStats', { transportId: sendTransport.id });
}

export async function getRecvTransportRemoteStats()
{
    console.debug('getRecvTransportRemoteStats()');

    if (!recvTransport)
        return;

    return protoo.request(
        'getTransportStats', { transportId: recvTransport.id });
}

export async function getAudioRemoteStats()
{
    console.debug('getAudioRemoteStats()');

    if (!micProducer)
        return;

    return protoo.request(
        'getProducerStats', { producerId: micProducer.id });
}

export async function getVideoRemoteStats()
{
    console.debug('getVideoRemoteStats()');

    const producer = webcamProducer || shareProducer;

    if (!producer)
        return;

    return protoo.request(
        'getProducerStats', { producerId: producer.id });
}

export async function getConsumerRemoteStats(consumerId)
{
    console.debug('getConsumerRemoteStats()');

    const consumer = consumers.get(consumerId);

    if (!consumer)
        return;

    return protoo.request('getConsumerStats', { consumerId });
}

export async function getSendTransportLocalStats()
{
    console.debug('getSendTransportLocalStats()');

    if (!sendTransport)
        return;

    return sendTransport.getStats();
}

export async function getRecvTransportLocalStats()
{
    console.debug('getRecvTransportLocalStats()');

    if (!recvTransport)
        return;

    return recvTransport.getStats();
}

export async function getAudioLocalStats()
{
    console.debug('getAudioLocalStats()');

    if (!micProducer)
        return;

    return micProducer.getStats();
}

export async function getVideoLocalStats()
{
    console.debug('getVideoLocalStats()');

    const producer = webcamProducer || shareProducer;

    if (!producer)
        return;

    return producer.getStats();
}

export async function getConsumerLocalStats(consumerId)
{
    const consumer = consumers.get(consumerId);

    if (!consumer)
        return;

    return consumer.getStats();
}

export async function applyNetworkThrottle({ uplink, downlink, rtt, secret, packetLoss })
{
    console.debug(
        'applyNetworkThrottle() [uplink:%s, downlink:%s, rtt:%s, packetLoss:%s]',
        uplink, downlink, rtt, packetLoss);

    try
    {
        await protoo.request(
            'applyNetworkThrottle',
            { secret, uplink, downlink, rtt, packetLoss });
    }
    catch (error)
    {
        console.error('applyNetworkThrottle() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error applying network throttle: ${error}`
            }));
        */
    }
}

export async function resetNetworkThrottle({ silent = false, secret })
{
    console.debug('resetNetworkThrottle()');

    try
    {
        await protoo.request('resetNetworkThrottle', { secret });
    }
    catch (error)
    {
        if (!silent)
        {
            console.error('resetNetworkThrottle() | failed:%o', error);

            // TODO: dotnet event

            /*
            store.dispatch(requestActions.notify(
                {
                    type : 'error',
                    text : `Error resetting network throttle: ${error}`
                }));
            */
        }
    }
}

export async function joinRoom()
{
    console.debug('joinRoom()');

    try
    {
        mediasoupDevice = new mediasoupClient.Device(
            {
                // handlerName : handlerName
            });

        // TODO: dotnet event

        /*
        store.dispatch(stateActions.setRoomMediasoupClientHandler(
            this._mediasoupDevice.handlerName
        ));
        */

        const routerRtpCapabilities =
            await protoo.request('getRouterRtpCapabilities');

        await mediasoupDevice.load({ routerRtpCapabilities });

        // NOTE: Stuff to play remote audios due to browsers' new autoplay policy.
        //
        // Just get access to the mic and DO NOT close the mic track for a while.
        // Super hack!
        {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            const audioTrack = stream.getAudioTracks()[0];

            audioTrack.enabled = false;

            setTimeout(() => audioTrack.stop(), 120000);
        }
        // Create mediasoup Transport for sending (unless we don't want to produce).
        if (produce)
        {
            const transportInfo = await protoo.request(
                'createWebRtcTransport',
                {
                    forceTcp         : forceTcp,
                    producing        : true,
                    consuming        : false,
                    sctpCapabilities : useDataChannel
                        ? mediasoupDevice.sctpCapabilities
                        : undefined
                });

            const {
                id,
                iceParameters,
                iceCandidates,
                dtlsParameters,
                sctpParameters
            } = transportInfo;

            sendTransport = mediasoupDevice.createSendTransport(
                {
                    id,
                    iceParameters,
                    iceCandidates,
                    dtlsParameters :
                        {
                            ...dtlsParameters,
                            // Remote DTLS role. We know it's always 'auto' by default so, if
                            // we want, we can force local WebRTC transport to be 'client' by
                            // indicating 'server' here and vice-versa.
                            role : 'auto'
                        },
                    sctpParameters,
                    iceServers             : [],
                    proprietaryConstraints : PC_PROPRIETARY_CONSTRAINTS,
                    additionalSettings 	   :
                        { encodedInsertableStreams: e2eKey && e2eIsSupported() }
                });

            sendTransport.on(
                'connect', ({ dtlsParameters }, callback, errback) => // eslint-disable-line no-shadow
                {
                    protoo.request(
                        'connectWebRtcTransport',
                        {
                            transportId : sendTransport.id,
                            dtlsParameters
                        })
                        .then(callback)
                        .catch(errback);
                });

            sendTransport.on(
                'produce', async ({ kind, rtpParameters, appData }, callback, errback) =>
                {
                    try
                    {
                        // eslint-disable-next-line no-shadow
                        const { id } = await protoo.request(
                            'produce',
                            {
                                transportId : sendTransport.id,
                                kind,
                                rtpParameters,
                                appData
                            });

                        callback({ id });
                    }
                    catch (error)
                    {
                        errback(error);
                    }
                });

            sendTransport.on('producedata', async (
                {
                    sctpStreamParameters,
                    label,
                    protocol,
                    appData
                },
                callback,
                errback
            ) =>
            {
                console.debug(
                    '"producedata" event: [sctpStreamParameters:%o, appData:%o]',
                    sctpStreamParameters, appData);

                try
                {
                    // eslint-disable-next-line no-shadow
                    const { id } = await protoo.request(
                        'produceData',
                        {
                            transportId : sendTransport.id,
                            sctpStreamParameters,
                            label,
                            protocol,
                            appData
                        });

                    callback({ id });
                }
                catch (error)
                {
                    errback(error);
                }
            });
        }

        // Create mediasoup Transport for receiving (unless we don't want to consume).
        if (consume)
        {
            const transportInfo = await protoo.request(
                'createWebRtcTransport',
                {
                    forceTcp         : forceTcp,
                    producing        : false,
                    consuming        : true,
                    sctpCapabilities : useDataChannel
                        ? mediasoupDevice.sctpCapabilities
                        : undefined
                });

            const {
                id,
                iceParameters,
                iceCandidates,
                dtlsParameters,
                sctpParameters
            } = transportInfo;

            recvTransport = mediasoupDevice.createRecvTransport(
                {
                    id,
                    iceParameters,
                    iceCandidates,
                    dtlsParameters :
                        {
                            ...dtlsParameters,
                            // Remote DTLS role. We know it's always 'auto' by default so, if
                            // we want, we can force local WebRTC transport to be 'client' by
                            // indicating 'server' here and vice-versa.
                            role : 'auto'
                        },
                    sctpParameters,
                    iceServers 	       : [],
                    additionalSettings :
                        { encodedInsertableStreams: e2eKey && e2eIsSupported() }
                });

            recvTransport.on(
                'connect', ({ dtlsParameters }, callback, errback) => // eslint-disable-line no-shadow
                {
                    protoo.request(
                        'connectWebRtcTransport',
                        {
                            transportId : recvTransport.id,
                            dtlsParameters
                        })
                        .then(callback)
                        .catch(errback);
                });
        }

        // Join now into the room.
        // NOTE: Don't send our RTP capabilities if we don't want to consume.
        const { peers } = await protoo.request(
            'join',
            {
                displayName     : displayName,
                device          : device,
                rtpCapabilities : consume
                    ? mediasoupDevice.rtpCapabilities
                    : undefined,
                sctpCapabilities : useDataChannel && consume
                    ? mediasoupDevice.sctpCapabilities
                    : undefined
            });

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.setRoomState('connected'));
        */

        // Clean all the existing notifcations.

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.removeAllNotifications());

        store.dispatch(requestActions.notify(
            {
                text    : 'You are in the room!',
                timeout : 3000
            }));

        for (const peer of peers)
        {
            store.dispatch(
                stateActions.addPeer(
                    { ...peer, consumers: [], dataConsumers: [] }));
        }

        */

        // Enable mic/webcam.
        if (produce)
        {
            // Set our media capabilities.

            // TODO: dotnet event

            /*
            store.dispatch(stateActions.setMediaCapabilities(
                {
                    canSendMic    : this.mediasoupDevice.canProduce('audio'),
                    canSendWebcam : this.mediasoupDevice.canProduce('video')
                }));
            */

            enableMic();

            const devicesCookie = getCookieDevices();

            if (!devicesCookie || devicesCookie.webcamEnabled)
                enableWebcam();

            sendTransport.on('connectionstatechange', (connectionState) =>
            {
                if (connectionState === 'connected')
                {
                    // enableChatDataProducer();
                    // enableBotDataProducer();
                }
            });
        }

        // NOTE: For testing.
        if (window.SHOW_INFO)
        {
            // TODO: dotnet event

            /*
            const { me } = store.getState();

            store.dispatch(
                stateActions.setRoomStatsPeerId(me.id));
            */
        }
    }
    catch (error)
    {
        console.error('joinRoom() failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Could not join the room: ${error}`
            }));
        */

        close();
    }
}

export async function updateWebcams()
{
    console.debug('updateWebcams()');

    // Reset the list.
    webcams = new Map();

    console.debug('updateWebcams() | calling enumerateDevices()');

    const devices = await navigator.mediaDevices.enumerateDevices();

    for (const device of devices)
    {
        if (device.kind !== 'videoinput')
            continue;

        webcams.set(device.deviceId, device);
    }

    const array = Array.from(webcams.values());
    const len = array.length;
    const currentWebcamId =
        webcam.device ? webcam.device.deviceId : undefined;

    console.debug('updateWebcams() [webcams:%o]', array);

    if (len === 0)
        webcam.device = null;
    else if (!webcams.has(currentWebcamId))
        webcam.device = array[0];

    // TODO: dotnet event
    
    /*
    store.dispatch(
        stateActions.setCanChangeWebcam(this.webcams.size > 1));
     */
}

export function getWebcamType(device)
{
    if (/(back|rear)/i.test(device.label))
    {
        console.debug('_getWebcamType() | it seems to be a back camera');

        return 'back';
    }
    else
    {
        console.debug('_getWebcamType() | it seems to be a front camera');

        return 'front';
    }
}

export async function pauseConsumer(consumer)
{
    if (consumer.paused)
        return;

    try
    {
        await protoo.request('pauseConsumer', { consumerId: consumer.id });

        consumer.pause();

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.setConsumerPaused(consumer.id, 'local'));
        */
    }
    catch (error)
    {
        console.error('pauseConsumer() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error pausing Consumer: ${error}`
            }));
        */
    }
}

export async function resumeConsumer(consumer)
{
    if (!consumer.paused)
        return;

    try
    {
        await protoo.request('resumeConsumer', { consumerId: consumer.id });

        consumer.resume();

        // TODO: dotnet event

        /*
        store.dispatch(
            stateActions.setConsumerResumed(consumer.id, 'local'));
        */
    }
    catch (error)
    {
        console.error('resumeConsumer() | failed:%o', error);

        // TODO: dotnet event

        /*
        store.dispatch(requestActions.notify(
            {
                type : 'error',
                text : `Error resuming Consumer: ${error}`
            }));
        */
    }
}

/////////////
// Cookies //
/////////////

const DEVICES_COOKIE = 'valour.mediasoup.devices';
export function getCookieDevices()
{
    return getCookie(DEVICES_COOKIE);
}

export function setCookieDevices({ webcamEnabled })
{
    setCookie(DEVICES_COOKIE, { webcamEnabled }, "valour.gg", 365);
}

////////////////////
// Protoo helpers //
////////////////////

export const protooPort = 8443;
export const hostname = "voice.valour.gg";

export function getProtooUrl(roomId, peerId, consumerReplicas)
{
    return `wss://${hostname}:${protooPort}/?roomId=${roomId}&peerId=${peerId}&consumerReplicas=${consumerReplicas}`;
}

///////////////////
// e2e functions //
///////////////////

/**
 * Insertable streams.
 *
 * https://github.com/webrtc/samples/blob/gh-pages/src/content/insertable-streams/endtoend-encryption/js/main.js
 */

export let e2eSupported = undefined;
export let worker = undefined;

export function e2eIsSupported()
{
    if (e2eSupported === undefined)
    {
        if (RTCRtpSender.prototype.createEncodedStreams)
        {
            try
            {
                const stream = new ReadableStream();

                window.postMessage(stream, '*', [ stream ]);
                worker = new Worker('/resources/js/e2e-worker.js', { name: 'e2e worker' });

                console.debug('isSupported() | supported');

                e2eSupported = true;
            }
            catch (error)
            {
                console.debug(`isSupported() | not supported: ${error}`);

                e2eSupported = false;
            }
        }
        else
        {
            console.debug('isSupported() | not supported');

            e2eSupported = false;
        }
    }

    return e2eSupported;
}

export function e2eSetCryptoKey(operation, key, useCryptoOffset)
{
    console.debug(
        'setCryptoKey() [operation:%o, useCryptoOffset:%o]',
        operation, useCryptoOffset);

    e2eAssertSupported();

    worker.postMessage(
        {
            operation        : operation,
            currentCryptoKey : key,
            useCryptoOffset  : useCryptoOffset
        });
}

export function e2eSetupSenderTransform(sender)
{
    console.debug('setupSenderTransform()');

    e2eAssertSupported();

    const senderStreams = sender.createEncodedStreams();
    const readableStream = senderStreams.readable || senderStreams.readableStream;
    const writableStream = senderStreams.writable || senderStreams.writableStream;

    worker.postMessage(
        {
            operation : 'encode',
            readableStream,
            writableStream
        },
        [ readableStream, writableStream ]
    );
}

export function e2eSetupReceiverTransform(receiver)
{
    console.debug('setupReceiverTransform()');

    e2eAssertSupported();

    const receiverStreams = receiver.createEncodedStreams();
    const readableStream = receiverStreams.readable || receiverStreams.readableStream;
    const writableStream = receiverStreams.writable || receiverStreams.writableStream;

    worker.postMessage(
        {
            operation : 'decode',
            readableStream,
            writableStream
        },
        [ readableStream, writableStream ]
    );
}

export function e2eAssertSupported()
{
    if (e2eSupported === false)
        throw new Error('e2e not supported');
    else if (e2eSupported === undefined)
        throw new Error('e2e not initialized, must call isSupported() first');
}
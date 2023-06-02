let channelListDotnetRef = null;

function setupChannelList(ref) {
    channelListDotnetRef =  ref;
}

function onChannelListOuterDown(e) 
{
    e.stopPropagation();
}

function onChannelListContextClick(e, channelId){
    console.log('Channel ' + channelId + ' context clicked', e);
    
    if (!channelListDotnetRef)
        return;
    
    channelListDotnetRef.invokeMethodAsync('OnChannelListContextClick', channelId);
}

function onChannelListItemClick(e, channelId){
    console.log('Channel ' + channelId + ' clicked', e);

    e.stopPropagation();

    if (!channelListDotnetRef)
        return;

    channelListDotnetRef.invokeMethodAsync('OnChannelListItemClick', channelId);
}
export function init(id, ref, emojiSet){
    const pickerOptions = {
        onEmojiSelect: e => onEmojiSelect(ref, e),
        onClickOutside: e => onClickOutside(ref, e),
        set: emojiSet,
        theme: 'dark'
    }
    const picker = new EmojiMart.Picker(pickerOptions);
    const wrapper = document.getElementById(id);
    wrapper.appendChild(picker);
}

export function onEmojiSelect(ref, e){
    console.log('', e)
    ref.invokeMethodAsync('EmojiClick', e);
}

export function onClickOutside(ref, e){
    ref.invokeMethodAsync('ClickOutside', { target: e.target?.id });
}

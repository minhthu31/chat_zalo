const bindings = new WeakMap();
export function bind(element, receiver, conversation, enabled, count) {
    let state = bindings.get(element);
    const first = !state;
    if (!state) {
        state = { receiver, conversation, enabled, busy: false };
        // pinned: người dùng đang ở (gần) cuối cuộc trò chuyện. Ảnh tải xong sau đó làm khung dài thêm thì giữ họ ở cuối.
        const updatePinned = () => { state.pinned = element.scrollHeight - element.scrollTop - element.clientHeight < 80; };
        state.pinned = true;
        state.onImageLoad = event => {
            if (event.target instanceof HTMLImageElement && state.pinned) element.scrollTop = element.scrollHeight;
        };
        element.addEventListener('load', state.onImageLoad, true); // 'load' không nổi bọt nên phải bắt ở giai đoạn capture
        state.listener = async () => {
            updatePinned();
            if (!state.enabled || state.busy || element.scrollTop > 24) return;
            state.busy = true;
            const height = element.scrollHeight;
            const top = element.scrollTop;
            const owner = state.conversation;
            try {
                await state.receiver.invokeMethodAsync('LoadOlderFromScroll');
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                if (state.conversation === owner) element.scrollTop = top + element.scrollHeight - height;
            } finally { state.busy = false; }
        };
        element.addEventListener('scroll', state.listener);
        bindings.set(element, state);
    }
    if (first || state.conversation !== conversation || (!state.count && count))
        element.scrollTop = element.scrollHeight;
    state.count = count;
    state.receiver = receiver;
    state.conversation = conversation;
    state.enabled = enabled;
}
export function unbind(element) {
    const state = bindings.get(element);
    if (state) {
        element.removeEventListener('scroll', state.listener);
        element.removeEventListener('load', state.onImageLoad, true);
    }
    bindings.delete(element);
}

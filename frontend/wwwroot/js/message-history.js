const bindings = new WeakMap();
export function bind(element, receiver, conversation, enabled, count) {
    let state = bindings.get(element);
    const first = !state;
    if (!state) {
        state = { receiver, conversation, enabled, busy: false };
        state.listener = async () => {
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
    if (state) element.removeEventListener('scroll', state.listener);
    bindings.delete(element);
}

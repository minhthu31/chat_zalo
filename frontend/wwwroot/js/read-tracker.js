// Cho trang chat biết TIN NÀO ĐANG ĐƯỢC NHÌN THẤY, để chỉ báo "đã xem" cho những tin đó (hợp đồng CHAT-28).
// Chỉ báo khi tab đang hiện và cửa sổ đang tập trung. Tin nằm ngoài vùng nhìn thấy (vd tin cũ vừa hiện lại phía trên
// khi đang ở cuối khung) KHÔNG được báo cho tới khi người dùng cuộn tới.
const trackers = new WeakMap();

function isActive() {
    return document.visibilityState === 'visible' && document.hasFocus();
}

// Một tin được tính là "đã thấy" khi ít nhất nửa chiều cao của nó (hoặc 80px với tin dài) nằm trong khung cuộn
// VÀ trong cửa sổ trình duyệt.
function visibleIds(element) {
    const box = element.getBoundingClientRect();
    const top = Math.max(box.top, 0);
    const bottom = Math.min(box.bottom, window.innerHeight);
    const ids = [];
    if (bottom <= top) return ids;

    for (const item of element.querySelectorAll('li[data-message-id]')) {
        const rect = item.getBoundingClientRect();
        const shown = Math.min(rect.bottom, bottom) - Math.max(rect.top, top);
        if (shown >= Math.min(rect.height * 0.5, 80) && shown > 0) ids.push(item.dataset.messageId);
    }
    return ids;
}

export function bind(element, reference) {
    unbind(element);

    let timer = null;
    const report = () => {
        timer = null;
        if (!isActive()) return;
        const ids = visibleIds(element);
        if (ids.length > 0) reference.invokeMethodAsync('MessagesSeen', ids).catch(() => { });
    };
    // Cuộn nhanh sinh rất nhiều sự kiện: gom lại, tối đa một lần báo mỗi 150ms
    const schedule = () => {
        if (timer === null) timer = setTimeout(report, 150);
    };

    element.addEventListener('scroll', schedule);
    window.addEventListener('scroll', schedule);
    window.addEventListener('resize', schedule);
    window.addEventListener('focus', schedule);
    document.addEventListener('visibilitychange', schedule);
    trackers.set(element, { schedule, cancel: () => { if (timer !== null) clearTimeout(timer); timer = null; } });
}

// Gọi sau mỗi lần vẽ lại danh sách (tin mới, tải thêm tin cũ): kiểm tra lại có tin nào vừa lọt vào vùng nhìn thấy không
export function check(element) {
    trackers.get(element)?.schedule();
}

export function unbind(element) {
    const tracker = trackers.get(element);
    if (!tracker) return;
    tracker.cancel();
    element.removeEventListener('scroll', tracker.schedule);
    window.removeEventListener('scroll', tracker.schedule);
    window.removeEventListener('resize', tracker.schedule);
    window.removeEventListener('focus', tracker.schedule);
    document.removeEventListener('visibilitychange', tracker.schedule);
    trackers.delete(element);
}

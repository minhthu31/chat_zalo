// Âm báo và kiểm tra "người dùng có đang nhìn trang này không" cho thông báo tin nhắn mới.
let context;

// Đang nhìn thấy và đang tập trung vào cửa sổ chat thì không cần banner/âm cho hội thoại đang mở
export function isActive() {
    return document.visibilityState === 'visible' && document.hasFocus();
}

// Báo cho trang chat mỗi khi người dùng QUAY LẠI trang (bấm vào cửa sổ, chuyển lại tab): lúc đó mới đánh dấu "đã xem"
// các tin đến khi họ đang nhìn chỗ khác.
let activity;

export function watchActivity(reference) {
    unwatchActivity();
    const notify = () => {
        if (isActive()) reference.invokeMethodAsync('OnPageActivated').catch(() => { });
    };
    window.addEventListener('focus', notify);
    document.addEventListener('visibilitychange', notify);
    activity = notify;
}

export function unwatchActivity() {
    if (!activity) return;
    window.removeEventListener('focus', activity);
    document.removeEventListener('visibilitychange', activity);
    activity = null;
}

// Hai tiếng "ting" ngắn bằng Web Audio. Trình duyệt chỉ cho phát sau khi người dùng đã bấm vào trang ít nhất một lần.
export function beep() {
    try {
        context ??= new (window.AudioContext || window.webkitAudioContext)();
        if (context.state === 'suspended') context.resume();
        const now = context.currentTime;
        [[880, 0], [1175, 0.14]].forEach(([frequency, offset]) => {
            const oscillator = context.createOscillator();
            const gain = context.createGain();
            oscillator.type = 'sine';
            oscillator.frequency.value = frequency;
            gain.gain.setValueAtTime(0.0001, now + offset);
            gain.gain.exponentialRampToValueAtTime(0.15, now + offset + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, now + offset + 0.16);
            oscillator.connect(gain).connect(context.destination);
            oscillator.start(now + offset);
            oscillator.stop(now + offset + 0.18);
        });
    } catch {
        // Trình duyệt chặn âm thanh: bỏ qua, banner vẫn hiện
    }
}

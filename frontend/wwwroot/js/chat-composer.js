// Xóa lựa chọn hiện tại của ô chọn tệp, để lần sau chọn lại ĐÚNG tệp vừa chọn vẫn tạo ra sự kiện thay đổi.
// Không thay phần tử ô chọn tệp: Blazor giữ dữ liệu các tệp đã chọn ngay trên phần tử đó.
export function resetInput(element) {
    if (element) element.value = '';
}

export function enableEnterToSend(textarea) {
    textarea?.addEventListener('keydown', event => {
        if (event.key !== 'Enter' || event.shiftKey || event.ctrlKey || event.altKey ||
            event.metaKey || event.isComposing || event.keyCode === 229) {
            return;
        }

        event.preventDefault();
        if (event.repeat || textarea.disabled) return;

        const form = textarea.form;
        const button = form?.querySelector('button[type="submit"]:enabled');
        if (button) form.requestSubmit(button);
    });
}

// Xóa lựa chọn hiện tại của ô chọn tệp, để lần sau chọn lại ĐÚNG tệp vừa chọn vẫn tạo ra sự kiện thay đổi.
// Không thay phần tử ô chọn tệp: Blazor giữ dữ liệu các tệp đã chọn ngay trên phần tử đó.
export function resetInput(element) {
    if (element) element.value = '';
}

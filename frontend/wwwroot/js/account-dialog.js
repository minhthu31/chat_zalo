window.chatAccount = {
    open(menu, dialog) {
        menu.open = false;
        if (dialog.open) return;

        dialog.addEventListener('close', () => menu.querySelector('summary')?.focus(), { once: true });
        dialog.showModal();
    }
};

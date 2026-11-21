// Reminders while a tab is open: one setTimeout per reminder in the next ~24 h, shown with the Notification API.
// Browsers can't schedule a notification for a closed tab without a push server, which Show Up doesn't have.
globalThis.ShowUpReminders = (() => {
    const lateLimitMs = 10 * 60 * 1000;
    const timers = new Map();
    let onOpen = null;

    const supported = () => typeof Notification !== "undefined";

    function permission() {
        return supported() ? Notification.permission : "unsupported";
    }

    async function request() {
        if (!supported()) {
            return "unsupported";
        }
        if (Notification.permission !== "default") {
            return Notification.permission;
        }
        return await Notification.requestPermission();
    }

    function setOpenHandler(callback) {
        onOpen = callback;
    }

    function clear() {
        for (const timer of timers.values()) {
            clearTimeout(timer);
        }
        timers.clear();
    }

    function schedule(keys, titles, bodies, times) {
        clear();
        const now = Date.now();
        for (let i = 0; i < keys.length; i++) {
            const reminder = { key: keys[i], title: titles[i], body: bodies[i], at: times[i] };
            const delay = reminder.at - now;
            // setTimeout overflows past 2^31-1 ms (24.8 days).
            if (delay > 0 && delay <= 0x7fffffff) {
                timers.set(reminder.key, setTimeout(() => show(reminder), delay));
            }
        }
        return timers.size;
    }

    function show(reminder) {
        timers.delete(reminder.key);
        // A sleeping laptop fires overdue timers on wake; a stale reminder is noise.
        if (Date.now() - reminder.at > lateLimitMs || permission() !== "granted") {
            return;
        }
        const options = { body: reminder.body, tag: reminder.key };
        try {
            const notification = new Notification(reminder.title, options);
            notification.onclick = () => {
                window.focus();
                notification.close();
                onOpen?.();
            };
        } catch {
            // Android Chrome only allows notifications through a service worker.
            navigator.serviceWorker?.ready.then(registration => registration.showNotification(reminder.title, options)).catch(() => { });
        }
    }

    return { permission, request, setOpenHandler, schedule, clear };
})();

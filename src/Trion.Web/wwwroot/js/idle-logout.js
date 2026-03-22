// idle-logout.js
// Watches for user activity and redirects to /login after a configurable
// period of inactivity.  Call trionIdle.init(timeoutMs) to start.

window.trionIdle = (() => {
    let _logoutTimer = null;
    let _ticker      = null;
    let _deadline    = null;
    let _timeout     = 15 * 60 * 1000; // default 15 min

    const ACTIVITY_EVENTS = [
        'mousemove', 'mousedown', 'keydown',
        'touchstart', 'scroll', 'click',
    ];

    function updateBadge() {
        const countdown = document.getElementById('trion-idle-countdown');
        const badge     = document.getElementById('trion-idle-timer');
        if (!countdown || !badge || !_deadline) return;

        const ms       = Math.max(0, _deadline - Date.now());
        const totalSec = Math.ceil(ms / 1000);
        const m        = Math.floor(totalSec / 60);
        const s        = totalSec % 60;
        countdown.textContent = `${m}:${s.toString().padStart(2, '0')}`;
        badge.classList.toggle('trion-session-badge--warn', ms < 2 * 60 * 1000);
    }

    function reset() {
        clearTimeout(_logoutTimer);
        _deadline    = Date.now() + _timeout;
        _logoutTimer = setTimeout(logout, _timeout);
        updateBadge();
    }

    function logout() {
        try { sessionStorage.removeItem('trion_access_token'); } catch (_) {}
        window.location.href = '/login';
    }

    function init(timeoutMs) {
        _timeout = timeoutMs ?? _timeout;

        for (const ev of ACTIVITY_EVENTS)
            window.addEventListener(ev, reset, { passive: true });

        _ticker = setInterval(updateBadge, 1000);
        reset();
    }

    function dispose() {
        clearTimeout(_logoutTimer);
        clearInterval(_ticker);
        _deadline = null;
        for (const ev of ACTIVITY_EVENTS)
            window.removeEventListener(ev, reset, { passive: true });
    }

    return { init, dispose };
})();

// Reads a cookie value by name (returns null if not found).
window.trionGetCookie = function (name) {
    const match = document.cookie.match(new RegExp('(?:^|;\\s*)' + name + '=([^;]*)'));
    return match ? decodeURIComponent(match[1]) : null;
};

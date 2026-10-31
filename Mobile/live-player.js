/* Fragmented MP4 player: fetch one live connection and append complete MP4 boxes. */
(function (root) {
    'use strict';
    function join(parts) {
        const output = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
        let offset = 0; for (const part of parts) { output.set(part, offset); offset += part.length; }
        return output;
    }
    function marker(bytes, text) {
        for (let i = 4; i + text.length <= bytes.length; i++) {
            let found = true;
            for (let j = 0; j < text.length; j++) if (bytes[i + j] !== text.charCodeAt(j)) { found = false; break; }
            if (found) return i;
        }
        return -1;
    }
    function mimeFor(moov) {
        const avcc = marker(moov, 'avcC');
        if (avcc < 0 || avcc + 7 >= moov.length) throw Error('Live video is missing H.264 decoder configuration.');
        const profile = [moov[avcc + 5], moov[avcc + 6], moov[avcc + 7]].map(n => n.toString(16).padStart(2, '0')).join('');
        return 'video/mp4; codecs="avc1.' + profile + (marker(moov, 'mp4a') >= 0 ? ', mp4a.40.2' : '') + '"';
    }
    class Boxes {
        constructor() { this.pending = new Uint8Array(0); }
        push(chunk) {
            this.pending = join([this.pending, chunk]);
            const result = [];
            while (this.pending.length >= 8) {
                const view = new DataView(this.pending.buffer, this.pending.byteOffset, this.pending.byteLength);
                let size = view.getUint32(0);
                if (size === 1) { if (this.pending.length < 16) break; const big = view.getBigUint64(8); if (big > 33554432n) throw Error('Live MP4 box is too large.'); size = Number(big); }
                if (size < 8 || size > 33554432) throw Error('Invalid live MP4 box size.');
                if (this.pending.length < size) break;
                result.push(this.pending.slice(0, size)); this.pending = this.pending.slice(size);
            }
            return result;
        }
    }
    function start(video, url, options = {}) {
        if (!root.MediaSource) throw Error('This browser does not support live Media Source playback. Please use an up-to-date Chrome browser.');
        const abort = new AbortController(), signal = abort.signal;
        const media = new root.MediaSource(), objectUrl = URL.createObjectURL(media);
        let stopped = false, sourceBuffer, readyResolve, readyReject, settled = false;
        const ready = new Promise((resolve, reject) => { readyResolve = resolve; readyReject = reject; });
        // Avoid an unhandled rejection if a caller only uses the status callback.
        ready.catch(() => {});
        const report = message => { options.status?.(message); options.diagnostic?.(message); };
        const settleError = error => { if (!settled) { settled = true; readyReject(error); } };
        const startup = setTimeout(() => {
            if (!settled && !stopped) {
                const error = Error('No live frame was decoded within 35 seconds. Please copy the Windows debug log.');
                settleError(error); report(error.message); options.error?.(error); abort.abort();
            }
        }, 35000);
        const onPlaying = () => { clearTimeout(startup); if (!settled) { settled = true; readyResolve(); } report('Live video is playing.'); };
        const onVideoError = () => {
            if (stopped || video.getAttribute('src') !== objectUrl) return;
            const error = Error('Browser video decoder error ' + (video.error?.code ?? 'unknown') + '.');
            clearTimeout(startup); settleError(error); report(error.message); options.error?.(error); abort.abort();
        };
        video.addEventListener('playing', onPlaying);
        video.addEventListener('error', onVideoError);
        video.pause(); video.muted = true; video.src = objectUrl;
        function stop() {
            if (stopped) return;
            stopped = true; clearTimeout(startup); abort.abort(); settleError(new DOMException('Live view stopped.', 'AbortError'));
            video.removeEventListener('playing', onPlaying);
            video.removeEventListener('error', onVideoError);
            if (video.getAttribute('src') === objectUrl) { video.pause(); video.removeAttribute('src'); video.load(); }
            URL.revokeObjectURL(objectUrl);
        }
        function waitEvent(target, success, failure) {
            const promise = new Promise((resolve, reject) => {
                const clear = () => { target.removeEventListener(success, done); if (failure) target.removeEventListener(failure, fail); signal.removeEventListener('abort', cancel); };
                const done = () => { clear(); resolve(); };
                const fail = () => { clear(); reject(Error('Browser rejected a live video fragment.')); };
                const cancel = () => { clear(); reject(new DOMException('Live view stopped.', 'AbortError')); };
                target.addEventListener(success, done, { once: true }); if (failure) target.addEventListener(failure, fail, { once: true }); signal.addEventListener('abort', cancel, { once: true });
                if (signal.aborted) cancel();
            });
            promise.catch(() => {}); return promise;
        }
        async function append(bytes) {
            if (sourceBuffer.updating) await waitEvent(sourceBuffer, 'updateend', 'error');
            if (signal.aborted) throw new DOMException('Live view stopped.', 'AbortError');
            const finished = waitEvent(sourceBuffer, 'updateend', 'error'); sourceBuffer.appendBuffer(bytes); await finished;
        }
        const opened = waitEvent(media, 'sourceopen', 'sourceclose');
        (async () => {
            try {
                await opened;
                report('Connecting to the live camera…');
                const response = await fetch(url, { credentials: 'same-origin', signal, cache: 'no-store' });
                if (!response.ok) throw Error('Live stream request failed (HTTP ' + response.status + ').');
                if (!response.body) throw Error('The browser did not expose the live video stream.');
                const reader = response.body.getReader(), parser = new Boxes();
                const initialization = []; let sawMedia = false;
                while (!signal.aborted) {
                    const { value, done } = await reader.read();
                    if (done) break;
                    for (const box of parser.push(value)) {
                        const type = String.fromCharCode(...box.slice(4, 8));
                        if (!sourceBuffer) {
                            initialization.push(box);
                            if (type !== 'moov') continue;
                            const mime = mimeFor(box); report('Live decoder: ' + mime);
                            if (!root.MediaSource.isTypeSupported(mime)) throw Error('This browser cannot decode ' + mime + '.');
                            sourceBuffer = media.addSourceBuffer(mime);
                            await append(join(initialization));
                            video.play().catch(error => { if (error.name === 'NotAllowedError') report('Tap the player triangle to start live video.'); else settleError(error); });
                        } else {
                            await append(box);
                            if (type === 'mdat' && !sawMedia) {
                                sawMedia = true;
                                if (sourceBuffer.buffered.length && video.currentTime < sourceBuffer.buffered.start(0)) video.currentTime = sourceBuffer.buffered.start(0);
                                report('Live video fragments received; waiting for the first frame.');
                            }
                            if (video.currentTime > 40 && sourceBuffer.buffered.length && sourceBuffer.buffered.start(0) < video.currentTime - 30) {
                                const removed = waitEvent(sourceBuffer, 'updateend', 'error'); sourceBuffer.remove(0, video.currentTime - 20); await removed;
                            }
                        }
                    }
                }
                if (!signal.aborted) {
                    if (media.readyState === 'open') media.endOfStream();
                    if (!sawMedia) throw Error('Live session ended without a playable video fragment.');
                    report('Live session ended; playing the remaining buffered video.');
                }
            } catch (error) {
                if (signal.aborted || error.name === 'AbortError') return;
                clearTimeout(startup); settleError(error); report(error.message); options.error?.(error); abort.abort();
            }
        })();
        return { ready, stop };
    }
    root.DontBlinkLive = { start, Boxes, mimeFor };
})(globalThis);

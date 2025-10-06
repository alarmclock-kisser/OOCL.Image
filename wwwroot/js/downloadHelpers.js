window.downloadGifViaPost = async function (url, payloadJson, filenameHint) {
    try {
        const resp = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: payloadJson
        });
        if (!resp.ok) {
            console.error('GIF POST failed', resp.status);
            return { ok: false, status: resp.status };
        }
        const disposition = resp.headers.get('Content-Disposition');
        let fileName = filenameHint || 'animation.gif';
        if (disposition && disposition.includes('filename=')) {
            const m = /filename="?([^"]+)"?/i.exec(disposition);
            if (m) fileName = m[1];
        }
        const blob = await resp.blob();
        if (blob.size === 0) {
            console.error('Empty GIF blob');
            return { ok: false, status: 0 };
        }
        const urlObj = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = urlObj;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        setTimeout(() => {
            URL.revokeObjectURL(urlObj);
            a.remove();
        }, 1500);
        return { ok: true, status: 200 };
    } catch (e) {
        console.error('downloadGifViaPost error', e);
        return { ok: false, status: -1, error: e?.toString() };
    }
};
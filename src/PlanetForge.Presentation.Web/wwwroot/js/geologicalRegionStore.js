// Durable per-device geology, isolated from gameplay save data.
// Transactions complete before callers regard checkpoint writes as persisted.
const databaseName = 'planetforge-regional-geology';
const storeName = 'archives';
const databaseVersion = 1;
const maxArchiveLength = 40_000_000;

function validateKey(key) {
    if (typeof key !== 'string' || !/^planetforge\/geology\/v1\/G\d+:S-?\d+:F\d+:L\d+:X\d+:Y\d+\/YBP:\d+$/.test(key)) {
        throw new Error('Invalid geological region identity.');
    }
}

function openDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, databaseVersion);
        request.onupgradeneeded = () => {
            const database = request.result;
            if (!database.objectStoreNames.contains(storeName)) database.createObjectStore(storeName);
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error ?? new Error('IndexedDB unavailable.'));
        request.onblocked = () => reject(new Error('Another tab blocks the geological archive database upgrade.'));
    });
}

async function transaction(mode, callback) {
    const db = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, mode);
            const request = callback(tx.objectStore(storeName));
            let value;
            request.onsuccess = () => { value = request.result; };
            request.onerror = () => reject(request.error ?? new Error('Geological archive request failed.'));
            tx.oncomplete = () => resolve(value ?? null);
            tx.onerror = () => reject(tx.error ?? new Error('Geological archive transaction failed.'));
            tx.onabort = () => reject(tx.error ?? new Error('Geological archive transaction aborted.'));
        });
    } finally {
        db.close();
    }
}

export async function saveArchive(key, json) {
    validateKey(key);
    if (typeof json !== 'string' || !json.length || json.length > maxArchiveLength) {
        throw new Error('Geological region archive exceeds the supported size.');
    }
    await transaction('readwrite', store => store.put(json, key));
}

export async function loadArchive(key) {
    validateKey(key);
    return await transaction('readonly', store => store.get(key));
}

export async function deleteArchive(key) {
    validateKey(key);
    await transaction('readwrite', store => store.delete(key));
}


const archivePrefix = 'planetforge/geology/v1/';

/**
 * Paginate keys, not 40 MB document values. A DB count is cheap; the cursor
 * traverses only as far as this page's final key.
 */
export async function listArchives(offset = 0, limit = 10) {
    if (!Number.isSafeInteger(offset) || offset < 0 || !Number.isSafeInteger(limit) || limit < 1 || limit > 25) {
        throw new Error('Invalid archive page request.');
    }
    const db = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readonly');
            const store = tx.objectStore(storeName);
            const range = IDBKeyRange.bound(archivePrefix, archivePrefix + '\uffff');
            const countRequest = store.count(range);
            const cursorRequest = store.openKeyCursor(range);
            let count = 0;
            const keys = [];
            let skipped = false;
            countRequest.onsuccess = () => { count = countRequest.result; };
            cursorRequest.onsuccess = () => {
                const cursor = cursorRequest.result;
                if (!cursor || keys.length >= limit) return;
                if (!skipped && offset > 0) {
                    skipped = true;
                    cursor.advance(offset);
                    return;
                }
                skipped = true;
                keys.push(cursor.key);
                if (keys.length < limit) cursor.continue();
            };
            tx.oncomplete = () => resolve({ total: count, keys });
            tx.onerror = () => reject(tx.error ?? new Error('Archive listing failed.'));
            tx.onabort = () => reject(tx.error ?? new Error('Archive listing interrupted.'));
        });
    } finally {
        db.close();
    }
}

/**
 * Compare and write within the same IndexedDB readwrite transaction. This
 * prevents an import preview from overwriting another tab's newer backup.
 * expectedJson=null means the region must not yet exist.
 */
export async function saveArchiveIfUnchanged(key, json, expectedJson) {
    validateKey(key);
    if (typeof json !== 'string' || !json.length || json.length > maxArchiveLength ||
        (expectedJson !== null && typeof expectedJson !== 'string')) {
        throw new Error('Invalid geological archive or replacement token.');
    }
    const db = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readwrite');
            const store = tx.objectStore(storeName);
            const current = store.get(key);
            let conflict = false;
            current.onsuccess = () => {
                if ((current.result ?? null) !== expectedJson) {
                    conflict = true;
                    tx.abort();
                    return;
                }
                store.put(json, key);
            };
            tx.oncomplete = () => resolve(true);
            tx.onabort = () => reject(new Error(conflict
                ? 'Archive changed in another tab. Reopen the region before replacing it.'
                : 'Archive save was interrupted.'));
            tx.onerror = () => reject(tx.error ?? new Error('Archive save failed.'));
        });
    } finally {
        db.close();
    }
}

export async function deleteArchiveIfUnchanged(key, expectedJson) {
    validateKey(key);
    if (typeof expectedJson !== 'string') throw new Error('Missing archive deletion token.');
    const db = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readwrite');
            const store = tx.objectStore(storeName);
            const current = store.get(key);
            let conflict = false;
            current.onsuccess = () => {
                if (current.result !== expectedJson) {
                    conflict = true;
                    tx.abort();
                    return;
                }
                store.delete(key);
            };
            tx.oncomplete = () => resolve(true);
            tx.onabort = () => reject(new Error(conflict
                ? 'Archive changed in another tab. Reopen it before deleting.'
                : 'Archive deletion was interrupted.'));
            tx.onerror = () => reject(tx.error ?? new Error('Archive deletion failed.'));
        });
    } finally {
        db.close();
    }
}

export function downloadArchive(json, filename) {
    if (typeof json !== 'string' || json.length > maxArchiveLength) {
        throw new Error('Invalid archive export.');
    }
    const safeName = String(filename).replace(/[^a-zA-Z0-9._-]/g, '_').slice(0, 150);
    const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = safeName.endsWith('.json') ? safeName : safeName + '.json';
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
}


/**
 * Read only geographical identities (no elevation arrays) for the present
 * epoch of one generated planet. Return at most 256 addresses to keep mobile
 * catalogues bounded; report truncation instead of pretending a partial
 * catalogue represents the whole saved planet.
 */
export async function listPresentGeologicalRegions(seed, version, limit = 256) {
    if (!Number.isSafeInteger(seed) || !Number.isSafeInteger(version) || version < 1 ||
        !Number.isSafeInteger(limit) || limit < 1 || limit > 256) {
        throw new Error('Invalid geological region catalogue request.');
    }
    const prefix = `${archivePrefix}G${version}:S${seed}:`;
    const db = await openDatabase();
    try {
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readonly');
            const cursorRequest = tx.objectStore(storeName).openKeyCursor(
                IDBKeyRange.bound(prefix, prefix + '\uffff'));
            const keys = [];
            let truncated = false;
            cursorRequest.onsuccess = () => {
                const cursor = cursorRequest.result;
                if (!cursor) return;
                if (String(cursor.key).endsWith('/YBP:0')) {
                    if (keys.length >= limit) {
                        truncated = true;
                        return;
                    }
                    keys.push(cursor.key);
                }
                cursor.continue();
            };
            tx.oncomplete = () => resolve({ keys, truncated });
            tx.onerror = () => reject(tx.error ?? new Error('Geological region catalogue failed.'));
            tx.onabort = () => reject(tx.error ?? new Error('Geological region catalogue was interrupted.'));
        });
    } finally {
        db.close();
    }
}

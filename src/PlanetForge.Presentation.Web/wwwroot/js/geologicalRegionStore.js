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

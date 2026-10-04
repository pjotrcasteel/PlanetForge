const storageKey = 'planetforge:planet-run:v1';
const freshRunParameter = 'fresh';
const waterFieldStudyPrefix = 'planetforge-water-field-study-';

export function load() {
    if (shouldStartFresh()) {
        clearAll();
        removeFreshRunParameter();
        return null;
    }

    const saved = window.localStorage.getItem(storageKey);
    if (!saved) {
        clearWaterFieldStudyState();
    }

    return saved;
}

export function save(json) {
    window.localStorage.setItem(storageKey, json);
}

export function clear() {
    window.localStorage.removeItem(storageKey);
}

export function clearAll() {
    clear();
    clearWaterFieldStudyState();
}

function shouldStartFresh() {
    const parameters = new URLSearchParams(window.location.search);
    return parameters.get(freshRunParameter) === '1' || parameters.has(freshRunParameter);
}

function clearWaterFieldStudyState() {
    const keys = [];
    for (let index = 0; index < window.localStorage.length; index++) {
        const key = window.localStorage.key(index);
        if (key?.startsWith(waterFieldStudyPrefix)) keys.push(key);
    }

    for (const key of keys) window.localStorage.removeItem(key);
}

function removeFreshRunParameter() {
    const url = new URL(window.location.href);
    url.searchParams.delete(freshRunParameter);
    window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`);
}

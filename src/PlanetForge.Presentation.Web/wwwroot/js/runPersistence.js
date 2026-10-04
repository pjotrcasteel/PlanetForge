const storageKey = 'planetforge:planet-run:v1';

export function load() {
    return window.localStorage.getItem(storageKey);
}

export function save(json) {
    window.localStorage.setItem(storageKey, json);
}

export function clear() {
    window.localStorage.removeItem(storageKey);
}
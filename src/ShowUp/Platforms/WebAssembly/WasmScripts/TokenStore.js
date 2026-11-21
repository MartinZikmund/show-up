// Session storage for Supabase. localStorage is readable by any script on this origin: see LocalStorageTokenStore.
globalThis.ShowUpTokens = {
    get: (key) => localStorage.getItem(key),
    set: (key, value) => localStorage.setItem(key, value),
    remove: (key) => localStorage.removeItem(key),
};

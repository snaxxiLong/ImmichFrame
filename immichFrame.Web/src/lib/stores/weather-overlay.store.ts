import { writable } from 'svelte/store';

/** True while the big weather overlay is open, so other overlays (e.g. appointments) can step aside. */
export const weatherOverlayOpenStore = writable<boolean>(false);

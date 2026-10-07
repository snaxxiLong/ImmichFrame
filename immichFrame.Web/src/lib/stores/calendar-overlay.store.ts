import { writable } from 'svelte/store';

/** True while the month calendar is open, so the clock and appointment list step aside. */
export const calendarOverlayOpenStore = writable<boolean>(false);

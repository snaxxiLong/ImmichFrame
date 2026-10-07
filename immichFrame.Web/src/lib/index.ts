// place files you want to import through the `$lib` alias in this folder.
import { defaults, type AssetTypeEnum, type IAppointment } from './immichFrameApi.js';
import { format } from 'date-fns';
import { authSecretStore } from '$lib/stores/persist.store';
import { get } from 'svelte/store';

export * from './immichFrameApi.js';

let isAuthListenerRegistered = false;

export const init = () => {
	setBearer();
	sendAuthSecretToServiceWorker();
};

const sendMessage = () => {
	if (navigator.serviceWorker.controller) {
		navigator.serviceWorker.controller.postMessage({
			type: 'SET_AUTH_SECRET',
			authSecret: get(authSecretStore)
		});
	}
};

export const sendAuthSecretToServiceWorker = () => {
	if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return;

	// Send immediately if controller is ready
	sendMessage();

	// Also send when service worker becomes ready (for initial page load)
	navigator.serviceWorker.ready.then(sendMessage);

	// Listen for auth secret requests from service worker (register only once)
	if (!isAuthListenerRegistered) {
		isAuthListenerRegistered = true;
		navigator.serviceWorker.addEventListener('message', (event) => {
			if (event.data && event.data.type === 'REQUEST_AUTH_SECRET') {
				sendMessage();
			}
		});
	}
};

export interface WeatherForecastEntry {
	time: string;
	temperature: number;
	description: string;
	iconId: string;
}

const authHeaders = () => ({ Authorization: 'Bearer ' + get(authSecretStore) });

export interface WeatherHour {
	time: string;
	temperature: number;
	description: string;
	iconId: string;
	precipitationProbability?: number | null;
	precipitation: number;
}

export interface WeatherDay {
	date: string;
	temperatureMax: number;
	temperatureMin: number;
	description: string;
	iconId: string;
	precipitationProbability?: number | null;
	precipitationSum: number;
	sunrise?: string | null;
	sunset?: string | null;
}

export interface WeatherDetails {
	hours: WeatherHour[];
	days: WeatherDay[];
}

export const getWeatherDetails = async (clientIdentifier?: string) => {
	const params = new URLSearchParams();
	if (clientIdentifier) params.set('clientIdentifier', clientIdentifier);
	const res = await fetch(`/api/Weather/Details?${params}`, { headers: authHeaders() });
	if (!res.ok) throw new Error(`Weather details request failed: ${res.status}`);
	return (await res.json()) as WeatherDetails;
};

export interface ScreenBrightness {
	enabled: boolean;
	brightness: number;
	irradiance?: number | null;
}

export const getScreenBrightness = async (clientIdentifier?: string) => {
	const params = new URLSearchParams();
	if (clientIdentifier) params.set('clientIdentifier', clientIdentifier);
	const res = await fetch(`/api/Weather/Brightness?${params}`, { headers: authHeaders() });
	if (!res.ok) throw new Error(`Brightness request failed: ${res.status}`);
	return (await res.json()) as ScreenBrightness;
};

export const getWeatherForecast = async (count: number, clientIdentifier?: string) => {
	const params = new URLSearchParams({ count: String(count) });
	if (clientIdentifier) params.set('clientIdentifier', clientIdentifier);
	const res = await fetch(`/api/Weather/Forecast?${params}`, { headers: authHeaders() });
	if (!res.ok) throw new Error(`Forecast request failed: ${res.status}`);
	return (await res.json()) as WeatherForecastEntry[];
};

export const deleteAsset = async (id: string, clientIdentifier?: string) => {
	const params = new URLSearchParams();
	if (clientIdentifier) params.set('clientIdentifier', clientIdentifier);
	const res = await fetch(`/api/Asset/${encodeURIComponent(id)}?${params}`, {
		method: 'DELETE',
		headers: authHeaders()
	});
	if (!res.ok) throw new Error(`Delete request failed: ${res.status}`);
};

/** Local date-time without zone, so the server reads it as its own local time. */
const localIso = (d: Date) => format(d, "yyyy-MM-dd'T'HH:mm:ss");

export const getAppointmentsInRange = async (from: Date, to: Date, clientIdentifier?: string) => {
	const params = new URLSearchParams({ from: localIso(from), to: localIso(to) });
	if (clientIdentifier) params.set('clientIdentifier', clientIdentifier);
	const res = await fetch(`/api/Calendar/range?${params}`, { headers: authHeaders() });
	if (!res.ok) throw new Error(`Calendar request failed: ${res.status}`);
	return (await res.json()) as IAppointment[];
};

export const getBaseUrl = () => defaults.baseUrl;

export const setBaseUrl = (baseUrl: string) => {
	defaults.baseUrl = baseUrl;
};

export const setBearer = () => {
	defaults.headers = defaults.headers || {};
	defaults.headers['Authorization'] = 'Bearer ' + get(authSecretStore);
};

export const getAssetStreamUrl = (
	id: string,
	clientIdentifier?: string,
	assetType?: AssetTypeEnum
) => {
	const params = new URLSearchParams();
	if (clientIdentifier) params.set('clientIdentifier', clientIdentifier);
	if (assetType !== undefined) params.set('assetType', String(assetType));
	const query = params.toString();
	return `/api/Asset/${encodeURIComponent(id)}/Asset${query ? '?' + query : ''}`;
};

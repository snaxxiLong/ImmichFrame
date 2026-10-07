<script lang="ts">
	import * as api from '$lib/index';
	import { onMount } from 'svelte';
	import { format } from 'date-fns';
	import * as locale from 'date-fns/locale';
	import { configStore } from '$lib/stores/config.store';
	import { clientIdentifierStore } from '$lib/stores/persist.store';
	import { weatherOverlayOpenStore } from '$lib/stores/weather-overlay.store';
	import { calendarOverlayOpenStore } from '$lib/stores/calendar-overlay.store';
	import WeatherOverlay from './weather-overlay.svelte';

	api.init();

	let weather = $state<api.IWeather | null>(null);
	// Preloaded with the weather so the overlay opens with its data instead of a loading state.
	let weatherDetails = $state<api.WeatherDetails | null>(null);
	let weatherOverlayOpen = $state(false);

	// The overlay is see-through and the slideshow keeps running behind it; only this compact
	// clock is hidden while the big weather view is open.
	function openWeatherOverlay(event: MouseEvent) {
		event.stopPropagation();
		weatherOverlayOpen = true;
		weatherOverlayOpenStore.set(true);
	}

	function closeWeatherOverlay() {
		weatherOverlayOpen = false;
		weatherOverlayOpenStore.set(false);
	}
	let forecast = $state<api.WeatherForecastEntry[]>([]);

	const FORECAST_COUNT = 3;

	const localeToUse = $derived(
		() => locale[$configStore.language as keyof typeof locale] ?? locale.enUS
	);

	let now = $state(new Date());

	const formattedDate = $derived(() =>
		format(now, $configStore.clockDateFormat ?? 'eee, MMM d', {
			locale: localeToUse()
		})
	);

	const timePortion = $derived(() => format(now, $configStore.clockFormat ?? 'HH:mm:ss'));

	const primaryIconId = $derived(() => {
        if (!weather?.iconId) return null;
        const firstId = weather.iconId.split(',')[0].trim();
        return firstId || null;
    });

	onMount(() => {
		const interval = setInterval(() => {
			now = new Date();
		}, 1000);

		getWeather();
		const weatherInterval = setInterval(() => getWeather(), 10 * 60 * 1000);

		return () => {
			clearInterval(interval);
			clearInterval(weatherInterval);
		};
	});

	async function getWeather() {
		try {
			const weatherRequest = await api.getWeather({ clientIdentifier: $clientIdentifierStore });
			if (weatherRequest.status === 200) {
				weather = weatherRequest.data;
			} else {
				console.warn('Unexpected weather status:', weatherRequest.status);
			}
		} catch (err) {
			console.error('Error fetching weather:', err);
		}

		try {
			forecast = await api.getWeatherForecast(FORECAST_COUNT, $clientIdentifierStore);
		} catch (err) {
			console.error('Error fetching weather forecast:', err);
		}

		try {
			weatherDetails = await api.getWeatherDetails($clientIdentifierStore);
		} catch (err) {
			console.error('Error fetching weather details:', err);
		}
	}

	const iconUrl = (iconId: string) =>
		$configStore.weatherIconUrl?.replace('{IconId}', encodeURIComponent(iconId.split(',')[0].trim()));
</script>

<div
	id="clock"
	class="fixed bottom-0 left-0 z-10 text-center text-frame-primary
	{$configStore.style == 'solid' ? 'bg-frame-secondary rounded-tr-2xl' : ''}
	{$configStore.style == 'transition' ? 'bg-linear-to-r from-frame-secondary from-0% pr-10' : ''}
	{$configStore.style == 'blur' ? 'backdrop-blur-lg rounded-tr-2xl' : ''}	
	drop-shadow-2xl p-3"
	style="z-index: 110; pointer-events: none; visibility: {weatherOverlayOpen || $calendarOverlayOpenStore ? 'hidden' : 'visible'}"
>
	<p id="clockdate" class="mt-2 text-sm sm:text-sm md:text-md lg:text-xl font-thin text-shadow-sm">
		{formattedDate()}
	</p>
	<p
		id="clocktime"
		class="mt-2 text-4xl sm:text-4xl md:text-6xl lg:text-8xl font-bold text-shadow-lg"
	>
		{timePortion()}
	</p>
	{#if weather}
    <!-- Sits above the slideshow's tap areas; tapping the weather opens the day overview. -->
    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div id="clockweather" style="zoom: 0.75; pointer-events: auto; cursor: pointer" onclick={openWeatherOverlay}>
        <div
            id="clockweatherinfo"
            class="text-xl sm:text-xl md:text-2xl lg:text-3xl font-semibold text-shadow-sm weather-info"
        >
            {#if $configStore.weatherIconUrl && primaryIconId()}
                <img 
                    src="{$configStore.weatherIconUrl.replace('{IconId}', encodeURIComponent(primaryIconId()!))}" 
                    class="icon-weather" 
                    alt="{weather.description}"
                >
            {/if}
            
            <div class="weather-location">{weather.location},</div>
            <div class="weather-temperature">{Math.round(weather.temperature ?? 0)}°</div>
        </div>
        
        {#if $configStore.showWeatherDescription}
            <p id="clockweatherdesc" class="text-sm sm:text-sm md:text-md lg:text-xl text-shadow-sm">
                {weather.description}
            </p>
        {/if}

        {#if forecast.length}
            <div id="clockforecast" class="mt-2 flex justify-center gap-4 text-sm sm:text-sm md:text-md lg:text-xl text-shadow-sm">
                {#each forecast as entry (entry.time)}
                    <div class="flex flex-col items-center leading-tight">
                        <span class="font-thin">{format(new Date(entry.time), 'HH:mm')}</span>
                        {#if $configStore.weatherIconUrl && entry.iconId}
                            <img src={iconUrl(entry.iconId)} class="icon-forecast h-8 w-8" alt={entry.description} />
                        {/if}
                        <span class="font-semibold">{Math.round(entry.temperature)}°</span>
                    </div>
                {/each}
            </div>
        {/if}
    </div>
{/if}
</div>

{#if weather}
	<WeatherOverlay {weather} preloaded={weatherDetails} open={weatherOverlayOpen} onClose={closeWeatherOverlay} />
{/if}

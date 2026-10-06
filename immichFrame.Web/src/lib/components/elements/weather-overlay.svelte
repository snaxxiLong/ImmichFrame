<script lang="ts">
	import * as api from '$lib/index';
	import { onMount } from 'svelte';
	import { format } from 'date-fns';
	import * as locale from 'date-fns/locale';
	import { configStore } from '$lib/stores/config.store';
	import { clientIdentifierStore } from '$lib/stores/persist.store';

	interface Props {
		weather: api.IWeather;
		onClose: () => void;
	}

	let { weather, onClose }: Props = $props();

	// Plain hex colors: older Android WebViews ignore Tailwind's oklch() palette.
	const SURFACE = '#171717';
	const TEXT = '#f5f5f5';
	const TEXT_MUTED = '#a3a3a3';
	const GRID = '#333333';
	const TEMP_COLOR = '#c2821a';
	const RAIN_COLOR = '#3a85d0';

	// Explicit margins: the frame's font collapses the spaces around a middle dot.
	const SEPARATOR = 'margin: 0 0.45em';

	const AUTO_CLOSE_MS = 60000;

	// Chart geometry in SVG units; the SVG scales uniformly with the panel width.
	const WIDTH = 1200;
	const TEMP_TOP = 40;
	const TEMP_BOTTOM = 150;
	const RAIN_TOP = 185;
	const RAIN_BOTTOM = 245;
	const TIME_Y = 280;
	const ICON_Y = 292;
	const ICON_SIZE = 44;
	const HEIGHT = 345;
	const LABEL_EVERY = 2;

	let details = $state<api.WeatherDetails | null>(null);
	let failed = $state(false);

	const isGerman = $derived(($configStore.language ?? '').toLowerCase().startsWith('de'));
	const dateLocale = $derived(locale[$configStore.language as keyof typeof locale] ?? locale.enUS);

	const iconUrl = (iconId: string) =>
		$configStore.weatherIconUrl?.replace('{IconId}', encodeURIComponent(iconId.split(',')[0].trim()));

	const hours = $derived(details?.hours ?? []);
	const today = $derived(details?.days?.[0]);
	const nextDays = $derived((details?.days ?? []).slice(1, 4));

	const columnWidth = $derived(hours.length ? WIDTH / hours.length : WIDTH);
	const xAt = (i: number) => (i + 0.5) * columnWidth;

	const tempRange = $derived.by(() => {
		if (!hours.length) return { min: 0, max: 1 };
		const temps = hours.map((h) => h.temperature);
		const min = Math.min(...temps);
		const max = Math.max(...temps);
		// Keep at least a few degrees of range so a flat day does not look dramatic.
		const pad = Math.max(0, 4 - (max - min)) / 2;
		return { min: min - pad, max: max + pad };
	});

	const yForTemp = (t: number) =>
		TEMP_BOTTOM - ((t - tempRange.min) / (tempRange.max - tempRange.min)) * (TEMP_BOTTOM - TEMP_TOP);

	const tempPath = $derived(
		hours.map((h, i) => `${i === 0 ? 'M' : 'L'}${xAt(i).toFixed(1)},${yForTemp(h.temperature).toFixed(1)}`).join(' ')
	);

	const rainHeight = (p?: number | null) => ((p ?? 0) / 100) * (RAIN_BOTTOM - RAIN_TOP);

	// Bar with 4px rounded top, anchored square to the baseline.
	const barPath = (x: number, w: number, h: number) => {
		if (h <= 0) return '';
		const r = Math.min(4, h, w / 2);
		const top = RAIN_BOTTOM - h;
		return `M${x},${RAIN_BOTTOM} V${top + r} Q${x},${top} ${x + r},${top} H${x + w - r} Q${x + w},${top} ${x + w},${top + r} V${RAIN_BOTTOM} Z`;
	};

	const time = (iso?: string | null) => (iso ? format(new Date(iso), 'HH:mm') : '–');
	const dayName = (iso: string, index: number) =>
		index === 0
			? isGerman
				? 'Morgen'
				: 'Tomorrow'
			: format(new Date(iso), 'EEEE', { locale: dateLocale });

	onMount(() => {
		api
			.getWeatherDetails($clientIdentifierStore)
			.then((d) => (details = d))
			.catch((err) => {
				console.error('Error fetching weather details:', err);
				failed = true;
			});
		const timeout = setTimeout(onClose, AUTO_CLOSE_MS);
		return () => clearTimeout(timeout);
	});
</script>

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div
	id="weatheroverlay"
	class="fixed inset-0 grid place-items-center"
	style="z-index: 300; background-color: rgba(0, 0, 0, 0.8)"
	onclick={onClose}
>
	<div
		class="rounded-2xl shadow-2xl"
		style="width: 94vw; max-height: 94vh; padding: 2.5vh 2.5vw; background-color: {SURFACE}; color: {TEXT}"
	>
		<!-- Now and today -->
		<div class="flex items-center justify-between" style="gap: 3vw">
			<div class="flex items-center" style="gap: 1.5vw">
				{#if $configStore.weatherIconUrl && weather.iconId}
					<img src={iconUrl(weather.iconId)} alt="" style="width: 12vh; height: 12vh" />
				{/if}
				<div>
					<div style="font-size: 9vh; font-weight: 700; line-height: 1">
						{Math.round(weather.temperature ?? 0)}°
					</div>
					<div style="font-size: 3vh; color: {TEXT_MUTED}">
						{weather.description}<span style={SEPARATOR}>·</span>{weather.location}
					</div>
				</div>
			</div>
			{#if today}
				<div class="grid" style="grid-template-columns: auto auto; column-gap: 2vw; row-gap: 0.8vh; font-size: 3vh">
					<span style="color: {TEXT_MUTED}">{isGerman ? 'Höchst / Tiefst' : 'High / Low'}</span>
					<span style="font-weight: 600">{Math.round(today.temperatureMax)}° / {Math.round(today.temperatureMin)}°</span>
					<span style="color: {TEXT_MUTED}">{isGerman ? 'Regen' : 'Rain'}</span>
					<span style="font-weight: 600">
						{today.precipitationProbability ?? 0}%<span style={SEPARATOR}>·</span>{today.precipitationSum.toFixed(1)} mm
					</span>
					<span style="color: {TEXT_MUTED}">{isGerman ? 'Sonne' : 'Sun'}</span>
					<span style="font-weight: 600">↑&nbsp;{time(today.sunrise)}<span style={SEPARATOR}>·</span>↓&nbsp;{time(today.sunset)}</span>
				</div>
			{/if}
		</div>

		<!-- Next 24 hours: temperature line and rain probability bars as two stacked panels on one time axis -->
		{#if hours.length}
			<svg viewBox="0 0 {WIDTH} {HEIGHT}" style="width: 100%; height: auto; margin-top: 2vh; display: block">
				<text x="0" y="18" fill={TEXT_MUTED} font-size="20">
					{isGerman ? 'Temperatur, nächste 24 Stunden' : 'Temperature, next 24 hours'}
				</text>
				<line x1="0" x2={WIDTH} y1={TEMP_BOTTOM + 10} y2={TEMP_BOTTOM + 10} stroke={GRID} stroke-width="1" />
				<path d={tempPath} fill="none" stroke={TEMP_COLOR} stroke-width="3" stroke-linejoin="round" stroke-linecap="round" />
				{#each hours as hour, i (hour.time)}
					{#if i % LABEL_EVERY === 0}
						<circle cx={xAt(i)} cy={yForTemp(hour.temperature)} r="5" fill={TEMP_COLOR} stroke={SURFACE} stroke-width="2" />
						<text x={xAt(i)} y={yForTemp(hour.temperature) - 14} text-anchor="middle" fill={TEXT} font-size="22" font-weight="600">
							{Math.round(hour.temperature)}°
						</text>
					{/if}
				{/each}

				<text x="0" y={RAIN_TOP - 12} fill={TEXT_MUTED} font-size="20">
					{isGerman ? 'Regenwahrscheinlichkeit' : 'Chance of rain'}
				</text>
				<line x1="0" x2={WIDTH} y1={RAIN_BOTTOM} y2={RAIN_BOTTOM} stroke={GRID} stroke-width="1" />
				{#if !hours.some((h) => (h.precipitationProbability ?? 0) > 0)}
					<text x={WIDTH / 2} y={RAIN_BOTTOM - 22} text-anchor="middle" fill={TEXT_MUTED} font-size="20">
						{isGerman ? 'Kein Regen erwartet' : 'No rain expected'}
					</text>
				{/if}
				{#each hours as hour, i (hour.time)}
					<path d={barPath(i * columnWidth + 1, columnWidth - 2, rainHeight(hour.precipitationProbability))} fill={RAIN_COLOR} />
					{#if i % LABEL_EVERY === 0 && (hour.precipitationProbability ?? 0) >= 10}
						<text
							x={xAt(i)}
							y={RAIN_BOTTOM - rainHeight(hour.precipitationProbability) - 6}
							text-anchor="middle"
							fill={TEXT}
							font-size="18"
						>
							{hour.precipitationProbability}%
						</text>
					{/if}
				{/each}

				{#each hours as hour, i (hour.time)}
					{#if i % LABEL_EVERY === 0}
						<text x={xAt(i)} y={TIME_Y} text-anchor="middle" fill={TEXT_MUTED} font-size="20">
							{format(new Date(hour.time), 'HH:mm')}
						</text>
						{#if $configStore.weatherIconUrl}
							<image
								href={iconUrl(hour.iconId)}
								x={xAt(i) - ICON_SIZE / 2}
								y={ICON_Y}
								width={ICON_SIZE}
								height={ICON_SIZE}
							/>
						{/if}
					{/if}
				{/each}
			</svg>
		{:else if failed}
			<p style="margin-top: 3vh; font-size: 3vh; color: {TEXT_MUTED}">
				{isGerman ? 'Vorhersage konnte nicht geladen werden.' : 'Could not load the forecast.'}
			</p>
		{:else}
			<p style="margin-top: 3vh; font-size: 3vh; color: {TEXT_MUTED}">
				{isGerman ? 'Lade Vorhersage …' : 'Loading forecast …'}
			</p>
		{/if}

		<!-- Next days -->
		{#if nextDays.length}
			<div class="grid" style="grid-template-columns: repeat({nextDays.length}, 1fr); gap: 2vw; margin-top: 2vh; border-top: 1px solid {GRID}; padding-top: 2vh">
				{#each nextDays as day, i (day.date)}
					<div class="flex items-center" style="gap: 1vw; font-size: 3vh">
						{#if $configStore.weatherIconUrl}
							<img src={iconUrl(day.iconId)} alt="" style="width: 7vh; height: 7vh" />
						{/if}
						<div>
							<div style="font-weight: 600">{dayName(day.date, i)}</div>
							<div>
								{Math.round(day.temperatureMax)}° / <span style="color: {TEXT_MUTED}">{Math.round(day.temperatureMin)}°</span>
								<span style="color: {TEXT_MUTED}"><span style={SEPARATOR}>·</span>{day.precipitationProbability ?? 0}%</span>
							</div>
						</div>
					</div>
				{/each}
			</div>
		{/if}

		<p style="margin-top: 1.5vh; text-align: center; font-size: 2.2vh; color: {TEXT_MUTED}">
			{isGerman ? 'Tippen zum Schließen' : 'Tap to close'}
		</p>
	</div>
</div>

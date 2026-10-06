<script lang="ts">
	import * as api from '$lib/index';
	import { onMount } from 'svelte';
	import { format } from 'date-fns';
	import * as locale from 'date-fns/locale';
	import { configStore } from '$lib/stores/config.store';
	import { clientIdentifierStore } from '$lib/stores/persist.store';

	interface Props {
		weather: api.IWeather;
		/** Details preloaded by the clock; fetched here only if they are not available yet. */
		preloaded?: api.WeatherDetails | null;
		onClose: () => void;
	}

	let { weather, preloaded = null, onClose }: Props = $props();

	// Plain colors: older Android WebViews ignore Tailwind's oklch() palette. The overlay is see-through,
	// so the photos keep running behind a dark scrim; shadows keep text and marks readable on top.
	const SCRIM = 'rgba(0, 0, 0, 0.55)';
	const SHADOW = 'rgba(0, 0, 0, 0.85)';
	const TEXT = '#f5f5f5';
	const TEXT_MUTED = '#d4d4d4';
	const GRID = 'rgba(255, 255, 255, 0.3)';
	const NOW_COLOR = 'rgba(255, 255, 255, 0.6)';
	const TEMP_COLOR = '#c2821a';
	const RAIN_COLOR = '#3a85d0';

	// A CSS dot instead of "·": the frame's font gives that glyph lopsided spacing.
	const SEPARATOR =
		'display: inline-block; width: 0.2em; height: 0.2em; border-radius: 50%; background-color: currentColor; vertical-align: middle; margin: 0 0.5em';

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

	let fetched = $state<api.WeatherDetails | null>(null);
	const details = $derived(preloaded ?? fetched);
	let failed = $state(false);

	const isGerman = $derived(($configStore.language ?? '').toLowerCase().startsWith('de'));
	const dateLocale = $derived(locale[$configStore.language as keyof typeof locale] ?? locale.enUS);

	const iconUrl = (iconId: string) =>
		$configStore.weatherIconUrl?.replace('{IconId}', encodeURIComponent(iconId.split(',')[0].trim()));

	const hours = $derived(details?.hours ?? []);
	const today = $derived(details?.days?.[0]);
	const nextDays = $derived((details?.days ?? []).slice(1, 4));

	// Side padding so the 00:00 and 24:00 labels are not cut off at the edges.
	const PAD_X = 28;
	const columnWidth = $derived(hours.length ? (WIDTH - 2 * PAD_X) / hours.length : WIDTH);
	const xAt = (i: number) => PAD_X + (i + 0.5) * columnWidth;

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

	// The series runs from today 00:00 to 24:00; the last point is tomorrow's midnight.
	const hourLabel = (iso: string, i: number) =>
		i === hours.length - 1 && i > 0 ? '24:00' : format(new Date(iso), 'HH:mm');

	// Current time as a position on the hour axis (column centers are the full hours).
	const nowX = $derived.by(() => {
		if (!hours.length) return null;
		const start = new Date(hours[0].time).getTime();
		const hoursSinceStart = (Date.now() - start) / 3600000;
		if (hoursSinceStart < 0 || hoursSinceStart > hours.length - 1) return null;
		return PAD_X + (hoursSinceStart + 0.5) * columnWidth;
	});

	const time = (iso?: string | null) => (iso ? format(new Date(iso), 'HH:mm') : '–');
	const dayName = (iso: string, index: number) =>
		index === 0
			? isGerman
				? 'Morgen'
				: 'Tomorrow'
			: format(new Date(iso), 'EEEE', { locale: dateLocale });

	onMount(() => {
		if (!preloaded) {
			api
				.getWeatherDetails($clientIdentifierStore)
				.then((d) => (fetched = d))
				.catch((err) => {
					console.error('Error fetching weather details:', err);
					failed = true;
				});
		}
		const timeout = setTimeout(onClose, AUTO_CLOSE_MS);
		return () => clearTimeout(timeout);
	});
</script>

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div
	id="weatheroverlay"
	class="fixed inset-0"
	style="z-index: 300; background-color: {SCRIM}"
	onclick={onClose}
>
	<div
		class="flex flex-col justify-between"
		style="width: 100vw; height: 100vh; padding: 4vh 4vw; box-sizing: border-box; color: {TEXT}; text-shadow: 0 1px 4px {SHADOW}"
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
						{weather.description}<span style={SEPARATOR}></span>{weather.location}
					</div>
				</div>
			</div>
			{#if today}
				<div class="grid" style="grid-template-columns: auto auto; column-gap: 2vw; row-gap: 0.8vh; font-size: 3vh">
					<span style="color: {TEXT_MUTED}">{isGerman ? 'Höchst / Tiefst' : 'High / Low'}</span>
					<span style="font-weight: 600">{Math.round(today.temperatureMax)}° / {Math.round(today.temperatureMin)}°</span>
					<span style="color: {TEXT_MUTED}">{isGerman ? 'Regen' : 'Rain'}</span>
					<span style="font-weight: 600">
						{today.precipitationProbability ?? 0}%<span style={SEPARATOR}></span>{today.precipitationSum.toFixed(1)} mm
					</span>
					<span style="color: {TEXT_MUTED}">{isGerman ? 'Sonne' : 'Sun'}</span>
					<span style="font-weight: 600">↑&nbsp;{time(today.sunrise)}<span style={SEPARATOR}></span>↓&nbsp;{time(today.sunset)}</span>
				</div>
			{/if}
		</div>

		<!-- Today 00:00-24:00: temperature line and rain probability bars as two stacked panels on one time axis -->
		{#if hours.length}
			<svg
				viewBox="0 0 {WIDTH} {HEIGHT}"
				style="width: 100%; height: auto; display: block; filter: drop-shadow(0 1px 3px {SHADOW})"
			>
				<text x="0" y="18" fill={TEXT_MUTED} font-size="20">
					{isGerman ? 'Temperatur heute' : 'Temperature today'}
				</text>
				<line x1="0" x2={WIDTH} y1={TEMP_BOTTOM + 10} y2={TEMP_BOTTOM + 10} stroke={GRID} stroke-width="1" />
				{#if nowX !== null}
					<!-- Two segments, leaving the rain panel title free -->
					<line x1={nowX} x2={nowX} y1={TEMP_TOP - 10} y2={TEMP_BOTTOM + 10} stroke={NOW_COLOR} stroke-width="2" stroke-dasharray="4 6" />
					<line x1={nowX} x2={nowX} y1={RAIN_TOP} y2={RAIN_BOTTOM} stroke={NOW_COLOR} stroke-width="2" stroke-dasharray="4 6" />
					<text x={nowX + 8} y={RAIN_TOP + 14} fill={TEXT_MUTED} font-size="16">
						{isGerman ? 'Jetzt' : 'Now'}
					</text>
				{/if}
				<path d={tempPath} fill="none" stroke={TEMP_COLOR} stroke-width="3" stroke-linejoin="round" stroke-linecap="round" />
				{#each hours as hour, i (hour.time)}
					{#if i % LABEL_EVERY === 0}
						<circle cx={xAt(i)} cy={yForTemp(hour.temperature)} r="5" fill={TEMP_COLOR} />
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
					<path d={barPath(PAD_X + i * columnWidth + 1, columnWidth - 2, rainHeight(hour.precipitationProbability))} fill={RAIN_COLOR} />
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
							{hourLabel(hour.time, i)}
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
			<div class="grid" style="grid-template-columns: repeat({nextDays.length}, 1fr); gap: 2vw; border-top: 1px solid {GRID}; padding-top: 2vh">
				{#each nextDays as day, i (day.date)}
					<div class="flex items-center" style="gap: 1vw; font-size: 3vh">
						{#if $configStore.weatherIconUrl}
							<img src={iconUrl(day.iconId)} alt="" style="width: 7vh; height: 7vh" />
						{/if}
						<div>
							<div style="font-weight: 600">{dayName(day.date, i)}</div>
							<div>
								{Math.round(day.temperatureMax)}° / <span style="color: {TEXT_MUTED}">{Math.round(day.temperatureMin)}°</span>
								<span style="color: {TEXT_MUTED}"><span style={SEPARATOR}></span>{day.precipitationProbability ?? 0}%</span>
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

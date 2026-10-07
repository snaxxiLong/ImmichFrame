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
		/**
		 * The overlay stays mounted, parked just below the screen on its own GPU layer. It is not
		 * display:none or visibility:hidden, because browsers do not paint hidden elements: the browser
		 * can paint it ahead of time, and opening only slides the finished layer in.
		 */
		open?: boolean;
		onClose: () => void;
	}

	let { weather, preloaded = null, open = false, onClose }: Props = $props();

	// Plain colors: older Android WebViews ignore Tailwind's oklch() palette. The overlay is see-through,
	// so the photos keep running behind a dark scrim; shadows keep text and marks readable on top.
	const SCRIM = 'rgba(0, 0, 0, 0.72)';
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
	// The overlay slides in from the bottom as one layer; only transform is animated, which older
	// GPUs can do without redrawing the page every frame.
	const SLIDE_MS = 450;
	const LABEL_EVERY = 2;
	const NEXT_DAYS = 5;

	let fetched = $state<api.WeatherDetails | null>(null);
	const hasData = (d?: api.WeatherDetails | null) => !!d?.hours?.length;
	const details = $derived(hasData(preloaded) ? preloaded : fetched);
	let failed = $state(false);

	const isGerman = $derived(($configStore.language ?? '').toLowerCase().startsWith('de'));
	const dateLocale = $derived(locale[$configStore.language as keyof typeof locale] ?? locale.enUS);

	const iconUrl = (iconId: string) =>
		$configStore.weatherIconUrl?.replace('{IconId}', encodeURIComponent(iconId.split(',')[0].trim()));

	const hours = $derived(details?.hours ?? []);
	const today = $derived(details?.days?.[0]);
	const nextDays = $derived((details?.days ?? []).slice(1, 1 + NEXT_DAYS));

	// The chart fills whatever height is left between the header and the day row. Its SVG uses the
	// real pixel size, and all chart text and spacing scale with that height.
	let chartWidth = $state(0);
	let chartHeight = $state(0);
	const scale = $derived(Math.min(2.2, Math.max(0.8, chartHeight / 360)));
	const px = (n: number) => Math.round(n * scale);

	const layout = $derived.by(() => {
		const iconSize = px(44);
		const iconY = chartHeight - iconSize;
		const timeY = iconY - px(10);
		const rainBottom = timeY - px(20) - px(14);
		const rainTop = rainBottom - Math.max(px(50), chartHeight * 0.17);
		const rainTitleY = rainTop - px(10);
		const tempBottom = rainTitleY - px(20) - px(16);
		// Room for the panel title and the value labels above the highest point.
		const tempTop = px(18) + px(22) + px(18);
		return { iconSize, iconY, timeY, rainBottom, rainTop, rainTitleY, tempBottom, tempTop };
	});

	// Side padding so the 00:00 and 24:00 labels are not cut off at the edges.
	const padX = $derived(px(28));
	const columnWidth = $derived(hours.length ? (chartWidth - 2 * padX) / hours.length : chartWidth);
	const xAt = (i: number) => padX + (i + 0.5) * columnWidth;

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
		layout.tempBottom -
		((t - tempRange.min) / (tempRange.max - tempRange.min)) * (layout.tempBottom - layout.tempTop);

	const tempPath = $derived(
		hours.map((h, i) => `${i === 0 ? 'M' : 'L'}${xAt(i).toFixed(1)},${yForTemp(h.temperature).toFixed(1)}`).join(' ')
	);

	const rainHeight = (p?: number | null) => ((p ?? 0) / 100) * (layout.rainBottom - layout.rainTop);

	// Bar with 4px rounded top, anchored square to the baseline.
	const barPath = (x: number, w: number, h: number) => {
		if (h <= 0) return '';
		const r = Math.min(4, h, w / 2);
		const bottom = layout.rainBottom;
		const top = bottom - h;
		return `M${x},${bottom} V${top + r} Q${x},${top} ${x + r},${top} H${x + w - r} Q${x + w},${top} ${x + w},${top + r} V${bottom} Z`;
	};

	// The series runs from today 00:00 to 24:00; the last point is tomorrow's midnight.
	const hourLabel = (iso: string, i: number) =>
		i === hours.length - 1 && i > 0 ? '24:00' : format(new Date(iso), 'HH:mm');

	// Refreshed whenever the overlay opens, because it can stay mounted for hours.
	let nowMs = $state(Date.now());

	// Current time as a position on the hour axis (column centers are the full hours).
	const nowX = $derived.by(() => {
		if (!hours.length) return null;
		const start = new Date(hours[0].time).getTime();
		const hoursSinceStart = (nowMs - start) / 3600000;
		if (hoursSinceStart < 0 || hoursSinceStart > hours.length - 1) return null;
		return padX + (hoursSinceStart + 0.5) * columnWidth;
	});

	const time = (iso?: string | null) => (iso ? format(new Date(iso), 'HH:mm') : '–');
	const dayName = (iso: string, index: number) =>
		index === 0
			? isGerman
				? 'Morgen'
				: 'Tomorrow'
			: format(new Date(iso), 'EEEE', { locale: dateLocale });

	onMount(() => {
		if (!hasData(preloaded)) {
			api
				.getWeatherDetails($clientIdentifierStore)
				.then((d) => {
					fetched = d;
					failed = !hasData(d);
				})
				.catch((err) => {
					console.error('Error fetching weather details:', err);
					failed = true;
				});
		}
	});

	// While open: close automatically after a while.
	$effect(() => {
		if (!open) return;
		nowMs = Date.now();
		const timeout = setTimeout(onClose, AUTO_CLOSE_MS);
		return () => clearTimeout(timeout);
	});
</script>

<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
<div
	id="weatheroverlay"
	class="fixed inset-0"
	aria-hidden={!open}
	style="z-index: 300; background-color: {SCRIM}; pointer-events: {open ? 'auto' : 'none'}; transform: translateY({open ? '0' : '100%'}); transition: transform {SLIDE_MS}ms cubic-bezier(0.2, 0.8, 0.2, 1); will-change: transform"
	onclick={onClose}
>
	<div
		class="flex flex-col"
		style="width: 100vw; height: 100vh; padding: 3.5vh 3.5vw 2vh; box-sizing: border-box; gap: 2.5vh; color: {TEXT}; text-shadow: 0 1px 4px {SHADOW}"
	>
		<!-- Now and today -->
		<div class="flex items-center justify-between" style="flex: none; gap: 3vw">
			<div class="flex items-center" style="gap: 1.5vw">
				{#if $configStore.weatherIconUrl && weather.iconId}
					<img src={iconUrl(weather.iconId)} alt="" style="width: 15vh; height: 15vh" />
				{/if}
				<div>
					<div style="font-size: 12vh; font-weight: 700; line-height: 1">
						{Math.round(weather.temperature ?? 0)}°
					</div>
					<div style="font-size: 3.6vh; color: {TEXT_MUTED}">
						{weather.description}<span style={SEPARATOR}></span>{weather.location}
					</div>
				</div>
			</div>
			{#if today}
				<div class="grid" style="grid-template-columns: auto auto; column-gap: 2vw; row-gap: 1vh; font-size: 3.6vh">
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
		<div style="flex: 1 1 auto; min-height: 0; position: relative" bind:clientWidth={chartWidth} bind:clientHeight={chartHeight}>
			{#if hours.length && chartWidth > 0 && chartHeight > 0}
				<svg
					width={chartWidth}
					height={chartHeight}
					viewBox="0 0 {chartWidth} {chartHeight}"
					style="position: absolute; inset: 0; display: block; overflow: visible"
				>
					<text x="0" y={px(18)} fill={TEXT_MUTED} font-size={px(20)}>
						{isGerman ? 'Temperatur heute' : 'Temperature today'}
					</text>
					<line x1="0" x2={chartWidth} y1={layout.tempBottom + px(10)} y2={layout.tempBottom + px(10)} stroke={GRID} stroke-width="1" />
					{#if nowX !== null}
						<!-- Two segments, leaving the rain panel title free -->
						<line x1={nowX} x2={nowX} y1={layout.tempTop - px(14)} y2={layout.tempBottom + px(10)} stroke={NOW_COLOR} stroke-width="2" stroke-dasharray="4 6" />
						<line x1={nowX} x2={nowX} y1={layout.rainTop} y2={layout.rainBottom} stroke={NOW_COLOR} stroke-width="2" stroke-dasharray="4 6" />
						<text x={nowX + px(8)} y={layout.rainTop + px(14)} fill={TEXT_MUTED} font-size={px(16)}>
							{isGerman ? 'Jetzt' : 'Now'}
						</text>
					{/if}
					<path d={tempPath} fill="none" stroke={TEMP_COLOR} stroke-width={Math.max(2, px(3))} stroke-linejoin="round" stroke-linecap="round" />
					{#each hours as hour, i (hour.time)}
						{#if i % LABEL_EVERY === 0}
							<circle cx={xAt(i)} cy={yForTemp(hour.temperature)} r={Math.max(4, px(5))} fill={TEMP_COLOR} />
							<text
								x={xAt(i)}
								y={yForTemp(hour.temperature) - px(14)}
								text-anchor="middle"
								fill={TEXT}
								font-size={px(22)}
								font-weight="600"
							>
								{Math.round(hour.temperature)}°
							</text>
						{/if}
					{/each}

					<text x="0" y={layout.rainTitleY} fill={TEXT_MUTED} font-size={px(20)}>
						{isGerman ? 'Regenwahrscheinlichkeit' : 'Chance of rain'}
					</text>
					<line x1="0" x2={chartWidth} y1={layout.rainBottom} y2={layout.rainBottom} stroke={GRID} stroke-width="1" />
					{#if !hours.some((h) => (h.precipitationProbability ?? 0) > 0)}
						<text
							x={chartWidth / 2}
							y={(layout.rainTop + layout.rainBottom) / 2 + px(7)}
							text-anchor="middle"
							fill={TEXT_MUTED}
							font-size={px(20)}
						>
							{isGerman ? 'Kein Regen erwartet' : 'No rain expected'}
						</text>
					{/if}
					{#each hours as hour, i (hour.time)}
						<path d={barPath(padX + i * columnWidth + 1, columnWidth - 2, rainHeight(hour.precipitationProbability))} fill={RAIN_COLOR} />
						{#if i % LABEL_EVERY === 0 && (hour.precipitationProbability ?? 0) >= 10}
							<text
								x={xAt(i)}
								y={layout.rainBottom - rainHeight(hour.precipitationProbability) - px(6)}
								text-anchor="middle"
								fill={TEXT}
								font-size={px(18)}
							>
								{hour.precipitationProbability}%
							</text>
						{/if}
					{/each}

					{#each hours as hour, i (hour.time)}
						{#if i % LABEL_EVERY === 0}
							<text x={xAt(i)} y={layout.timeY} text-anchor="middle" fill={TEXT_MUTED} font-size={px(20)}>
								{hourLabel(hour.time, i)}
							</text>
							{#if $configStore.weatherIconUrl}
								<image
									href={iconUrl(hour.iconId)}
									x={xAt(i) - layout.iconSize / 2}
									y={layout.iconY}
									width={layout.iconSize}
									height={layout.iconSize}
								/>
							{/if}
						{/if}
					{/each}
				</svg>
			{:else if failed}
				<p style="font-size: 3.4vh; color: {TEXT_MUTED}">
					{isGerman ? 'Vorhersage konnte nicht geladen werden.' : 'Could not load the forecast.'}
				</p>
			{:else if !hours.length}
				<p style="font-size: 3.4vh; color: {TEXT_MUTED}">
					{isGerman ? 'Lade Vorhersage …' : 'Loading forecast …'}
				</p>
			{/if}
		</div>

		<!-- Next days -->
		{#if nextDays.length}
			<div
				class="grid"
				style="flex: none; grid-template-columns: repeat({nextDays.length}, 1fr); gap: 2vw; border-top: 1px solid {GRID}; padding-top: 2.5vh"
			>
				{#each nextDays as day, i (day.date)}
					<div class="flex items-center" style="gap: 1vw; font-size: 3.4vh">
						{#if $configStore.weatherIconUrl}
							<img src={iconUrl(day.iconId)} alt="" style="width: 8vh; height: 8vh" />
						{/if}
						<div style="line-height: 1.25; white-space: nowrap">
							<div style="font-weight: 600">{dayName(day.date, i)}</div>
							<div>
								{Math.round(day.temperatureMax)}° / <span style="color: {TEXT_MUTED}">{Math.round(day.temperatureMin)}°</span>
							</div>
							<div style="font-size: 2.6vh; color: {TEXT_MUTED}">
								{isGerman ? 'Regen' : 'Rain'}&nbsp;{day.precipitationProbability ?? 0}&nbsp;%
							</div>
						</div>
					</div>
				{/each}
			</div>
		{/if}

		<p style="flex: none; text-align: center; font-size: 2vh; color: {TEXT_MUTED}">
			{isGerman ? 'Tippen zum Schließen' : 'Tap to close'}
		</p>
	</div>
</div>

<script lang="ts">
	import * as api from '$lib/index';
	import { untrack } from 'svelte';
	import {
		addDays,
		addMonths,
		endOfMonth,
		format,
		isSameDay,
		isSameMonth,
		startOfDay,
		startOfMonth,
		startOfWeek
	} from 'date-fns';
	import * as locale from 'date-fns/locale';
	import { configStore } from '$lib/stores/config.store';
	import { clientIdentifierStore } from '$lib/stores/persist.store';

	interface Props {
		open: boolean;
		onClose: () => void;
	}

	let { open, onClose }: Props = $props();

	// Plain colors: older Android WebViews ignore Tailwind's oklch() palette.
	const SCRIM = 'rgba(0, 0, 0, 0.93)';
	const SHADOW = 'rgba(0, 0, 0, 0.85)';
	const TEXT = '#f5f5f5';
	const TEXT_MUTED = '#b8b8b8';
	const GRID = 'rgba(255, 255, 255, 0.16)';
	const ACCENT = '#3a85d0';
	const ACCENT_BG = 'rgba(58, 133, 208, 0.38)';
	const TODAY_BG = '#c2821a';
	const SELECTED_BG = 'rgba(255, 255, 255, 0.12)';
	const FADE_MS = 300;
	const AUTO_CLOSE_MS = 120000;
	const MAX_CHIPS = 3;

	interface CalEvent {
		start: Date;
		end: Date;
		allDay: boolean;
		summary: string;
		location: string;
		description: string;
	}

	const isGerman = $derived(($configStore.language ?? '').toLowerCase().startsWith('de'));
	const dateLocale = $derived(locale[$configStore.language as keyof typeof locale] ?? locale.enUS);
	const timeFormat = $derived(($configStore.clockFormat ?? 'HH:mm:ss').replace(/[:.]ss$/, ''));

	let month = $state(startOfMonth(new Date()));
	let selected = $state(startOfDay(new Date()));
	let events = $state<CalEvent[]>([]);
	let loading = $state(false);
	let failed = $state(false);
	let requestId = 0;

	// Monday-first grid that always shows six full weeks, like the Samsung calendar.
	const gridStart = $derived(startOfWeek(month, { weekStartsOn: 1 }));
	const days = $derived(Array.from({ length: 42 }, (_, i) => addDays(gridStart, i)));
	const weekdays = $derived(days.slice(0, 7).map((d) => format(d, 'EEEEEE', { locale: dateLocale })));

	const isAllDay = (e: api.IAppointment, start: Date, end: Date) =>
		start.getHours() === 0 &&
		start.getMinutes() === 0 &&
		end.getHours() === 0 &&
		end.getMinutes() === 0 &&
		end.getTime() > start.getTime();

	async function load() {
		const id = ++requestId;
		loading = true;
		try {
			const from = gridStart;
			const to = addDays(gridStart, 42);
			const result = await api.getAppointmentsInRange(from, to, $clientIdentifierStore);
			if (id !== requestId) return;
			events = result
				.map((a) => {
					const start = new Date(a.startTime ?? '');
					const end = new Date(a.endTime ?? a.startTime ?? '');
					return {
						start,
						end,
						allDay: isAllDay(a, start, end),
						summary: a.summary ?? '',
						location: a.location ?? '',
						description: a.description ?? ''
					};
				})
				.filter((e) => !isNaN(e.start.getTime()))
				.sort((a, b) => a.start.getTime() - b.start.getTime());
			failed = false;
		} catch (err) {
			console.error('Error fetching calendar:', err);
			if (id === requestId) failed = true;
		} finally {
			if (id === requestId) loading = false;
		}
	}

	// An event belongs to a day when it overlaps it; all-day ends are exclusive (end = next midnight).
	const onDay = (e: CalEvent, day: Date) => {
		const dayStart = day.getTime();
		const dayEnd = addDays(day, 1).getTime();
		const endMs = e.end.getTime();
		return e.start.getTime() < dayEnd && (endMs > dayStart || (endMs === e.start.getTime() && e.start.getTime() >= dayStart));
	};

	const byDay = $derived.by(() => {
		const map = new Map<number, CalEvent[]>();
		for (const day of days) {
			const list = events.filter((e) => onDay(e, day));
			// All-day events first, then by start time.
			list.sort((a, b) => Number(b.allDay) - Number(a.allDay) || a.start.getTime() - b.start.getTime());
			map.set(day.getTime(), list);
		}
		return map;
	});

	const eventsOf = (day: Date) => byDay.get(day.getTime()) ?? [];
	const selectedEvents = $derived(eventsOf(selected));

	const timeLabel = (e: CalEvent, day: Date) => {
		if (e.allDay) {
			const lastDay = addDays(e.end, -1);
			if (isSameDay(e.start, lastDay)) return isGerman ? 'Ganztägig' : 'All day';
			return `${format(e.start, 'eee d. MMM', { locale: dateLocale })} – ${format(lastDay, 'eee d. MMM', { locale: dateLocale })}`;
		}
		if (isSameDay(e.start, e.end)) return `${format(e.start, timeFormat)} – ${format(e.end, timeFormat)}`;
		const from = isSameDay(e.start, day) ? format(e.start, timeFormat) : format(e.start, 'eee d. MMM', { locale: dateLocale });
		const to = isSameDay(e.end, day) ? format(e.end, timeFormat) : format(e.end, 'eee d. MMM', { locale: dateLocale });
		return `${from} – ${to}`;
	};

	function goTo(target: Date) {
		month = startOfMonth(target);
		load();
	}

	function today() {
		const now = new Date();
		selected = startOfDay(now);
		goTo(now);
	}

	function pick(day: Date) {
		selected = day;
		if (!isSameMonth(day, month)) goTo(day);
	}

	// Every time the calendar opens: jump to today and refresh; close by itself after a while.
	let idleTimer: ReturnType<typeof setTimeout> | undefined;
	const resetIdle = () => {
		clearTimeout(idleTimer);
		if (open) idleTimer = setTimeout(onClose, AUTO_CLOSE_MS);
	};

	$effect(() => {
		if (!open) {
			clearTimeout(idleTimer);
			return;
		}
		untrack(() => today());
		resetIdle();
		return () => clearTimeout(idleTimer);
	});
</script>

{#if open}
	<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
	<div
		id="calendaroverlay"
		class="fixed inset-0"
		style="z-index: 300; background-color: {SCRIM}; animation: calendar-fade {FADE_MS}ms ease-out; color: {TEXT}; text-shadow: 0 1px 4px {SHADOW}"
		onclick={(e) => {
			e.stopPropagation();
			resetIdle();
		}}
	>
		<div
			class="flex"
			style="width: 100vw; height: 100vh; padding: 3vh 2.5vw; box-sizing: border-box; gap: 2vw"
		>
			<!-- Month grid -->
			<div class="flex flex-col" style="flex: 1 1 0; min-width: 0; gap: 1.5vh">
				<div class="flex items-center" style="flex: none; gap: 1.2vw">
					<div style="font-size: 5vh; font-weight: 700; flex: 1 1 auto; text-transform: capitalize">
						{format(month, 'LLLL yyyy', { locale: dateLocale })}
					</div>
					<button class="nav" onclick={() => goTo(addMonths(month, -1))} aria-label="previous">‹</button>
					<button class="nav today" onclick={today}>{isGerman ? 'Heute' : 'Today'}</button>
					<button class="nav" onclick={() => goTo(addMonths(month, 1))} aria-label="next">›</button>
					<button class="nav" onclick={onClose} aria-label="close">✕</button>
				</div>

				<div class="grid" style="flex: none; grid-template-columns: repeat(7, 1fr); color: {TEXT_MUTED}; font-size: 2.4vh; text-align: center">
					{#each weekdays as name}
						<div>{name}</div>
					{/each}
				</div>

				<div
					class="grid"
					style="flex: 1 1 auto; min-height: 0; grid-template-columns: repeat(7, 1fr); grid-template-rows: repeat(6, 1fr); border-top: 1px solid {GRID}; border-left: 1px solid {GRID}"
				>
					{#each days as day (day.getTime())}
						{@const list = eventsOf(day)}
						<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
						<div
							class="cell"
							style="border-right: 1px solid {GRID}; border-bottom: 1px solid {GRID}; background-color: {isSameDay(day, selected) ? SELECTED_BG : 'transparent'}; opacity: {isSameMonth(day, month) ? 1 : 0.45}"
							onclick={() => pick(day)}
						>
							<div
								class="daynum"
								style={isSameDay(day, new Date()) ? `background-color: ${TODAY_BG}; color: #fff` : ''}
							>
								{format(day, 'd')}
							</div>
							{#each list.slice(0, MAX_CHIPS) as e}
								<div class="chip" style="background-color: {ACCENT_BG}; border-left: 3px solid {ACCENT}">{e.summary}</div>
							{/each}
							{#if list.length > MAX_CHIPS}
								<div class="more">+{list.length - MAX_CHIPS}</div>
							{/if}
						</div>
					{/each}
				</div>
			</div>

			<!-- Selected day -->
			<div
				class="flex flex-col"
				style="flex: 0 0 29vw; min-width: 0; gap: 1.5vh; border-left: 1px solid {GRID}; padding-left: 2vw"
			>
				<div style="flex: none">
					<div style="font-size: 3vh; color: {TEXT_MUTED}; text-transform: capitalize">
						{format(selected, 'EEEE', { locale: dateLocale })}
					</div>
					<div style="font-size: 4.4vh; font-weight: 700">
						{format(selected, 'd. MMMM', { locale: dateLocale })}
					</div>
				</div>
				<div style="flex: 1 1 auto; min-height: 0; overflow-y: auto; -webkit-overflow-scrolling: touch">
					{#if failed}
						<div style="font-size: 2.6vh; color: {TEXT_MUTED}">
							{isGerman ? 'Kalender konnte nicht geladen werden.' : 'Could not load the calendar.'}
						</div>
					{:else if loading && !events.length}
						<div style="font-size: 2.6vh; color: {TEXT_MUTED}">{isGerman ? 'Lade …' : 'Loading …'}</div>
					{:else if !selectedEvents.length}
						<div style="font-size: 2.6vh; color: {TEXT_MUTED}">
							{isGerman ? 'Keine Termine' : 'No appointments'}
						</div>
					{/if}
					{#each selectedEvents as e}
						<div class="item" style="border-left: 4px solid {ACCENT}">
							<div style="font-size: 2.2vh; color: {TEXT_MUTED}">{timeLabel(e, selected)}</div>
							<div style="font-size: 3vh; font-weight: 600; line-height: 1.2">{e.summary}</div>
							{#if e.location}
								<div style="font-size: 2.2vh; color: {TEXT_MUTED}">{e.location}</div>
							{/if}
							{#if e.description}
								<div style="font-size: 2.2vh; color: {TEXT_MUTED}; white-space: pre-line">{e.description}</div>
							{/if}
						</div>
					{/each}
				</div>
			</div>
		</div>
	</div>
{/if}

<style>
	@keyframes calendar-fade {
		from {
			opacity: 0;
		}
		to {
			opacity: 1;
		}
	}

	.nav {
		flex: none;
		min-width: 6vh;
		height: 6vh;
		padding: 0 1.4vh;
		border-radius: 3vh;
		border: 1px solid rgba(255, 255, 255, 0.3);
		background-color: rgba(255, 255, 255, 0.08);
		color: inherit;
		font-size: 3vh;
		line-height: 1;
	}

	.nav.today {
		font-size: 2.4vh;
	}

	.cell {
		min-width: 0;
		min-height: 0;
		overflow: hidden;
		padding: 0.4vh 0.3vw;
	}

	.daynum {
		width: 3.6vh;
		height: 3.6vh;
		line-height: 3.6vh;
		border-radius: 50%;
		text-align: center;
		font-size: 2.3vh;
		margin: 0 auto 0.4vh;
	}

	.chip {
		font-size: 1.9vh;
		line-height: 2.5vh;
		height: 2.5vh;
		margin-bottom: 0.3vh;
		padding: 0 0.4vw;
		border-radius: 3px;
		overflow: hidden;
		white-space: nowrap;
		text-overflow: ellipsis;
		text-shadow: none;
	}

	.more {
		font-size: 1.8vh;
		color: #b8b8b8;
		padding-left: 0.4vw;
	}

	.item {
		padding: 0.6vh 0 0.6vh 1vw;
		margin-bottom: 1.6vh;
	}
</style>

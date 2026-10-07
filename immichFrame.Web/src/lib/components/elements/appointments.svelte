<script lang="ts">
	import * as api from '$lib/index';
	import { onMount } from 'svelte';
	import { addDays, format, isSameDay, startOfDay } from 'date-fns';
	import * as locale from 'date-fns/locale';
	import { configStore } from '$lib/stores/config.store';
	import { clientIdentifierStore } from '$lib/stores/persist.store';
	import { weatherOverlayOpenStore } from '$lib/stores/weather-overlay.store';
	import { calendarOverlayOpenStore } from '$lib/stores/calendar-overlay.store';
	import CalendarOverlay from './calendar-overlay.svelte';

	api.init();

	const SHOW_COUNT = 5;
	const LOOKAHEAD_DAYS = 60;

	interface Upcoming {
		start: Date;
		end: Date;
		allDay: boolean;
		summary: string;
	}

	const isGerman = $derived(($configStore.language ?? '').toLowerCase().startsWith('de'));
	const dateLocale = $derived(locale[$configStore.language as keyof typeof locale] ?? locale.enUS);
	const timeFormat = $derived(($configStore.clockFormat ?? 'HH:mm:ss').replace(/[:.]ss$/, ''));

	let events = $state<Upcoming[]>([]);
	let loaded = $state(false);
	// Re-evaluated every minute so finished appointments drop out and "Tomorrow" becomes "Today".
	let now = $state(new Date());

	// Tapping the appointments opens the month calendar; the slideshow keeps running behind it.
	function openCalendar(event: MouseEvent) {
		event.stopPropagation();
		calendarOverlayOpenStore.set(true);
	}

	function closeCalendar() {
		calendarOverlayOpenStore.set(false);
	}

	// Everything from today on, including today's appointments that are already over (times are often not entered exactly, so they stay as they are).
	const upcoming = $derived(events.filter((e) => e.end.getTime() > startOfDay(now).getTime()).slice(0, SHOW_COUNT));

	const dayWord = (day: Date) => {
		const today = startOfDay(now);
		if (isSameDay(day, today)) return isGerman ? 'Heute' : 'Today';
		if (isSameDay(day, addDays(today, 1))) return isGerman ? 'Morgen' : 'Tomorrow';
		return format(day, isGerman ? 'eee, d. MMM' : 'eee, MMM d', { locale: dateLocale });
	};

	// Short, quiet time line above the title.
	function whenLabel(e: Upcoming) {
		if (e.allDay) {
			const lastDay = addDays(e.end, -1);
			if (e.start.getTime() < startOfDay(now).getTime()) {
				return (isGerman ? 'Bis ' : 'Until ') + dayWord(lastDay);
			}
			if (isSameDay(e.start, lastDay)) return dayWord(e.start);
			return `${dayWord(e.start)} – ${dayWord(lastDay)}`;
		}
		// Already running over several days: show when it ends.
		if (!isSameDay(e.start, e.end) && e.start.getTime() < now.getTime()) {
			return `${isGerman ? 'Bis ' : 'Until '}${dayWord(e.end)} ${format(e.end, timeFormat)}`;
		}
		return isSameDay(e.start, e.end)
			? `${dayWord(e.start)} ${format(e.start, timeFormat)} – ${format(e.end, timeFormat)}`
			: `${dayWord(e.start)} ${format(e.start, timeFormat)}`;
	}

	onMount(() => {
		getAppointments();
		const appointmentInterval = setInterval(() => getAppointments(), 10 * 60 * 1000); //every 10 minutes
		const clockInterval = setInterval(() => (now = new Date()), 60 * 1000);

		return () => {
			clearInterval(appointmentInterval);
			clearInterval(clockInterval);
		};
	});

	async function getAppointments() {
		try {
			const from = startOfDay(new Date());
			const result = await api.getAppointmentsInRange(from, addDays(from, LOOKAHEAD_DAYS), $clientIdentifierStore);
			events = result
				.map((a) => {
					const start = new Date(a.startTime ?? '');
					const end = new Date(a.endTime ?? a.startTime ?? '');
					const allDay =
						start.getHours() === 0 &&
						start.getMinutes() === 0 &&
						end.getHours() === 0 &&
						end.getMinutes() === 0 &&
						end.getTime() > start.getTime();
					return { start, end, allDay, summary: a.summary ?? '' };
				})
				.filter((e) => !isNaN(e.start.getTime()))
				.sort((a, b) => a.start.getTime() - b.start.getTime());
			loaded = true;
		} catch (err) {
			console.error('Error fetching appointments:', err);
		}
	}
</script>

{#if loaded && upcoming.length}
	<!-- Sits above the slideshow's tap areas; tapping the appointments opens the month calendar. -->
	<!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
	<div
		id="appointments"
		class="fixed top-0 right-0 w-auto text-frame-primary max-w-[24%] hidden md:block md:min-w-[10%] pt-4"
		style="z-index: 110; pointer-events: auto; cursor: pointer; visibility: {$weatherOverlayOpenStore || $calendarOverlayOpenStore ? 'hidden' : 'visible'}"
		onclick={openCalendar}
	>
		{#each upcoming as appointment}
			<!-- Every appointment on its own background, with a small gap in between -->
			<div
				class="mb-2 text-left drop-shadow-2xl text-shadow-sm py-3 pr-2 pl-3
				{$configStore.style == 'solid' ? 'bg-frame-secondary rounded-l-2xl' : ''}
				{$configStore.style == 'transition' ? 'bg-linear-to-l from-frame-secondary from-40% pl-6' : ''}
				{$configStore.style == 'blur' ? 'backdrop-blur-lg rounded-l-2xl' : ''}"
			>
				<p class="text-xs font-light opacity-75">{whenLabel(appointment)}</p>
				<p class="text-lg leading-snug">{appointment.summary}</p>
			</div>
		{/each}
	</div>
{/if}

<CalendarOverlay open={$calendarOverlayOpenStore} onClose={closeCalendar} />

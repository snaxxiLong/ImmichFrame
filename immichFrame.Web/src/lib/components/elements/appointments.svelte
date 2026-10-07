<script lang="ts">
	import * as api from '$lib/index';
	import { onMount } from 'svelte';
	import { format } from 'date-fns';
	import { configStore } from '$lib/stores/config.store';
	import { clientIdentifierStore } from '$lib/stores/persist.store';
	import { weatherOverlayOpenStore } from '$lib/stores/weather-overlay.store';

	api.init();

	function formatDates(startTime: string, endTime: string) {
		let startDate = new Date(startTime);
		let endDate = new Date(endTime);
		let sameDay = startDate.getDate() == endDate.getDate();

		let clockFormat = $configStore.clockFormat ?? 'HH:mm';
		let clockDateFormat = $configStore.clockDateFormat ?? 'eee, MMM d';
		let fullFormat = clockDateFormat + ' ' + clockFormat;

		if (sameDay) {
			return format(startDate, clockFormat) + ' - ' + format(endDate, clockFormat);
		}

		return format(startDate, fullFormat) + ' - ' + format(endDate, fullFormat);
	}

	let appointments: api.IAppointment[] = $state() as api.IAppointment[];

	onMount(() => {
		GetAppointments();
		const appointmentInterval = setInterval(() => GetAppointments(), 10 * 60 * 1000); //every 10 minutes

		return () => {
			clearInterval(appointmentInterval);
		};
	});

	async function GetAppointments() {
		let appointmentRequest = await api.getAppointments({
			clientIdentifier: $clientIdentifierStore
		});
		if (appointmentRequest.status == 200) {
			appointments = appointmentRequest.data;

			appointments = appointmentRequest.data.sort((a, b) => {
				return new Date(a.startTime ?? '').getTime() - new Date(b.startTime ?? '').getTime();
			});
		}
	}
</script>

{#if appointments}
	<div
		id="appointments"
		class="fixed top-0 right-0 w-auto z-10 text-center text-frame-primary m-5 max-w-[20%] hidden md:block md:min-w-[10%]"
		style="visibility: {$weatherOverlayOpenStore ? 'hidden' : 'visible'}"
	>
		<!-- <div class="text-4xl mx-8 font-bold">Appointments</div> -->
		<div class="">
			{#each appointments as appointment}
				<div class="mb-2 text-left rounded-2xl p-3 drop-shadow-2xl text-shadow-sm {$configStore.style == 'blur' ? 'backdrop-blur-lg' : 'bg-frame-secondary'}">
					<p class="text-xs">
						{formatDates(appointment.startTime ?? '', appointment.endTime ?? '')}
					</p>
					{appointment.summary}
					{#if appointment.description}
						<p class="text-xs font-light">{appointment.description}</p>
					{/if}
				</div>
			{/each}
		</div>
	</div>
{/if}
